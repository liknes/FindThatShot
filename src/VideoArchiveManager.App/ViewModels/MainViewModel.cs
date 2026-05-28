using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using VideoArchiveManager.App.Views;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Models.Enums;
using VideoArchiveManager.Core.Services;
using VideoArchiveManager.Data;

namespace VideoArchiveManager.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IDbContextFactory<VideoArchiveDbContext> _contextFactory;
    private readonly ISearchService _searchService;
    private readonly ITagService _tagService;
    private readonly IVideoScannerService _scannerService;
    private readonly IVideoLibraryService _libraryService;
    private readonly IFfprobeService _ffprobeService;
    private readonly IThumbnailService _thumbnailService;
    private readonly IServiceProvider _services;

    private CancellationTokenSource? _scanCts;
    private DispatcherTimer? _scanHeartbeatTimer;
    private DateTime _scanStartedAt;
    private DateTime _currentFileStartedAt;
    private string? _currentFilePath;

    public MainViewModel(
        IDbContextFactory<VideoArchiveDbContext> contextFactory,
        ISearchService searchService,
        ITagService tagService,
        IVideoScannerService scannerService,
        IVideoLibraryService libraryService,
        IFfprobeService ffprobeService,
        IThumbnailService thumbnailService,
        IServiceProvider services,
        VideoDetailViewModel detail)
    {
        _contextFactory = contextFactory;
        _searchService = searchService;
        _tagService = tagService;
        _scannerService = scannerService;
        _libraryService = libraryService;
        _ffprobeService = ffprobeService;
        _thumbnailService = thumbnailService;
        _services = services;
        Detail = detail;
    }

    public ObservableCollection<RootFolder> RootFolders { get; } = new();
    public ObservableCollection<Tag> AllTags { get; } = new();
    public ObservableCollection<string> Cameras { get; } = new();
    public ObservableCollection<VideoStatus> AvailableStatuses { get; } = new(Enum.GetValues<VideoStatus>());
    public ObservableCollection<VideoItemViewModel> Videos { get; } = new();
    public ObservableCollection<VideoItemViewModel> SelectedVideos { get; } = new();

    public VideoDetailViewModel Detail { get; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private VideoStatus? _statusFilter;

    [ObservableProperty]
    private int _minRatingFilter;

    [ObservableProperty]
    private string? _cameraFilter;

    [ObservableProperty]
    private Tag? _selectedTagFilter;

    [ObservableProperty]
    private RootFolder? _selectedRootFolder;

    [ObservableProperty]
    private DateTime? _dateFrom;

    [ObservableProperty]
    private DateTime? _dateTo;

    [ObservableProperty]
    private bool _showOnlyAvailable = true;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private double _progressMaximum = 100;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _scanElapsedText = string.Empty;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private VideoItemViewModel? _selectedVideo;

    partial void OnSelectedVideoChanged(VideoItemViewModel? value)
    {
        _ = Detail.LoadAsync(value);
    }

    public async Task InitializeAsync()
    {
        if (!_ffprobeService.IsAvailable() || !_thumbnailService.IsAvailable())
        {
            StatusMessage = "FFmpeg/FFprobe not found. Configure paths in Settings.";
        }

        await ReloadFiltersAsync();
        await SearchAsync();
    }

    private async Task ReloadFiltersAsync()
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var roots = await ctx.RootFolders.OrderBy(r => r.Path).ToListAsync();
        RootFolders.Clear();
        foreach (var r in roots) RootFolders.Add(r);

        var tags = await _tagService.GetAllAsync();
        AllTags.Clear();
        foreach (var t in tags) AllTags.Add(t);

        var cams = await _searchService.GetDistinctCamerasAsync();
        Cameras.Clear();
        foreach (var c in cams) Cameras.Add(c);
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        var query = new SearchQuery
        {
            Text = SearchText,
            Status = StatusFilter,
            MinRating = MinRatingFilter > 0 ? MinRatingFilter : null,
            Camera = string.IsNullOrWhiteSpace(CameraFilter) ? null : CameraFilter,
            TagIds = SelectedTagFilter is null ? null : new[] { SelectedTagFilter.Id },
            DateFrom = DateFrom,
            DateTo = DateTo,
            RootFolderPath = SelectedRootFolder?.Path,
            FileExists = ShowOnlyAvailable ? true : null,
            Take = 500
        };

        var result = await _searchService.SearchAsync(query);
        Videos.Clear();
        foreach (var v in result.Items)
        {
            Videos.Add(new VideoItemViewModel(v));
        }
        TotalCount = result.TotalCount;
        StatusMessage = $"Showing {Videos.Count} of {TotalCount} videos";
    }

    [RelayCommand]
    private async Task AddRootFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select a root folder to scan"
        };
        if (dialog.ShowDialog() != true) return;
        var path = dialog.FolderName;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;

        await using var ctx = await _contextFactory.CreateDbContextAsync();
        if (await ctx.RootFolders.AnyAsync(r => r.Path == path))
        {
            StatusMessage = "Root folder already exists";
            return;
        }

        var rf = new RootFolder
        {
            Path = path,
            Name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        };
        ctx.RootFolders.Add(rf);
        await ctx.SaveChangesAsync();
        RootFolders.Add(rf);
        StatusMessage = $"Added root folder: {rf.Path}";
    }

    [RelayCommand]
    private async Task RemoveRootFolderAsync(RootFolder? folder)
    {
        if (folder is null) return;

        var videoCount = await _libraryService.CountUnderRootAsync(folder.Path);

        var message = videoCount > 0
            ? $"Remove the root folder \"{folder.Path}\" and {videoCount} video record(s) imported from it?\n\n" +
              "This affects the catalog database and the app's thumbnail cache only. " +
              "The source video files on disk will NOT be touched."
            : $"Remove the root folder \"{folder.Path}\"?\n\n" +
              "No video records were imported from this folder. The folder on disk will NOT be touched.";

        var result = MessageBox.Show(
            message,
            "Remove root folder",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel);
        if (result != MessageBoxResult.OK) return;

        await using (var ctx = await _contextFactory.CreateDbContextAsync())
        {
            var entity = await ctx.RootFolders.FirstOrDefaultAsync(r => r.Id == folder.Id);
            if (entity is null)
            {
                RootFolders.Remove(folder);
                return;
            }
            ctx.RootFolders.Remove(entity);
            await ctx.SaveChangesAsync();
        }

        var removedVideos = videoCount > 0
            ? await _libraryService.RemoveUnderRootAsync(folder.Path)
            : 0;

        RootFolders.Remove(folder);
        if (SelectedRootFolder?.Id == folder.Id) SelectedRootFolder = null;

        StatusMessage = removedVideos > 0
            ? $"Removed root folder and {removedVideos} video record(s) from the catalog. Source video files untouched."
            : "Removed root folder. Source video files untouched.";

        await ReloadFiltersAsync();
        await SearchAsync();
    }

    [RelayCommand]
    private async Task RemoveSelectedVideosAsync()
    {
        var ids = SelectedVideos.Select(v => v.Id).ToList();
        if (ids.Count == 0)
        {
            StatusMessage = "Select one or more videos to remove";
            return;
        }

        var preview = string.Join(
            Environment.NewLine,
            SelectedVideos.Take(5).Select(v => "  • " + v.FileName));
        if (SelectedVideos.Count > 5)
        {
            preview += Environment.NewLine + $"  …and {SelectedVideos.Count - 5} more";
        }

        var message =
            $"Remove {ids.Count} video record(s) from the database?\n\n" +
            preview + "\n\n" +
            "This removes the catalog entries (tags, ratings, notes, etc.) and their " +
            "cached thumbnails. The source video files on disk will NOT be touched.";

        var result = MessageBox.Show(
            message,
            "Remove videos from database",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel);
        if (result != MessageBoxResult.OK) return;

        var removed = await _libraryService.RemoveByIdsAsync(ids);

        if (SelectedVideo is not null && ids.Contains(SelectedVideo.Id))
        {
            SelectedVideo = null;
        }
        SelectedVideos.Clear();

        StatusMessage = $"Removed {removed} video record(s) from the catalog. Source video files untouched.";
        await SearchAsync();
    }

    [RelayCommand]
    private async Task RemoveOfflineVideosAsync()
    {
        await using (var ctx = await _contextFactory.CreateDbContextAsync())
        {
            var offlineCount = await ctx.VideoItems.CountAsync(v => !v.FileExists);
            if (offlineCount == 0)
            {
                MessageBox.Show(
                    "There are no offline video records to remove.",
                    "Remove offline videos",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Remove all {offlineCount} offline video record(s) from the database?\n\n" +
                "These are records whose source file is no longer found on disk " +
                "(based on the latest Refresh / scan).\n\n" +
                "This cleans up the catalog and the app's thumbnail cache. " +
                "No source video files will be touched.",
                "Remove offline videos",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Cancel);
            if (result != MessageBoxResult.OK) return;
        }

        var removed = await _libraryService.RemoveOfflineAsync();
        StatusMessage = $"Removed {removed} offline video record(s) from the catalog. Source video files untouched.";
        await ReloadFiltersAsync();
        await SearchAsync();
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning) return;
        if (RootFolders.Count == 0)
        {
            StatusMessage = "Add at least one root folder before scanning";
            return;
        }

        if (!_ffprobeService.IsAvailable())
        {
            MessageBox.Show(
                "ffprobe.exe was not found. Open Settings to configure the FFmpeg paths.",
                "Video Archive Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        ProgressValue = 0;
        ProgressMaximum = 100;

        _scanStartedAt = DateTime.UtcNow;
        _currentFileStartedAt = _scanStartedAt;
        _currentFilePath = null;
        ScanElapsedText = "Elapsed 00:00";
        StartHeartbeat();

        var progress = new Progress<ScanProgress>(p =>
        {
            if (p.TotalFound > 0) ProgressMaximum = p.TotalFound;
            ProgressValue = p.Processed;
            if (!string.Equals(p.CurrentFile, _currentFilePath, StringComparison.Ordinal))
            {
                _currentFilePath = p.CurrentFile;
                _currentFileStartedAt = DateTime.UtcNow;
            }
            StatusMessage = p.Message ?? p.CurrentFile ?? StatusMessage;
        });

        try
        {
            var roots = RootFolders.ToList();
            await _scannerService.ScanAsync(roots, progress, _scanCts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Scan cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan failed: {ex.Message}";
        }
        finally
        {
            StopHeartbeat();
            ScanElapsedText = string.Empty;
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }

        await ReloadFiltersAsync();
        await SearchAsync();
    }

    [RelayCommand]
    private void CancelScan()
    {
        _scanCts?.Cancel();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await _scannerService.UpdateFileAvailabilityAsync();
        await ReloadFiltersAsync();
        await SearchAsync();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var window = new SettingsWindow(_services.GetRequiredService<SettingsViewModel>())
        {
            Owner = App.Current.MainWindow
        };
        window.ShowDialog();
    }

    [RelayCommand]
    private async Task OpenBulkEditAsync()
    {
        var ids = SelectedVideos.Select(v => v.Id).ToList();
        if (ids.Count == 0) return;

        var vm = _services.GetRequiredService<BulkEditViewModel>();
        vm.Initialize(ids);
        var window = new BulkEditDialog(vm)
        {
            Owner = App.Current.MainWindow
        };
        window.ShowDialog();
        await SearchAsync();
    }

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        SearchText = string.Empty;
        StatusFilter = null;
        MinRatingFilter = 0;
        CameraFilter = null;
        SelectedTagFilter = null;
        SelectedRootFolder = null;
        DateFrom = null;
        DateTo = null;
        await SearchAsync();
    }

    private void StartHeartbeat()
    {
        StopHeartbeat();
        _scanHeartbeatTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _scanHeartbeatTimer.Tick += ScanHeartbeat_Tick;
        _scanHeartbeatTimer.Start();
    }

    private void StopHeartbeat()
    {
        if (_scanHeartbeatTimer is null) return;
        _scanHeartbeatTimer.Stop();
        _scanHeartbeatTimer.Tick -= ScanHeartbeat_Tick;
        _scanHeartbeatTimer = null;
    }

    private void ScanHeartbeat_Tick(object? sender, EventArgs e)
    {
        if (!IsScanning) return;
        var now = DateTime.UtcNow;
        var total = now - _scanStartedAt;
        var onFile = now - _currentFileStartedAt;
        ScanElapsedText = _currentFilePath is null
            ? $"Elapsed {FormatDuration(total)}"
            : $"Elapsed {FormatDuration(total)} · current file {FormatDuration(onFile)}";
    }

    private static string FormatDuration(TimeSpan t)
    {
        if (t.TotalHours >= 1)
        {
            return $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}";
        }
        return $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}";
    }
}
