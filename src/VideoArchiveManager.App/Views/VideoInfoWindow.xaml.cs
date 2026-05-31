using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using VideoArchiveManager.App.Helpers;
using VideoArchiveManager.App.ViewModels;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Models.Enums;
using VideoArchiveManager.Core.Services;

namespace VideoArchiveManager.App.Views;

/// <summary>
/// Non-modal "Get info" / "Properties" popup for a single clip.
///
/// <para>
/// Surfaces every piece of metadata the catalog has captured about a
/// <see cref="VideoItem"/>, sectioned by source (file system / video stream /
/// camera / location / catalog / internal). The window is intentionally
/// non-modal so the user can leave it pinned beside the main window while
/// browsing other clips. <c>Esc</c> closes; the X button closes; opening
/// twice for the same clip just brings the existing instance to the front
/// (handled by <see cref="MainWindow"/>'s instance tracking).
/// </para>
/// <para>
/// Bindings target the public properties exposed on this window itself
/// (<c>DataContext = this</c>) — values are pre-formatted snapshots taken
/// at construction time, so the popup doesn't react to live edits in the
/// main editor pane. Reopening the popup is the explicit refresh.
/// </para>
/// </summary>
public partial class VideoInfoWindow : Window
{
    public VideoInfoWindow(VideoItemViewModel item, IEnumerable<Tag> tags, ISidecarService sidecar)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(sidecar);

        InitializeComponent();

        Tags = tags?.ToArray() ?? Array.Empty<Tag>();
        Populate(item, sidecar);

