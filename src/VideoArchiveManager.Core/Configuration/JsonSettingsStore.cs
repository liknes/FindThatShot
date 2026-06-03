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
        _current.PinnedTags = SanitizePinnedTags(_current.PinnedTags);
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
        settings.PinnedTags = SanitizePinnedTags(settings.PinnedTags);

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
                PreferProxyForPlayback = loaded.PreferProxyForPlayback,
                ShowDroneFlightPaths = loaded.ShowDroneFlightPaths,
                ShowPlayerTelemetry = loaded.ShowPlayerTelemetry,
                UpdateRepoUrl = !string.IsNullOrWhiteSpace(loaded.UpdateRepoUrl) ? loaded.UpdateRepoUrl : baseline.UpdateRepoUrl,
                MaxScanParallelism = loaded.MaxScanParallelism > 0 ? loaded.MaxScanParallelism : baseline.MaxScanParallelism,
                PageSize = loaded.PageSize > 0 ? loaded.PageSize : baseline.PageSize,
                // Sidebar width: clamp to a sane range so a corrupted /
                // hand-edited settings.json can't shove the rail off-screen
                // or down to a single-pixel slit.
                SidebarWidth = loaded.SidebarWidth >= 200d && loaded.SidebarWidth <= 600d
                    ? loaded.SidebarWidth
                    : baseline.SidebarWidth,
                SidebarFoldersExpanded = loaded.SidebarFoldersExpanded,
                SidebarTagsExpanded = loaded.SidebarTagsExpanded,
                SidebarCamerasExpanded = loaded.SidebarCamerasExpanded,
                SidebarDateExpanded = loaded.SidebarDateExpanded,
                PinnedTags = SanitizePinnedTags(
                    loaded.PinnedTags is not null ? loaded.PinnedTags : baseline.PinnedTags),
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

    // Normalises the pinned-tag list before it's persisted or surfaced:
    // drops entries with no name or an out-of-range slot, keeps only the
    // first tag seen per slot (so a hand-edited file with duplicate slots
    // can't bind two tags to one digit), and orders by slot for a tidy
    // settings.json. At most 10 entries survive (slots 0-9).
    private static IReadOnlyList<PinnedTag> SanitizePinnedTags(IReadOnlyList<PinnedTag>? values)
    {
        if (values is null || values.Count == 0) return Array.Empty<PinnedTag>();

        var seenSlots = new HashSet<int>();
        var result = new List<PinnedTag>(Math.Min(values.Count, 10));
        foreach (var p in values)
        {
            if (p is null) continue;
            if (p.Slot < 0 || p.Slot > 9) continue;
            if (string.IsNullOrWhiteSpace(p.Name)) continue;
            if (!seenSlots.Add(p.Slot)) continue;
            result.Add(new PinnedTag { Slot = p.Slot, Name = p.Name.Trim(), Type = p.Type });
        }

        result.Sort((a, b) => a.Slot.CompareTo(b.Slot));
        return result;
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
