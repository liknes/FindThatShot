namespace VideoArchiveManager.Core.Services;

/// <summary>
/// Telemetry values extracted from a DJI flight-data SRT companion file. Only a
/// minimal subset is exposed today — the SRT format also carries altitude, ISO,
/// shutter, etc. that could be lifted later if useful.
/// </summary>
public sealed class DjiTelemetrySummary
{
    /// <summary>First non-zero latitude (typically the GPS lock / takeoff point).</summary>
    public double? Latitude { get; init; }

    /// <summary>First non-zero longitude (typically the GPS lock / takeoff point).</summary>
    public double? Longitude { get; init; }
}

/// <summary>
/// A single WGS84 coordinate sampled from a DJI flight log. Immutable value
/// type used to build a flight-path polyline for the sidebar map.
/// <paramref name="RelativeAltitude"/> is the DJI <c>rel_alt</c> reading
/// (metres above the takeoff point), surfaced in the start / end
/// tooltips; <c>null</c> when the sample didn't carry one.
/// </summary>
public readonly record struct GeoPoint(double Latitude, double Longitude, double? RelativeAltitude = null);

public interface IDjiSrtTelemetryReader
{
    /// <summary>
    /// If a sibling <c>.SRT</c> companion file exists next to the given video,
    /// parse it for DJI flight telemetry. Returns <c>null</c> when no companion
    /// is found or the file can't be read.
    /// </summary>
    /// <param name="videoPath">Absolute path to the video file (e.g. <c>...\DJI_..._D.MP4</c>).</param>
    Task<DjiTelemetrySummary?> TryReadAsync(string videoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// If a sibling <c>.SRT</c> companion file exists next to the given video,
    /// parse it into the ordered list of GPS fixes that make up the flight path.
    /// Uninitialised (near-zero) and out-of-range samples are dropped, and the
    /// result is uniformly downsampled to at most <paramref name="maxPoints"/>
    /// points (always keeping the first and last fix) so the polyline stays
    /// light for the map. Returns <c>null</c> when no companion exists or the
    /// file can't be read, and an empty list when the flight never got a fix.
    /// </summary>
    /// <param name="videoPath">Absolute path to the video file (e.g. <c>...\DJI_..._D.MP4</c>).</param>
    /// <param name="maxPoints">Upper bound on the number of returned points.</param>
    Task<IReadOnlyList<GeoPoint>?> TryReadFlightPathAsync(
        string videoPath,
        int maxPoints = 600,
        CancellationToken cancellationToken = default);
}
