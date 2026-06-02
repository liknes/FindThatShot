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

/// <summary>
/// One telemetry frame lifted from a DJI flight-data SRT cue, tagged with the
/// cue's playback time window so the in-app player can show the live readout
/// that matches the current position. Every camera / GPS field is nullable
/// because DJI's SRT layout varies by drone / firmware — a missing bracket
/// simply leaves that field <c>null</c> and the UI hides that chip.
/// </summary>
/// <param name="Start">Cue start time, relative to the clip's start.</param>
/// <param name="End">Cue end time; the sample is "current" for [Start, End).</param>
public readonly record struct DjiTelemetrySample(
    TimeSpan Start,
    TimeSpan End,
    int? Iso = null,
    string? Shutter = null,
    double? FNumber = null,
    double? Ev = null,
    double? FocalLength = null,
    double? RelativeAltitude = null,
    double? AbsoluteAltitude = null,
    double? Latitude = null,
    double? Longitude = null);

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

    /// <summary>
    /// If a sibling <c>.SRT</c> companion file exists next to the given video,
    /// parse every cue into a time-ordered telemetry track (camera + GPS fields
    /// tagged with each cue's playback window) for the in-app player's live
    /// readout overlay. Unlike <see cref="TryReadFlightPathAsync"/> this keeps
    /// every cue (no downsampling) so the readout can track the exact frame, but
    /// it caps the total to <paramref name="maxSamples"/> as a guard against a
    /// pathologically long file. Returns <c>null</c> when no companion exists,
    /// the file can't be read, or it carried no parseable telemetry.
    /// </summary>
    /// <param name="videoPath">Absolute path to the video file (e.g. <c>...\DJI_..._D.MP4</c>).</param>
    /// <param name="maxSamples">Upper bound on the number of retained samples.</param>
    Task<IReadOnlyList<DjiTelemetrySample>?> TryReadTelemetryTrackAsync(
        string videoPath,
        int maxSamples = 200_000,
        CancellationToken cancellationToken = default);
}
