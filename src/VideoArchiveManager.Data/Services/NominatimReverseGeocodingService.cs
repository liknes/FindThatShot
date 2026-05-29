using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Services;

namespace VideoArchiveManager.Data.Services;

/// <summary>
/// Reverse-geocodes coordinates via the public OpenStreetMap Nominatim service.
/// Results are cached in the catalog database so repeated lookups (and clips
/// taken near each other) do not generate repeated network traffic.
///
/// Nominatim's usage policy requires:
///   * a meaningful User-Agent identifying the application,
///   * no more than 1 request per second,
///   * caching of results.
/// Reference: https://operations.osmfoundation.org/policies/nominatim/
/// </summary>
public sealed class NominatimReverseGeocodingService : IReverseGeocodingService
{
    private const string ProviderId = "nominatim";

    /// <summary>
    /// Name used to register the configured <see cref="HttpClient"/> in the
    /// host's HTTP client factory. The host project wires this up.
    /// </summary>
    public const string HttpClientName = "nominatim";

    private static readonly TimeSpan MinDelayBetweenRequests = TimeSpan.FromSeconds(1);

    private readonly Func<HttpClient> _httpClientProvider;
    private readonly IDbContextFactory<VideoArchiveDbContext> _contextFactory;
    private readonly ILogger<NominatimReverseGeocodingService>? _logger;

    // Serialises access so we never fire two HTTP requests in parallel (Nominatim
    // is a shared free service; the policy is sequential, 1 req/sec).
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _nextAllowedRequestUtc = DateTime.MinValue;

    public NominatimReverseGeocodingService(
        Func<HttpClient> httpClientProvider,
        IDbContextFactory<VideoArchiveDbContext> contextFactory,
        ILogger<NominatimReverseGeocodingService>? logger = null)
    {
        _httpClientProvider = httpClientProvider;
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<GeocodeResult?> LookupAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
    {
        var latKey = GeocodeCacheEntry.Round(latitude);
        var lonKey = GeocodeCacheEntry.Round(longitude);

        await using (var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var cached = await ctx.GeocodeCacheEntries
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    e => e.Provider == ProviderId &&
                         e.LatRounded == latKey &&
                         e.LonRounded == lonKey,
                    cancellationToken)
                .ConfigureAwait(false);
            if (cached is not null)
            {
                return new GeocodeResult
                {
                    LocationShort = cached.LocationShort,
                    DisplayName = cached.DisplayName,
                    Country = cached.Country,
                    Region = cached.Region
                };
            }
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check the cache after acquiring the gate; another caller may have
            // just filled it in for the same coord while we were waiting.
            await using (var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var raced = await ctx.GeocodeCacheEntries
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        e => e.Provider == ProviderId &&
                             e.LatRounded == latKey &&
                             e.LonRounded == lonKey,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (raced is not null)
                {
                    return new GeocodeResult
                    {
                        LocationShort = raced.LocationShort,
                        DisplayName = raced.DisplayName,
                        Country = raced.Country,
                        Region = raced.Region
                    };
                }
            }

            var now = DateTime.UtcNow;
            if (now < _nextAllowedRequestUtc)
            {
                await Task.Delay(_nextAllowedRequestUtc - now, cancellationToken).ConfigureAwait(false);
            }

            NominatimResponse? response = null;
            try
            {
                var url = string.Format(
                    CultureInfo.InvariantCulture,
                    "reverse?format=jsonv2&lat={0}&lon={1}&zoom=12&addressdetails=1",
                    latitude, longitude);

                var http = _httpClientProvider();
                response = await http
                    .GetFromJsonAsync<NominatimResponse>(url, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger?.LogWarning(ex, "Nominatim lookup failed for {Lat},{Lon}", latitude, longitude);
                _nextAllowedRequestUtc = DateTime.UtcNow + MinDelayBetweenRequests;
                return null;
            }

            _nextAllowedRequestUtc = DateTime.UtcNow + MinDelayBetweenRequests;

            if (response is null)
            {
                return null;
            }

            var locationShort = ChooseLocationShort(response);
            if (string.IsNullOrWhiteSpace(locationShort))
            {
                return null;
            }

            var entry = new GeocodeCacheEntry
            {
                Provider = ProviderId,
                LatRounded = latKey,
                LonRounded = lonKey,
                LocationShort = locationShort.Trim(),
                DisplayName = response.DisplayName,
                Country = response.Address?.Country,
                Region = response.Address?.County ?? response.Address?.State,
                LookedUpAt = DateTime.UtcNow
            };

            await using (var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                ctx.GeocodeCacheEntries.Add(entry);
                try
                {
                    await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (DbUpdateException)
                {
                    // Almost certainly a unique-constraint race; the other writer's
                    // value is just as good as ours, so swallow and return it.
                }
            }

            return new GeocodeResult
            {
                LocationShort = entry.LocationShort,
                DisplayName = entry.DisplayName,
                Country = entry.Country,
                Region = entry.Region
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Picks the most "human" / specific name from a Nominatim address breakdown.
    /// We prefer named places (a national park, a town, a village) over admin
    /// boundaries (county, state).
    /// </summary>
    private static string? ChooseLocationShort(NominatimResponse r)
    {
        var a = r.Address;
        if (a is null) return r.Name ?? r.DisplayName;

        return
            FirstNonEmpty(
                a.Tourism,
                a.NaturalFeature,
                a.ProtectedArea,
                a.Park,
                a.Hamlet,
                a.Village,
                a.Town,
                a.Suburb,
                a.City,
                a.Municipality,
                a.County,
                a.State,
                r.Name)
            ?? r.DisplayName;
    }

    private static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (var c in candidates)
        {
            if (!string.IsNullOrWhiteSpace(c)) return c;
        }
        return null;
    }

    private sealed class NominatimResponse
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
        [JsonPropertyName("address")] public NominatimAddress? Address { get; set; }
    }

    private sealed class NominatimAddress
    {
        [JsonPropertyName("tourism")] public string? Tourism { get; set; }
        [JsonPropertyName("natural")] public string? NaturalFeature { get; set; }
        [JsonPropertyName("protected_area")] public string? ProtectedArea { get; set; }
        [JsonPropertyName("park")] public string? Park { get; set; }
        [JsonPropertyName("hamlet")] public string? Hamlet { get; set; }
        [JsonPropertyName("village")] public string? Village { get; set; }
        [JsonPropertyName("town")] public string? Town { get; set; }
        [JsonPropertyName("suburb")] public string? Suburb { get; set; }
        [JsonPropertyName("city")] public string? City { get; set; }
        [JsonPropertyName("municipality")] public string? Municipality { get; set; }
        [JsonPropertyName("county")] public string? County { get; set; }
        [JsonPropertyName("state")] public string? State { get; set; }
        [JsonPropertyName("country")] public string? Country { get; set; }
    }
}
