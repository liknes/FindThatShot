using System.Globalization;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoArchiveManager.App.Helpers;
using VideoArchiveManager.Core.Services;

namespace VideoArchiveManager.App.ViewModels;

/// <summary>
/// Backs the global map browse window: loads every geotagged clip as a
/// lightweight <see cref="MapClipPoint"/> for the map to cluster, tracks the
/// clip the user clicked (for the side preview panel), and exposes the scope
/// toggle (whole archive vs. the current grid filters). Read-only over the
/// catalog; no source file is touched.
/// </summary>
public partial class MapBrowseViewModel : ObservableObject
{
    private readonly ISearchService _searchService;
    private IReadOnlyList<MapClipPoint> _points = Array.Empty<MapClipPoint>();

    public MapBrowseViewModel(ISearchService searchService)
    {
        _searchService = searchService;
    }

    /// <summary>
    /// The current grid filter, supplied by the host when the window opens, so
    /// the "current filters" scope can mirror what the catalog grid is showing.
    /// </summary>
    public SearchQuery? CurrentFilter { get; set; }

    [ObservableProperty]
    private bool _scopeWholeArchive = true;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _resultSummary = string.Empty;

    [ObservableProperty]
    private MapClipPreviewViewModel? _selectedClip;

    public bool HasSelection => SelectedClip is not null;

    /// <summary>
    /// The geotagged clips from the most recent load — pushed into the map
    /// control via <see cref="PointsLoaded"/>.
    /// </summary>
    public IReadOnlyList<MapClipPoint> Points => _points;

    /// <summary>Raised after each load so the window plots the points.</summary>
    public event EventHandler<IReadOnlyList<MapClipPoint>>? PointsLoaded;

    /// <summary>Raised to ask the host to select a clip in the catalog grid.</summary>
    public event EventHandler<int>? ClipSelected;

    /// <summary>Raised to ask the host to play a clip in the in-app player.</summary>
    public event EventHandler<int>? PlayRequested;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var filter = ScopeWholeArchive ? null : CurrentFilter;
            var points = await _searchService.GetGeotaggedClipsAsync(filter);
            _points = points;

            ResultSummary = points.Count == 0
                ? ScopeWholeArchive
                    ? "No geotagged clips in the catalog."
                    : "No geotagged clips match the current filters."
                : $"{points.Count} geotagged clip{(points.Count == 1 ? "" : "s")}"
                    + (ScopeWholeArchive ? "" : " (current filters)");

            // A reload can orphan the previewed clip (e.g. scope change drops it).
            if (SelectedClip is not null && points.All(p => p.Id != SelectedClip.Id))
            {
                SelectedClip = null;
            }

            PointsLoaded?.Invoke(this, points);
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnScopeWholeArchiveChanged(bool value) => _ = LoadAsync();

    partial void OnSelectedClipChanged(MapClipPreviewViewModel? value) => OnPropertyChanged(nameof(HasSelection));

    /// <summary>Populates the side preview panel for the given catalog id.</summary>
    public void ShowPreview(int id)
    {
        var point = _points.FirstOrDefault(p => p.Id == id);
        SelectedClip = point is null ? null : new MapClipPreviewViewModel(point);
    }

    [RelayCommand]
    private void ShowInGrid()
    {
        if (SelectedClip is not null)
        {
            ClipSelected?.Invoke(this, SelectedClip.Id);
        }
    }

    [RelayCommand]
    private void Play()
    {
        if (SelectedClip is not null)
        {
            PlayRequested?.Invoke(this, SelectedClip.Id);
        }
    }
}

/// <summary>
/// The clip currently shown in the map window's side preview panel: its
/// thumbnail, identity, location, and online state.
/// </summary>
public sealed class MapClipPreviewViewModel
{
    private readonly MapClipPoint _point;

    public MapClipPreviewViewModel(MapClipPoint point)
    {
        _point = point;
    }

    public int Id => _point.Id;

    public string FileName => _point.FileName;

    public string FolderPath => _point.FolderPath;

    public bool FileExists => _point.FileExists;

    public string LocationText => string.IsNullOrWhiteSpace(_point.LocationText)
        ? CoordinatesText
        : _point.LocationText!.Trim();

    public string CoordinatesText =>
        $"{_point.Latitude.ToString("0.0000", CultureInfo.InvariantCulture)}, " +
        $"{_point.Longitude.ToString("0.0000", CultureInfo.InvariantCulture)}";

    public string RatingText => _point.Rating > 0 ? new string('\u2605', _point.Rating) : string.Empty;

    public string AvailabilityText => _point.FileExists ? "Online" : "Offline";

    public BitmapImage? Thumbnail => ThumbnailLoader.LoadLarge(_point.ThumbnailPath);
}
