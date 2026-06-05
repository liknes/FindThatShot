using VideoArchiveManager.Core.Models.Enums;

namespace VideoArchiveManager.Core.Services.Ai;

// A pending AI tag suggestion projected for the review queue, carrying enough
// of the parent clip to render a card without a second query.
public class AiSuggestionItem
{
    public int SuggestionId { get; init; }
    public int VideoItemId { get; init; }
    public string TagName { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public double? BestFrameSeconds { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string FolderPath { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string? ThumbnailPath { get; init; }
    public bool FileExists { get; init; }
}

// One clip's worth of pending suggestions for the grouped review UI.
public class AiSuggestionGroup
{
    public int VideoItemId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string FolderPath { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string? ThumbnailPath { get; init; }
    public bool FileExists { get; init; }
    public IReadOnlyList<AiSuggestionItem> Suggestions { get; init; } = Array.Empty<AiSuggestionItem>();
}

public interface IAiSuggestionService
{
    Task<int> CountPendingAsync(CancellationToken cancellationToken = default);

    // Pending suggestions grouped by clip, highest-confidence clips first.
    Task<IReadOnlyList<AiSuggestionGroup>> GetPendingGroupedAsync(int maxClips, CancellationToken cancellationToken = default);

    // Accepts a suggestion: gets-or-creates the real Tag, links it to the clip,
    // and marks the suggestion Accepted. Returns the created/linked tag id.
    Task<int> AcceptAsync(int suggestionId, TagType tagType, CancellationToken cancellationToken = default);

    Task RejectAsync(int suggestionId, CancellationToken cancellationToken = default);

    // Accept / reject every pending suggestion for a clip in one go.
    Task AcceptAllForClipAsync(int videoItemId, TagType tagType, CancellationToken cancellationToken = default);

    Task RejectAllForClipAsync(int videoItemId, CancellationToken cancellationToken = default);
}
