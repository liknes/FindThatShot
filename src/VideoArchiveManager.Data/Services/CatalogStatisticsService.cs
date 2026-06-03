using Microsoft.EntityFrameworkCore;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Models.Enums;
using VideoArchiveManager.Core.Services;

namespace VideoArchiveManager.Data.Services;

// Read-only aggregate reporting over the catalog. Every query here is a
// COUNT / SUM / GROUP BY against the SQLite catalog; no source video file is
// ever opened or touched to build the dashboard.
public class CatalogStatisticsService : ICatalogStatisticsService
{
    // Cap on "top N" breakdowns so a catalog with hundreds of cameras / codecs
    // / tags / years doesn't turn the dashboard into an endless wall of bars.
    private const int TopN = 12;

    private readonly IDbContextFactory<VideoArchiveDbContext> _contextFactory;

    public CatalogStatisticsService(IDbContextFactory<VideoArchiveDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<CatalogStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var videos = ctx.VideoItems.AsNoTracking();

        var totalClips = await videos.CountAsync(cancellationToken);

        // Short-circuit an empty catalog so the dashboard can show a friendly
        // "nothing scanned yet" state instead of a grid of zeros.
        if (totalClips == 0)
        {
            return new CatalogStatistics
            {
                RootFolderCount = await ctx.RootFolders.CountAsync(cancellationToken),
                TotalTags = await ctx.Tags.CountAsync(cancellationToken)
            };
        }

        var onlineClips = await videos.CountAsync(v => v.FileExists, cancellationToken);
        var totalSize = await videos.SumAsync(v => v.FileSize, cancellationToken);
        var totalDuration = await videos.SumAsync(v => v.DurationSeconds ?? 0d, cancellationToken);

        var rootFolderCount = await ctx.RootFolders.CountAsync(cancellationToken);
        var distinctFolders = await videos
            .Where(v => v.FolderPath != null && v.FolderPath != string.Empty)
            .Select(v => v.FolderPath)
            .Distinct()
            .CountAsync(cancellationToken);
        var distinctCameras = await videos
            .Where(v => v.Camera != null && v.Camera != string.Empty)
            .Select(v => v.Camera)
            .Distinct()
            .CountAsync(cancellationToken);
        var totalTags = await ctx.Tags.CountAsync(cancellationToken);

        var unreviewed = await videos
            .CountAsync(v => v.Status == VideoStatus.Unreviewed || !v.VideoTags.Any(), cancellationToken);
        var tagged = await videos.CountAsync(v => v.VideoTags.Any(), cancellationToken);
        var rated = await videos.CountAsync(v => v.Rating > 0, cancellationToken);
        var geotagged = await videos
            .CountAsync(v => v.GpsLatitude != null && v.GpsLongitude != null, cancellationToken);

        var byStatus = await BuildByStatusAsync(videos, cancellationToken);
        var byRating = await BuildByRatingAsync(videos, cancellationToken);
        var byResolution = await BuildByResolutionAsync(videos, cancellationToken);
        var byYear = await BuildByYearAsync(videos, cancellationToken);

        var topCameras = await videos
            .Where(v => v.Camera != null && v.Camera != string.Empty)
            .GroupBy(v => v.Camera!)
            .Select(g => new { Label = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).ThenBy(x => x.Label)
            .Take(TopN)
            .ToListAsync(cancellationToken);

        var topCodecs = await videos
            .Where(v => v.Codec != null && v.Codec != string.Empty)
            .GroupBy(v => v.Codec!)
            .Select(g => new { Label = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).ThenBy(x => x.Label)
            .Take(TopN)
            .ToListAsync(cancellationToken);

        var topTags = await ctx.Tags
            .AsNoTracking()
            .Select(t => new { Label = t.Name, Count = t.VideoTags.Count })
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count).ThenBy(x => x.Label)
            .Take(TopN)
            .ToListAsync(cancellationToken);

        return new CatalogStatistics
        {
            TotalClips = totalClips,
            OnlineClips = onlineClips,
            OfflineClips = totalClips - onlineClips,
            TotalSizeBytes = totalSize,
            TotalDurationSeconds = totalDuration,
            RootFolderCount = rootFolderCount,
            DistinctFolders = distinctFolders,
            DistinctCameras = distinctCameras,
            TotalTags = totalTags,
            UnreviewedClips = unreviewed,
            TaggedClips = tagged,
            RatedClips = rated,
            GeotaggedClips = geotagged,
            ByStatus = byStatus,
            ByRating = byRating,
            ByResolution = byResolution,
            ByYear = byYear,
            TopCameras = topCameras.Select(x => new StatCount(x.Label, x.Count)).ToList(),
            TopCodecs = topCodecs.Select(x => new StatCount(x.Label, x.Count)).ToList(),
            TopTags = topTags.Select(x => new StatCount(x.Label, x.Count)).ToList()
        };
    }

    private static async Task<IReadOnlyList<StatCount>> BuildByStatusAsync(
        IQueryable<VideoItem> videos, CancellationToken cancellationToken)
    {
        var raw = await videos
            .GroupBy(v => v.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var counts = raw.ToDictionary(x => x.Status, x => x.Count);

        // Emit every status in its declared order so the breakdown reads like
        // a workflow (Unreviewed → Keep → … → Archive), skipping ones with no
        // clips so the chart stays compact.
        var result = new List<StatCount>();
        foreach (var status in Enum.GetValues<VideoStatus>())
        {
            if (counts.TryGetValue(status, out var count) && count > 0)
            {
                result.Add(new StatCount(FormatStatus(status), count));
            }
        }
        return result;
    }

    private static async Task<IReadOnlyList<StatCount>> BuildByRatingAsync(
        IQueryable<VideoItem> videos, CancellationToken cancellationToken)
    {
        var raw = await videos
            .GroupBy(v => v.Rating)
            .Select(g => new { Rating = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var counts = raw.ToDictionary(x => Math.Clamp(x.Rating, 0, 5), x => x.Count);

        var result = new List<StatCount>();
        for (var rating = 0; rating <= 5; rating++)
        {
            var count = counts.TryGetValue(rating, out var c) ? c : 0;
            if (count == 0) continue;
            var label = rating == 0
                ? "Not rated"
                : new string('\u2605', rating);
            result.Add(new StatCount(label, count));
        }
        return result;
    }

    private static async Task<IReadOnlyList<StatCount>> BuildByResolutionAsync(
        IQueryable<VideoItem> videos, CancellationToken cancellationToken)
    {
        // Pull distinct (width, height) pairs with their counts — there are at
        // most a few dozen of these even in a big catalog — then bucket them in
        // memory by the long edge so we can apply Math.Max (untranslatable in
        // SQLite) and tidy human labels.
        var raw = await videos
            .GroupBy(v => new { v.Width, v.Height })
            .Select(g => new { g.Key.Width, g.Key.Height, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var buckets = new Dictionary<string, int>();
        foreach (var r in raw)
        {
            var label = ResolutionBucket(r.Width, r.Height);
            buckets[label] = buckets.TryGetValue(label, out var existing) ? existing + r.Count : r.Count;
        }

        // Fixed, meaningful order (highest fidelity first; Unknown last).
        var order = new[] { "8K", "4K (UHD)", "1440p (QHD)", "1080p (FHD)", "720p (HD)", "SD", "Unknown" };
        return order
            .Where(buckets.ContainsKey)
            .Select(label => new StatCount(label, buckets[label]))
            .ToList();
    }

    private static string ResolutionBucket(int? width, int? height)
    {
        var w = width ?? 0;
        var h = height ?? 0;
        var longEdge = Math.Max(w, h);
        if (longEdge <= 0) return "Unknown";
        if (longEdge >= 7680) return "8K";
        if (longEdge >= 3840) return "4K (UHD)";
        if (longEdge >= 2560) return "1440p (QHD)";
        if (longEdge >= 1920) return "1080p (FHD)";
        if (longEdge >= 1280) return "720p (HD)";
        return "SD";
    }

    private static async Task<IReadOnlyList<StatCount>> BuildByYearAsync(
        IQueryable<VideoItem> videos, CancellationToken cancellationToken)
    {
        // Prefer the parsed folder date (the user's authored shoot date); fall
        // back to the file's modified date when no folder date was parsed. Run
        // as two grouped queries on a single DateTime member each so SQLite can
        // translate the year extraction (a coalesce-then-.Year expression does
        // not translate reliably).
        var folderYears = await videos
            .Where(v => v.FolderDate != null)
            .GroupBy(v => v.FolderDate!.Value.Year)
            .Select(g => new { Year = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var fileYears = await videos
            .Where(v => v.FolderDate == null)
            .GroupBy(v => v.ModifiedAtFile.Year)
            .Select(g => new { Year = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var byYear = new Dictionary<int, int>();
        foreach (var y in folderYears) byYear[y.Year] = byYear.TryGetValue(y.Year, out var c) ? c + y.Count : y.Count;
        foreach (var y in fileYears) byYear[y.Year] = byYear.TryGetValue(y.Year, out var c) ? c + y.Count : y.Count;

        return byYear
            .OrderByDescending(kv => kv.Key)
            .Select(kv => new StatCount(kv.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), kv.Value))
            .ToList();
    }

    private static string FormatStatus(VideoStatus status) => status switch
    {
        VideoStatus.Unreviewed => "Unreviewed",
        VideoStatus.Keep => "Keep",
        VideoStatus.Favorite => "Favorite",
        VideoStatus.ForStock => "For stock",
        VideoStatus.UploadedPond5 => "Uploaded \u2014 Pond5",
        VideoStatus.UploadedShutterstock => "Uploaded \u2014 Shutterstock",
        VideoStatus.UploadedAdobe => "Uploaded \u2014 Adobe Stock",
        VideoStatus.Rejected => "Rejected",
        VideoStatus.Archive => "Archive",
        _ => status.ToString()
    };
}
