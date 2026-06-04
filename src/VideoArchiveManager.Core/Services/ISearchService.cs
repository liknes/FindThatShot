using VideoArchiveManager.Core.Models;

namespace VideoArchiveManager.Core.Services;

public class SearchResult
{
    public IReadOnlyList<VideoItem> Items { get; init; } = Array.Empty<VideoItem>();
    public int TotalCount { get; init; }
}

public interface ISearchService
{
    Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetDistinctCamerasAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every geotagged clip as a lightweight <see cref="MapClipPoint"/>
    /// for the global map browse view. When <paramref name="filter"/> is null
    /// the whole archive is returned; otherwise the same filter pipeline as
    /// <see cref="SearchAsync"/> is applied (so the map can mirror the current
    /// grid filters). Read only — no source file is touched.
    /// </summary>
    Task<IReadOnlyList<MapClipPoint>> GetGeotaggedClipsAsync(SearchQuery? filter = null, CancellationToken cancellationToken = default);
}
