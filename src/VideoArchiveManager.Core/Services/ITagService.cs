using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Models.Enums;

namespace VideoArchiveManager.Core.Services;

public interface ITagService
{
    Task<IReadOnlyList<Tag>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Tag> GetOrCreateAsync(string name, TagType type, CancellationToken cancellationToken = default);

    Task AttachAsync(int videoItemId, int tagId, CancellationToken cancellationToken = default);

    Task DetachAsync(int videoItemId, int tagId, CancellationToken cancellationToken = default);

    Task BulkAttachAsync(IEnumerable<int> videoItemIds, int tagId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Tag>> GetTagsForVideoAsync(int videoItemId, CancellationToken cancellationToken = default);
}
