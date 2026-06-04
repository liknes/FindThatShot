using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VideoArchiveManager.Core.Configuration;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Models.Enums;
using VideoArchiveManager.Core.Services;
using VideoArchiveManager.Core.Services.Ai;

namespace VideoArchiveManager.Data.Services;

// Orchestrates the CLIP scoring pass: samples frames per clip, embeds them,
// stores pooled + per-frame embeddings, and writes AiTagSuggestion rows by
// max-pooling label similarity across frames. Tags are never auto-applied and
// source files are only ever read.
public class AiTaggingService : IAiTaggingService
{
    private readonly IDbContextFactory<VideoArchiveDbContext> _contextFactory;
    private readonly IAiModelProvider _modelProvider;
    private readonly IFrameSampler _sampler;
    private readonly IFileSystemService _fileSystem;
    private readonly ISettingsStore _settings;
    private readonly IAiSemanticSearchService _semanticSearch;
    private readonly ILogger<AiTaggingService> _logger;

    public AiTaggingService(
        IDbContextFactory<VideoArchiveDbContext> contextFactory,
        IAiModelProvider modelProvider,
        IFrameSampler sampler,
        IFileSystemService fileSystem,
        ISettingsStore settings,
        IAiSemanticSearchService semanticSearch,
        ILogger<AiTaggingService> logger)
    {
        _contextFactory = contextFactory;
        _modelProvider = modelProvider;
        _sampler = sampler;
        _fileSystem = fileSystem;
        _settings = settings;
        _semanticSearch = semanticSearch;
        _logger = logger;
    }

    public async Task<int> CountPendingAsync(bool reprocessAll, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var q = ctx.VideoItems.AsNoTracking().Where(v => v.FileExists);
        if (!reprocessAll)
        {
            q = q.Where(v => !ctx.AiClipEmbeddings.Any(e => e.VideoItemId == v.Id));
        }
        return await q.CountAsync(cancellationToken);
    }

    public async Task GenerateAsync(
        bool reprocessAll,
        IProgress<AiTaggingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var model = _modelProvider.GetModel();
        var settings = _settings.Current;
        var framesPerClip = Math.Clamp(settings.AiFramesPerClip, 1, 64);
        var threshold = settings.AiSuggestionThreshold;
        var maxSuggestions = Math.Max(1, settings.AiMaxSuggestionsPerClip);

        // Precompute one ensembled, normalized text embedding per label.
        var labels = AiLabelVocabulary.Default;
        var labelVectors = new float[labels.Count][];
        for (var i = 0; i < labels.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var promptVectors = labels[i].Prompts.Select(p => model.EncodeText(p)).ToList();
            labelVectors[i] = VectorMath.MeanNormalized(promptVectors);
        }

        var ids = await GetPendingIdsAsync(reprocessAll, cancellationToken).ConfigureAwait(false);
        var total = ids.Count;
        var processed = 0;
        var tagged = 0;
        var failed = 0;
        var skipped = 0;

        progress?.Report(new AiTaggingProgress
        {
            Total = total,
            Message = total == 0 ? "Nothing to tag." : $"Scoring {total} clip(s)…"
        });

        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? currentFile = null;
            try
            {
                var result = await ProcessClipAsync(
                    id, model, labels, labelVectors, framesPerClip, threshold, maxSuggestions, cancellationToken)
                    .ConfigureAwait(false);
                currentFile = result.FileName;
                processed++;
                if (result.Skipped) skipped++;
                else if (result.SuggestionsWritten > 0) tagged++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                processed++;
                failed++;
                _logger.LogWarning(ex, "AI tagging failed for video id {Id}", id);
            }

            progress?.Report(new AiTaggingProgress
            {
                Total = total,
                Processed = processed,
                Tagged = tagged,
                Failed = failed,
                Skipped = skipped,
                CurrentFile = currentFile,
                Message = $"Scored {processed}/{total}…"
            });
        }

        _semanticSearch.InvalidateCache();

