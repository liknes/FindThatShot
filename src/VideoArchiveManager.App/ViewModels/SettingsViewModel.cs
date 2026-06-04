using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VideoArchiveManager.Core.Configuration;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Services;
using VideoArchiveManager.Core.Services.Ai;

namespace VideoArchiveManager.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _store;
    private readonly ICatalogBackupService _backupService;
    private readonly ITagService _tagService;
    private readonly IAiModelProvider _aiModelProvider;
    private readonly IAiTaggingService _aiTaggingService;

    public SettingsViewModel(
        ISettingsStore store,
        ICatalogBackupService backupService,
        ITagService tagService,
        IAiModelProvider aiModelProvider,
        IAiTaggingService aiTaggingService)
    {
        _store = store;
        _backupService = backupService;
        _tagService = tagService;
        _aiModelProvider = aiModelProvider;
        _aiTaggingService = aiTaggingService;

        // Ten fixed hotkey slots (digits 1-9 then 0). Built up front so the
        // Settings UI can bind immediately; the tag catalog + the user's
        // current bindings are filled in asynchronously by LoadPinnedTagsAsync.
        for (var i = 0; i < 10; i++)
        {
            PinnedTagSlots.Add(new PinnedTagSlotViewModel(i));
        }

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
        _preferProxyForPlayback = current.PreferProxyForPlayback;
        _showDroneFlightPaths = current.ShowDroneFlightPaths;
        _showPlayerTelemetry = current.ShowPlayerTelemetry;
        _enableAiTagging = current.EnableAiTagging;
        _aiModelDirectory = current.AiModelDirectory ?? string.Empty;
        _aiFramesPerClip = current.AiFramesPerClip;
        _aiSuggestionThreshold = current.AiSuggestionThreshold;

        _ = RefreshBackupsAsync();
        _ = LoadPinnedTagsAsync();
        RefreshAiModelStatus();
    }

    public ObservableCollection<CatalogBackupInfo> Backups { get; } = new();

    // Every tag in the catalog, shown in each slot's picker. Loaded once on
    // open by LoadPinnedTagsAsync (ordered by type then name, matching the
    // sidebar picker).
    public ObservableCollection<Tag> AvailableTags { get; } = new();

    // The ten review-mode hotkey slots (index 0 = "1" key … index 9 = "0"
    // key). Each holds an optional Tag; an empty slot leaves that digit unbound.
    public ObservableCollection<PinnedTagSlotViewModel> PinnedTagSlots { get; } = new();

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
    private bool _preferProxyForPlayback;

    [ObservableProperty]
    private bool _showDroneFlightPaths;

    [ObservableProperty]
    private bool _showPlayerTelemetry;

    [ObservableProperty]
    private string _backupStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBackingUp;

    // --- AI auto-tagging & semantic search -------------------------------
    [ObservableProperty]
    private bool _enableAiTagging;

    [ObservableProperty]
    private string _aiModelDirectory;

    [ObservableProperty]
    private int _aiFramesPerClip;

    [ObservableProperty]
    private double _aiSuggestionThreshold;

    [ObservableProperty]
    private string _aiModelStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownloadModel))]
    private bool _aiModelInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownloadModel))]
    private bool _isDownloadingModel;

    [ObservableProperty]
    private double _aiDownloadProgress;

    public bool CanDownloadModel => !AiModelInstalled && !IsDownloadingModel;

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

    // Loads the tag catalog into the slot pickers and selects whatever the
    // user has already pinned. Resolves stored bindings by name + type (the
    // catalog's natural key) against the freshly-loaded AvailableTags so the
    // ComboBox shows the exact instance it has in its item list.
    private async Task LoadPinnedTagsAsync()
    {
        var all = await _tagService.GetAllAsync();
        AvailableTags.Clear();
        foreach (var t in all) AvailableTags.Add(t);

        var pinned = _store.Current.PinnedTags;
        if (pinned is null) return;

        foreach (var p in pinned)
        {
            if (p is null || p.Slot < 0 || p.Slot > 9) continue;
            var match = AvailableTags.FirstOrDefault(
                t => t.Type == p.Type && string.Equals(t.Name, p.Name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                PinnedTagSlots[p.Slot].SelectedTag = match;
            }
        }
    }

    // Bootstraps the bindings from the user's existing vocabulary: drops the
    // most-used tags (that aren't already pinned) into the empty slots, top to
    // bottom. Filled slots are left untouched so this never clobbers a
    // deliberate choice.
    [RelayCommand]
    private async Task FillFromMostUsedAsync()
    {
        var top = await _tagService.GetMostUsedAsync(10);
        if (top.Count == 0) return;

        var alreadyPinned = PinnedTagSlots
            .Where(s => s.SelectedTag is not null)
            .Select(s => s.SelectedTag!.Id)
            .ToHashSet();

        var candidates = new Queue<Tag>(top.Where(t => !alreadyPinned.Contains(t.Id)));

        foreach (var slot in PinnedTagSlots)
        {
            if (candidates.Count == 0) break;
            if (slot.SelectedTag is not null) continue;
            var tag = candidates.Dequeue();
            // Prefer the AvailableTags instance so the ComboBox renders it as
            // selected (GetMostUsedAsync returns rows from a separate context).
            slot.SelectedTag = AvailableTags.FirstOrDefault(t => t.Id == tag.Id) ?? tag;
        }
    }

    [RelayCommand]
    private void ClearAllPinnedTags()
    {
        foreach (var slot in PinnedTagSlots)
        {
            slot.SelectedTag = null;
        }
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
            PreferProxyForPlayback = PreferProxyForPlayback,
            ShowDroneFlightPaths = ShowDroneFlightPaths,
            ShowPlayerTelemetry = ShowPlayerTelemetry,
            EnableAiTagging = EnableAiTagging,
            AiModelDirectory = string.IsNullOrWhiteSpace(AiModelDirectory) ? null : AiModelDirectory,
            AiModelDownloadUrl = _store.Current.AiModelDownloadUrl,
            AiFramesPerClip = AiFramesPerClip is > 0 and <= 64 ? AiFramesPerClip : 9,
            AiSuggestionThreshold = AiSuggestionThreshold is > 0 and < 1 ? AiSuggestionThreshold : 0.26,
            AiMaxSuggestionsPerClip = _store.Current.AiMaxSuggestionsPerClip,
            MaxScanParallelism = MaxScanParallelism > 0 ? MaxScanParallelism : 4,
            PageSize = _store.Current.PageSize,
            SupportedExtensions = _store.Current.SupportedExtensions,
            ExcludedFolderNames = SplitList(ExcludedFolderNames),
            ExcludedFileNamePatterns = SplitList(ExcludedFileNamePatterns),
            // Review-mode hotkey bindings: one entry per filled slot, carrying
            // its digit slot so empty slots are simply absent (no gaps to track).
            PinnedTags = PinnedTagSlots
                .Where(s => s.SelectedTag is not null)
                .Select(s => new PinnedTag
                {
                    Slot = s.Slot,
                    Name = s.SelectedTag!.Name,
                    Type = s.SelectedTag!.Type
                })
                .ToList(),
            // Carry forward sidebar layout state from the live store
            // so saving the Settings dialog doesn't accidentally reset
            // the user's last drag-resize / collapse state on the rail.
            SidebarWidth = _store.Current.SidebarWidth,
            SidebarFoldersExpanded = _store.Current.SidebarFoldersExpanded,
            SidebarTagsExpanded = _store.Current.SidebarTagsExpanded,
            SidebarCamerasExpanded = _store.Current.SidebarCamerasExpanded,
            SidebarDateExpanded = _store.Current.SidebarDateExpanded
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

    private void RefreshAiModelStatus()
    {
        // Persist the latest enable/path choice into the live store first so
        // the provider resolves against what the user currently sees.
        AiModelInstalled = _aiModelProvider.IsModelInstalled();
        if (!EnableAiTagging)
        {
            AiModelStatus = "Disabled. Turn on to enable AI tagging and natural-language search.";
        }
        else if (AiModelInstalled)
        {
            AiModelStatus = $"Model ready ({_aiModelProvider.ModelDirectory}).";
        }
        else
        {
            AiModelStatus = "Model not installed. Download it, or point at a folder containing the CLIP ONNX bundle.";
        }
    }

    partial void OnEnableAiTaggingChanged(bool value) => RefreshAiModelStatus();

    partial void OnAiModelDirectoryChanged(string value)
    {
        // Reflect a freshly-typed/picked drop-in folder immediately by saving
        // it to the live store so the provider resolves there.
        var settings = BuildSettings();
        _store.SaveAsync(settings);
        RefreshAiModelStatus();
    }

    [RelayCommand]
    private void BrowseAiModelDirectory()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the CLIP model folder",
            InitialDirectory = string.IsNullOrWhiteSpace(AiModelDirectory) ? null! : AiModelDirectory
        };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            AiModelDirectory = dialog.FolderName;
        }
    }

    [RelayCommand]
    private async Task DownloadModelAsync()
    {
        if (IsDownloadingModel || AiModelInstalled) return;

        // Make sure the enable flag / paths are persisted before downloading.
        await _store.SaveAsync(BuildSettings());

        IsDownloadingModel = true;
        AiDownloadProgress = 0;
        AiModelStatus = "Downloading model…";
        try
        {
            var progress = new Progress<double>(p => AiDownloadProgress = p);
            var ok = await _aiModelProvider.EnsureDownloadedAsync(progress);
            AiModelStatus = ok
                ? "Model downloaded and ready."
                : "Download failed. Check the model URL / your connection, or supply the bundle manually.";
        }
        catch (Exception ex)
        {
            AiModelStatus = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsDownloadingModel = false;
            RefreshAiModelStatus();
        }
    }

    [RelayCommand]
    private async Task ClearAiDataAsync()
    {
        var confirm = MessageBox.Show(
            "Remove all AI-generated data (embeddings and pending tag suggestions) from the catalog?\n\n" +
            "Tags you've already accepted stay. Source files are never touched. You can regenerate later.",
            "Clear AI data",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel);
        if (confirm != MessageBoxResult.OK) return;

        var removed = await _aiTaggingService.ClearAllAiDataAsync();
        AiModelStatus = $"Cleared {removed:N0} AI row(s).";
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

// One review-mode hotkey row in the Settings dialog: the digit it's triggered
// by plus the (optional) tag bound to it. Slot 0 is the "1" key … slot 8 the
// "9" key, slot 9 the "0" key, matching VideoDetailViewModel.ToggleTagBySlotAsync.
public partial class PinnedTagSlotViewModel : ObservableObject
{
    public PinnedTagSlotViewModel(int slot)
    {
        Slot = slot;
    }

    public int Slot { get; }

    // The digit printed on the triggering key (slot 9 maps to "0").
    public string KeyLabel => Slot == 9 ? "0" : (Slot + 1).ToString();

    [ObservableProperty]
    private Tag? _selectedTag;

    [RelayCommand]
    private void Clear() => SelectedTag = null;
}
