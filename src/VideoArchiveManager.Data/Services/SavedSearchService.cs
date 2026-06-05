// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.
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
