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

    /// <summary>
    /// Aggregates clip counts per calendar month by effective shoot date
    /// (<c>FolderDate ?? ModifiedAtFile</c>) for the calendar browse heatmap.
    /// When <paramref name="filter"/> is null the whole archive is counted;
    /// otherwise the same filter pipeline as <see cref="SearchAsync"/> is applied
    /// (so the heatmap can mirror the current grid filters). Read only.
    /// </summary>
    Task<IReadOnlyList<MonthShootCount>> GetShootMonthCountsAsync(SearchQuery? filter = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the clips whose effective shoot date
    /// (<c>FolderDate ?? ModifiedAtFile</c>) falls in the given month as
    /// lightweight <see cref="CalendarClip"/> projections, ordered by that date.
    /// Honours the same optional <paramref name="filter"/> as the heatmap so the
    /// list matches a clicked cell exactly. Read only.
    /// </summary>
    Task<IReadOnlyList<CalendarClip>> GetClipsInMonthAsync(int year, int month, SearchQuery? filter = null, CancellationToken cancellationToken = default);
}
