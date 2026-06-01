namespace VideoArchiveManager.Core.Configuration;

public class AppSettings
{
    public string? FfmpegPath { get; set; }

    public string? FfprobePath { get; set; }

    public string? ThumbnailDirectory { get; set; }

    public string? DatabasePath { get; set; }

    public string? BackupDirectory { get; set; }

    public bool AutoBackupOnStartup { get; set; } = true;

    public int BackupRetentionCount { get; set; } = 7;

    public bool WriteSidecarFiles { get; set; } = false;

    // When true, the in-app player substitutes the matching DaVinci Resolve
    // proxy (sibling "Proxy" folder, same base name as the hero) for the
    // original clip on Play in app. Falls back to the hero file when no proxy
    // is found. Defaults to false so new users see no surprise substitution —
    // opt in via Settings once a proxy workflow is established. The catalog,
    // thumbnails, ffprobe metadata and "Play externally" command always
    // continue to point at the hero file regardless of this flag.
    public bool PreferProxyForPlayback { get; set; } = false;

    // GitHub repo (https://github.com/owner/repo) that hosts Velopack
    // release artifacts. The app's "Check for updates" command reads this
    // value at runtime. Empty / null disables the check.
    public string? UpdateRepoUrl { get; set; } = "https://github.com/liknes/FindThatShot";

    public int MaxScanParallelism { get; set; } = 4;

    public int PageSize { get; set; } = 200;

    // Persisted main-window sidebar layout. Width is the user's last
    // dragged width of the FOLDERS / TAGS / CAMERAS rail; the three
    // *Expanded flags persist Lightroom-style panel collapse state so
    // the user's layout choices survive an app restart. All four
    // honour their defaults if absent or out-of-range from settings.json.
    public double SidebarWidth { get; set; } = 260d;

    public bool SidebarFoldersExpanded { get; set; } = true;

    public bool SidebarTagsExpanded { get; set; } = true;

    public bool SidebarCamerasExpanded { get; set; } = true;

    public IReadOnlyList<string> SupportedExtensions { get; set; } = new[]
    {
        ".mp4", ".mov", ".mxf", ".avi", ".mkv"
    };

    public IReadOnlyList<string> ExcludedFolderNames { get; set; } = new[]
    {
        "proxy",
        "proxies",
        "edits",
        "_proxy",
        "_edits",
        "_originals",
        "@eaDir",
        "@Recycle",
        ".Trash",
        "$RECYCLE.BIN",
        "System Volume Information"
    };

    public IReadOnlyList<string> ExcludedFileNamePatterns { get; set; } = new[]
    {
        "*.LRV",
        "*.THM"
    };

    public static string DefaultBaseDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VideoArchiveManager");

    public static string DefaultDatabasePath =>
        Path.Combine(DefaultBaseDirectory, "catalog.db");

    public static string DefaultThumbnailDirectory =>
        Path.Combine(DefaultBaseDirectory, "Thumbnails");

    public static string DefaultBackupDirectory =>
        Path.Combine(DefaultBaseDirectory, "Backups");

    public static string UserSettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VideoArchiveManager",
            "settings.json");

    public string EffectiveDatabasePath =>
        string.IsNullOrWhiteSpace(DatabasePath) ? DefaultDatabasePath : DatabasePath;

    public string EffectiveThumbnailDirectory =>
        string.IsNullOrWhiteSpace(ThumbnailDirectory) ? DefaultThumbnailDirectory : ThumbnailDirectory;

    public string EffectiveBackupDirectory =>
        string.IsNullOrWhiteSpace(BackupDirectory) ? DefaultBackupDirectory : BackupDirectory;
}
