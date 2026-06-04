namespace VideoArchiveManager.Core.Services;

/// <summary>
/// Lightweight projection of a geotagged clip for the global map browse view.
/// Carries only what the map and its side preview panel need (coordinates,
/// identity, thumbnail, online state) so plotting thousands of points doesn't
/// drag the full <see cref="Models.VideoItem"/> graph (tags, moments) into
/// memory. The catalog is read only — no source file is touched to build it.
/// </summary>
public sealed class MapClipPoint
{
    public int Id { get; init; }

    public double Latitude { get; init; }

    public double Longitude { get; init; }

    public string FileName { get; init; } = string.Empty;

    public string FolderPath { get; init; } = string.Empty;

    public string? LocationText { get; init; }

    public string? ThumbnailPath { get; init; }

    public bool FileExists { get; init; }

    public int Rating { get; init; }
}
