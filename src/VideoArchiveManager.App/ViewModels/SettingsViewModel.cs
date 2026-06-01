using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VideoArchiveManager.Core.Configuration;
using VideoArchiveManager.Core.Services;

namespace VideoArchiveManager.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _store;
    private readonly ICatalogBackupService _backupService;

    public SettingsViewModel(ISettingsStore store, ICatalogBackupService backupService)
    {
        _store = store;
        _backupService = backupService;

        var current = store.Current;
        _ffmpegPath = current.FfmpegPath ?? string.Empty;
        _ffprobePath = current.FfprobePath ?? string.Empty;
        _thumbnailDirectory = current.EffectiveThumbnailDirectory;
        _databasePath = current.EffectiveDatabasePath;
        _maxScanParallelism = current.MaxScanParallelism;
        _excludedFolderNames = JoinList(current.ExcludedFolderNames);
        _excludedFileNamePatterns = JoinList(current.ExcludedFileNamePatterns);
        _backupDirectory = current.EffectiveBackupDirectory;
        _autoBackupOnStartup = current.AutoBackupOnStartup;
        _backupRetentionCount = current.BackupRetentionCount;
        _writeSidecarFiles = current.WriteSidecarFiles;

        _ = RefreshBackupsAsync();
    }

    public ObservableCollection<CatalogBackupInfo> Backups { get; } = new();

    [ObservableProperty]
    private CatalogBackupInfo? _selectedBackup;

    [ObservableProperty]
    private string _ffmpegPath;

    [ObservableProperty]
    private string _ffprobePath;

    [ObservableProperty]
    private string _thumbnailDirectory;

    [ObservableProperty]
    private string _databasePath;

    [ObservableProperty]
    private int _maxScanParallelism;

    [ObservableProperty]
    private string _excludedFolderNames;

    [ObservableProperty]
    private string _excludedFileNamePatterns;

    [ObservableProperty]
    private string _backupDirectory;

    [ObservableProperty]
    private bool _autoBackupOnStartup;

    [ObservableProperty]
    private int _backupRetentionCount;

    [ObservableProperty]
    private bool _writeSidecarFiles;

    [ObservableProperty]
    private string _backupStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBackingUp;

    public event Action? Saved;
    public event Action? Cancelled;

    [RelayCommand]
    private void BrowseFfmpeg()
    {
        var path = PickExecutable("Select ffmpeg.exe", FfmpegPath);
        if (!string.IsNullOrEmpty(path)) FfmpegPath = path;
    }

    [RelayCommand]
    private void BrowseFfprobe()
    {
        var path = PickExecutable("Select ffprobe.exe", FfprobePath);
        if (!string.IsNullOrEmpty(path)) FfprobePath = path;
    }

    [RelayCommand]
    private void BrowseBackupDirectory()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select catalog backup folder",
            InitialDirectory = string.IsNullOrWhiteSpace(BackupDirectory) ? null! : BackupDirectory
        };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            BackupDirectory = dialog.FolderName;
        }
    }

    [RelayCommand]
    private async Task BackupNowAsync()
    {
        if (IsBackingUp) return;
        IsBackingUp = true;
        BackupStatusMessage = "Backing up catalog...";

        var settings = BuildSettings();
        await _store.SaveAsync(settings);

        var result = await _backupService.BackupNowAsync();
        if (result.Success)
        {
            BackupStatusMessage = result.Pruned > 0
                ? $"Backup saved to {result.BackupPath} ({FormatBytes(result.Bytes)}). Pruned {result.Pruned} old backup(s)."
                : $"Backup saved to {result.BackupPath} ({FormatBytes(result.Bytes)}).";
        }
        else
        {
            BackupStatusMessage = $"Backup failed: {result.ErrorMessage}";
        }

        await RefreshBackupsAsync();
        IsBackingUp = false;
    }

    [RelayCommand]
    private async Task RefreshBackupsAsync()
    {
        var list = await _backupService.ListBackupsAsync();
        Backups.Clear();
        foreach (var b in list) Backups.Add(b);
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (SelectedBackup is null) return;

        var confirm = MessageBox.Show(
            $"Restore catalog from this backup?\n\n{SelectedBackup.FileName}\n" +
            $"Created: {SelectedBackup.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n" +
            $"Size: {FormatBytes(SelectedBackup.Bytes)}\n\n" +
            "Your current catalog will first be copied into the Backups folder as a " +
            "safety snapshot ('catalog-pre-restore-...'). The app will then need to RESTART " +
            "to swap the database file in safely. Source video files are not affected.",
            "Restore catalog",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (confirm != MessageBoxResult.OK) return;

        BackupStatusMessage = "Staging restore...";
        var result = await _backupService.RestoreAsync(SelectedBackup.Path);

        if (!result.Success)
        {
            BackupStatusMessage = $"Restore failed: {result.ErrorMessage}";
            MessageBox.Show(
                $"Restore failed:\n\n{result.ErrorMessage}\n\nYour catalog has not been changed.",
                "Restore catalog",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        BackupStatusMessage = $"Restore staged. Safety copy: {result.SafetyBackupPath}";

        var restartNow = MessageBox.Show(
            "Restore staged successfully.\n\n" +
            (result.SafetyBackupPath is null
                ? string.Empty
                : $"Safety copy of the previous catalog: {result.SafetyBackupPath}\n\n") +
            "The app needs to restart to complete the restore. Restart now?",
            "Restore catalog",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information,
            MessageBoxResult.OK);

        if (restartNow == MessageBoxResult.OK)
        {
            RestartApp();
        }
        else
        {
            MessageBox.Show(
                "The restore will be applied automatically the next time you start the app.",
                "Restore catalog",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private static void RestartApp()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = true
                });
            }
        }
        catch
        {
            // If we can't relaunch, the user can start the app manually —
            // the staged restore will still be applied on next startup.
        }
        Application.Current.Shutdown();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var settings = BuildSettings();
        await _store.SaveAsync(settings);
        Saved?.Invoke();
    }

    private AppSettings BuildSettings()
    {
        return new AppSettings
        {
            FfmpegPath = string.IsNullOrWhiteSpace(FfmpegPath) ? null : FfmpegPath,
            FfprobePath = string.IsNullOrWhiteSpace(FfprobePath) ? null : FfprobePath,
            ThumbnailDirectory = ThumbnailDirectory,
            DatabasePath = DatabasePath,
            BackupDirectory = BackupDirectory,
            AutoBackupOnStartup = AutoBackupOnStartup,
            BackupRetentionCount = BackupRetentionCount > 0 ? BackupRetentionCount : 7,
            WriteSidecarFiles = WriteSidecarFiles,
            MaxScanParallelism = MaxScanParallelism > 0 ? MaxScanParallelism : 4,
            PageSize = _store.Current.PageSize,
            SupportedExtensions = _store.Current.SupportedExtensions,
            ExcludedFolderNames = SplitList(ExcludedFolderNames),
            ExcludedFileNamePatterns = SplitList(ExcludedFileNamePatterns),
            // Carry forward sidebar layout state from the live store
            // so saving the Settings dialog doesn't accidentally reset
            // the user's last drag-resize / collapse state on the rail.
            SidebarWidth = _store.Current.SidebarWidth,
            SidebarFoldersExpanded = _store.Current.SidebarFoldersExpanded,
            SidebarTagsExpanded = _store.Current.SidebarTagsExpanded,
            SidebarCamerasExpanded = _store.Current.SidebarCamerasExpanded
        };
    }

    private static string JoinList(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0) return string.Empty;
        return string.Join(", ", values);
    }

    private static IReadOnlyList<string> SplitList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        return raw
            .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToArray();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double v = bytes;
        string[] units = { "KB", "MB", "GB", "TB" };
        var i = -1;
        do { v /= 1024; i++; } while (v >= 1024 && i < units.Length - 1);
        return $"{v:0.##} {units[i]}";
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();

    private static string? PickExecutable(string title, string? initial)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
            FileName = initial ?? string.Empty
        };
        var result = dialog.ShowDialog();
        return result == true ? dialog.FileName : null;
    }
}
