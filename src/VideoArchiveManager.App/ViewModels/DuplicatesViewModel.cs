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
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoArchiveManager.App.Helpers;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Services;
using System.Windows.Media.Imaging;

namespace VideoArchiveManager.App.ViewModels;

// Backs the Find duplicates window. Pulls metadata-fingerprint duplicate
// groups from IDuplicateDetectionService, lets the user pick which redundant
// copies to forget, and removes only those catalog rows via
// IVideoLibraryService. No source video file is ever read or modified here.
public partial class DuplicatesViewModel : ObservableObject
{
    private readonly IDuplicateDetectionService _detector;
    private readonly IVideoLibraryService _library;

    public DuplicatesViewModel(IDuplicateDetectionService detector, IVideoLibraryService library)
    {
        _detector = detector;
        _library = library;
    }

    // Raised after catalog rows are removed so the owning window can refresh
    // the main grid (which would otherwise still show the now-gone clips).
    public event EventHandler? CatalogChanged;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasData;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRemove))]
    private int _selectedCount;

    [ObservableProperty]
    private string _selectionText = string.Empty;

    public bool CanRemove => SelectedCount > 0 && !IsLoading;

    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = new();

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;
        OnPropertyChanged(nameof(CanRemove));

        try
        {
            var groups = await _detector.FindDuplicatesAsync();

            Groups.Clear();
            foreach (var g in groups)
            {
                Groups.Add(new DuplicateGroupViewModel(g, RecomputeSelection));
            }

            IsEmpty = Groups.Count == 0;
            HasData = Groups.Count > 0;
            SummaryText = BuildSummary(groups);
            RecomputeSelection();
        }
        catch (Exception ex)
        {
            HasData = false;
            IsEmpty = false;
            ErrorMessage = $"Couldn't scan for duplicates: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(CanRemove));
        }
    }

    // Pre-select every redundant copy across all groups (everything except the
    // suggested keep in each set) as a one-click starting point. The user can
    // still toggle individual rows before committing.
    [RelayCommand]
    private void SelectRedundant()
    {
        foreach (var group in Groups)
        {
            foreach (var item in group.Items)
            {
                item.MarkedForRemoval = !item.IsSuggestedKeep;
            }
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var group in Groups)
        {
            foreach (var item in group.Items)
            {
                item.MarkedForRemoval = false;
            }
        }
    }

    [RelayCommand]
    private async Task RemoveSelectedAsync()
    {
        var marked = Groups
            .SelectMany(g => g.Items)
            .Where(i => i.MarkedForRemoval)
            .ToList();

        if (marked.Count == 0) return;

        // Guard against the user emptying an entire group. Keeping at least one
        // copy of each clip in the catalog is almost always what's intended;
        // make them confirm explicitly if a whole set is selected.
        var groupsFullySelected = Groups.Count(g => g.Items.Count > 0 && g.Items.All(i => i.MarkedForRemoval));

        var ids = marked.Select(i => i.Id).ToList();

        var preview = string.Join(
            Environment.NewLine,
            marked.Take(6).Select(i => "  • " + i.FileName));
        if (marked.Count > 6)
        {
            preview += Environment.NewLine + $"  …and {marked.Count - 6} more";
        }

        var warning = groupsFullySelected > 0
            ? $"\n\nWARNING: {groupsFullySelected} duplicate set(s) have EVERY copy selected — " +
              "removing them leaves no catalog entry for that clip at all."
            : string.Empty;

        var message =
            $"Remove {ids.Count} duplicate catalog entr(y/ies) from the database?\n\n" +
            preview + warning + "\n\n" +
            "This removes the catalog rows (tags, ratings, notes) and their cached " +
            "thumbnails only. The source video files on disk will NOT be touched, " +
            "moved, or deleted.";

        var result = MessageBox.Show(
            message,
            "Remove duplicates from database",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel);
        if (result != MessageBoxResult.OK) return;

        IsLoading = true;
        OnPropertyChanged(nameof(CanRemove));
        try
        {
            await _library.RemoveByIdsAsync(ids);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(CanRemove));
        }

        CatalogChanged?.Invoke(this, EventArgs.Empty);

        // Re-scan so freshly resolved groups (now single-copy, or gone) drop off.
        await LoadAsync();
    }

    private void RecomputeSelection()
    {
        var count = Groups.Sum(g => g.Items.Count(i => i.MarkedForRemoval));
        SelectedCount = count;

        if (count == 0)
        {
            SelectionText = "No copies selected";
        }
        else
        {
            var bytes = Groups
                .SelectMany(g => g.Items)
                .Where(i => i.MarkedForRemoval)
                .Sum(i => i.FileSizeBytes);
            SelectionText = $"{count:N0} selected \u00b7 {FormatSize(bytes)} of disk no longer cataloged";
        }
    }

    private static string BuildSummary(IReadOnlyList<DuplicateGroup> groups)
    {
        var sets = groups.Count;
        var redundant = groups.Sum(g => g.RedundantCount);
        var reclaim = groups.Sum(g => g.RedundantBytes);
        return $"{sets:N0} duplicate set{(sets == 1 ? "" : "s")} \u00b7 " +
               $"{redundant:N0} redundant cop{(redundant == 1 ? "y" : "ies")} \u00b7 " +
               $"{FormatSize(reclaim)} reclaimable on disk";
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(CanRemove));

    internal static string FormatSize(long bytes)
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

    internal static string FormatDuration(double? totalSeconds)
    {
        if (totalSeconds is null || totalSeconds <= 0) return "—";
        var t = TimeSpan.FromSeconds(totalSeconds.Value);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }
}

