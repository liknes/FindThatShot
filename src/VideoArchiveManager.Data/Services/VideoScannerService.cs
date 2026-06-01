using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VideoArchiveManager.Core.Configuration;
using VideoArchiveManager.Core.Helpers;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Services;

namespace VideoArchiveManager.Data.Services;

public class VideoScannerService : IVideoScannerService
{
    private readonly IDbContextFactory<VideoArchiveDbContext> _contextFactory;
    private readonly IFileSystemService _fileSystem;
    private readonly IFfprobeService _ffprobe;
    private readonly IThumbnailService _thumbnails;
    private readonly IDjiSrtTelemetryReader _djiSrt;
    private readonly ISettingsStore _settings;
    private readonly ILogger<VideoScannerService> _logger;

    public VideoScannerService(
        IDbContextFactory<VideoArchiveDbContext> contextFactory,
        IFileSystemService fileSystem,
        IFfprobeService ffprobe,
        IThumbnailService thumbnails,
        IDjiSrtTelemetryReader djiSrt,
        ISettingsStore settings,
        ILogger<VideoScannerService> logger)
    {
        _contextFactory = contextFactory;
        _fileSystem = fileSystem;
        _ffprobe = ffprobe;
        _thumbnails = thumbnails;
        _djiSrt = djiSrt;
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
            ctx.VideoItems.Add(entity);
            await ctx.SaveChangesAsync(cancellationToken);
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
