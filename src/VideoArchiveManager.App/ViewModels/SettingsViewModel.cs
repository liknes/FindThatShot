using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VideoArchiveManager.Core.Configuration;

namespace VideoArchiveManager.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _store;

    public SettingsViewModel(ISettingsStore store)
    {
        _store = store;
        var current = store.Current;
        _ffmpegPath = current.FfmpegPath ?? string.Empty;
        _ffprobePath = current.FfprobePath ?? string.Empty;
        _thumbnailDirectory = current.EffectiveThumbnailDirectory;
        _databasePath = current.EffectiveDatabasePath;
        _maxScanParallelism = current.MaxScanParallelism;
        _excludedFolderNames = JoinList(current.ExcludedFolderNames);
        _excludedFileNamePatterns = JoinList(current.ExcludedFileNamePatterns);
    }

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
    private async Task SaveAsync()
    {
        var settings = new AppSettings
        {
            FfmpegPath = string.IsNullOrWhiteSpace(FfmpegPath) ? null : FfmpegPath,
            FfprobePath = string.IsNullOrWhiteSpace(FfprobePath) ? null : FfprobePath,
            ThumbnailDirectory = ThumbnailDirectory,
            DatabasePath = DatabasePath,
            MaxScanParallelism = MaxScanParallelism > 0 ? MaxScanParallelism : 4,
            PageSize = _store.Current.PageSize,
            SupportedExtensions = _store.Current.SupportedExtensions,
            ExcludedFolderNames = SplitList(ExcludedFolderNames),
            ExcludedFileNamePatterns = SplitList(ExcludedFileNamePatterns)
        };
        await _store.SaveAsync(settings);
        Saved?.Invoke();
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
