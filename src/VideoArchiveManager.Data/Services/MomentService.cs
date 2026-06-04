using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Services;

namespace VideoArchiveManager.Data.Services;

public class MomentService : IMomentService
{
    private readonly IDbContextFactory<VideoArchiveDbContext> _contextFactory;
    private readonly IThumbnailService _thumbnails;
    private readonly ILogger<MomentService> _logger;

    public MomentService(
        IDbContextFactory<VideoArchiveDbContext> contextFactory,
        IThumbnailService thumbnails,
        ILogger<MomentService> logger)
    {
        _contextFactory = contextFactory;
        _thumbnails = thumbnails;
        _logger = logger;
    }

    public async Task<IReadOnlyList<VideoMoment>> GetForVideoAsync(int videoItemId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await ctx.VideoMoments
            .AsNoTracking()
            .Include(m => m.MomentTags)
                .ThenInclude(mt => mt.Tag)
            .Where(m => m.VideoItemId == videoItemId)
            .OrderBy(m => m.StartSeconds)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountForVideoAsync(int videoItemId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await ctx.VideoMoments.CountAsync(m => m.VideoItemId == videoItemId, cancellationToken);
    }

    public async Task<VideoMoment> AddAsync(int videoItemId, double startSeconds, double? endSeconds, string? label, CancellationToken cancellationToken = default)
    {
        if (startSeconds < 0) startSeconds = 0;
        // Normalise a reversed range (out before in) by swapping, so the data is
        // always start <= end regardless of which order the user pressed I / O.
        if (endSeconds is double e && e < startSeconds)
        {
            (startSeconds, endSeconds) = (e, startSeconds);
        }

        string? sourcePath;
        VideoMoment moment;
        await using (var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken))
        {
            sourcePath = await ctx.VideoItems
                .Where(v => v.Id == videoItemId)
                .Select(v => v.FilePath)
                .FirstOrDefaultAsync(cancellationToken);

            moment = new VideoMoment
            {
                VideoItemId = videoItemId,
                StartSeconds = startSeconds,
                EndSeconds = endSeconds,
                Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            ctx.VideoMoments.Add(moment);
            await ctx.SaveChangesAsync(cancellationToken);
        }

        // Capture the thumbnail at the in-point now that we have the moment id.
        // Best-effort: a missing/offline file just leaves ThumbnailPath null.
        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            await TryGenerateThumbnailAsync(moment.Id, sourcePath, startSeconds, cancellationToken);
            // Reload the freshly-set ThumbnailPath onto the returned instance.
            await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var reloaded = await ctx.VideoMoments.AsNoTracking().FirstOrDefaultAsync(m => m.Id == moment.Id, cancellationToken);
            if (reloaded is not null) moment.ThumbnailPath = reloaded.ThumbnailPath;
        }

        return moment;
    }

    public async Task UpdateAsync(VideoMoment moment, bool regenerateThumbnail = false, CancellationToken cancellationToken = default)
    {
        string? sourcePath = null;
        double seek = 0;
        int momentId = moment.Id;

        await using (var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken))
        {
            var entity = await ctx.VideoMoments.FirstOrDefaultAsync(m => m.Id == moment.Id, cancellationToken);
            if (entity is null) return;

            var start = moment.StartSeconds < 0 ? 0 : moment.StartSeconds;
            var end = moment.EndSeconds;
            if (end is double e && e < start)
            {
                (start, end) = (e, start);
            }

            entity.StartSeconds = start;
            entity.EndSeconds = end;
            entity.Label = string.IsNullOrWhiteSpace(moment.Label) ? null : moment.Label.Trim();
            entity.Notes = moment.Notes;
            entity.Rating = moment.Rating;
            entity.UpdatedAt = DateTime.UtcNow;
            await ctx.SaveChangesAsync(cancellationToken);

            seek = start;

            if (regenerateThumbnail)
            {
                sourcePath = await ctx.VideoItems
                    .Where(v => v.Id == entity.VideoItemId)
                    .Select(v => v.FilePath)
                    .FirstOrDefaultAsync(cancellationToken);
            }
        }

