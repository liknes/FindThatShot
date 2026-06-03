namespace VideoArchiveManager.Core.Models;

/// <summary>
/// A read-only snapshot of aggregate catalog metrics, computed on demand for
/// the statistics dashboard. Every value is derived purely from the catalog
/// database — no source video files are read or touched to build it.
/// </summary>
public class CatalogStatistics
{
    public int TotalClips { get; init; }
    public int OnlineClips { get; init; }
    public int OfflineClips { get; init; }

    public long TotalSizeBytes { get; init; }
    public double TotalDurationSeconds { get; init; }

    public int RootFolderCount { get; init; }
    public int DistinctFolders { get; init; }
    public int DistinctCameras { get; init; }
    public int TotalTags { get; init; }

    // Curation progress signals.
    public int UnreviewedClips { get; init; }
    public int TaggedClips { get; init; }
    public int RatedClips { get; init; }
    public int GeotaggedClips { get; init; }

    public IReadOnlyList<StatCount> ByStatus { get; init; } = Array.Empty<StatCount>();
    public IReadOnlyList<StatCount> ByRating { get; init; } = Array.Empty<StatCount>();
    public IReadOnlyList<StatCount> ByResolution { get; init; } = Array.Empty<StatCount>();
    public IReadOnlyList<StatCount> ByYear { get; init; } = Array.Empty<StatCount>();
    public IReadOnlyList<StatCount> TopCameras { get; init; } = Array.Empty<StatCount>();
    public IReadOnlyList<StatCount> TopCodecs { get; init; } = Array.Empty<StatCount>();
    public IReadOnlyList<StatCount> TopTags { get; init; } = Array.Empty<StatCount>();
}

/// <summary>
/// A single labelled bucket in a breakdown (e.g. one status, one camera, one
/// year). <see cref="Count"/> is the number of clips in that bucket.
/// </summary>
public class StatCount
{
    public StatCount(string label, int count)
    {
        Label = label;
        Count = count;
    }

    public string Label { get; }
    public int Count { get; }
}
