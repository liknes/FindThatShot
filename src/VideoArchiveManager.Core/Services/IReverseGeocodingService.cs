namespace VideoArchiveManager.Core.Services;

public sealed class GeocodeResult
{
    public required string LocationShort { get; init; }
    public string? DisplayName { get; init; }
    public string? Country { get; init; }
    public string? Region { get; init; }
}

public interface IReverseGeocodingService
{
    /// <summary>
    /// Resolve a GPS coordinate to a human-readable place name. Implementations
    /// MUST cache results so that calling this repeatedly with the same (or very
    /// nearby) coordinates does not generate repeated network calls.
    /// </summary>
    /// <returns><c>null</c> if the coordinate could not be resolved.</returns>
    Task<GeocodeResult?> LookupAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default);
}
