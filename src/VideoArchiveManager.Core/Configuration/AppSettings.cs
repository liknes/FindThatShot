namespace VideoArchiveManager.Core.Configuration;

public class AppSettings
{
    public string? FfmpegPath { get; set; }

    public string? FfprobePath { get; set; }

    public string? ThumbnailDirectory { get; set; }

    public string? DatabasePath { get; set; }

    public int MaxScanParallelism { get; set; } = 4;

    public int PageSize { get; set; } = 200;

    public IReadOnlyList<string> SupportedExtensions { get; set; } = new[]
    {
        ".mp4", ".mov", ".mxf", ".avi", ".mkv"
    };

    public static string DefaultBaseDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VideoArchiveManager");

    public static string DefaultDatabasePath =>
        Path.Combine(DefaultBaseDirectory, "catalog.db");

    public static string DefaultThumbnailDirectory =>
        Path.Combine(DefaultBaseDirectory, "Thumbnails");

    public static string UserSettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VideoArchiveManager",
            "settings.json");

    public string EffectiveDatabasePath =>
        string.IsNullOrWhiteSpace(DatabasePath) ? DefaultDatabasePath : DatabasePath;

    public string EffectiveThumbnailDirectory =>
        string.IsNullOrWhiteSpace(ThumbnailDirectory) ? DefaultThumbnailDirectory : ThumbnailDirectory;
}
