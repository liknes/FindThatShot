using Microsoft.EntityFrameworkCore;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Models.Enums;
using VideoArchiveManager.Core.Services;

namespace VideoArchiveManager.Data.Services;

public class TagService : ITagService
{
    private readonly IDbContextFactory<VideoArchiveDbContext> _contextFactory;

    public TagService(IDbContextFactory<VideoArchiveDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<Tag>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await ctx.Tags
            .AsNoTracking()
            .OrderBy(t => t.Type)
            .ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Tag>> GetMostUsedAsync(int count, CancellationToken cancellationToken = default)
    {
        if (count <= 0) return Array.Empty<Tag>();

        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Rank tag ids by attachment count, then materialise the matching Tag
        // rows and re-order them to the ranked id sequence (a Contains() filter
        // doesn't preserve order on its own).
        var rankedIds = await ctx.VideoTags
            .GroupBy(vt => vt.TagId)
            .Select(g => new { TagId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.TagId)
            .Take(count)
            .Select(x => x.TagId)
            .ToListAsync(cancellationToken);

        if (rankedIds.Count == 0) return Array.Empty<Tag>();

        var tags = await ctx.Tags
            .AsNoTracking()
            .Where(t => rankedIds.Contains(t.Id))
            .ToListAsync(cancellationToken);

        var byId = tags.ToDictionary(t => t.Id);
        return rankedIds
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .ToList();
    }

    public async Task<Tag> GetOrCreateAsync(string name, TagType type, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tag name is required", nameof(name));

        var normalized = name.Trim();
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await ctx.Tags
            .FirstOrDefaultAsync(t => t.Name == normalized && t.Type == type, cancellationToken);
        if (existing != null) return existing;

        var tag = new Tag { Name = normalized, Type = type };
        ctx.Tags.Add(tag);
        await ctx.SaveChangesAsync(cancellationToken);
        return tag;
    }

    public async Task AttachAsync(int videoItemId, int tagId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var exists = await ctx.VideoTags.AnyAsync(
            vt => vt.VideoItemId == videoItemId && vt.TagId == tagId, cancellationToken);
        if (exists) return;
        ctx.VideoTags.Add(new VideoTag { VideoItemId = videoItemId, TagId = tagId });
        await ctx.SaveChangesAsync(cancellationToken);
    }

    public async Task DetachAsync(int videoItemId, int tagId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await ctx.VideoTags.FirstOrDefaultAsync(
            vt => vt.VideoItemId == videoItemId && vt.TagId == tagId, cancellationToken);
        if (entity == null) return;
        ctx.VideoTags.Remove(entity);
        await ctx.SaveChangesAsync(cancellationToken);
    }

    public async Task BulkAttachAsync(IEnumerable<int> videoItemIds, int tagId, CancellationToken cancellationToken = default)
    {
        var ids = videoItemIds.Distinct().ToList();
        if (ids.Count == 0) return;

        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await ctx.VideoTags
            .Where(vt => vt.TagId == tagId && ids.Contains(vt.VideoItemId))
            .Select(vt => vt.VideoItemId)
            .ToListAsync(cancellationToken);

        var toAdd = ids.Except(existing)
            .Select(vid => new VideoTag { VideoItemId = vid, TagId = tagId })
            .ToList();

        if (toAdd.Count == 0) return;
        ctx.VideoTags.AddRange(toAdd);
        await ctx.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Tag>> GetTagsForVideoAsync(int videoItemId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await ctx.VideoTags
            .AsNoTracking()
            .Where(vt => vt.VideoItemId == videoItemId)
            .Select(vt => vt.Tag)
            .OrderBy(t => t.Type)
            .ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<VideoTag>> GetVideoTagsForVideoAsync(int videoItemId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await ctx.VideoTags
            .AsNoTracking()
            .Include(vt => vt.Tag)
            .Where(vt => vt.VideoItemId == videoItemId)
            .OrderBy(vt => vt.Tag.Type)
            .ThenBy(vt => vt.Tag.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task SetTagProminenceAsync(int videoItemId, int tagId, bool isBackground, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await ctx.VideoTags.FirstOrDefaultAsync(
            vt => vt.VideoItemId == videoItemId && vt.TagId == tagId, cancellationToken);
        if (entity == null || entity.IsBackground == isBackground) return;
        entity.IsBackground = isBackground;
        await ctx.SaveChangesAsync(cancellationToken);
    }
}
