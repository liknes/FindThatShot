using VideoArchiveManager.Core.Models;

namespace VideoArchiveManager.Core.Services;

// Criteria for searching across moments (sub-clips) catalog-wide. Mirrors the
// shape of the whole-clip SearchQuery but scoped to the fields a moment owns.
public class MomentSearchQuery
{
    public string? Text { get; set; }
    public int? MinRating { get; set; }
    public IReadOnlyCollection<int>? TagIds { get; set; }
    public bool? FileExists { get; set; }
    public int Take { get; set; } = 500;
}

public class MomentSearchResult
{
    public IReadOnlyList<VideoMoment> Moments { get; init; } = Array.Empty<VideoMoment>();
    public int TotalCount { get; init; }
}

// CRUD + search for timestamped moments. Tags reuse the global Tag vocabulary
// via the MomentTag join. Thumbnails are captured at the moment's in-point
// through IThumbnailService and never touch the source file.
public interface IMomentService
{
    Task<IReadOnlyList<VideoMoment>> GetForVideoAsync(int videoItemId, CancellationToken cancellationToken = default);

    // Creates a moment, then best-effort captures its thumbnail at StartSeconds.
    // The returned entity has its Id (and ThumbnailPath if capture succeeded).
    Task<VideoMoment> AddAsync(int videoItemId, double startSeconds, double? endSeconds, string? label, CancellationToken cancellationToken = default);

    // Persists label / notes / rating / in-out edits. Regenerates the thumbnail
    // only when the in-point moved (regenerateThumbnail = true).
    Task UpdateAsync(VideoMoment moment, bool regenerateThumbnail = false, CancellationToken cancellationToken = default);

    Task DeleteAsync(int momentId, CancellationToken cancellationToken = default);

    Task AttachTagAsync(int momentId, int tagId, CancellationToken cancellationToken = default);

    Task DetachTagAsync(int momentId, int tagId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Tag>> GetTagsForMomentAsync(int momentId, CancellationToken cancellationToken = default);

    Task<MomentSearchResult> SearchAsync(MomentSearchQuery query, CancellationToken cancellationToken = default);

    // Total number of moments attached to a clip (for the catalog card badge).
    Task<int> CountForVideoAsync(int videoItemId, CancellationToken cancellationToken = default);
}
