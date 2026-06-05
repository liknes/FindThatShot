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

        var tagIds = query.TagIds?.ToArray() ?? Array.Empty<int>();
        q = ApplySort(q, query.SortBy, tagIds);

        if (query.Skip > 0) q = q.Skip(query.Skip);
        if (query.Take > 0) q = q.Take(query.Take);

        var items = await q.ToListAsync(cancellationToken);
        return new SearchResult { Items = items, TotalCount = total };
    }

    // Applies the user's chosen sort. When a tag filter is active, a
    // primary-first key is prepended so clips where a matched tag is the main
    // subject rank above clips where it's only incidental/background; the
    // user's SortBy then acts as the tiebreaker.
    private static IQueryable<VideoItem> ApplySort(IQueryable<VideoItem> q, SearchSortField sortBy, int[] tagIds)
    {
        if (tagIds.Length > 0)
        {
            var primaryFirst = q.OrderByDescending(
                v => v.VideoTags.Any(vt => tagIds.Contains(vt.TagId) && !vt.IsBackground));
            return sortBy switch
            {
                SearchSortField.ModifiedDescending => primaryFirst.ThenByDescending(v => v.ModifiedAtFile),
                SearchSortField.ModifiedAscending => primaryFirst.ThenBy(v => v.ModifiedAtFile),
                SearchSortField.FileNameAscending => primaryFirst.ThenBy(v => v.FileName),
                SearchSortField.FileNameDescending => primaryFirst.ThenByDescending(v => v.FileName),
                SearchSortField.RatingDescending => primaryFirst.ThenByDescending(v => v.Rating).ThenByDescending(v => v.ModifiedAtFile),
                SearchSortField.FolderDateDescending => primaryFirst.ThenByDescending(v => v.FolderDate).ThenByDescending(v => v.ModifiedAtFile),
                _ => primaryFirst.ThenByDescending(v => v.ModifiedAtFile)
            };
        }

        return sortBy switch
        {
            SearchSortField.ModifiedDescending => q.OrderByDescending(v => v.ModifiedAtFile),
            SearchSortField.ModifiedAscending => q.OrderBy(v => v.ModifiedAtFile),
            SearchSortField.FileNameAscending => q.OrderBy(v => v.FileName),
            SearchSortField.FileNameDescending => q.OrderByDescending(v => v.FileName),
            SearchSortField.RatingDescending => q.OrderByDescending(v => v.Rating).ThenByDescending(v => v.ModifiedAtFile),
            SearchSortField.FolderDateDescending => q.OrderByDescending(v => v.FolderDate).ThenByDescending(v => v.ModifiedAtFile),
            _ => q.OrderByDescending(v => v.ModifiedAtFile)
        };
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
            var mainOnly = query.MainSubjectOnly;
            foreach (var tagId in tagIds)
            {
                // When MainSubjectOnly is set, the matched tag must be a primary
                // (non-background) subject on the clip; otherwise any attachment
                // counts (the clip stays findable even when the tag is incidental).
                q = mainOnly
                    ? q.Where(v => v.VideoTags.Any(vt => vt.TagId == tagId && !vt.IsBackground))
                    : q.Where(v => v.VideoTags.Any(vt => vt.TagId == tagId));
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

    public async Task<IReadOnlyList<MonthShootCount>> GetShootMonthCountsAsync(
        SearchQuery? filter = null,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<VideoItem> q = ctx.VideoItems.AsNoTracking();
        if (filter is not null)
        {
            q = ApplyFilters(q, filter);
        }

        // Effective shoot date = FolderDate ?? ModifiedAtFile. Run as two grouped
        // queries on a single DateTime member each so SQLite can translate the
        // year/month extraction — a coalesce-then-.Month expression does not
        // translate reliably (same split as CatalogStatisticsService.BuildByYear).
        var folderMonths = await q
            .Where(v => v.FolderDate != null)
            .GroupBy(v => new { v.FolderDate!.Value.Year, v.FolderDate!.Value.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var fileMonths = await q
            .Where(v => v.FolderDate == null)
            .GroupBy(v => new { v.ModifiedAtFile.Year, v.ModifiedAtFile.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var merged = new Dictionary<(int Year, int Month), int>();
        foreach (var m in folderMonths)
        {
            var key = (m.Year, m.Month);
            merged[key] = merged.TryGetValue(key, out var c) ? c + m.Count : m.Count;
        }
        foreach (var m in fileMonths)
        {
            var key = (m.Year, m.Month);
            merged[key] = merged.TryGetValue(key, out var c) ? c + m.Count : m.Count;
        }

        return merged
            .OrderByDescending(kv => kv.Key.Year).ThenBy(kv => kv.Key.Month)
            .Select(kv => new MonthShootCount(kv.Key.Year, kv.Key.Month, kv.Value))
            .ToList();
    }

    public async Task<IReadOnlyList<CalendarClip>> GetClipsInMonthAsync(
        int year,
        int month,
        SearchQuery? filter = null,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<VideoItem> q = ctx.VideoItems.AsNoTracking();
        if (filter is not null)
        {
            q = ApplyFilters(q, filter);
        }

        // Mirror the heatmap's effective-date bucketing: a clip belongs to the
        // month if its FolderDate is in it, or (when it has none) its
        // ModifiedAtFile is. Translates as Year/Month on a single member each.
        q = q.Where(v =>
            (v.FolderDate != null && v.FolderDate!.Value.Year == year && v.FolderDate!.Value.Month == month) ||
            (v.FolderDate == null && v.ModifiedAtFile.Year == year && v.ModifiedAtFile.Month == month));

        // Pull the scalar columns the list/preview need, then compute the
        // effective date and order in memory (a single month is bounded).
        var rows = await q
            .Select(v => new
            {
                v.Id,
                v.FileName,
                v.FolderPath,
                v.ThumbnailPath,
                v.FileExists,
                v.Rating,
                v.FolderDate,
                v.ModifiedAtFile
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(v => new CalendarClip
            {
                Id = v.Id,
                FileName = v.FileName,
                FolderPath = v.FolderPath,
                ThumbnailPath = v.ThumbnailPath,
                FileExists = v.FileExists,
                Rating = v.Rating,
                EffectiveDate = v.FolderDate ?? v.ModifiedAtFile
            })
            .OrderBy(v => v.EffectiveDate)
            .ThenBy(v => v.FileName)
            .ToList();
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
