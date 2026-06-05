using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Models.Enums;

namespace VideoArchiveManager.Core.Services;

public interface ITagService
{
    Task<IReadOnlyList<Tag>> GetAllAsync(CancellationToken cancellationToken = default);

    // Returns up to <paramref name="count"/> tags ordered by how many videos
    // carry them (most-used first). Used to bootstrap the review-mode pinned
    // tag hotkeys from the user's existing vocabulary. Tags with no usage are
    // excluded.
    Task<IReadOnlyList<Tag>> GetMostUsedAsync(int count, CancellationToken cancellationToken = default);

    Task<Tag> GetOrCreateAsync(string name, TagType type, CancellationToken cancellationToken = default);

    Task AttachAsync(int videoItemId, int tagId, CancellationToken cancellationToken = default);

    Task DetachAsync(int videoItemId, int tagId, CancellationToken cancellationToken = default);

    Task BulkAttachAsync(IEnumerable<int> videoItemIds, int tagId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Tag>> GetTagsForVideoAsync(int videoItemId, CancellationToken cancellationToken = default);

    // Like GetTagsForVideoAsync but returns the join rows so callers can see
    // each tag's per-clip prominence (VideoTag.IsBackground), with Tag loaded.
    Task<IReadOnlyList<VideoTag>> GetVideoTagsForVideoAsync(int videoItemId, CancellationToken cancellationToken = default);

    // Sets whether a tag is "background" (incidental) on a specific clip.
    // No-op if the tag isn't attached.
    Task SetTagProminenceAsync(int videoItemId, int tagId, bool isBackground, CancellationToken cancellationToken = default);
}
