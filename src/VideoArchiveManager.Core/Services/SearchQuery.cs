using VideoArchiveManager.Core.Models.Enums;

namespace VideoArchiveManager.Core.Services;

public class SearchQuery
{
    public string? Text { get; set; }
    public VideoStatus? Status { get; set; }
    public int? MinRating { get; set; }
    public string? Camera { get; set; }
    public IReadOnlyCollection<int>? TagIds { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string? RootFolderPath { get; set; }
    public bool? FileExists { get; set; }

    public int Skip { get; set; }
    public int Take { get; set; } = 200;

    public SearchSortField SortBy { get; set; } = SearchSortField.ModifiedDescending;
}

public enum SearchSortField
{
    ModifiedDescending,
    ModifiedAscending,
    FileNameAscending,
    FileNameDescending,
    RatingDescending,
    FolderDateDescending
}
