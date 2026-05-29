using System.Text.Json;

namespace VideoArchiveManager.Core.Configuration;

public class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private AppSettings _current;

    public JsonSettingsStore(AppSettings? initial = null)
    {
        _current = initial ?? new AppSettings();
        _current.SupportedExtensions = DedupePreserveOrder(_current.SupportedExtensions);
        _current.ExcludedFolderNames = DedupePreserveOrder(_current.ExcludedFolderNames);
        _current.ExcludedFileNamePatterns = DedupePreserveOrder(_current.ExcludedFileNamePatterns);
        _current = MergeWithUserOverrides(_current);
    }

    public AppSettings Current => _current;

    public AppSettings Load()
    {
        _current = MergeWithUserOverrides(_current);
        return _current;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var path = AppSettings.UserSettingsPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Sanitize list-typed fields before persisting so the file never
        // accumulates duplicates (which Microsoft.Extensions.Configuration's
        // Bind() can introduce when defaults + JSON both contribute values).
        settings.SupportedExtensions = DedupePreserveOrder(settings.SupportedExtensions);
        settings.ExcludedFolderNames = DedupePreserveOrder(settings.ExcludedFolderNames);
        settings.ExcludedFileNamePatterns = DedupePreserveOrder(settings.ExcludedFileNamePatterns);

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        _current = settings;
    }

    private static AppSettings MergeWithUserOverrides(AppSettings baseline)
    {
        var path = AppSettings.UserSettingsPath;
        if (!File.Exists(path))
        {
            return baseline;
        }

        try
        {
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (loaded == null) return baseline;

            return new AppSettings
            {
                FfmpegPath = !string.IsNullOrWhiteSpace(loaded.FfmpegPath) ? loaded.FfmpegPath : baseline.FfmpegPath,
                FfprobePath = !string.IsNullOrWhiteSpace(loaded.FfprobePath) ? loaded.FfprobePath : baseline.FfprobePath,
                ThumbnailDirectory = !string.IsNullOrWhiteSpace(loaded.ThumbnailDirectory) ? loaded.ThumbnailDirectory : baseline.ThumbnailDirectory,
                DatabasePath = !string.IsNullOrWhiteSpace(loaded.DatabasePath) ? loaded.DatabasePath : baseline.DatabasePath,
                BackupDirectory = !string.IsNullOrWhiteSpace(loaded.BackupDirectory) ? loaded.BackupDirectory : baseline.BackupDirectory,
                AutoBackupOnStartup = loaded.AutoBackupOnStartup,
                BackupRetentionCount = loaded.BackupRetentionCount > 0 ? loaded.BackupRetentionCount : baseline.BackupRetentionCount,
                WriteSidecarFiles = loaded.WriteSidecarFiles,
                UpdateRepoUrl = !string.IsNullOrWhiteSpace(loaded.UpdateRepoUrl) ? loaded.UpdateRepoUrl : baseline.UpdateRepoUrl,
                MaxScanParallelism = loaded.MaxScanParallelism > 0 ? loaded.MaxScanParallelism : baseline.MaxScanParallelism,
                PageSize = loaded.PageSize > 0 ? loaded.PageSize : baseline.PageSize,
                SupportedExtensions = DedupePreserveOrder(
                    loaded.SupportedExtensions is { Count: > 0 } ? loaded.SupportedExtensions : baseline.SupportedExtensions),
                ExcludedFolderNames = DedupePreserveOrder(
                    loaded.ExcludedFolderNames is not null ? loaded.ExcludedFolderNames : baseline.ExcludedFolderNames),
                ExcludedFileNamePatterns = DedupePreserveOrder(
                    loaded.ExcludedFileNamePatterns is not null ? loaded.ExcludedFileNamePatterns : baseline.ExcludedFileNamePatterns)
            };
        }
        catch
        {
            return baseline;
        }
    }

    private static IReadOnlyList<string> DedupePreserveOrder(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0) return Array.Empty<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(values.Count);
        foreach (var v in values)
        {
            if (string.IsNullOrWhiteSpace(v)) continue;
            if (seen.Add(v)) result.Add(v);
        }
        return result;
    }
}
