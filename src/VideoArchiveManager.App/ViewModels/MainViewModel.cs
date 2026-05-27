using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
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
    private readonly IFfprobeService _ffprobeService;
    private readonly IThumbnailService _thumbnailService;
    private readonly IServiceProvider _services;

    private CancellationTokenSource? _scanCts;

    public MainViewModel(
        IDbContextFactory<VideoArchiveDbContext> contextFactory,
        ISearchService searchService,
        ITagService tagService,
        IVideoScannerService scannerService,
        IFfprobeService ffprobeService,
        IThumbnailService thumbnailService,
        IServiceProvider services,
        VideoDetailViewModel detail)
    {
        _contextFactory = contextFactory;
        _searchService = searchService;
        _tagService = tagService;
        _scannerService = scannerService;
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
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var entity = await ctx.RootFolders.FirstOrDefaultAsync(r => r.Id == folder.Id);
        if (entity is null) return;
        ctx.RootFolders.Remove(entity);
        await ctx.SaveChangesAsync();
        RootFolders.Remove(folder);
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

        var progress = new Progress<ScanProgress>(p =>
        {
            if (p.TotalFound > 0) ProgressMaximum = p.TotalFound;
            ProgressValue = p.Processed;
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
}
