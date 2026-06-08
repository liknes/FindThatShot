// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoArchiveManager.App.Localization;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Services;

namespace VideoArchiveManager.App.ViewModels;

// Backs the read-only catalog statistics dashboard. Pulls a single aggregate
// snapshot from ICatalogStatisticsService and reshapes the raw counts into
// overview cards + proportional bar lists the window can bind to directly.
public partial class CatalogStatsViewModel : ObservableObject
{
    private readonly ICatalogStatisticsService _statsService;
    private static LocalizationManager L => LocalizationManager.Instance;

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
            ErrorMessage = L.Format("CatalogStats_Error", ex.Message);
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
            L["CatalogStats_Metric_Clips"],
            L.Format("CatalogStats_Metric_ClipsDetail", s.OnlineClips.ToString("N0", CultureInfo.CurrentCulture), s.OfflineClips.ToString("N0", CultureInfo.CurrentCulture))));

        Overview.Add(new OverviewMetric(
            FormatSize(s.TotalSizeBytes),
            L["CatalogStats_Metric_TotalSize"],
            L["CatalogStats_Metric_TotalSizeDetail"]));

        Overview.Add(new OverviewMetric(
            FormatDuration(s.TotalDurationSeconds),
            L["CatalogStats_Metric_TotalFootage"],
            L["CatalogStats_Metric_TotalFootageDetail"]));

        Overview.Add(new OverviewMetric(
            s.DistinctFolders.ToString("N0", CultureInfo.CurrentCulture),
            L["CatalogStats_Metric_Folders"],
            L.Format("CatalogStats_Metric_FoldersDetail", s.RootFolderCount)));

        Overview.Add(new OverviewMetric(
            s.DistinctCameras.ToString("N0", CultureInfo.CurrentCulture),
            L["CatalogStats_Metric_Cameras"],
            L["CatalogStats_Metric_CamerasDetail"]));

        Overview.Add(new OverviewMetric(
            s.TotalTags.ToString("N0", CultureInfo.CurrentCulture),
            L["CatalogStats_Metric_Tags"],
            L["CatalogStats_Metric_TagsDetail"]));

        var reviewed = s.TotalClips - s.UnreviewedClips;
        Overview.Add(new OverviewMetric(
            FormatPercent(reviewed, s.TotalClips),
            L["CatalogStats_Metric_Reviewed"],
            L.Format("CatalogStats_Metric_ReviewedDetail", reviewed.ToString("N0", CultureInfo.CurrentCulture), s.TotalClips.ToString("N0", CultureInfo.CurrentCulture))));

        Overview.Add(new OverviewMetric(
            FormatPercent(s.GeotaggedClips, s.TotalClips),
            L["CatalogStats_Metric_Geotagged"],
            L.Format("CatalogStats_Metric_GeotaggedDetail", s.GeotaggedClips.ToString("N0", CultureInfo.CurrentCulture))));

        Sections.Clear();
        AddSection(L["CatalogStats_Section_Status"], s.ByStatus, s.TotalClips);
        AddSection(L["CatalogStats_Section_Rating"], s.ByRating, s.TotalClips);
        AddSection(L["CatalogStats_Section_Resolution"], s.ByResolution, s.TotalClips);
        AddSection(L["CatalogStats_Section_Year"], s.ByYear, s.TotalClips);
        AddSection(L["CatalogStats_Section_Cameras"], s.TopCameras, s.TotalClips);
        AddSection(L["CatalogStats_Section_Codecs"], s.TopCodecs, s.TotalClips);
        AddSection(L["CatalogStats_Section_Tags"], s.TopTags, s.TotalClips);
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
        if (pct > 0 && pct < 1) return LocalizationManager.Instance["CatalogStats_PercentUnder1"];
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
