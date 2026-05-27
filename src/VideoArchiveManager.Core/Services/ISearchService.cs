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
}