        if (regenerateThumbnail && !string.IsNullOrWhiteSpace(sourcePath))
        {
            await TryGenerateThumbnailAsync(momentId, sourcePath, seek, cancellationToken);
        }
    }

    public async Task DeleteAsync(int momentId, CancellationToken cancellationToken = default)
    {
        await using (var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken))
        {
            var entity = await ctx.VideoMoments.FirstOrDefaultAsync(m => m.Id == momentId, cancellationToken);
            if (entity is null) return;
            ctx.VideoMoments.Remove(entity);
            await ctx.SaveChangesAsync(cancellationToken);
        }

        // Drop the cached thumbnail too (catalog-only cleanup; never the source).
        _thumbnails.DeleteForMoments(new[] { momentId });
    }

    public async Task AttachTagAsync(int momentId, int tagId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var exists = await ctx.MomentTags.AnyAsync(
            mt => mt.VideoMomentId == momentId && mt.TagId == tagId, cancellationToken);
        if (exists) return;
        ctx.MomentTags.Add(new MomentTag { VideoMomentId = momentId, TagId = tagId });
        await ctx.SaveChangesAsync(cancellationToken);
    }

    public async Task DetachTagAsync(int momentId, int tagId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await ctx.MomentTags.FirstOrDefaultAsync(
            mt => mt.VideoMomentId == momentId && mt.TagId == tagId, cancellationToken);
        if (entity is null) return;
        ctx.MomentTags.Remove(entity);
        await ctx.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Tag>> GetTagsForMomentAsync(int momentId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await ctx.MomentTags
            .AsNoTracking()
            .Where(mt => mt.VideoMomentId == momentId)
            .Select(mt => mt.Tag)
            .OrderBy(t => t.Type)
            .ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<MomentSearchResult> SearchAsync(MomentSearchQuery query, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<VideoMoment> q = ctx.VideoMoments
            .AsNoTracking()
            .Include(m => m.VideoItem)
            .Include(m => m.MomentTags)
                .ThenInclude(mt => mt.Tag);

        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var tokens = query.Text
                .Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToArray();

            foreach (var token in tokens)
            {
                var like = $"%{token}%";
                q = q.Where(m =>
                    (m.Label != null && EF.Functions.Like(m.Label, like)) ||
                    (m.Notes != null && EF.Functions.Like(m.Notes, like)) ||
                    EF.Functions.Like(m.VideoItem.FileName, like) ||
                    EF.Functions.Like(m.VideoItem.FolderPath, like) ||
                    m.MomentTags.Any(mt => EF.Functions.Like(mt.Tag.Name, like)));
            }
        }

        if (query.MinRating is > 0)
        {
            q = q.Where(m => m.Rating >= query.MinRating.Value);
        }

        if (query.TagIds is { Count: > 0 })
        {
            foreach (var tagId in query.TagIds.ToArray())
            {
                q = q.Where(m => m.MomentTags.Any(mt => mt.TagId == tagId));
            }
        }

        if (query.FileExists.HasValue)
        {
            var fe = query.FileExists.Value;
            q = q.Where(m => m.VideoItem.FileExists == fe);
        }

        var total = await q.CountAsync(cancellationToken);

        q = q.OrderByDescending(m => m.UpdatedAt);
        if (query.Take > 0) q = q.Take(query.Take);

        var moments = await q.ToListAsync(cancellationToken);
        return new MomentSearchResult { Moments = moments, TotalCount = total };
    }

    private async Task TryGenerateThumbnailAsync(int momentId, string sourcePath, double seekSeconds, CancellationToken cancellationToken)
    {
        try
        {
            var path = await _thumbnails.GenerateAtAsync(momentId, sourcePath, seekSeconds, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(path)) return;

            await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var entity = await ctx.VideoMoments.FirstOrDefaultAsync(m => m.Id == momentId, cancellationToken);
            if (entity is null) return;
            entity.ThumbnailPath = path;
            entity.UpdatedAt = DateTime.UtcNow;
            await ctx.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate thumbnail for moment {Id}", momentId);
        }
    }
}
