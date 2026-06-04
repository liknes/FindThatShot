using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VideoArchiveManager.Core.Configuration;
using VideoArchiveManager.Core.Services.Ai;

namespace VideoArchiveManager.Data.Services;

// Natural-language search over stored CLIP clip embeddings. The pooled per-clip
// vectors are small, so we cache them in memory and brute-force cosine
// similarity (a dot product, since everything is normalized) against the
// query's text embedding. Best-matching timestamps for the top hits come from
// the per-frame embeddings, enabling jump-to-the-moment.
public class AiSemanticSearchService : IAiSemanticSearchService
{
    private readonly IDbContextFactory<VideoArchiveDbContext> _contextFactory;
    private readonly IAiModelProvider _modelProvider;
    private readonly ISettingsStore _settings;
    private readonly ILogger<AiSemanticSearchService> _logger;

    private readonly object _cacheLock = new();
    private int[]? _cacheIds;
    private float[][]? _cacheVectors;

    public AiSemanticSearchService(
        IDbContextFactory<VideoArchiveDbContext> contextFactory,
        IAiModelProvider modelProvider,
        ISettingsStore settings,
        ILogger<AiSemanticSearchService> logger)
    {
        _contextFactory = contextFactory;
        _modelProvider = modelProvider;
        _settings = settings;
        _logger = logger;
    }

    public bool IsAvailable =>
        _settings.Current.EnableAiTagging && _modelProvider.IsModelInstalled();

    public void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cacheIds = null;
            _cacheVectors = null;
        }
    }

    public async Task<IReadOnlyList<SemanticSearchHit>> SearchAsync(
        string query,
        int maxResults,
        double minScore,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || !IsAvailable)
            return Array.Empty<SemanticSearchHit>();

        await EnsureCacheAsync(cancellationToken).ConfigureAwait(false);

        int[] ids;
        float[][] vectors;
        lock (_cacheLock)
        {
            if (_cacheIds == null || _cacheVectors == null || _cacheIds.Length == 0)
                return Array.Empty<SemanticSearchHit>();
            ids = _cacheIds;
            vectors = _cacheVectors;
        }

        var model = _modelProvider.GetModel();
        var queryVector = model.EncodeText(query);

        // Rank by dot product against the pooled clip vectors.
        var scored = new List<(int Id, double Score)>(ids.Length);
        for (var i = 0; i < ids.Length; i++)
        {
            var score = VectorMath.Dot(queryVector, vectors[i]);
            if (score >= minScore) scored.Add((ids[i], score));
        }

        var top = scored
            .OrderByDescending(x => x.Score)
            .Take(maxResults > 0 ? maxResults : 200)
            .ToList();

        if (top.Count == 0) return Array.Empty<SemanticSearchHit>();

        var bestTimes = await ResolveBestFramesAsync(top.Select(t => t.Id).ToList(), queryVector, cancellationToken)
            .ConfigureAwait(false);

        return top.Select(t => new SemanticSearchHit
        {
            VideoItemId = t.Id,
            Score = t.Score,
            BestFrameSeconds = bestTimes.TryGetValue(t.Id, out var s) ? s : null
        }).ToList();
    }

    // For the (small) set of top hits, find the single frame that best matches
    // the query so the UI can jump straight to it.
    private async Task<Dictionary<int, double>> ResolveBestFramesAsync(
        List<int> clipIds, float[] queryVector, CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, double>();
        try
        {
            await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var frames = await ctx.AiFrameEmbeddings
                .AsNoTracking()
                .Where(f => clipIds.Contains(f.VideoItemId))
                .Select(f => new { f.VideoItemId, f.TimeSeconds, f.Vector })
                .ToListAsync(cancellationToken);

            foreach (var group in frames.GroupBy(f => f.VideoItemId))
            {
                var best = double.MinValue;
                var bestTime = 0.0;
                foreach (var f in group)
                {
                    var sim = VectorMath.Dot(queryVector, VectorMath.FromBytes(f.Vector));
                    if (sim > best) { best = sim; bestTime = f.TimeSeconds; }
                }
                result[group.Key] = bestTime;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve best frame timestamps for semantic search.");
        }
        return result;
    }

    private async Task EnsureCacheAsync(CancellationToken cancellationToken)
    {
        lock (_cacheLock)
        {
            if (_cacheIds != null && _cacheVectors != null) return;
        }

        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await ctx.AiClipEmbeddings
            .AsNoTracking()
            .Select(e => new { e.VideoItemId, e.Vector })
            .ToListAsync(cancellationToken);

        var ids = new int[rows.Count];
        var vectors = new float[rows.Count][];
        for (var i = 0; i < rows.Count; i++)
        {
            ids[i] = rows[i].VideoItemId;
            vectors[i] = VectorMath.FromBytes(rows[i].Vector);
        }

        lock (_cacheLock)
        {
            _cacheIds = ids;
            _cacheVectors = vectors;
        }
        _logger.LogInformation("Loaded {Count} clip embeddings into the semantic search cache.", rows.Count);
    }
}
