using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VideoArchiveManager.Core.Configuration;
using VideoArchiveManager.Core.Helpers;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Models.Enums;
using VideoArchiveManager.Core.Services;

namespace VideoArchiveManager.Data.Services;

public class VideoScannerService : IVideoScannerService
{
    private readonly IDbContextFactory<VideoArchiveDbContext> _contextFactory;
    private readonly IFileSystemService _fileSystem;
    private readonly IFfprobeService _ffprobe;
    private readonly IThumbnailService _thumbnails;
    private readonly IDjiSrtTelemetryReader _djiSrt;
    private readonly ISidecarService _sidecar;
    private readonly ISettingsStore _settings;
    private readonly ILogger<VideoScannerService> _logger;

    // Serializes sidecar tag get-or-create across the parallel scan. Tags
    // carry a unique (Name, Type) index, so two workers importing the same
    // tag at once would race into a constraint violation; the section is
    // tiny and only runs for newly-imported clips that have a sidecar.
    private readonly SemaphoreSlim _tagImportLock = new(1, 1);

    public VideoScannerService(
        IDbContextFactory<VideoArchiveDbContext> contextFactory,
        IFileSystemService fileSystem,
        IFfprobeService ffprobe,
        IThumbnailService thumbnails,
        IDjiSrtTelemetryReader djiSrt,
        ISidecarService sidecar,
        ISettingsStore settings,
        ILogger<VideoScannerService> logger)
    {
        _contextFactory = contextFactory;
        _fileSystem = fileSystem;
        _ffprobe = ffprobe;
        _thumbnails = thumbnails;
        _djiSrt = djiSrt;
        _sidecar = sidecar;
        _settings = settings;
        _logger = logger;
    }

    public async Task ScanAsync(
        IEnumerable<RootFolder> rootFolders,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var roots = rootFolders.ToList();
        if (roots.Count == 0)
        {
            progress?.Report(new ScanProgress { IsComplete = true, Message = "No root folders configured" });
            return;
        }

        var extensions = _settings.Current.SupportedExtensions;
        var excludedFolders = _settings.Current.ExcludedFolderNames;
        var excludedPatterns = _settings.Current.ExcludedFileNamePatterns;
        var allFiles = new List<string>();

        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_fileSystem.DirectoryExists(root.Path))
            {
                _logger.LogWarning("Root folder missing: {Path}", root.Path);
                continue;
            }

