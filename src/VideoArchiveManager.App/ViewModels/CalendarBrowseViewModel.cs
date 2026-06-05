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
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoArchiveManager.App.Helpers;
using VideoArchiveManager.Core.Services;

namespace VideoArchiveManager.App.ViewModels;

/// <summary>
/// Backs the calendar browse window: loads a year-month heatmap of clip counts
/// by effective shoot date as an alternate navigation surface, lists the clips
/// for a clicked month in a side panel, and exposes the scope toggle (whole
/// archive vs. the current grid filters). Read-only over the catalog; no source
/// file is touched. Modeled on <see cref="MapBrowseViewModel"/>.
/// </summary>
public partial class CalendarBrowseViewModel : ObservableObject
{
    private readonly ISearchService _searchService;

    public CalendarBrowseViewModel(ISearchService searchService)
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

    /// <summary>The year rows of the heatmap (most recent first).</summary>
    public ObservableCollection<CalendarYearRow> Years { get; } = new();

    /// <summary>Clips of the currently-selected month (the side list).</summary>
    public ObservableCollection<CalendarClipViewModel> MonthClips { get; } = new();

    [ObservableProperty]
    private CalendarMonthCell? _selectedMonth;

    [ObservableProperty]
    private CalendarClipViewModel? _selectedClip;

    [ObservableProperty]
    private string _monthClipsHeader = string.Empty;

    public bool HasMonthSelection => SelectedMonth is not null;

    public bool HasSelection => SelectedClip is not null;

    public bool HasYears => Years.Count > 0;

    /// <summary>Raised to ask the host to select a clip in the catalog grid.</summary>
    public event EventHandler<int>? ClipSelected;

    /// <summary>Raised to ask the host to play a clip in the in-app player.</summary>
    public event EventHandler<int>? PlayRequested;

    /// <summary>
    /// Raised to ask the host to scope the catalog grid to a date range — from
    /// the "Filter grid to this month" action.
    /// </summary>
    public event EventHandler<(DateTime From, DateTime To)>? FilterToMonthRequested;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var filter = ScopeWholeArchive ? null : CurrentFilter;
            var counts = await _searchService.GetShootMonthCountsAsync(filter);

            BuildHeatmap(counts);

            var totalClips = counts.Sum(c => c.Count);
            ResultSummary = totalClips == 0
                ? ScopeWholeArchive
                    ? "No dated clips in the catalog."
                    : "No dated clips match the current filters."
                : $"{totalClips} clip{(totalClips == 1 ? "" : "s")} across {Years.Count} year{(Years.Count == 1 ? "" : "s")}"
                    + (ScopeWholeArchive ? "" : " (current filters)");

