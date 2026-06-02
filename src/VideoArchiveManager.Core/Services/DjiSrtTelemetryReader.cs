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

    // Matches the DJI "[rel_alt: 107.400 abs_alt: 47.605]" bracket; we lift
    // rel_alt (height above takeoff, in metres) for the endpoint tooltips.
    private static readonly Regex RelAltPattern = new(
        @"\[rel_alt:\s*(?<alt>-?\d+(?:\.\d+)?)",
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
                if (!TryParseLatLon(line, out var lat, out var lon)) continue;

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

    public async Task<IReadOnlyList<GeoPoint>?> TryReadFlightPathAsync(
        string videoPath,
        int maxPoints = 600,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoPath)) return null;
        if (maxPoints < 2) maxPoints = 2;

        var srtPath = TryResolveCompanion(videoPath);
        if (srtPath is null) return null;

        // Collect the raw, ordered fixes first, then downsample once at the
        // end. DJI logs ~30 samples/second, so a multi-minute clip can carry
        // tens of thousands of identical-ish points — far more than the map
        // needs. Coalescing consecutive duplicate coordinates while reading
        // keeps the working set small for long hovers.
        var raw = new List<GeoPoint>();
        try
        {
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
                if (!TryParseLatLon(line, out var lat, out var lon)) continue;

                // Drop exact consecutive repeats (the drone hovering / GPS not
                // refreshing) so the polyline isn't thousands of stacked points.
                if (raw.Count > 0)
                {
                    var last = raw[^1];
                    if (Math.Abs(last.Latitude - lat) < NearZero &&
                        Math.Abs(last.Longitude - lon) < NearZero)
                    {
                        continue;
                    }
                }

                raw.Add(new GeoPoint(lat, lon, TryParseRelAlt(line)));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "DJI SRT flight-path parse failed for {Path}", srtPath);
            return null;
        }

        return Downsample(raw, maxPoints);
    }

    // Uniformly thins the points down to <= maxPoints while always preserving
    // the first and last fix so the takeoff and landing markers stay accurate.
    private static IReadOnlyList<GeoPoint> Downsample(List<GeoPoint> points, int maxPoints)
    {
        if (points.Count <= maxPoints) return points;

        var result = new List<GeoPoint>(maxPoints);
        // Step across the source so the last sampled index lands on the final
        // point; the explicit add of points[^1] below guarantees the endpoint.
        var step = (double)(points.Count - 1) / (maxPoints - 1);
        for (int i = 0; i < maxPoints - 1; i++)
        {
            result.Add(points[(int)(i * step)]);
        }
        result.Add(points[^1]);
        return result;
    }

    // Shared lat/lon extraction used by both the single-fix and full-path
    // readers. Returns true only for an in-range, past-the-no-fix-zero sample.
    private static bool TryParseLatLon(string line, out double lat, out double lon)
    {
        lat = 0;
        lon = 0;

        var match = LatLonPattern.Match(line);
        if (!match.Success) return false;

        if (!double.TryParse(
                match.Groups["lat"].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out lat)) return false;
        if (!double.TryParse(
                match.Groups["lon"].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out lon)) return false;

        // Skip uninitialised / no-fix readings (exactly 0.0/0.0 before lock).
        if (Math.Abs(lat) < NearZero && Math.Abs(lon) < NearZero) return false;

        // Drop anything outside the valid WGS84 envelope (parser glitches,
        // corrupt SRT, etc.).
        if (lat is < -90 or > 90) return false;
        if (lon is < -180 or > 180) return false;

        return true;
    }

    // Pulls the DJI rel_alt (metres above takeoff) from a telemetry line, or
    // null when the line doesn't carry one / it doesn't parse.
    private static double? TryParseRelAlt(string line)
    {
        var match = RelAltPattern.Match(line);
        if (!match.Success) return null;
        return double.TryParse(
            match.Groups["alt"].Value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var alt) ? alt : null;
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
