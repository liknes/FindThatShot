using Microsoft.EntityFrameworkCore;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Services;

namespace VideoArchiveManager.Data.Services;

public class SavedSearchService : ISavedSearchService
{
    private readonly IDbContextFactory<VideoArchiveDbContext> _contextFactory;

    public SavedSearchService(IDbContextFactory<VideoArchiveDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<SavedSearch>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await ctx.SavedSearches
            .AsNoTracking()
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<SavedSearch> SaveAsync(string name, SavedSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("A saved search needs a name.", nameof(name));
        }

        var json = criteria.Serialize();

        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Upsert by name (case-insensitive) so re-saving under an existing
        // name updates that entry instead of tripping the unique index.
        var existing = await ctx.SavedSearches
            .FirstOrDefaultAsync(s => s.Name.ToLower() == trimmed.ToLower(), cancellationToken);

        if (existing is not null)
        {
            existing.CriteriaJson = json;
            await ctx.SaveChangesAsync(cancellationToken);
            return existing;
        }

        var maxSort = await ctx.SavedSearches.AnyAsync(cancellationToken)
            ? await ctx.SavedSearches.MaxAsync(s => s.SortOrder, cancellationToken)
            : 0;

        var entity = new SavedSearch
        {
            Name = trimmed,
            CriteriaJson = json,
            CreatedAt = DateTime.UtcNow,
            SortOrder = maxSort + 1
        };
        ctx.SavedSearches.Add(entity);
        await ctx.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task RenameAsync(int id, string newName, CancellationToken cancellationToken = default)
    {
        var trimmed = (newName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("A saved search needs a name.", nameof(newName));
        }

        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await ctx.SavedSearches.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (entity is null) return;

        entity.Name = trimmed;
        await ctx.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await ctx.SavedSearches.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (entity is null) return;

        ctx.SavedSearches.Remove(entity);
        await ctx.SaveChangesAsync(cancellationToken);
    }
}
