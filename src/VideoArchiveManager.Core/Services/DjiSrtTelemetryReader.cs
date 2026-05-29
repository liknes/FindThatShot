using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace VideoArchiveManager.Core.Services;

/// <summary>
/// Reads DJI's standard flight-data <c>.SRT</c> companion file (written by the
/// DJI Fly app next to every recorded clip). The format is a normal SubRip
/// subtitle file whose text payload carries per-frame telemetry like:
/// <code>
/// [iso: 400] [shutter: 1/100] [fnum: 1.8] [latitude: 60.391263]
/// [longitude: 5.322054] [rel_alt: 5.1 abs_alt: 29.054]
/// </code>
/// We scan forward and return the first non-zero <c>latitude/longitude</c> pair
/// (the GPS values start at exactly 0.0/0.0 before the drone gets a fix). For
/// indoor flights where no fix is ever obtained, the method returns <c>null</c>.
/// </summary>
public sealed class DjiSrtTelemetryReader : IDjiSrtTelemetryReader
{
    // Matches "[latitude: <num>] [longitude: <num>]" with the standard DJI
    // bracket layout. Whitespace between the two brackets is allowed.
    private static readonly Regex LatLonPattern = new(
        @"\[latitude:\s*(?<lat>-?\d+(?:\.\d+)?)\][^\[]*\[longitude:\s*(?<lon>-?\d+(?:\.\d+)?)\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // 1e-6 ≈ 0.1 m at the equator — anything smaller is "still at zero".
    private const double NearZero = 1e-6;

    private readonly ILogger<DjiSrtTelemetryReader>? _logger;

    public DjiSrtTelemetryReader(ILogger<DjiSrtTelemetryReader>? logger = null)
    {
        _logger = logger;
    }

    public async Task<DjiTelemetrySummary?> TryReadAsync(
        string videoPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoPath)) return null;

        var srtPath = TryResolveCompanion(videoPath);
        if (srtPath is null) return null;

        try
        {
            // Stream the file line-by-line so we never pay to load a multi-MB
            // SRT into memory; we usually bail out within the first few hundred
            // lines once the GPS gets a fix.
            using var stream = new FileStream(
                srtPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 8192,
                options: FileOptions.SequentialScan | FileOptions.Asynchronous);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                var match = LatLonPattern.Match(line);
                if (!match.Success) continue;

                if (!double.TryParse(
                        match.Groups["lat"].Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var lat)) continue;
                if (!double.TryParse(
                        match.Groups["lon"].Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var lon)) continue;

                // Skip uninitialised / no-fix readings.
                if (Math.Abs(lat) < NearZero && Math.Abs(lon) < NearZero) continue;

                // Defensive: a sane latitude is in [-90, 90] and longitude in
                // [-180, 180]. Drop anything else (parser glitches, corrupt
                // SRT, etc.) rather than poisoning the DB.
                if (lat is < -90 or > 90) continue;
                if (lon is < -180 or > 180) continue;

                return new DjiTelemetrySummary { Latitude = lat, Longitude = lon };
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "DJI SRT parse failed for {Path}", srtPath);
            return null;
        }

        return null;
    }

    /// <summary>
    /// Looks for a sibling SRT file with the same base name as the video, using
    /// the platform's case-sensitivity. DJI writes uppercase ".SRT" so we try
    /// that first, then fall back to lowercase for Linux/macOS users.
    /// </summary>
    private static string? TryResolveCompanion(string videoPath)
    {
        var upper = Path.ChangeExtension(videoPath, ".SRT");
        if (File.Exists(upper)) return upper;

        var lower = Path.ChangeExtension(videoPath, ".srt");
        if (File.Exists(lower)) return lower;

        return null;
    }
}
