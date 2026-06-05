using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoArchiveManager.App.Helpers;
using VideoArchiveManager.Core.Models;

namespace VideoArchiveManager.App.ViewModels;

// Editor wrapper around a VideoMoment. Label / Rating / Notes are editable and
// pushed back to the catalog by VideoDetailViewModel.SaveMomentCommand; the
// time range, thumbnail, and tag chips are display-derived from the model.
public partial class MomentViewModel : ObservableObject
{
    public VideoMoment Model { get; }

    public MomentViewModel(VideoMoment model, IEnumerable<MomentTag>? tags = null)
    {
        Model = model;
        _label = model.Label;
        _notes = model.Notes;
        _rating = model.Rating;
        if (tags is not null)
        {
            foreach (var mt in tags)
            {
                if (mt.Tag is not null) Tags.Add(new AttachedTag(mt.Tag, mt.IsBackground));
            }
        }
        RefreshTagSummary();
    }

    public int Id => Model.Id;
    public int VideoItemId => Model.VideoItemId;
    public double StartSeconds => Model.StartSeconds;
    public double? EndSeconds => Model.EndSeconds;

    [ObservableProperty]
    private string? _label;

    [ObservableProperty]
    private string? _notes;

    [ObservableProperty]
    private int _rating;

    public ObservableCollection<AttachedTag> Tags { get; } = new();

    [ObservableProperty]
    private string _tagSummary = string.Empty;

    public void RefreshTagSummary()
        => TagSummary = string.Join(", ", Tags.Select(t => t.Name));

    public string StartText => Format(StartSeconds);

    // "00:01:12 → 00:01:20" for a range, or just the in-point for a point marker.
    public string TimeRangeText =>
        EndSeconds is double e && e > StartSeconds
            ? $"{Format(StartSeconds)} \u2192 {Format(e)}"
            : Format(StartSeconds);

    public string DurationText
    {
        get
        {
            if (EndSeconds is not double e || e <= StartSeconds) return "instant";
            var span = TimeSpan.FromSeconds(e - StartSeconds);
            return span.TotalMinutes >= 1
                ? $"{(int)span.TotalMinutes}m {span.Seconds}s"
                : $"{span.Seconds}.{span.Milliseconds / 100}s";
        }
    }

    // Display label that never renders blank: falls back to the timecode so a
    // moment with no name still reads sensibly in the list.
    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? StartText : Label!.Trim();

    public BitmapImage? Thumbnail => ThumbnailLoader.Load(Model.ThumbnailPath);

    public void RefreshThumbnail() => OnPropertyChanged(nameof(Thumbnail));

    partial void OnLabelChanged(string? value) => OnPropertyChanged(nameof(DisplayLabel));

    private static string Format(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
            : $"{t.Minutes:D2}:{t.Seconds:D2}";
    }
}
