namespace VideoArchiveManager.Core.Services;

// SAFETY CONTRACT (do not relax):
//
//   * Source video files (anything under user-configured root folders or any
//     path that did not originate from this app) MUST NEVER be deleted, moved,
//     renamed, or altered in any way. "Removing" a video here means forgetting
//     it from the catalog database, not touching the file on disk.
//
//   * App-generated thumbnail cache files inside the configured thumbnail
//     directory ARE allowed to be cleaned up when their video record is
//     removed. The thumbnail service is responsible for verifying any file it
//     deletes resolves inside that cache directory.
public interface IVideoLibraryService
{
    Task<int> RemoveByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    Task<int> RemoveOfflineAsync(CancellationToken cancellationToken = default);

    Task<int> CountUnderRootAsync(string rootPath, CancellationToken cancellationToken = default);

    Task<int> RemoveUnderRootAsync(string rootPath, CancellationToken cancellationToken = default);
}
