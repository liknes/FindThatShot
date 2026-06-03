using Microsoft.EntityFrameworkCore;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Services;

namespace VideoArchiveManager.Data.Services;

// Tier 1 duplicate detection: a pure metadata fingerprint over the catalog.
// Two clips with identical exact file size AND identical duration AND identical
// resolution are, for real footage, effectively certain to be the same file in
// two places (e.g. an internal copy + an external-drive backup). This is a
// read-only report — it never opens, hashes, moves, or modifies a source video
// file. Acting on the results goes through IVideoLibraryService, which only
// removes catalog rows, never files on disk.
public class DuplicateDetectionService : IDuplicateDetectionService
{
    private readonly IDbContextFactory<VideoArchiveDbContext> _contextFactory;

    public DuplicateDetectionService(IDbContextFactory<VideoArchiveDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<DuplicateGroup>> FindDuplicatesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Step 1 (DB-side, cheap): find the file sizes that occur more than once.
        // A zero/unknown size can't be fingerprinted reliably, so it's excluded.
        // Grouping on a single scalar column translates cleanly to SQLite and
        // narrows the catalog down to only the rows worth pulling into memory.
        var duplicateSizes = await ctx.VideoItems
            .AsNoTracking()
            .Where(v => v.FileSize > 0)
            .GroupBy(v => v.FileSize)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToListAsync(cancellationToken);

        if (duplicateSizes.Count == 0)
        {
            return Array.Empty<DuplicateGroup>();
        }

        var sizeSet = duplicateSizes.ToHashSet();

        // Step 2: pull a thin projection of just those candidate rows. Even on a
        // very large catalog this is a small slice (only size-colliding clips),
        // so the final fingerprint grouping is done in memory — both to keep the
        // exact-second duration rounding out of SQL (which doesn't translate
        // reliably) and to compute the "suggested keep" heuristic comfortably.
        var candidates = await ctx.VideoItems
            .AsNoTracking()
            .Where(v => v.FileSize > 0 && sizeSet.Contains(v.FileSize))
            .Select(v => new DuplicateVideo
            {
                Id = v.Id,
                FileName = v.FileName,
                FilePath = v.FilePath,
                FolderPath = v.FolderPath,
                FileSizeBytes = v.FileSize,
                DurationSeconds = v.DurationSeconds,
                Width = v.Width,
                Height = v.Height,
                Codec = v.Codec,
                Camera = v.Camera,
                Rating = v.Rating,
                TagCount = v.VideoTags.Count,
                FileExists = v.FileExists,
                ThumbnailPath = v.ThumbnailPath
            })
            .ToListAsync(cancellationToken);

        // Fingerprint = exact size + duration (rounded to the nearest second) +
        // resolution. Null duration buckets to -1 so unknown-duration clips only
        // ever group with each other, never with a clip that has a real length.
        var groups = candidates
            .GroupBy(v => new
            {
                v.FileSizeBytes,
                DurationBucket = v.DurationSeconds.HasValue
                    ? (long)Math.Round(v.DurationSeconds.Value)
                    : -1L,
                v.Width,
                v.Height
            })
            .Where(g => g.Count() > 1)
            .Select(g => new DuplicateGroup
            {
                FileSizeBytes = g.Key.FileSizeBytes,
                DurationSeconds = g.Key.DurationBucket >= 0 ? g.Key.DurationBucket : null,
                Width = g.Key.Width,
                Height = g.Key.Height,
                Videos = OrderAndFlagKeep(g.ToList())
            })
            // Biggest reclaimable savings first, then largest sets — that's the
            // order most useful for someone cleaning up an archive.
            .OrderByDescending(g => g.RedundantBytes)
            .ThenByDescending(g => g.Videos.Count)
            .ToList();

        return groups;
    }

    // Order a group's members and flag exactly one as the suggested copy to
    // keep. Preference: online over offline, then better-curated (higher
    // rating, more tags), then the oldest catalog id (likely the original
    // import). Purely advisory — the UI never auto-removes anything.
    private static IReadOnlyList<DuplicateVideo> OrderAndFlagKeep(List<DuplicateVideo> members)
    {
        var ordered = members
            .OrderByDescending(v => v.FileExists)
            .ThenByDescending(v => v.Rating)
            .ThenByDescending(v => v.TagCount)
            .ThenBy(v => v.Id)
            .ToList();

        if (ordered.Count > 0)
        {
            ordered[0].IsSuggestedKeep = true;
        }
        return ordered;
    }
}