        // Set DataContext *after* Populate so the XAML's bindings evaluate
        // against fully-populated values on first attach. The window's
        // properties are immutable after construction (popup is a snapshot,
        // not a live view), so no INotifyPropertyChanged is needed.
        DataContext = this;
    }

    public BitmapImage? Thumbnail { get; private set; }
    public string? FileName { get; private set; }
    public string? FilePath { get; private set; }
    public string? FolderPath { get; private set; }
    public string? Extension { get; private set; }
    public string? FileSizeText { get; private set; }
    public string? FileSizeBytesText { get; private set; }
    public string? ModifiedAtText { get; private set; }
    public string? CreatedAtFileText { get; private set; }
    public string? FileExistsText { get; private set; }

    public string? DurationText { get; private set; }
    public string? Resolution { get; private set; }
    public string? AspectRatioText { get; private set; }
    public string? FrameRateText { get; private set; }
    public string? Codec { get; private set; }

    public string? Camera { get; private set; }
    public bool HasCamera { get; private set; }

    public string? LocationText { get; private set; }
    public string? GpsText { get; private set; }
    public string? GpsMapUrl { get; private set; }
    public bool HasGps { get; private set; }
    public string? FolderDateText { get; private set; }
    public bool HasAnyLocation { get; private set; }

    public string? StatusText { get; private set; }
    public string? RatingText { get; private set; }
    public Tag[] Tags { get; }
    public bool HasTags => Tags.Length > 0;
    public string? NotesPreview { get; private set; }
    public string? SidecarStatusText { get; private set; }
    public string? SidecarPathText { get; private set; }
    public string? CreatedAtText { get; private set; }
    public string? UpdatedAtText { get; private set; }

    public string? CatalogIdText { get; private set; }
    public string? ThumbnailPath { get; private set; }

    private void Populate(VideoItemViewModel item, ISidecarService sidecar)
    {
        var m = item.Model;

        Thumbnail = ThumbnailLoader.LoadLarge(m.ThumbnailPath);
        FileName = m.FileName;
        FilePath = m.FilePath;
        FolderPath = m.FolderPath;
        Extension = string.IsNullOrEmpty(m.Extension) ? "-" : m.Extension;

        // Two views of the same number: humanised on top, exact byte count
        // underneath. The byte count is what audit / scripting workflows
        // actually need — humanised is for at-a-glance.
        FileSizeText = item.FileSizeText;
        FileSizeBytesText = $"{m.FileSize:N0} bytes";

        ModifiedAtText = FormatLocalDateTime(m.ModifiedAtFile);
        CreatedAtFileText = FormatLocalDateTime(m.CreatedAtFile);
        FileExistsText = m.FileExists ? "Available" : "Offline (file missing)";

        DurationText = item.DurationText;
        Resolution = item.Resolution;
        AspectRatioText = FormatAspectRatio(m.Width, m.Height);
        FrameRateText = m.FrameRate is double fr && fr > 0
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.##} fps", fr)
            : null;
        Codec = string.IsNullOrEmpty(m.Codec) ? null : m.Codec;

        HasCamera = !string.IsNullOrWhiteSpace(m.Camera);
        Camera = m.Camera;

        LocationText = item.LocationText;
        HasGps = item.HasGps;
        GpsText = item.GpsText;
        GpsMapUrl = item.GpsMapUrl;
        FolderDateText = m.FolderDate is DateTime fd ? fd.ToString("yyyy-MM-dd") : null;
        HasAnyLocation = !string.IsNullOrWhiteSpace(LocationText) || HasGps || FolderDateText is not null;

        StatusText = FormatStatus(m.Status);
        RatingText = FormatRating(m.Rating);
        NotesPreview = TrimNotes(m.Notes);

        var (sidecarStatus, sidecarPath) = ResolveSidecarStatus(sidecar, m);
        SidecarStatusText = sidecarStatus;
        SidecarPathText = sidecarPath;

        CreatedAtText = FormatLocalDateTime(m.CreatedAt.ToLocalTime());
        UpdatedAtText = FormatLocalDateTime(m.UpdatedAt.ToLocalTime());

        CatalogIdText = m.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ThumbnailPath = string.IsNullOrEmpty(m.ThumbnailPath) ? null : m.ThumbnailPath;
    }

    private static string FormatLocalDateTime(DateTime dt)
    {
        // Catalog timestamps are stored as UTC and converted at the call
        // site; file-system timestamps come from Win32 and are already
        // local. Either way we render in the user's locale.
        return dt == default
            ? "-"
            : dt.ToString("yyyy-MM-dd HH:mm:ss");
    }

    private static string? FormatAspectRatio(int? widthOpt, int? heightOpt)
    {
        if (widthOpt is not int w || heightOpt is not int h || w <= 0 || h <= 0) return null;

        // Nominal label first (16:9 / 4:3 / etc.) so a 3840x2160 clip reads
        // as "16:9 (1.778:1)" instead of "1280:720" GCD-reduced. Reduced
        // ratio is shown in parens so the raw shape is still recoverable.
        var nominal = ResolveNominal(w, h);
        var ratio = (double)w / h;
        var ratioText = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.###}:1", ratio);
        return nominal is null ? ratioText : $"{nominal} ({ratioText})";

        static string? ResolveNominal(int w, int h)
        {
            // Tolerate mild rounding (e.g. 3840x2160 → 1.7778, 1920x1080 → 1.7778).
            var r = (double)w / h;
            if (Math.Abs(r - 16.0 / 9.0) < 0.01) return "16:9";
            if (Math.Abs(r - 4.0 / 3.0) < 0.01) return "4:3";
            if (Math.Abs(r - 21.0 / 9.0) < 0.02) return "21:9";
            if (Math.Abs(r - 9.0 / 16.0) < 0.01) return "9:16";
            if (Math.Abs(r - 1.0) < 0.01) return "1:1";
            if (Math.Abs(r - 17.0 / 9.0) < 0.02) return "17:9";
            return null;
        }
    }

    private static string FormatStatus(VideoStatus status) => status switch
    {
        VideoStatus.Unreviewed => "Unreviewed",
        VideoStatus.Keep => "Keep",
        VideoStatus.Favorite => "Favorite",
        VideoStatus.ForStock => "For stock",
        VideoStatus.UploadedPond5 => "Uploaded \u2014 Pond5",
        VideoStatus.UploadedShutterstock => "Uploaded \u2014 Shutterstock",
        VideoStatus.UploadedAdobe => "Uploaded \u2014 Adobe Stock",
        VideoStatus.Rejected => "Rejected",
        VideoStatus.Archive => "Archive",
        _ => status.ToString()
    };

    private static string FormatRating(int rating)
    {
        rating = Math.Clamp(rating, 0, 5);
        if (rating == 0) return "Not rated";
        // U+2605 BLACK STAR / U+2606 WHITE STAR keeps the rating compact
        // and matches the convention used in the catalog grid.
        return new string('\u2605', rating) + new string('\u2606', 5 - rating);
    }

    private static string? TrimNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return null;
        const int max = 240;
        var single = notes.Replace("\r", " ").Replace("\n", " ").Trim();
        return single.Length <= max ? single : single[..max] + "\u2026";
    }

    private static (string status, string? path) ResolveSidecarStatus(ISidecarService sidecar, VideoItem video)
    {
        if (!sidecar.IsEnabled)
        {
            return ("Disabled", null);
        }

        try
        {
            var path = sidecar.GetSidecarPathFor(video.FilePath);
            if (string.IsNullOrEmpty(path))
            {
                return ("Unknown", null);
            }
            return File.Exists(path)
                ? ("Written", path)
                : ("Not written yet", path);
        }
        catch
        {
            // Path resolution can fail for malformed paths (legacy entries
            // imported from another machine, very long paths, etc.) — show
            // a polite fallback rather than tearing down the popup.
            return ("Unavailable", null);
        }
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        var url = e.Uri?.ToString();
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            e.Handled = true;
        }
        catch
        {
            // Locked-down systems may block ShellExecute; ignore.
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Close_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e) => Close();
}
