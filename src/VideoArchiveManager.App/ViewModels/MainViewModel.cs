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
using VideoArchiveManager.Core.Configuration;
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
    private readonly IVideoLocationService _locationService;
    private readonly ISettingsStore _settingsStore;
    private readonly IUpdateService _updateService;
    private readonly IServiceProvider _services;

    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _geocodeCts;
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
        IVideoLocationService locationService,
        ISettingsStore settingsStore,
        IUpdateService updateService,
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
        _locationService = locationService;
        _settingsStore = settingsStore;
        _updateService = updateService;
        _services = services;
        Detail = detail;

        // Mirror VideoDetailViewModel's LastSaveStatus into the main status
        // bar so users get immediate confirmation of what just happened
        // (saved · sidecar written / disabled / failed). This is the
        // primary visibility fix for "I clicked Save and nothing seemed
        // to happen with the sidecar".
        Detail.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(VideoDetailViewModel.LastSaveStatus)
                && !string.IsNullOrEmpty(Detail.LastSaveStatus))
            {
                StatusMessage = Detail.LastSaveStatus!;
            }
        };

        // Any change to the user's chip selection re-runs the catalog
        // search AND refreshes the picker list (so just-added tags no
        // longer appear in the list, just-removed tags reappear).
        SelectedTagFilters.CollectionChanged += (_, _) =>
        {
            RebuildFilteredTags();
            ClearTagFiltersCommand.NotifyCanExecuteChanged();
            OnFilterChanged();
        };
    }

    // Live indicator for the bottom status bar; reflects the current
    // WriteSidecarFiles setting. Re-evaluated after the Settings dialog
    // closes (see OpenSettings).
    public string SidecarStatusText =>
        _settingsStore.Current.WriteSidecarFiles ? "Sidecars: ON" : "Sidecars: OFF";

    public ObservableCollection<RootFolder> RootFolders { get; } = new();
    public ObservableCollection<Tag> AllTags { get; } = new();
    public ObservableCollection<string> Cameras { get; } = new();

    // Tag filters the user has picked. Search applies them as AND
    // (a video must carry every selected tag). Backed by the Tag picker
    // list + chip buttons in the sidebar.
    public ObservableCollection<Tag> SelectedTagFilters { get; } = new();

    // Subset of AllTags filtered by TagFilterSearchText and excluding any
    // tag that is already a chip. Rebuilt by RebuildFilteredTags(). The
    // sidebar list binds to this so typing narrows what's shown.
    public ObservableCollection<Tag> FilteredTags { get; } = new();
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

    // Live filter for the tag picker list. Doesn't itself filter the
    // catalog — only narrows which tags the user sees while choosing.
    [ObservableProperty]
    private string _tagFilterSearchText = string.Empty;

    [ObservableProperty]
    private RootFolder? _selectedRootFolder;

    [ObservableProperty]
    private DateTime? _dateFrom;

    [ObservableProperty]
    private DateTime? _dateTo;

    [ObservableProperty]
    private bool _showOnlyAvailable = true;

    // Backs the sidebar "Show only unreviewed" checkbox AND the toolbar
    // "Start review session" button. Treated as a union filter server-side
    // (Status == Unreviewed OR no tags) — see SearchService.
    [ObservableProperty]
    private bool _onlyUnreviewed;

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

    // When true, filter setters that would normally trigger a re-search stay
    // quiet. Used by ClearFiltersAsync so 7+ property updates result in one
    // search rather than 7 overlapping ones.
    private bool _suppressFilterSearch;

    private void OnFilterChanged()
    {
        if (_suppressFilterSearch) return;
        _ = SearchAsync();
    }

    // Auto-search whenever any filter changes via UI (combo boxes, date
    // pickers, sidebar lists, checkboxes). Without these, the filter values
    // only get applied when the user explicitly clicks Search.
    partial void OnStatusFilterChanged(VideoStatus? value) => OnFilterChanged();
    partial void OnMinRatingFilterChanged(int value) => OnFilterChanged();
    partial void OnCameraFilterChanged(string? value) => OnFilterChanged();
    partial void OnTagFilterSearchTextChanged(string value) => RebuildFilteredTags();
    partial void OnSelectedRootFolderChanged(RootFolder? value) => OnFilterChanged();
    partial void OnDateFromChanged(DateTime? value) => OnFilterChanged();
    partial void OnDateToChanged(DateTime? value) => OnFilterChanged();
    partial void OnShowOnlyAvailableChanged(bool value) => OnFilterChanged();
    partial void OnOnlyUnreviewedChanged(bool value) => OnFilterChanged();

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

        // Drop chips whose tag no longer exists (e.g. cleaned up via bulk
        // edit or admin tools) and refresh the picker against the new set.
        var validIds = AllTags.Select(t => t.Id).ToHashSet();
        for (var i = SelectedTagFilters.Count - 1; i >= 0; i--)
        {
            if (!validIds.Contains(SelectedTagFilters[i].Id))
            {
                SelectedTagFilters.RemoveAt(i);
            }
        }
        RebuildFilteredTags();

        var cams = await _searchService.GetDistinctCamerasAsync();
        Cameras.Clear();
        foreach (var c in cams) Cameras.Add(c);
    }

    // Repopulates FilteredTags from AllTags, excluding anything the user
    // already picked as a chip and applying the live text filter.
    // Case-insensitive substring match keeps "birds" / "Birds" equivalent.
    private void RebuildFilteredTags()
    {
        var search = TagFilterSearchText?.Trim() ?? string.Empty;
        var selectedIds = SelectedTagFilters.Select(t => t.Id).ToHashSet();

        FilteredTags.Clear();
        foreach (var t in AllTags)
        {
            if (selectedIds.Contains(t.Id)) continue;
            if (search.Length > 0
                && t.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }
            FilteredTags.Add(t);
        }
    }

    [RelayCommand]
    private void AddTagFilter(Tag? tag)
    {
        if (tag is null) return;
        if (SelectedTagFilters.Any(t => t.Id == tag.Id)) return;
        SelectedTagFilters.Add(tag);
    }

    [RelayCommand]
    private void RemoveTagFilter(Tag? tag)
    {
        if (tag is null) return;
        var existing = SelectedTagFilters.FirstOrDefault(t => t.Id == tag.Id);
        if (existing is not null)
        {
            SelectedTagFilters.Remove(existing);
        }
    }

    [RelayCommand(CanExecute = nameof(CanClearTagFilters))]
    private void ClearTagFilters() => SelectedTagFilters.Clear();

    private bool CanClearTagFilters() => SelectedTagFilters.Count > 0;

    // Tracks the currently-running search so that fast successive filter
    // changes don't race — a newer call cancels any older one before its
    // results can clobber the grid.
    private CancellationTokenSource? _searchCts;

    [RelayCommand]
    private async Task SearchAsync()
    {
        // Supersede any in-flight search. Cancelling makes the EF query in
        // SearchService throw OperationCanceledException, which we catch
        // below. Disposing the old CTS here is safe because it's idempotent
        // and the awaiting code never touches the CTS itself again.
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        var token = cts.Token;

        var query = new SearchQuery
        {
            Text = SearchText,
            Status = StatusFilter,
            MinRating = MinRatingFilter > 0 ? MinRatingFilter : null,
            Camera = string.IsNullOrWhiteSpace(CameraFilter) ? null : CameraFilter,
            TagIds = SelectedTagFilters.Count == 0
                ? null
                : SelectedTagFilters.Select(t => t.Id).ToArray(),
            DateFrom = DateFrom,
            DateTo = DateTo,
            RootFolderPath = SelectedRootFolder?.Path,
            FileExists = ShowOnlyAvailable ? true : null,
            OnlyUnreviewed = OnlyUnreviewed ? true : null,
            Take = 500
        };

        try
        {
            var result = await _searchService.SearchAsync(query, token);

            // Belt-and-suspenders: if we were superseded after the await
            // resumed but before we got here, bail out so we don't overwrite
            // a fresher result.
            if (token.IsCancellationRequested) return;

            Videos.Clear();
            foreach (var v in result.Items)
            {
                Videos.Add(new VideoItemViewModel(v));
            }
            TotalCount = result.TotalCount;
            StatusMessage = $"Showing {Videos.Count} of {TotalCount} videos";
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer query — drop the stale result silently.
        }
        finally
        {
            // Only dispose if we're still the active CTS. If a newer call has
            // already replaced (and disposed) us, it's not ours to clean up.
            if (ReferenceEquals(_searchCts, cts))
            {
                cts.Dispose();
                _searchCts = null;
            }
        }
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

    [ObservableProperty]
    private bool _isGeocodingLocations;

    [RelayCommand]
    private async Task FillMissingLocationsAsync()
    {
        if (IsGeocodingLocations) return;

        _geocodeCts = new CancellationTokenSource();
        IsGeocodingLocations = true;
        ProgressValue = 0;
        ProgressMaximum = 1;
        StatusMessage = "Looking up missing locations…";

        var progress = new Progress<GeocodeProgress>(p =>
        {
            if (p.TotalCandidates > 0) ProgressMaximum = p.TotalCandidates;
            ProgressValue = p.Processed;
            if (!string.IsNullOrWhiteSpace(p.Message)) StatusMessage = p.Message;
        });

        try
        {
            var filled = await _locationService.FillMissingLocationsAsync(progress, _geocodeCts.Token);
            StatusMessage = filled > 0
                ? $"Filled {filled} location(s) from GPS."
                : "No locations needed updating.";
            if (filled > 0)
            {
                await SearchAsync();
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Location lookup cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Location lookup failed: {ex.Message}";
        }
        finally
        {
            IsGeocodingLocations = false;
            _geocodeCts?.Dispose();
            _geocodeCts = null;
        }
    }

    [RelayCommand]
    private void CancelFillMissingLocations()
    {
        _geocodeCts?.Cancel();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var window = new SettingsWindow(_services.GetRequiredService<SettingsViewModel>())
        {
            Owner = App.Current.MainWindow
        };
        window.ShowDialog();

        // Settings may have flipped WriteSidecarFiles; refresh the status
        // bar indicator regardless of how the window was closed (Save / X
        // / Cancel all funnel through this point).
        OnPropertyChanged(nameof(SidecarStatusText));
    }

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (IsCheckingForUpdates) return;
        IsCheckingForUpdates = true;
        try
        {
            StatusMessage = "Checking for updates…";

            var result = await _updateService.CheckAsync();

            if (!result.Success)
            {
                StatusMessage = $"Update check failed: {result.ErrorMessage}";
                MessageBox.Show(
                    $"Could not check for updates.\n\n{result.ErrorMessage}",
                    "Check for updates",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (result.NotInstalledMode)
            {
                var msg = "This build is not running from an installer, so updates can't be applied.\n\n" +
                          "To test the update flow:\n" +
                          "  1. Run scripts/publish.ps1\n" +
                          "  2. Install the produced VideoArchiveManager-win-Setup.exe\n" +
                          "  3. Open the installed app and try Check for updates again.\n\n" +
                          (result.CurrentVersion is null
                              ? string.Empty
                              : $"Current build version: {result.CurrentVersion}");
                StatusMessage = "Update check skipped (not an installed build)";
                MessageBox.Show(msg, "Check for updates", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!result.UpdateAvailable)
            {
                var v = result.CurrentVersion is null ? string.Empty : $" (v{result.CurrentVersion})";
                StatusMessage = $"Up to date{v}";
                MessageBox.Show(
                    $"You're on the latest version{v}.",
                    "Check for updates",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            StatusMessage = $"Update available: v{result.AvailableVersion}";
            var confirm = MessageBox.Show(
                $"An update is available.\n\n" +
                $"Current: v{result.CurrentVersion}\n" +
                $"New:     v{result.AvailableVersion}\n\n" +
                "Download and install it now? The app will close and restart on the new version when ready.\n\n" +
                "Your catalog database, settings, and sidecar files will NOT be affected.",
                "Update available",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question,
                MessageBoxResult.OK);
            if (confirm != MessageBoxResult.OK)
            {
                StatusMessage = $"Update deferred (v{result.AvailableVersion} available)";
                return;
            }

            StatusMessage = $"Downloading v{result.AvailableVersion}…";
            var apply = await _updateService.DownloadAndApplyAsync(onProgress: p =>
            {
                // Velopack reports 0..100 ints. Throttle the UI updates a bit
                // so we don't spam the property change pipeline.
                if (p % 5 == 0)
                {
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        StatusMessage = $"Downloading v{result.AvailableVersion}… {p}%";
                    });
                }
            });

            // We only reach this point if apply did NOT exit the process
            // (typical case is it never returns because ApplyUpdatesAndRestart
            // tears the process down). So this path == failure.
            if (!apply.Success)
            {
                StatusMessage = $"Update failed: {apply.ErrorMessage}";
                MessageBox.Show(
                    $"The update could not be installed.\n\n{apply.ErrorMessage}\n\n" +
                    "Your current version is unaffected.",
                    "Update failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
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

    // One-click "I want to do a review batch now" affordance.
    // Strips every other filter so candidate clips aren't hidden by stale
    // state (e.g. an old tag chip still in play), flips OnlyUnreviewed on,
    // runs the search once at the end, and selects the first result so the
    // user can start reviewing immediately. Keeps ShowOnlyAvailable in
    // whatever state the user prefers — offline drives shouldn't appear in
    // a review queue unless they've explicitly opted in.
    [RelayCommand]
    private async Task StartReviewSessionAsync()
    {
        _suppressFilterSearch = true;
        try
        {
            SearchText = string.Empty;
            StatusFilter = null;
            MinRatingFilter = 0;
            CameraFilter = null;
            SelectedTagFilters.Clear();
            TagFilterSearchText = string.Empty;
            SelectedRootFolder = null;
            DateFrom = null;
            DateTo = null;
            OnlyUnreviewed = true;
        }
        finally
        {
            _suppressFilterSearch = false;
        }

        await SearchAsync();

        if (Videos.Count > 0)
        {
            SelectedVideo = Videos[0];
            StatusMessage = $"Review session: {Videos.Count} clip(s) waiting";
        }
        else
        {
            StatusMessage = "Review session: nothing to review — you're caught up.";
        }
    }

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        // Batch: avoid 7+ overlapping searches as each property setter fires.
        _suppressFilterSearch = true;
        try
        {
            SearchText = string.Empty;
            StatusFilter = null;
            MinRatingFilter = 0;
            CameraFilter = null;
            SelectedTagFilters.Clear();
            TagFilterSearchText = string.Empty;
            SelectedRootFolder = null;
            DateFrom = null;
            DateTo = null;
            OnlyUnreviewed = false;
        }
        finally
        {
            _suppressFilterSearch = false;
        }
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
