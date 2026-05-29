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

    // GitHub repo (https://github.com/owner/repo) that hosts Velopack
    // release artifacts. The app's "Check for updates" command reads this
    // value at runtime. Empty / null disables the check.
    public string? UpdateRepoUrl { get; set; } = "https://github.com/liknes/FindThatShot";

    public int MaxScanParallelism { get; set; } = 4;

    public int PageSize { get; set; } = 200;

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
