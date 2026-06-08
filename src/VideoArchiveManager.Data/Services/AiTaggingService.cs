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
        var secondsPerFrame = settings.AiSecondsPerFrame > 0 ? settings.AiSecondsPerFrame : 20d;
        var minFrames = Math.Clamp(settings.AiMinFramesPerClip, 1, 128);
        var maxFrames = Math.Clamp(settings.AiMaxFramesPerClip, minFrames, 128);
        var threshold = settings.AiSuggestionThreshold;
        var maxSuggestions = Math.Max(1, settings.AiMaxSuggestionsPerClip);

        // Optionally learn a per-label threshold from prior accept/reject
        // decisions; labels with too little history fall back to the global one.
        var labelThresholds = settings.AiAdaptiveThresholds
            ? await BuildLabelThresholdsAsync(threshold, cancellationToken).ConfigureAwait(false)
            : new Dictionary<string, double>();

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
        // For the incremental pass, surface the full online catalog size so the
        // progress count makes sense (e.g. "12 new clips" out of "2103 total"),
        // instead of looking like the app forgot the already-scored clips. For a
        // re-score, the work set already is the full catalog.
        var catalogTotal = reprocessAll
            ? total
            : await CountPendingAsync(reprocessAll: true, cancellationToken).ConfigureAwait(false);
        var processed = 0;
        var tagged = 0;
        var failed = 0;
        var skipped = 0;

        string ProgressMessage(int done) => reprocessAll
            ? $"Scored {done} of {total} clip(s)"
            : $"Scored {done} of {total} new clip(s) ({catalogTotal} total)";

        progress?.Report(new AiTaggingProgress
        {
            Total = total,
            Message = total == 0
                ? "Nothing to tag."
                : reprocessAll
                    ? $"Scoring {total} clip(s)"
                    : $"Scoring {total} new clip(s) ({catalogTotal} total)"
        });

        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? currentFile = null;
            try
            {
                var result = await ProcessClipAsync(
                    id, model, labels, labelVectors, secondsPerFrame, minFrames, maxFrames, threshold, labelThresholds, maxSuggestions, cancellationToken)
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
                Message = ProgressMessage(processed)
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

    // Duration-proportional frame count: ~one frame per secondsPerFrame of
    // footage, clamped to [min, max]. Unknown/zero duration falls back to the
    // floor so a clip we can't measure still gets a handful of viewpoints.
    private static int ResolveFrameCount(double? durationSeconds, double secondsPerFrame, int minFrames, int maxFrames)
    {
        if (durationSeconds is not > 0 || secondsPerFrame <= 0)
            return minFrames;
        var n = (int)Math.Ceiling(durationSeconds.Value / secondsPerFrame);
        return Math.Clamp(n, minFrames, maxFrames);
    }

    // Minimum number of decided (accepted/rejected) suggestions a label needs
    // before we trust a learned threshold over the global one.
    private const int MinDecisionsForCalibration = 4;

    // Hard band any learned threshold is clamped into, so a quirky run of
    // decisions can't drive a label to never-suggest or always-suggest.
    private const double CalibrationFloor = 0.18;
    private const double CalibrationCeil = 0.45;

    // Learns a per-label confidence threshold from the user's accept/reject
    // history (the decided AiTagSuggestion rows + their stored confidence).
    private async Task<Dictionary<string, double>> BuildLabelThresholdsAsync(
        double globalThreshold, CancellationToken cancellationToken)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var decided = await ctx.AiTagSuggestions
            .AsNoTracking()
            .Where(s => s.State == AiSuggestionState.Accepted || s.State == AiSuggestionState.Rejected)
            .Select(s => new { s.TagName, s.Confidence, s.State })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = new Dictionary<string, double>();
        foreach (var group in decided.GroupBy(d => d.TagName))
        {
            if (group.Count() < MinDecisionsForCalibration) continue;
            var pos = group.Where(d => d.State == AiSuggestionState.Accepted).Select(d => d.Confidence).ToList();
            var neg = group.Where(d => d.State == AiSuggestionState.Rejected).Select(d => d.Confidence).ToList();
            result[group.Key] = ComputeLabelThreshold(pos, neg, globalThreshold);
        }
        return result;
    }

    // Picks the threshold that best separates accepted (positive) from rejected
    // (negative) confidences for one label:
    //   * both classes present -> maximize Youden's J (TPR - FPR), favouring
    //     recall on ties (lower threshold), clamped to the calibration band;
    //   * only accepts -> lower the bar toward the lowest score they accepted
    //     (never above the global threshold), so we surface this liked label sooner;
    //   * only rejects -> raise the bar just above the highest score they
    //     rejected, effectively suppressing a label they never want.
    private static double ComputeLabelThreshold(List<double> pos, List<double> neg, double globalThreshold)
    {
        if (pos.Count == 0 && neg.Count == 0) return globalThreshold;
        if (pos.Count == 0)
            return Math.Clamp(neg.Max() + 0.005, globalThreshold, CalibrationCeil);
        if (neg.Count == 0)
            return Math.Clamp(pos.Min() - 0.005, CalibrationFloor, globalThreshold);

        var candidates = pos.Concat(neg).Distinct().OrderBy(v => v).ToList();
        var bestT = globalThreshold;
        var bestJ = double.NegativeInfinity;
        foreach (var t in candidates)
        {
            var tp = pos.Count(p => p >= t);
            var fn = pos.Count - tp;
            var fp = neg.Count(n => n >= t);
            var tn = neg.Count - fp;
            var tpr = tp + fn == 0 ? 0 : (double)tp / (tp + fn);
            var fpr = fp + tn == 0 ? 0 : (double)fp / (fp + tn);
            var j = tpr - fpr;
            // Strictly-greater keeps the first (lowest) threshold among ties,
            // which favours recall — missed tags never reach the review queue.
            if (j > bestJ)
            {
                bestJ = j;
                bestT = t;
            }
        }
        return Math.Clamp(bestT, CalibrationFloor, CalibrationCeil);
    }

    private readonly record struct ClipResult(string? FileName, bool Skipped, int SuggestionsWritten);

    private async Task<ClipResult> ProcessClipAsync(
        int videoId,
        IClipModel model,
        IReadOnlyList<AiLabel> labels,
        float[][] labelVectors,
        double secondsPerFrame,
        int minFrames,
        int maxFrames,
        double globalThreshold,
        IReadOnlyDictionary<string, double> labelThresholds,
        int maxSuggestions,
        CancellationToken cancellationToken)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await ctx.VideoItems.FirstOrDefaultAsync(v => v.Id == videoId, cancellationToken);
        if (entity == null) return new ClipResult(null, true, 0);
        if (!_fileSystem.FileExists(entity.FilePath)) return new ClipResult(entity.FileName, true, 0);

        var framesPerClip = ResolveFrameCount(entity.DurationSeconds, secondsPerFrame, minFrames, maxFrames);
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
            var tagName = labels[li].TagName;
            var labelThreshold = labelThresholds.TryGetValue(tagName, out var t) ? t : globalThreshold;
            if (best >= labelThreshold) scored.Add((tagName, best, bestTime));
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
