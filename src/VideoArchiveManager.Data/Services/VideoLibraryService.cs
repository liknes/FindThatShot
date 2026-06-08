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
using Microsoft.Extensions.Logging;
using VideoArchiveManager.Core.Services;

namespace VideoArchiveManager.Data.Services;

// SAFETY CONTRACT (do not relax):
//
//   * Source video files MUST NEVER be deleted, moved, renamed, or altered.
//     This service is allowed to modify rows in the database and to ask the
//     thumbnail service to clean up its own cache files. It must NEVER call
//     File.Delete / Directory.Delete / File.Move on a path that points to a
//     user video file. The thumbnail service is the only component that
//     touches files on disk here, and it constrains itself to its own cache
//     directory.
public class VideoLibraryService : IVideoLibraryService
{
    private readonly IDbContextFactory<VideoArchiveDbContext> _contextFactory;
    private readonly IThumbnailService _thumbnails;
    private readonly ICatalogBackupService _backup;
    private readonly ILogger<VideoLibraryService> _logger;

    public VideoLibraryService(
        IDbContextFactory<VideoArchiveDbContext> contextFactory,
        IThumbnailService thumbnails,
        ICatalogBackupService backup,
        ILogger<VideoLibraryService> logger)
    {
        _contextFactory = contextFactory;
        _thumbnails = thumbnails;
        _backup = backup;
        _logger = logger;
    }

    // Best-effort safety net: snapshot the catalog before any operation that
    // bulk-deletes user curation, so a mistaken removal is always recoverable
    // from a backup that is at most one destructive action old. Never throws —
    // a backup failure (e.g. read-only disk) must not block the user's action,
    // it just means this particular removal isn't covered by a fresh snapshot.
    private async Task TrySafetyBackupAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _backup.BackupNowAsync(cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                _logger.LogWarning(
                    "Pre-removal safety backup did not run: {Error}", result.ErrorMessage);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pre-removal safety backup failed; proceeding with removal.");
        }
    }

    public async Task<int> RemoveByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
    {
        if (ids is null || ids.Count == 0) return 0;

        await TrySafetyBackupAsync(cancellationToken);

        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var momentIds = await CollectMomentIdsAsync(ctx, ids, cancellationToken);

        // VideoTags, AiTagSuggestions, and VideoMoments cascade on VideoItem
        // delete (see EF configurations).
        var deleted = await ctx.VideoItems
            .Where(v => ids.Contains(v.Id))
            .ExecuteDeleteAsync(cancellationToken);

        _thumbnails.DeleteForVideos(ids);
        _thumbnails.DeleteForMoments(momentIds);
        return deleted;
    }

    public async Task<int> RemoveOfflineAsync(CancellationToken cancellationToken = default)
    {
        await TrySafetyBackupAsync(cancellationToken);

        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var ids = await ctx.VideoItems
            .Where(v => !v.FileExists)
            .Select(v => v.Id)
            .ToListAsync(cancellationToken);
        if (ids.Count == 0) return 0;

        var momentIds = await CollectMomentIdsAsync(ctx, ids, cancellationToken);

        var deleted = await ctx.VideoItems
            .Where(v => ids.Contains(v.Id))
            .ExecuteDeleteAsync(cancellationToken);

        _thumbnails.DeleteForVideos(ids);
        _thumbnails.DeleteForMoments(momentIds);
        return deleted;
    }

    public async Task<int> CountUnderRootAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        var prefix = NormalizePrefix(rootPath);
        if (prefix is null) return 0;

        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await ctx.VideoItems
            .CountAsync(v => v.FilePath.StartsWith(prefix), cancellationToken);
    }

    public async Task<int> RemoveUnderRootAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        var prefix = NormalizePrefix(rootPath);
        if (prefix is null) return 0;

        await TrySafetyBackupAsync(cancellationToken);

        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var ids = await ctx.VideoItems
            .Where(v => v.FilePath.StartsWith(prefix))
            .Select(v => v.Id)
            .ToListAsync(cancellationToken);
        if (ids.Count == 0) return 0;

        var momentIds = await CollectMomentIdsAsync(ctx, ids, cancellationToken);

        var deleted = await ctx.VideoItems
            .Where(v => ids.Contains(v.Id))
            .ExecuteDeleteAsync(cancellationToken);

        _thumbnails.DeleteForVideos(ids);
        _thumbnails.DeleteForMoments(momentIds);
        return deleted;
    }

    public async Task<int> RemoveRootFolderAsync(int rootFolderId, string rootPath, CancellationToken cancellationToken = default)
    {
        await TrySafetyBackupAsync(cancellationToken);

        var prefix = NormalizePrefix(rootPath);

        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await ctx.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            IReadOnlyCollection<int> ids = prefix is null
                ? Array.Empty<int>()
                : await ctx.VideoItems
                    .Where(v => v.FilePath.StartsWith(prefix))
                    .Select(v => v.Id)
                    .ToListAsync(cancellationToken);

            var momentIds = ids.Count > 0
                ? await CollectMomentIdsAsync(ctx, ids, cancellationToken)
                : Array.Empty<int>();

            var deleted = ids.Count > 0
                ? await ctx.VideoItems
                    .Where(v => ids.Contains(v.Id))
                    .ExecuteDeleteAsync(cancellationToken)
                : 0;

            // Remove the root folder row in the SAME transaction so the two
            // deletions commit (or roll back) as one unit.
            var rootEntity = await ctx.RootFolders
                .FirstOrDefaultAsync(r => r.Id == rootFolderId, cancellationToken);
            if (rootEntity is not null)
            {
                ctx.RootFolders.Remove(rootEntity);
                await ctx.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            // Thumbnail cache cleanup is deferred until AFTER the commit: it
            // only touches app-generated cache files, and doing it post-commit
            // means a rollback never leaves us with deleted thumbnails for rows
            // that still exist.
            if (ids.Count > 0)
            {
                _thumbnails.DeleteForVideos(ids);
                _thumbnails.DeleteForMoments(momentIds);
            }

            return deleted;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    // Collect moment ids for the videos about to be deleted so their cached
    // thumbnails can be pruned. The DB cascades the moment rows themselves; we
    // just need their ids beforehand to clean up the matching cache files.
    private static async Task<IReadOnlyCollection<int>> CollectMomentIdsAsync(
        VideoArchiveDbContext ctx,
        IReadOnlyCollection<int> videoIds,
        CancellationToken cancellationToken)
    {
        return await ctx.VideoMoments
            .Where(m => videoIds.Contains(m.VideoItemId))
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);
    }

    // Build a directory prefix that always ends with a separator so we don't
    // match siblings like "C:\Photos2" when removing "C:\Photos".
    private static string? NormalizePrefix(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath)) return null;

        var trimmed = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmed.Length == 0) return null;

        return trimmed + Path.DirectorySeparatorChar;
    }
}
