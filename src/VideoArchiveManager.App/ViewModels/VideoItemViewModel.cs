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
}
