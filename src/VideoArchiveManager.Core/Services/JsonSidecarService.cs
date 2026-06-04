using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using VideoArchiveManager.Core.Configuration;
using VideoArchiveManager.Core.Models;

namespace VideoArchiveManager.Core.Services;

// SAFETY CONTRACT:
//   * This service writes ONE auxiliary file per video, located next to the
//     source file, with the suffix '.findthatshot.json'. It never modifies,
//     renames, or deletes the source video file. Failures (read-only drive,
//     offline drive, permission errors) are logged and surfaced via the
//     return type — they never throw or block the calling save flow.
public class JsonSidecarService : ISidecarService
{
    public const string SidecarSuffix = ".findthatshot.json";

    // v2 adds the "moments" array (timestamped sub-clips). v1 readers simply
    // ignore the extra property, and v1 files round-trip cleanly through a v2
    // reader (moments default to empty), so the bump is backward compatible.
    public const string SchemaId = "findthatshot/v2";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ISettingsStore _settings;
    private readonly ILogger<JsonSidecarService> _logger;

    public JsonSidecarService(ISettingsStore settings, ILogger<JsonSidecarService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public bool IsEnabled => _settings.Current.WriteSidecarFiles;

    public string GetSidecarPathFor(string videoPath) => videoPath + SidecarSuffix;

    public bool SidecarExistsFor(string videoPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath)) return false;
        try
        {
            return File.Exists(GetSidecarPathFor(videoPath));
        }
        catch
        {
            // Malformed/over-long paths or an offline drive can throw on the
            // existence probe — treat as "no sidecar" rather than surfacing.
            return false;
        }
    }

    public bool ShouldWriteFor(string videoPath) =>
        IsEnabled || SidecarExistsFor(videoPath);

    public async Task<SidecarWriteResult> WriteAsync(
        VideoItem video,
        IEnumerable<Tag> tags,
        IReadOnlyList<VideoMoment>? moments = null,
        CancellationToken cancellationToken = default)
    {
        if (video is null || string.IsNullOrWhiteSpace(video.FilePath))
        {
            return SidecarWriteResult.Failure("Video has no file path.");
        }

        var sidecarPath = GetSidecarPathFor(video.FilePath);

        // Write when enabled, OR when a sidecar already exists — an existing
        // file is the user's opt-in, so we keep it in sync with the catalog
        // (e.g. a tag removed while "write new sidecars" is off) instead of
        // letting it silently go stale. Brand-new files are only created when
        // the setting is on.
        if (!IsEnabled && !File.Exists(sidecarPath))
        {
            return SidecarWriteResult.DisabledOrSkipped();
        }

        var folder = Path.GetDirectoryName(sidecarPath);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            // Drive offline or folder gone — quietly skip; will be retried next save.
            return SidecarWriteResult.Failure(
                $"Folder not available: '{folder}'.",
                sidecarPath);
        }

        // Resolve the moments section: rewrite from the supplied list, or — when
        // the caller didn't pass one (whole-clip save, bulk edit) — preserve
        // whatever moments the existing sidecar already holds so they're never
        // silently dropped.
        IReadOnlyList<SidecarMoment> momentPayload;
        if (moments is not null)
        {
            momentPayload = moments.Select(MapMoment).ToArray();
        }
        else
        {
            momentPayload = await ReadExistingMomentsAsync(sidecarPath, cancellationToken).ConfigureAwait(false);
        }

        var payload = BuildPayload(video, tags, momentPayload);

        try
        {
            // Atomic-ish write: write to a temp file in the same folder, then
            // replace. This avoids leaving half-written JSON if the process is
            // killed mid-write.
            var tempPath = sidecarPath + ".tmp";

            await using (var fs = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1 << 14,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(fs, payload, JsonOptions, cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(sidecarPath))
            {
                File.Replace(tempPath, sidecarPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, sidecarPath);
            }

            return SidecarWriteResult.Ok(sidecarPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Sidecar write denied for {Path}", sidecarPath);
            return SidecarWriteResult.Failure($"Access denied: {ex.Message}", sidecarPath);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Sidecar I/O error for {Path}", sidecarPath);
            return SidecarWriteResult.Failure(ex.Message, sidecarPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sidecar write failed for {Path}", sidecarPath);
            return SidecarWriteResult.Failure(ex.Message, sidecarPath);
        }
    }

    public async Task<SidecarBatchResult> WriteManyAsync(
        IEnumerable<(VideoItem Video, IReadOnlyList<Tag> Tags)> items,
        CancellationToken cancellationToken = default)
    {
        if (items is null)
        {
            return new SidecarBatchResult();
        }

        // No blanket skip when disabled: each item is decided individually by
        // WriteAsync, so existing sidecars among the batch are kept in sync
        // while clips without a sidecar are left untouched.
        var attempted = 0;
        var written = 0;
        var failed = 0;
        var skipped = 0;
        var errors = new List<string>();

        foreach (var (video, tags) in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempted++;
            // moments left null → existing moments in each sidecar are preserved.
            var result = await WriteAsync(video, tags, moments: null, cancellationToken).ConfigureAwait(false);
            if (result.Written) written++;
            else if (result.Skipped) skipped++;
            else
            {
                failed++;
                if (!string.IsNullOrWhiteSpace(result.ErrorMessage) && errors.Count < 10)
                {
                    errors.Add($"{video.FilePath}: {result.ErrorMessage}");
                }
            }
        }

        return new SidecarBatchResult
        {
            Attempted = attempted,
            Written = written,
            Failed = failed,
            Skipped = skipped,
            Errors = errors
        };
    }

    public async Task<SidecarData?> TryReadAsync(
        string videoPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoPath)) return null;

        var sidecarPath = GetSidecarPathFor(videoPath);
        try
        {
            if (!File.Exists(sidecarPath)) return null;

            await using var fs = new FileStream(
                sidecarPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1 << 14,
                useAsync: true);

            var payload = await JsonSerializer
                .DeserializeAsync<SidecarPayload>(fs, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (payload is null) return null;

            return new SidecarData
            {
                Rating = payload.Rating,
                Status = payload.Status,
                Notes = payload.Notes,
                LocationText = payload.LocationText,
                ContextText = payload.ContextText,
                FolderDate = payload.FolderDate,
                Tags = payload.Tags
                    .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                    .Select(t => new SidecarTagData { Name = t.Name, Type = t.Type })
                    .ToArray(),
                Moments = payload.Moments
                    .Select(m => new SidecarMomentData
                    {
                        StartSeconds = m.StartSeconds,
                        EndSeconds = m.EndSeconds,
                        Label = m.Label,
                        Notes = m.Notes,
                        Rating = m.Rating,
                        Tags = (m.Tags ?? Array.Empty<SidecarTag>())
                            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                            .Select(t => new SidecarTagData { Name = t.Name, Type = t.Type })
                            .ToArray()
                    })
                    .ToArray()
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Corrupt/partial JSON, locked file, offline drive — metadata is
            // best-effort, so degrade to "no sidecar" rather than failing.
            _logger.LogWarning(ex, "Sidecar read failed for {Path}", sidecarPath);
            return null;
        }
    }

    private static SidecarPayload BuildPayload(VideoItem video, IEnumerable<Tag> tags, IReadOnlyList<SidecarMoment> moments)
    {
        return new SidecarPayload
        {
            Schema = SchemaId,
            WrittenAtUtc = DateTime.UtcNow,
            File = new SidecarFile
            {
                Name = video.FileName,
                SizeBytes = video.FileSize,
                DurationSeconds = video.DurationSeconds,
                Width = video.Width,
                Height = video.Height,
                FrameRate = video.FrameRate,
                Codec = video.Codec,
                Camera = video.Camera
            },
            Rating = video.Rating,
            Status = video.Status.ToString(),
            Notes = video.Notes,
            LocationText = video.LocationText,
            ContextText = video.ContextText,
            FolderDate = video.FolderDate,
            Tags = tags
                .Select(t => new SidecarTag { Name = t.Name, Type = t.Type.ToString() })
                .ToArray(),
            Moments = moments
        };
    }

    private static SidecarMoment MapMoment(VideoMoment m)
    {
        return new SidecarMoment
        {
            StartSeconds = m.StartSeconds,
            EndSeconds = m.EndSeconds,
            Label = m.Label,
            Notes = m.Notes,
            Rating = m.Rating,
            Tags = (m.MomentTags ?? new List<MomentTag>())
                .Where(mt => mt.Tag is not null)
                .Select(mt => new SidecarTag { Name = mt.Tag.Name, Type = mt.Tag.Type.ToString() })
                .ToArray()
        };
    }

    // Reads just the moments array from an existing sidecar so a write that
    // doesn't supply moments can carry the user's existing record forward.
    // Returns an empty list when there's no file or it can't be parsed.
    private async Task<IReadOnlyList<SidecarMoment>> ReadExistingMomentsAsync(string sidecarPath, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(sidecarPath)) return Array.Empty<SidecarMoment>();

            await using var fs = new FileStream(
                sidecarPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1 << 14,
                useAsync: true);

            var payload = await JsonSerializer
                .DeserializeAsync<SidecarPayload>(fs, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return payload?.Moments ?? Array.Empty<SidecarMoment>();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read existing moments from {Path}; writing without them.", sidecarPath);
            return Array.Empty<SidecarMoment>();
        }
    }

    private sealed class SidecarPayload
    {
        [JsonPropertyName("schema")]
        public string Schema { get; set; } = SchemaId;

        [JsonPropertyName("writtenAtUtc")]
        public DateTime WrittenAtUtc { get; set; }

        [JsonPropertyName("file")]
        public SidecarFile? File { get; set; }

        [JsonPropertyName("rating")]
        public int Rating { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }

        [JsonPropertyName("locationText")]
        public string? LocationText { get; set; }

        [JsonPropertyName("contextText")]
        public string? ContextText { get; set; }

        [JsonPropertyName("folderDate")]
        public DateTime? FolderDate { get; set; }

        [JsonPropertyName("tags")]
        public IReadOnlyList<SidecarTag> Tags { get; set; } = Array.Empty<SidecarTag>();

        [JsonPropertyName("moments")]
        public IReadOnlyList<SidecarMoment> Moments { get; set; } = Array.Empty<SidecarMoment>();
    }

    private sealed class SidecarMoment
    {
        [JsonPropertyName("startSeconds")]
        public double StartSeconds { get; set; }

        [JsonPropertyName("endSeconds")]
        public double? EndSeconds { get; set; }

        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }

        [JsonPropertyName("rating")]
        public int Rating { get; set; }

        [JsonPropertyName("tags")]
        public IReadOnlyList<SidecarTag> Tags { get; set; } = Array.Empty<SidecarTag>();
    }

    private sealed class SidecarFile
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("sizeBytes")]
        public long SizeBytes { get; set; }

        [JsonPropertyName("durationSeconds")]
        public double? DurationSeconds { get; set; }

        [JsonPropertyName("width")]
        public int? Width { get; set; }

        [JsonPropertyName("height")]
        public int? Height { get; set; }

        [JsonPropertyName("frameRate")]
        public double? FrameRate { get; set; }

        [JsonPropertyName("codec")]
        public string? Codec { get; set; }

        [JsonPropertyName("camera")]
        public string? Camera { get; set; }
    }

    private sealed class SidecarTag
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;
    }
}
