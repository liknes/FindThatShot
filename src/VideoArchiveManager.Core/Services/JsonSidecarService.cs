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
    public const string SchemaId = "findthatshot/v1";

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

    public async Task<SidecarWriteResult> WriteAsync(
        VideoItem video,
        IEnumerable<Tag> tags,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return SidecarWriteResult.DisabledOrSkipped();
        }

        if (video is null || string.IsNullOrWhiteSpace(video.FilePath))
        {
            return SidecarWriteResult.Failure("Video has no file path.");
        }

        var sidecarPath = GetSidecarPathFor(video.FilePath);

        var folder = Path.GetDirectoryName(sidecarPath);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            // Drive offline or folder gone — quietly skip; will be retried next save.
            return SidecarWriteResult.Failure(
                $"Folder not available: '{folder}'.",
                sidecarPath);
        }

        var payload = BuildPayload(video, tags);

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
        if (!IsEnabled)
        {
            return new SidecarBatchResult { Skipped = items?.Count() ?? 0 };
        }

        var attempted = 0;
        var written = 0;
        var failed = 0;
        var skipped = 0;
        var errors = new List<string>();

        foreach (var (video, tags) in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempted++;
            var result = await WriteAsync(video, tags, cancellationToken).ConfigureAwait(false);
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

    private static SidecarPayload BuildPayload(VideoItem video, IEnumerable<Tag> tags)
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
                .ToArray()
        };
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
