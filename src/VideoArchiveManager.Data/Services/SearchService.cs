using Microsoft.EntityFrameworkCore;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Models.Enums;
using VideoArchiveManager.Core.Services;

namespace VideoArchiveManager.Data.Services;

public class SearchService : ISearchService
{
    private readonly IDbContextFactory<VideoArchiveDbContext> _contextFactory;

    public SearchService(IDbContextFactory<VideoArchiveDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<VideoItem> q = ctx.VideoItems
            .AsNoTracking()
            .Include(v => v.VideoTags)
                .ThenInclude(vt => vt.Tag)
            .Include(v => v.Moments);

        q = ApplyFilters(q, query);

        var total = await q.CountAsync(cancellationToken);

        q = query.SortBy switch
        {
            SearchSortField.ModifiedDescending => q.OrderByDescending(v => v.ModifiedAtFile),
            SearchSortField.ModifiedAscending => q.OrderBy(v => v.ModifiedAtFile),
            SearchSortField.FileNameAscending => q.OrderBy(v => v.FileName),
            SearchSortField.FileNameDescending => q.OrderByDescending(v => v.FileName),
            SearchSortField.RatingDescending => q.OrderByDescending(v => v.Rating).ThenByDescending(v => v.ModifiedAtFile),
            SearchSortField.FolderDateDescending => q.OrderByDescending(v => v.FolderDate).ThenByDescending(v => v.ModifiedAtFile),
            _ => q.OrderByDescending(v => v.ModifiedAtFile)
        };

        if (query.Skip > 0) q = q.Skip(query.Skip);
        if (query.Take > 0) q = q.Take(query.Take);

        var items = await q.ToListAsync(cancellationToken);
        return new SearchResult { Items = items, TotalCount = total };
    }

    // Shared WHERE-clause pipeline used by both the grid search and the global
    // map projection so the two stay perfectly in sync (a clip the grid would
    // show under a filter is exactly a clip the map plots under that filter).
    private static IQueryable<VideoItem> ApplyFilters(IQueryable<VideoItem> q, SearchQuery query)
    {
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
                q = q.Where(v =>
                    EF.Functions.Like(v.FileName, like) ||
                    EF.Functions.Like(v.FolderPath, like) ||
                    (v.LocationText != null && EF.Functions.Like(v.LocationText, like)) ||
                    (v.ContextText != null && EF.Functions.Like(v.ContextText, like)) ||
                    (v.Notes != null && EF.Functions.Like(v.Notes, like)) ||
                    (v.Camera != null && EF.Functions.Like(v.Camera, like)) ||
                    (v.Codec != null && EF.Functions.Like(v.Codec, like)) ||
                    v.VideoTags.Any(vt => EF.Functions.Like(vt.Tag.Name, like)));
            }
        }

        if (query.Status.HasValue)
        {
            q = q.Where(v => v.Status == query.Status.Value);
        }

        if (query.MinRating is > 0)
        {
            q = q.Where(v => v.Rating >= query.MinRating.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Camera))
        {
            q = q.Where(v => v.Camera == query.Camera);
        }

        if (query.TagIds is { Count: > 0 })
        {
            var tagIds = query.TagIds.ToArray();
            foreach (var tagId in tagIds)
            {
                q = q.Where(v => v.VideoTags.Any(vt => vt.TagId == tagId));
            }
        }

        if (query.DateFrom.HasValue)
        {
            var from = query.DateFrom.Value;
            q = q.Where(v =>
                (v.FolderDate != null && v.FolderDate >= from) ||
                v.ModifiedAtFile >= from);
        }

        if (query.DateTo.HasValue)
        {
            var to = query.DateTo.Value;
            q = q.Where(v =>
                (v.FolderDate != null && v.FolderDate <= to) ||
                v.ModifiedAtFile <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.RootFolderPath))
        {
            var prefix = query.RootFolderPath;
            q = q.Where(v => v.FilePath.StartsWith(prefix));
        }

        if (query.FileExists.HasValue)
        {
            var fe = query.FileExists.Value;
            q = q.Where(v => v.FileExists == fe);
        }

        if (query.VideoIds is { Count: > 0 })
        {
            var ids = query.VideoIds as IReadOnlyCollection<int> ?? query.VideoIds.ToArray();
            q = q.Where(v => ids.Contains(v.Id));
        }

        // Union semantics: a video is "unreviewed" if EITHER the user has
        // never bumped its status off the default, OR they've never tagged
        // it. Catches forgetful workflows (status untouched) and behavioural
        // workflows (no tags yet) without requiring the user to remember
        // both gestures.
        if (query.OnlyUnreviewed == true)
        {
            q = q.Where(v => v.Status == VideoStatus.Unreviewed || !v.VideoTags.Any());
        }

        return q;
    }

    public async Task<IReadOnlyList<MapClipPoint>> GetGeotaggedClipsAsync(
        SearchQuery? filter = null,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // No tag/moment includes here: the projection below pulls only the
        // handful of scalar columns the map + side panel need, so plotting
        // thousands of points stays cheap.
        IQueryable<VideoItem> q = ctx.VideoItems.AsNoTracking();

        if (filter is not null)
        {
            q = ApplyFilters(q, filter);
        }

        return await q
            .Where(v => v.GpsLatitude != null && v.GpsLongitude != null)
            .Select(v => new MapClipPoint
            {
                Id = v.Id,
                Latitude = v.GpsLatitude!.Value,
                Longitude = v.GpsLongitude!.Value,
                FileName = v.FileName,
                FolderPath = v.FolderPath,
                LocationText = v.LocationText,
                ThumbnailPath = v.ThumbnailPath,
                FileExists = v.FileExists,
                Rating = v.Rating
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetDistinctCamerasAsync(CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await ctx.VideoItems
            .AsNoTracking()
            .Where(v => v.Camera != null && v.Camera != "")
            .Select(v => v.Camera!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(cancellationToken);
    }
}
