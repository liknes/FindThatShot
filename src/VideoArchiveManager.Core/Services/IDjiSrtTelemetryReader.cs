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

public interface IDjiSrtTelemetryReader
{
    /// <summary>
    /// If a sibling <c>.SRT</c> companion file exists next to the given video,
    /// parse it for DJI flight telemetry. Returns <c>null</c> when no companion
    /// is found or the file can't be read.
    /// </summary>
    /// <param name="videoPath">Absolute path to the video file (e.g. <c>...\DJI_..._D.MP4</c>).</param>
    Task<DjiTelemetrySummary?> TryReadAsync(string videoPath, CancellationToken cancellationToken = default);
}
