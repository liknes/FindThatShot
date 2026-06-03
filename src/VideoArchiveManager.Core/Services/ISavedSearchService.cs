using VideoArchiveManager.Core.Models;

namespace VideoArchiveManager.Core.Services;

public interface ISavedSearchService
{
    Task<IReadOnlyList<SavedSearch>> GetAllAsync(CancellationToken cancellationToken = default);

    // Upsert by name (case-insensitive): saving under an existing name
    // overwrites that saved search's criteria rather than creating a
    // duplicate, so re-saving "My best aerials" just updates it.
    Task<SavedSearch> SaveAsync(string name, SavedSearchCriteria criteria, CancellationToken cancellationToken = default);

    Task RenameAsync(int id, string newName, CancellationToken cancellationToken = default);

    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
