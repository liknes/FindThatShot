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
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoArchiveManager.App.Helpers;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Services;

namespace VideoArchiveManager.App.ViewModels;

// Catalog-wide search over timestamped moments (sub-clips). Returns each shot
// as a card with its in-point thumbnail and a "jump to" action that opens the
// parent clip in the player and seeks to the moment.
public partial class MomentSearchViewModel : ObservableObject
{
    private readonly IMomentService _momentService;

    public MomentSearchViewModel(IMomentService momentService)
    {
        _momentService = momentService;
    }

    public ObservableCollection<MomentResultViewModel> Results { get; } = new();
    public ObservableCollection<int> RatingValues { get; } = new(new[] { 0, 1, 2, 3, 4, 5 });

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _minRating;

    [ObservableProperty]
    private bool _onlyAvailable = true;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string _resultSummary = string.Empty;

    // Raised when the user picks a result. The window forwards it to MainWindow,
    // which selects the clip and seeks the player to startSeconds.
    public event EventHandler<(int VideoItemId, double StartSeconds)>? JumpRequested;

    [RelayCommand]
    private async Task SearchAsync()
    {
        IsSearching = true;
        try
        {
            var query = new MomentSearchQuery
            {
                Text = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
                MinRating = MinRating > 0 ? MinRating : null,
                FileExists = OnlyAvailable ? true : null,
                Take = 500
            };

            var result = await _momentService.SearchAsync(query);

            Results.Clear();
            foreach (var m in result.Moments)
            {
                Results.Add(new MomentResultViewModel(m));
            }

            ResultSummary = result.TotalCount == 0
                ? "No moments match."
                : result.TotalCount > Results.Count
                    ? $"Showing {Results.Count} of {result.TotalCount} moments"
                    : $"{Results.Count} moment{(Results.Count == 1 ? "" : "s")}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private void Jump(MomentResultViewModel? result)
    {
        if (result is null) return;
        JumpRequested?.Invoke(this, (result.VideoItemId, result.StartSeconds));
    }
}

// One moment in the search results: its thumbnail, label, time range, parent
// clip, tags, and rating.
public partial class MomentResultViewModel : ObservableObject
{
    private readonly VideoMoment _model;

    public MomentResultViewModel(VideoMoment model)
    {
        _model = model;
        TagSummary = string.Join(", ", model.MomentTags.Select(mt => mt.Tag?.Name).Where(n => !string.IsNullOrEmpty(n)));
    }

    public int VideoItemId => _model.VideoItemId;
    public double StartSeconds => _model.StartSeconds;
    public int Rating => _model.Rating;
    public string TagSummary { get; }

    public string FileName => _model.VideoItem?.FileName ?? string.Empty;
    public string FolderPath => _model.VideoItem?.FolderPath ?? string.Empty;
    public bool FileExists => _model.VideoItem?.FileExists ?? false;

    public string DisplayLabel => string.IsNullOrWhiteSpace(_model.Label) ? StartText : _model.Label!.Trim();

    public string StartText => Format(_model.StartSeconds);

    public string TimeRangeText =>
        _model.EndSeconds is double e && e > _model.StartSeconds
            ? $"{Format(_model.StartSeconds)} \u2192 {Format(e)}"
            : Format(_model.StartSeconds);

    public string RatingText => Rating > 0 ? new string('\u2605', Rating) : string.Empty;

    public BitmapImage? Thumbnail => ThumbnailLoader.Load(_model.ThumbnailPath);

    private static string Format(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
            : $"{t.Minutes:D2}:{t.Seconds:D2}";
    }
}