            foreach (var path in _fileSystem.EnumerateVideoFiles(root.Path, extensions, excludedFolders, excludedPatterns, cancellationToken))
            {
                allFiles.Add(path);
                if (allFiles.Count % 100 == 0)
                {
                    progress?.Report(new ScanProgress
                    {
                        TotalFound = allFiles.Count,
                        Message = $"Discovering files... ({allFiles.Count})",
                        CurrentFile = path
                    });
                }
            }
        }

        var total = allFiles.Count;
        progress?.Report(new ScanProgress { TotalFound = total, Message = $"Found {total} files. Probing metadata..." });

        var added = 0;
        var updated = 0;
        var skipped = 0;
        var failed = 0;
        var processed = 0;

        var thumbnailChannel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

        var thumbWorker = Task.Run(() => ProcessThumbnailQueueAsync(thumbnailChannel.Reader, cancellationToken), cancellationToken);

        var parallelism = Math.Max(1, _settings.Current.MaxScanParallelism);
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = parallelism,
            CancellationToken = cancellationToken
        };

        var processedLock = new object();

        try
        {
            await Parallel.ForEachAsync(allFiles, options, async (path, ct) =>
            {
                progress?.Report(new ScanProgress
                {
                    TotalFound = total,
                    Processed = processed,
                    Added = added,
                    Updated = updated,
                    Skipped = skipped,
                    Failed = failed,
                    CurrentFile = path,
                    Message = $"Processing {Path.GetFileName(path)}..."
                });

                try
                {
                    var result = await ProcessFileAsync(path, ct).ConfigureAwait(false);
                    lock (processedLock)
                    {
                        processed++;
                        switch (result.Action)
                        {
                            case ProcessAction.Added: added++; break;
                            case ProcessAction.Updated: updated++; break;
                            case ProcessAction.Skipped: skipped++; break;
                            case ProcessAction.Failed: failed++; break;
                        }
                    }

                    if (result.VideoId is int id && (result.Action == ProcessAction.Added || result.Action == ProcessAction.Updated))
                    {
                        await thumbnailChannel.Writer.WriteAsync(id, ct).ConfigureAwait(false);
                    }

                    progress?.Report(new ScanProgress
                    {
                        TotalFound = total,
                        Processed = processed,
                        Added = added,
                        Updated = updated,
                        Skipped = skipped,
                        Failed = failed,
                        CurrentFile = path
                    });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process {Path}", path);
                    lock (processedLock)
                    {
                        processed++;
                        failed++;
                    }
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            thumbnailChannel.Writer.TryComplete();
            try
            {
                await thumbWorker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected on cancel
            }
        }

        await using (var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken))
        {
            foreach (var root in roots)
            {
                var entity = await ctx.RootFolders.FirstOrDefaultAsync(r => r.Id == root.Id, cancellationToken);
                if (entity != null)
                {
                    entity.LastScannedAt = DateTime.UtcNow;
                }
            }
            await ctx.SaveChangesAsync(cancellationToken);
        }

        progress?.Report(new ScanProgress
        {
            TotalFound = total,
            Processed = processed,
            Added = added,
            Updated = updated,
            Skipped = skipped,
            Failed = failed,
            IsComplete = true,
            Message = $"Scan complete. Added {added}, updated {updated}, skipped {skipped}, failed {failed}."
        });
    }

    public async Task UpdateFileAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var batchSize = 500;
        var offset = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = await ctx.VideoItems
                .OrderBy(v => v.Id)
                .Skip(offset)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0) break;

            foreach (var item in batch)
            {
                var exists = _fileSystem.FileExists(item.FilePath);
                if (item.FileExists != exists)
                {
                    item.FileExists = exists;
                    item.UpdatedAt = DateTime.UtcNow;
                }
            }
            await ctx.SaveChangesAsync(cancellationToken);
            offset += batch.Count;
        }
    }

    private async Task<ProcessResult> ProcessFileAsync(string path, CancellationToken cancellationToken)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists)
            {
                return new ProcessResult(ProcessAction.Skipped, null);
            }
        }
        catch
        {
            return new ProcessResult(ProcessAction.Failed, null);
        }

        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await ctx.VideoItems.FirstOrDefaultAsync(v => v.FilePath == path, cancellationToken);

        if (existing != null &&
            existing.FileSize == info.Length &&
            existing.ModifiedAtFile == info.LastWriteTimeUtc)
        {
            if (!existing.FileExists)
            {
                existing.FileExists = true;
                existing.UpdatedAt = DateTime.UtcNow;
                await ctx.SaveChangesAsync(cancellationToken);
            }
            return new ProcessResult(ProcessAction.Skipped, existing.Id);
        }

        var ffprobeResult = await _ffprobe.ProbeAsync(path, cancellationToken).ConfigureAwait(false);

        // Fallback for cameras (notably DJI drones) that don't populate the
        // standard QuickTime location tag and instead write per-frame GPS into
        // a companion .SRT file alongside the .MP4. We only consult it when
        // ffprobe didn't find coordinates, so this is cheap and never overrides
        // first-party metadata.
        double? gpsLat = ffprobeResult?.GpsLatitude;
        double? gpsLon = ffprobeResult?.GpsLongitude;
        if (gpsLat is null || gpsLon is null)
        {
            var dji = await _djiSrt.TryReadAsync(path, cancellationToken).ConfigureAwait(false);
            if (dji is not null && dji.Latitude.HasValue && dji.Longitude.HasValue)
            {
                gpsLat = dji.Latitude;
                gpsLon = dji.Longitude;
            }
        }

        var folder = Path.GetDirectoryName(path) ?? string.Empty;
        var folderName = Path.GetFileName(folder);
        var parsed = FolderNameParser.Parse(folderName);

        if (existing == null)
        {
            var entity = new VideoItem
            {
                FilePath = path,
                FileName = Path.GetFileName(path),
                Extension = Path.GetExtension(path).ToLowerInvariant(),
                FolderPath = folder,
                FileSize = info.Length,
                CreatedAtFile = info.CreationTimeUtc,
                ModifiedAtFile = info.LastWriteTimeUtc,
                FileExists = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                FolderDate = parsed.FolderDate,
                LocationText = parsed.LocationText,
                ContextText = parsed.ContextText,
                DurationSeconds = ffprobeResult?.DurationSeconds,
                Width = ffprobeResult?.Width,
                Height = ffprobeResult?.Height,
                FrameRate = ffprobeResult?.FrameRate,
                Codec = ffprobeResult?.Codec,
                Camera = ffprobeResult?.Camera,
                GpsLatitude = gpsLat,
                GpsLongitude = gpsLon
            };

            // Rehydrate from a sidecar written by a previous install/catalog,
            // if present. We only do this for brand-new records (the import
            // path) — a rescan of an existing clip never reapplies the
            // sidecar, so the live catalog stays the source of truth and
            // user edits are never clobbered.
            var sidecar = await _sidecar.TryReadAsync(path, cancellationToken).ConfigureAwait(false);
            if (sidecar != null)
            {
                ApplySidecarScalars(entity, sidecar);
            }

            ctx.VideoItems.Add(entity);
            await ctx.SaveChangesAsync(cancellationToken);

            if (sidecar != null && sidecar.Tags.Count > 0)
            {
                await ApplySidecarTagsAsync(ctx, entity.Id, sidecar.Tags, cancellationToken).ConfigureAwait(false);
            }

            if (sidecar != null && sidecar.Moments.Count > 0)
            {
                await ApplySidecarMomentsAsync(ctx, entity.Id, path, sidecar.Moments, cancellationToken).ConfigureAwait(false);
            }

            return new ProcessResult(ProcessAction.Added, entity.Id);
        }
        else
        {
            existing.FileSize = info.Length;
            existing.CreatedAtFile = info.CreationTimeUtc;
            existing.ModifiedAtFile = info.LastWriteTimeUtc;
            existing.FileExists = true;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.FolderPath = folder;
            existing.FolderDate ??= parsed.FolderDate;
            existing.LocationText ??= parsed.LocationText;
            existing.ContextText ??= parsed.ContextText;
            if (ffprobeResult != null)
            {
                existing.DurationSeconds = ffprobeResult.DurationSeconds ?? existing.DurationSeconds;
                existing.Width = ffprobeResult.Width ?? existing.Width;
                existing.Height = ffprobeResult.Height ?? existing.Height;
                existing.FrameRate = ffprobeResult.FrameRate ?? existing.FrameRate;
                existing.Codec = ffprobeResult.Codec ?? existing.Codec;
                existing.Camera ??= ffprobeResult.Camera;
            }
            // Embedded GPS (DJI SRT telemetry or FFprobe-extracted QuickTime
            // location) is always authoritative: overwrite whatever is on
            // the entity, but never null out an existing value just because
            // a rescan happened to miss it. This is what makes the manual
            // GPS picker safe — user-entered coordinates survive rescans
            // that don't find embedded GPS, but they get replaced the
            // moment a real GPS source appears for the clip.
            if (gpsLat.HasValue) existing.GpsLatitude = gpsLat;
            if (gpsLon.HasValue) existing.GpsLongitude = gpsLon;
            await ctx.SaveChangesAsync(cancellationToken);
            return new ProcessResult(ProcessAction.Updated, existing.Id);
        }
    }

    // Copies the sidecar's scalar metadata onto a freshly-built entity.
    // Sidecar values win over the folder-name-parsed defaults here because
    // they reflect deliberate curation from a prior catalog; we only keep a
    // parsed/empty value when the sidecar doesn't carry one.
    private static void ApplySidecarScalars(VideoItem entity, SidecarData sidecar)
    {
        entity.Rating = sidecar.Rating;

        if (!string.IsNullOrWhiteSpace(sidecar.Status) &&
            Enum.TryParse<VideoStatus>(sidecar.Status, ignoreCase: true, out var status))
        {
            entity.Status = status;
        }

        if (!string.IsNullOrWhiteSpace(sidecar.Notes)) entity.Notes = sidecar.Notes;
        if (!string.IsNullOrWhiteSpace(sidecar.LocationText)) entity.LocationText = sidecar.LocationText;
        if (!string.IsNullOrWhiteSpace(sidecar.ContextText)) entity.ContextText = sidecar.ContextText;
        if (sidecar.FolderDate.HasValue) entity.FolderDate = sidecar.FolderDate;
    }

    // Get-or-create each sidecar tag by (name, type) and link it to the new
    // video. Serialized behind _tagImportLock so concurrent scan workers
    // can't race two inserts of the same tag past the unique index.
    private async Task ApplySidecarTagsAsync(
        VideoArchiveDbContext ctx,
        int videoItemId,
        IReadOnlyList<SidecarTagData> tags,
        CancellationToken cancellationToken)
    {
        // Dedupe within the sidecar (defensive — a hand-edited file could
        // repeat a tag) so we don't try to add the same link twice.
        var distinct = tags
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .Select(t => (Name: t.Name.Trim(), Type: ParseTagType(t.Type)))
            .Distinct()
            .ToList();
        if (distinct.Count == 0) return;

        await _tagImportLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var (name, type) in distinct)
            {
                var tag = await ctx.Tags
                    .FirstOrDefaultAsync(t => t.Name == name && t.Type == type, cancellationToken)
                    .ConfigureAwait(false);
                if (tag == null)
                {
                    tag = new Tag { Name = name, Type = type };
                    ctx.Tags.Add(tag);
                    await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }

                ctx.VideoTags.Add(new VideoTag { VideoItemId = videoItemId, TagId = tag.Id });
            }

            await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _tagImportLock.Release();
        }
    }

    // Rehydrate timestamped moments from a sidecar onto a freshly-imported clip.
    // Each moment row is created, its in-point thumbnail is grabbed best-effort
    // (the source file is present — we just scanned it), and its tags are linked
    // through the same get-or-create path as clip tags. Only runs for brand-new
    // imports, so a rescan never re-creates moments the user may have since
    // pruned. The source video file is only read for the thumbnail, never written.
    private async Task ApplySidecarMomentsAsync(
        VideoArchiveDbContext ctx,
        int videoItemId,
        string videoPath,
        IReadOnlyList<SidecarMomentData> moments,
        CancellationToken cancellationToken)
    {
        await _tagImportLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var sm in moments)
            {
                var moment = new VideoMoment
                {
                    VideoItemId = videoItemId,
                    StartSeconds = sm.StartSeconds < 0 ? 0 : sm.StartSeconds,
                    EndSeconds = sm.EndSeconds,
                    Label = string.IsNullOrWhiteSpace(sm.Label) ? null : sm.Label.Trim(),
                    Notes = sm.Notes,
                    Rating = sm.Rating,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                ctx.VideoMoments.Add(moment);
                await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                // Best-effort in-point thumbnail; a missing/offline file just
                // leaves ThumbnailPath null (the moment still works).
                try
                {
                    var thumb = await _thumbnails
                        .GenerateAtAsync(moment.Id, videoPath, moment.StartSeconds, cancellationToken)
                        .ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(thumb))
                    {
                        moment.ThumbnailPath = thumb;
                        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // Thumbnail capture is non-essential; never fail the import.
                }

                foreach (var st in sm.Tags)
                {
                    if (string.IsNullOrWhiteSpace(st.Name)) continue;
                    var name = st.Name.Trim();
                    var type = ParseTagType(st.Type);

                    var tag = await ctx.Tags
                        .FirstOrDefaultAsync(t => t.Name == name && t.Type == type, cancellationToken)
                        .ConfigureAwait(false);
                    if (tag == null)
                    {
                        tag = new Tag { Name = name, Type = type };
                        ctx.Tags.Add(tag);
                        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }

                    ctx.MomentTags.Add(new MomentTag { VideoMomentId = moment.Id, TagId = tag.Id });
                }

                await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _tagImportLock.Release();
        }
    }

    private static TagType ParseTagType(string? type) =>
        !string.IsNullOrWhiteSpace(type) && Enum.TryParse<TagType>(type, ignoreCase: true, out var parsed)
            ? parsed
            : TagType.Subject;

    private async Task ProcessThumbnailQueueAsync(ChannelReader<int> reader, CancellationToken cancellationToken)
    {
        var parallelism = Math.Max(1, _settings.Current.MaxScanParallelism);
        using var semaphore = new SemaphoreSlim(parallelism);
        var tasks = new List<Task>();

        try
        {
            await foreach (var videoId in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await GenerateThumbnailForAsync(videoId, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken));
            }
        }
        catch (OperationCanceledException)
        {
            // graceful cancel
        }

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
            // individual failures already logged
        }
    }

    private async Task GenerateThumbnailForAsync(int videoId, CancellationToken cancellationToken)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await ctx.VideoItems.FirstOrDefaultAsync(v => v.Id == videoId, cancellationToken);
        if (entity == null || !_fileSystem.FileExists(entity.FilePath)) return;

        if (!string.IsNullOrEmpty(entity.ThumbnailPath) && File.Exists(entity.ThumbnailPath))
        {
            return;
        }

        var thumbPath = await _thumbnails.GenerateAsync(entity.Id, entity.FilePath, entity.DurationSeconds, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(thumbPath))
        {
            entity.ThumbnailPath = thumbPath;
            entity.UpdatedAt = DateTime.UtcNow;
            await ctx.SaveChangesAsync(cancellationToken);
        }
    }

    private enum ProcessAction { Added, Updated, Skipped, Failed }

    private readonly record struct ProcessResult(ProcessAction Action, int? VideoId);
}