        progress?.Report(new AiTaggingProgress
        {
            Total = total,
            Processed = processed,
            Tagged = tagged,
            Failed = failed,
            Skipped = skipped,
            IsComplete = true,
            Message = $"Done. {tagged} clip(s) got suggestions, {skipped} had none, {failed} failed."
        });
    }

    private async Task<List<int>> GetPendingIdsAsync(bool reprocessAll, CancellationToken cancellationToken)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var q = ctx.VideoItems.AsNoTracking().Where(v => v.FileExists);
        if (!reprocessAll)
        {
            q = q.Where(v => !ctx.AiClipEmbeddings.Any(e => e.VideoItemId == v.Id));
        }
        return await q.OrderBy(v => v.Id).Select(v => v.Id).ToListAsync(cancellationToken);
    }

    private readonly record struct ClipResult(string? FileName, bool Skipped, int SuggestionsWritten);

    private async Task<ClipResult> ProcessClipAsync(
        int videoId,
        IClipModel model,
        IReadOnlyList<AiLabel> labels,
        float[][] labelVectors,
        int framesPerClip,
        double threshold,
        int maxSuggestions,
        CancellationToken cancellationToken)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await ctx.VideoItems.FirstOrDefaultAsync(v => v.Id == videoId, cancellationToken);
        if (entity == null) return new ClipResult(null, true, 0);
        if (!_fileSystem.FileExists(entity.FilePath)) return new ClipResult(entity.FileName, true, 0);

        var frames = await _sampler
            .SampleAsync(entity.FilePath, entity.DurationSeconds, framesPerClip, model.ImageSize, cancellationToken)
            .ConfigureAwait(false);
        if (frames.Count == 0) return new ClipResult(entity.FileName, true, 0);

        var frameVectors = new List<float[]>(frames.Count);
        var frameTimes = new List<double>(frames.Count);
        foreach (var f in frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            frameVectors.Add(model.EncodeImage(f.Rgb24));
            frameTimes.Add(f.TimeSeconds);
        }

        // Replace any prior embeddings for this clip (re-tagging is idempotent).
        await ctx.AiFrameEmbeddings.Where(e => e.VideoItemId == videoId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await ctx.AiClipEmbeddings.Where(e => e.VideoItemId == videoId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        for (var i = 0; i < frameVectors.Count; i++)
        {
            ctx.AiFrameEmbeddings.Add(new AiFrameEmbedding
            {
                VideoItemId = videoId,
                TimeSeconds = frameTimes[i],
                Vector = VectorMath.ToBytes(frameVectors[i]),
                Dim = frameVectors[i].Length,
                ModelId = model.ModelId
            });
        }

        var pooled = VectorMath.MeanNormalized(frameVectors);
        ctx.AiClipEmbeddings.Add(new AiClipEmbedding
        {
            VideoItemId = videoId,
            Vector = VectorMath.ToBytes(pooled),
            Dim = pooled.Length,
            FrameCount = frameVectors.Count,
            ModelId = model.ModelId,
            CreatedAt = DateTime.UtcNow
        });

        // Score each label by its best (max) matching frame.
        var scored = new List<(string Tag, double Sim, double Time)>();
        for (var li = 0; li < labels.Count; li++)
        {
            var best = double.MinValue;
            var bestTime = 0.0;
            for (var fi = 0; fi < frameVectors.Count; fi++)
            {
                var sim = VectorMath.Dot(labelVectors[li], frameVectors[fi]);
                if (sim > best)
                {
                    best = sim;
                    bestTime = frameTimes[fi];
                }
            }
            if (best >= threshold) scored.Add((labels[li].TagName, best, bestTime));
        }

        var top = scored.OrderByDescending(s => s.Sim).Take(maxSuggestions).ToList();

        // Existing suggestions for this clip — never resurrect a rejected /
        // accepted label, just refresh confidence on still-pending ones.
        var existing = await ctx.AiTagSuggestions
            .Where(s => s.VideoItemId == videoId)
            .ToDictionaryAsync(s => s.TagName, cancellationToken)
            .ConfigureAwait(false);

        var written = 0;
        foreach (var (tag, sim, time) in top)
        {
            if (existing.TryGetValue(tag, out var prior))
            {
                if (prior.State == AiSuggestionState.Pending)
                {
                    prior.Confidence = sim;
                    prior.BestFrameSeconds = time;
                    prior.Source = model.ModelId;
                    written++;
                }
                continue;
            }

            ctx.AiTagSuggestions.Add(new AiTagSuggestion
            {
                VideoItemId = videoId,
                TagName = tag,
                Confidence = sim,
                BestFrameSeconds = time,
                Source = model.ModelId,
                State = AiSuggestionState.Pending,
                Approved = false,
                CreatedAt = DateTime.UtcNow
            });
            written++;
        }

        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ClipResult(entity.FileName, false, written);
    }

    public async Task<int> ClearAllAiDataAsync(CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var frames = await ctx.AiFrameEmbeddings.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        var clips = await ctx.AiClipEmbeddings.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        var suggestions = await ctx.AiTagSuggestions.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        _semanticSearch.InvalidateCache();
        return frames + clips + suggestions;
    }
}