            // A reload can orphan the selected month (e.g. scope change drops it).
            if (SelectedMonth is not null)
            {
                var match = Years
                    .SelectMany(y => y.Months)
                    .FirstOrDefault(m => m.Year == SelectedMonth.Year && m.Month == SelectedMonth.Month && m.HasClips);
                SelectedMonth = match;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void BuildHeatmap(IReadOnlyList<MonthShootCount> counts)
    {
        Years.Clear();

        if (counts.Count == 0)
        {
            OnPropertyChanged(nameof(HasYears));
            return;
        }

        var max = counts.Max(c => c.Count);
        var byYear = counts.GroupBy(c => c.Year).OrderByDescending(g => g.Key);

        foreach (var yearGroup in byYear)
        {
            var monthCounts = yearGroup.ToDictionary(c => c.Month, c => c.Count);
            var cells = new List<CalendarMonthCell>(12);
            for (var month = 1; month <= 12; month++)
            {
                var count = monthCounts.TryGetValue(month, out var c) ? c : 0;
                var intensity = max > 0 ? (double)count / max : 0d;
                cells.Add(new CalendarMonthCell(yearGroup.Key, month, count, intensity));
            }
            Years.Add(new CalendarYearRow(yearGroup.Key, cells, yearGroup.Sum(c => c.Count)));
        }

        OnPropertyChanged(nameof(HasYears));
    }

    partial void OnScopeWholeArchiveChanged(bool value) => _ = LoadAsync();

    partial void OnSelectedClipChanged(CalendarClipViewModel? value) => OnPropertyChanged(nameof(HasSelection));

    partial void OnSelectedMonthChanged(CalendarMonthCell? value)
    {
        OnPropertyChanged(nameof(HasMonthSelection));
        FilterMonthToGridCommand.NotifyCanExecuteChanged();
        _ = LoadMonthClipsAsync(value);
    }

    [RelayCommand]
    private void SelectMonth(CalendarMonthCell? cell)
    {
        // Empty months aren't navigable.
        SelectedMonth = cell is { HasClips: true } ? cell : SelectedMonth;
    }

    private async Task LoadMonthClipsAsync(CalendarMonthCell? cell)
    {
        MonthClips.Clear();
        SelectedClip = null;

        if (cell is null)
        {
            MonthClipsHeader = string.Empty;
            return;
        }

        var filter = ScopeWholeArchive ? null : CurrentFilter;
        var clips = await _searchService.GetClipsInMonthAsync(cell.Year, cell.Month, filter);
        foreach (var clip in clips)
        {
            MonthClips.Add(new CalendarClipViewModel(clip));
        }

        var monthName = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(cell.Month);
        MonthClipsHeader = $"{monthName} {cell.Year} \u2014 {clips.Count} clip{(clips.Count == 1 ? "" : "s")}";
    }

    /// <summary>Populates the side preview for the given catalog id.</summary>
    public void ShowPreview(int id)
    {
        SelectedClip = MonthClips.FirstOrDefault(c => c.Id == id);
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

    [RelayCommand(CanExecute = nameof(HasMonthSelection))]
    private void FilterMonthToGrid()
    {
        if (SelectedMonth is null) return;
        var from = new DateTime(SelectedMonth.Year, SelectedMonth.Month, 1);
        var to = from.AddMonths(1).AddTicks(-1);
        FilterToMonthRequested?.Invoke(this, (from, to));
    }
}

/// <summary>One year row of the heatmap: the year, its 12 month cells, and the
/// year's total clip count (shown alongside the row).</summary>
public sealed class CalendarYearRow
{
    public CalendarYearRow(int year, IReadOnlyList<CalendarMonthCell> months, int total)
    {
        Year = year;
        Months = months;
        Total = total;
    }

    public int Year { get; }

    public IReadOnlyList<CalendarMonthCell> Months { get; }

    public int Total { get; }

    public string TotalLabel => Total.ToString(CultureInfo.InvariantCulture);
}

/// <summary>One month cell of the heatmap: count, color intensity, and labels.</summary>
public sealed class CalendarMonthCell
{
    public CalendarMonthCell(int year, int month, int count, double intensity)
    {
        Year = year;
        Month = month;
        Count = count;
        Intensity = intensity;
    }

    public int Year { get; }

    public int Month { get; }

    public int Count { get; }

    public double Intensity { get; }

    public bool HasClips => Count > 0;

    public string MonthShort =>
        CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(Month);

    public string CountLabel => Count > 0 ? Count.ToString(CultureInfo.InvariantCulture) : string.Empty;

    public string Tooltip
    {
        get
        {
            var name = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(Month);
            return Count > 0
                ? $"{name} {Year} \u2014 {Count} clip{(Count == 1 ? "" : "s")}"
                : $"{name} {Year} \u2014 no clips";
        }
    }
}

/// <summary>A clip in the month list / side preview of the calendar window.</summary>
public sealed class CalendarClipViewModel
{
    private readonly CalendarClip _clip;

    public CalendarClipViewModel(CalendarClip clip)
    {
        _clip = clip;
    }

    public int Id => _clip.Id;

    public string FileName => _clip.FileName;

    public string FolderPath => _clip.FolderPath;

    public bool FileExists => _clip.FileExists;

    public string AvailabilityText => _clip.FileExists ? "Online" : "Offline";

    public string RatingText => _clip.Rating > 0 ? new string('\u2605', _clip.Rating) : string.Empty;

    public string DateText => _clip.EffectiveDate.ToString("d", CultureInfo.CurrentCulture);

    public BitmapImage? Thumbnail => ThumbnailLoader.Load(_clip.ThumbnailPath);

    public BitmapImage? PreviewThumbnail => ThumbnailLoader.LoadLarge(_clip.ThumbnailPath);
}
