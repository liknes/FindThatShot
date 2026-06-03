using VideoArchiveManager.Core.Models;

namespace VideoArchiveManager.Core.Services;

/// <summary>
/// Finds likely-duplicate clips in the catalog by metadata fingerprint
/// (exact file size + duration + resolution). Read-only: it only queries the
/// catalog database and never reads, hashes, moves, or modifies any source
/// video file. Acting on the results (removing redundant catalog entries) is
/// done through <see cref="IVideoLibraryService"/>, which only ever forgets
/// rows from the database — never deletes files on disk.
/// </summary>
public interface IDuplicateDetectionService
{
    Task<IReadOnlyList<DuplicateGroup>> FindDuplicatesAsync(CancellationToken cancellationToken = default);
}