// One duplicate set: a short fingerprint header plus its member rows.
public class DuplicateGroupViewModel
{
    public DuplicateGroupViewModel(DuplicateGroup model, Action onSelectionChanged)
    {
        var resolution = model.Width is > 0 && model.Height is > 0
            ? $"{model.Width}\u00d7{model.Height}"
            : "resolution unknown";

        HeaderText =
            $"{model.Videos.Count} copies \u00b7 {DuplicatesViewModel.FormatSize(model.FileSizeBytes)} each \u00b7 " +
            $"{resolution} \u00b7 {DuplicatesViewModel.FormatDuration(model.DurationSeconds)}";

        ReclaimText = $"{DuplicatesViewModel.FormatSize(model.RedundantBytes)} redundant";

        Items = new ObservableCollection<DuplicateItemViewModel>(
            model.Videos.Select(v => new DuplicateItemViewModel(v, onSelectionChanged)));
    }

    public string HeaderText { get; }
    public string ReclaimText { get; }
    public ObservableCollection<DuplicateItemViewModel> Items { get; }
}

// One member row inside a duplicate set.
public partial class DuplicateItemViewModel : ObservableObject
{
    private readonly Action _onSelectionChanged;

    public DuplicateItemViewModel(DuplicateVideo model, Action onSelectionChanged)
    {
        _onSelectionChanged = onSelectionChanged;

        Id = model.Id;
        FileName = model.FileName;
        FolderPath = string.IsNullOrEmpty(model.FolderPath) ? model.FilePath : model.FolderPath;
        FileSizeBytes = model.FileSizeBytes;
        IsSuggestedKeep = model.IsSuggestedKeep;
        FileExists = model.FileExists;
        _thumbnailPath = model.ThumbnailPath;

        var resolution = model.Width is > 0 && model.Height is > 0
            ? $"{model.Width}\u00d7{model.Height}"
            : null;
        var bits = new List<string> { DuplicatesViewModel.FormatSize(model.FileSizeBytes) };
        if (resolution is not null) bits.Add(resolution);
        if (!string.IsNullOrWhiteSpace(model.Codec)) bits.Add(model.Codec!);
        if (!string.IsNullOrWhiteSpace(model.Camera)) bits.Add(model.Camera!);
        DetailText = string.Join("  \u00b7  ", bits);
    }

    public int Id { get; }
    public string FileName { get; }
    public string FolderPath { get; }
    public long FileSizeBytes { get; }
    public bool IsSuggestedKeep { get; }
    public bool FileExists { get; }
    public string DetailText { get; }

    public string StatusText => FileExists ? "Online" : "Offline";

    private readonly string? _thumbnailPath;
    public BitmapImage? Thumbnail => ThumbnailLoader.Load(_thumbnailPath);

    [ObservableProperty]
    private bool _markedForRemoval;

    partial void OnMarkedForRemovalChanged(bool value) => _onSelectionChanged();
}
