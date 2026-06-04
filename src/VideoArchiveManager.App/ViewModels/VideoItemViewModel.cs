using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoArchiveManager.App.Helpers;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Models.Enums;

namespace VideoArchiveManager.App.ViewModels;

public partial class VideoItemViewModel : ObservableObject
{
    public VideoItem Model { get; }

    public VideoItemViewModel(VideoItem model)
    {
        Model = model;
        _tagSummary = string.Join(", ", model.VideoTags.Select(vt => vt.Tag?.Name).Where(n => !string.IsNullOrEmpty(n)));
        _momentCount = model.Moments?.Count ?? 0;
    }

    public int Id => Model.Id;
    public string FilePath => Model.FilePath;
    public string FileName => Model.FileName;
    public string FolderPath => Model.FolderPath;
    public string? Camera => Model.Camera;
    public string? Codec => Model.Codec;
    public bool FileExists => Model.FileExists;

    public string? LocationText => Model.LocationText;
    public bool HasLocation => !string.IsNullOrWhiteSpace(Model.LocationText);

    public double? GpsLatitude => Model.GpsLatitude;
    public double? GpsLongitude => Model.GpsLongitude;
    public bool HasGps => Model.GpsLatitude.HasValue && Model.GpsLongitude.HasValue;

    public string? GpsText
    {
        get
        {
            if (Model.GpsLatitude is not double lat || Model.GpsLongitude is not double lon) return null;
            // Use invariant culture so "60.4827, 6.9023" doesn't render as "60,4827; 6,9023"
            // on locales that use comma as the decimal separator.
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.######}, {1:0.######}", lat, lon);
        }
    }

    public string? GpsMapUrl
    {
        get
        {
            if (Model.GpsLatitude is not double lat || Model.GpsLongitude is not double lon) return null;
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "https://www.openstreetmap.org/?mlat={0}&mlon={1}#map=13/{0}/{1}",
                lat, lon);
        }
    }

    public string Resolution => Model.Width is int w && Model.Height is int h ? $"{w} x {h}" : "-";

    public string DurationText
    {
        get
        {
            if (Model.DurationSeconds is not double s || s <= 0) return "-";
            var ts = TimeSpan.FromSeconds(s);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }
    }

    public string FileSizeText
    {
        get
        {
            double size = Model.FileSize;
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (size >= 1024 && i < units.Length - 1)
            {
                size /= 1024;
                i++;
            }
            return $"{size:0.##} {units[i]}";
        }
    }

    [ObservableProperty]
    private string _tagSummary = string.Empty;

    // Number of timestamped moments (sub-clips) marked inside this clip. Drives
    // a small "N" badge on the catalog card so clips with marked shots stand
    // out. Populated at search time and kept live while the clip is selected.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMoments))]
    [NotifyPropertyChangedFor(nameof(MomentBadgeText))]
    private int _momentCount;

    public bool HasMoments => MomentCount > 0;

    public string MomentBadgeText => MomentCount.ToString();

    public int Rating
    {
        get => Model.Rating;
        set
        {
            if (Model.Rating != value)
            {
                Model.Rating = value;
                OnPropertyChanged();
            }
        }
    }

    public VideoStatus Status
    {
        get => Model.Status;
        set
        {
            if (Model.Status != value)
            {
                Model.Status = value;
                OnPropertyChanged();
            }
        }
    }

    public BitmapImage? Thumbnail => ThumbnailLoader.Load(Model.ThumbnailPath);

    public void RefreshThumbnail() => OnPropertyChanged(nameof(Thumbnail));

    // The current hover-scrub frame overlaid on the static thumbnail while the
    // pointer sweeps across the card. Null = not scrubbing (the static
    // thumbnail shows through). Driven entirely by the view's hover handlers;
    // frame generation is lazy and cached on disk, so clips that are never
    // hovered cost nothing.
    [ObservableProperty]
    private BitmapImage? _scrubFrame;

    // Raise change notifications for every GPS / location-derived property
    // after the underlying Model fields have been updated out-of-band (e.g.
    // by the manual GPS picker writing through VideoDetailViewModel). The
    // getters themselves are read-throughs to Model, so the value the
    // bindings re-read after this call reflects the new state.
    public void RefreshLocation()
    {
        OnPropertyChanged(nameof(GpsLatitude));
        OnPropertyChanged(nameof(GpsLongitude));
        OnPropertyChanged(nameof(HasGps));
        OnPropertyChanged(nameof(GpsText));
        OnPropertyChanged(nameof(GpsMapUrl));
        OnPropertyChanged(nameof(LocationText));
        OnPropertyChanged(nameof(HasLocation));
    }
}
