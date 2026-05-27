namespace VideoArchiveManager.Core.Services;

public class FfprobeResult
{
    public double? DurationSeconds { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public double? FrameRate { get; init; }
    public string? Codec { get; init; }
    public string? Camera { get; init; }
    public double? GpsLatitude { get; init; }
    public double? GpsLongitude { get; init; }
}
