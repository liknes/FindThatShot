using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Services;

namespace VideoArchiveManager.App.ViewModels;

// Backs the read-only catalog statistics dashboard. Pulls a single aggregate
// snapshot from ICatalogStatisticsService and reshapes the raw counts into
// overview cards + proportional bar lists the window can bind to directly.
public partial class CatalogStatsViewModel : ObservableObject
{
    private readonly ICatalogStatisticsService _statsService;

    public CatalogStatsViewModel(ICatalogStatisticsService statsService)
    {
        _statsService = statsService;
    }

    [ObservableProperty]
    private bool _isLoading;

    // True once a snapshot loaded against a non-empty catalog. Gates the body
    // of the dashboard so we don't flash empty charts before data arrives.
    [ObservableProperty]
    private bool _hasData;

    // True when the catalog has no clips at all — shows a friendly hint to scan
    // a folder instead of a grid of zeroes.
    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<OverviewMetric> Overview { get; } = new();

    // Each breakdown (status, rating, resolution, …) is a titled card holding
    // a list of proportional bars. Exposed as a single collection so the
    // window can render the whole dashboard with one responsive panel.
    public ObservableCollection<StatSection> Sections { get; } = new();

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var stats = await _statsService.GetStatisticsAsync();
            Populate(stats);
            IsEmpty = stats.TotalClips == 0;
            HasData = stats.TotalClips > 0;
        }
        catch (Exception ex)
        {
            HasData = false;
            IsEmpty = false;
            ErrorMessage = $"Couldn't compute statistics: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Populate(CatalogStatistics s)
    {
        Overview.Clear();

        Overview.Add(new OverviewMetric(
            s.TotalClips.ToString("N0", CultureInfo.CurrentCulture),
            "Clips",
            $"{s.OnlineClips:N0} online \u00b7 {s.OfflineClips:N0} offline"));

        Overview.Add(new OverviewMetric(
            FormatSize(s.TotalSizeBytes),
            "Total size",
            "across the catalog"));

        Overview.Add(new OverviewMetric(
            FormatDuration(s.TotalDurationSeconds),
            "Total footage",
            "combined runtime"));

        Overview.Add(new OverviewMetric(
            s.DistinctFolders.ToString("N0", CultureInfo.CurrentCulture),
            "Folders",
            $"{s.RootFolderCount:N0} root folder{(s.RootFolderCount == 1 ? "" : "s")}"));

        Overview.Add(new OverviewMetric(
            s.DistinctCameras.ToString("N0", CultureInfo.CurrentCulture),
            "Cameras",
            "distinct models"));

        Overview.Add(new OverviewMetric(
            s.TotalTags.ToString("N0", CultureInfo.CurrentCulture),
            "Tags",
            "in the catalog"));

        var reviewed = s.TotalClips - s.UnreviewedClips;
        Overview.Add(new OverviewMetric(
            FormatPercent(reviewed, s.TotalClips),
            "Reviewed",
            $"{reviewed:N0} of {s.TotalClips:N0} clips"));

        Overview.Add(new OverviewMetric(
            FormatPercent(s.GeotaggedClips, s.TotalClips),
            "Geotagged",
            $"{s.GeotaggedClips:N0} with GPS"));

        Sections.Clear();
        AddSection("By status", s.ByStatus, s.TotalClips);
        AddSection("By rating", s.ByRating, s.TotalClips);
        AddSection("By resolution", s.ByResolution, s.TotalClips);
        AddSection("By year", s.ByYear, s.TotalClips);
        AddSection("Top cameras", s.TopCameras, s.TotalClips);
        AddSection("Top codecs", s.TopCodecs, s.TotalClips);
        AddSection("Top tags", s.TopTags, s.TotalClips);
    }

    private void AddSection(string title, IReadOnlyList<StatCount> source, int totalClips)
    {
        if (source.Count == 0) return;
        Sections.Add(new StatSection(title, BuildBars(source, totalClips)));
    }

    // Bars are scaled against the largest bucket in their own group (so the
    // top bar always fills the track) while the trailing count shows the share
    // of the whole catalog — the two most useful readings at a glance.
    private static IReadOnlyList<StatBarItem> BuildBars(IReadOnlyList<StatCount> source, int totalClips)
    {
        var max = source.Count > 0 ? source.Max(x => x.Count) : 1;
        if (max <= 0) max = 1;

        return source.Select(item => new StatBarItem
        {
            Label = item.Label,
            Count = item.Count,
            Max = max,
            CountText = totalClips > 0
                ? $"{item.Count:N0}  ({FormatPercent(item.Count, totalClips)})"
                : item.Count.ToString("N0", CultureInfo.CurrentCulture)
        }).ToList();
    }

    private static string FormatPercent(int part, int whole)
    {
        if (whole <= 0) return "0%";
        var pct = 100.0 * part / whole;
        // Sub-1% but non-zero shares round up to "<1%" so they don't read as 0.
        if (pct > 0 && pct < 1) return "<1%";
        return $"{Math.Round(pct, MidpointRounding.AwayFromZero):0}%";
    }

    private static string FormatSize(long bytes)
    {
        double size = bytes;
        string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
        var i = 0;
        while (size >= 1024 && i < units.Length - 1)
        {
            size /= 1024;
            i++;
        }
        return string.Format(CultureInfo.CurrentCulture, "{0:0.##} {1}", size, units[i]);
    }

    private static string FormatDuration(double totalSeconds)
    {
        if (totalSeconds <= 0) return "0m";
        var t = TimeSpan.FromSeconds(totalSeconds);

        if (t.TotalDays >= 1)
        {
            return $"{(int)t.TotalDays}d {t.Hours}h";
        }
        if (t.TotalHours >= 1)
        {
            return $"{(int)t.TotalHours}h {t.Minutes}m";
        }
        if (t.TotalMinutes >= 1)
        {
            return $"{(int)t.TotalMinutes}m {t.Seconds}s";
        }
        return $"{(int)t.TotalSeconds}s";
    }
}

// A headline number plus its caption, shown as a card in the dashboard's
// overview strip.
public class OverviewMetric
{
    public OverviewMetric(string value, string label, string? detail = null)
    {
        Value = value;
        Label = label;
        Detail = detail;
    }

    public string Value { get; }
    public string Label { get; }
    public string? Detail { get; }
}

// A titled breakdown (e.g. "By status") and the proportional bars under it.
public class StatSection
{
    public StatSection(string title, IReadOnlyList<StatBarItem> items)
    {
        Title = title;
        Items = items;
    }

    public string Title { get; }
    public IReadOnlyList<StatBarItem> Items { get; }
}

// One row in a breakdown chart: a label, its proportional bar (Count / Max),
// and a pre-formatted trailing count string.
public class StatBarItem
{
    public string Label { get; init; } = string.Empty;
    public int Count { get; init; }
    public int Max { get; init; } = 1;
    public string CountText { get; init; } = string.Empty;
}
