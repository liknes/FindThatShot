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
