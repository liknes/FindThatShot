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
using VideoArchiveManager.App.Localization;
using VideoArchiveManager.Core.Models.Enums;
using VideoArchiveManager.Core.Services;
using VideoArchiveManager.Core.Services.Ai;

namespace VideoArchiveManager.App.ViewModels;

// Backs the AI suggestion review queue. Pending AiTagSuggestion rows are shown
// grouped by clip as accept/reject chips. Accepting promotes a suggestion to a
// real tag; rejecting remembers the dismissal. Source files are never touched.
public partial class AiReviewViewModel : ObservableObject
{
    private readonly IAiSuggestionService _suggestions;
    private readonly IThumbnailService _thumbnails;
    private static LocalizationManager L => LocalizationManager.Instance;

    public AiReviewViewModel(IAiSuggestionService suggestions, IThumbnailService thumbnails)
    {
        _suggestions = suggestions;
        _thumbnails = thumbnails;
    }

    // Raised after any accept (which creates a real tag) so the owner can
    // refresh the main grid's tag chips / sidebar tag list.
    public event EventHandler? TagsChanged;

    // Raised when the user asks to verify a suggestion in the player. The owner
    // (MainWindow) selects the parent clip and seeks to the tag's best frame.
    public event EventHandler<(int VideoItemId, double Seconds)>? JumpRequested;

    internal void RequestJump(int videoItemId, double seconds) =>
        JumpRequested?.Invoke(this, (videoItemId, seconds));

    // Lazily extracts (and caches) the single best-scoring frame for a suggestion
    // so the reviewer can eyeball whether the tag really belongs. Decoding is kept
    // off the UI thread; returns null when the source is offline or extraction fails.
    internal async Task<BitmapImage?> GeneratePreviewAsync(int suggestionId, string filePath, double seconds)
    {
        var path = await _thumbnails.GenerateAtPathAsync(suggestionId, filePath, seconds).ConfigureAwait(false);
        return path is null ? null : await ThumbnailLoader.LoadLargeAsync(path).ConfigureAwait(false);
    }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasData;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    public ObservableCollection<AiReviewGroupViewModel> Groups { get; } = new();

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        try
        {
            var groups = await _suggestions.GetPendingGroupedAsync(300);
            Groups.Clear();
            foreach (var g in groups)
            {
                Groups.Add(new AiReviewGroupViewModel(g, this));
            }

            var pending = await _suggestions.CountPendingAsync();
            IsEmpty = Groups.Count == 0;
            HasData = Groups.Count > 0;
            SummaryText = pending == 0
                ? L["AiReview_SummaryEmpty"]
                : L.Format("AiReview_Summary", pending.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), Groups.Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture));
        }
        finally
        {
            IsLoading = false;
        }
    }

    internal async Task AcceptSuggestionAsync(AiReviewGroupViewModel group, AiReviewChipViewModel chip)
    {
        await _suggestions.AcceptAsync(chip.SuggestionId, TagType.Subject);
        RemoveChip(group, chip);
        TagsChanged?.Invoke(this, EventArgs.Empty);
    }

    internal async Task RejectSuggestionAsync(AiReviewGroupViewModel group, AiReviewChipViewModel chip)
    {
        await _suggestions.RejectAsync(chip.SuggestionId);
        RemoveChip(group, chip);
    }

    internal async Task AcceptAllAsync(AiReviewGroupViewModel group)
    {
        await _suggestions.AcceptAllForClipAsync(group.VideoItemId, TagType.Subject);
        Groups.Remove(group);
        TagsChanged?.Invoke(this, EventArgs.Empty);
        UpdateSummaryAfterGroupChange();
    }

    internal async Task RejectAllAsync(AiReviewGroupViewModel group)
    {
        await _suggestions.RejectAllForClipAsync(group.VideoItemId);
        Groups.Remove(group);
        UpdateSummaryAfterGroupChange();
    }

    private void RemoveChip(AiReviewGroupViewModel group, AiReviewChipViewModel chip)
    {
        group.Chips.Remove(chip);
        if (group.Chips.Count == 0) Groups.Remove(group);
        UpdateSummaryAfterGroupChange();
    }

    private void UpdateSummaryAfterGroupChange()
    {
        var remaining = Groups.Sum(g => g.Chips.Count);
        IsEmpty = Groups.Count == 0;
        HasData = Groups.Count > 0;
        SummaryText = remaining == 0
            ? L["AiReview_SummaryEmpty"]
            : L.Format("AiReview_Summary", remaining.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), Groups.Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture));
    }
}

// One clip's worth of pending suggestion chips.
public partial class AiReviewGroupViewModel : ObservableObject
{
    private readonly AiReviewViewModel _parent;

    public AiReviewGroupViewModel(AiSuggestionGroup model, AiReviewViewModel parent)
    {
        _parent = parent;
        VideoItemId = model.VideoItemId;
        FileName = model.FileName;
        FolderPath = string.IsNullOrEmpty(model.FolderPath) ? model.FileName : model.FolderPath;
        FileExists = model.FileExists;
        ThumbnailPath = model.ThumbnailPath;

        Chips = new ObservableCollection<AiReviewChipViewModel>(
            model.Suggestions.Select(s => new AiReviewChipViewModel(s, this, parent)));
    }

    public int VideoItemId { get; }
    public string FileName { get; }
    public string FolderPath { get; }
    public bool FileExists { get; }

    // Exposed as a path (not a decoded BitmapImage): the AsyncImage behavior in
    // the card template decodes it off the UI thread, and only for the realized,
    // on-screen cards, so opening with hundreds of clips stays snappy.
    public string? ThumbnailPath { get; }

    public ObservableCollection<AiReviewChipViewModel> Chips { get; }

    [RelayCommand]
    private Task AcceptAll() => _parent.AcceptAllAsync(this);

    [RelayCommand]
    private Task RejectAll() => _parent.RejectAllAsync(this);
}

// One accept/reject chip for a single suggested tag.
public partial class AiReviewChipViewModel : ObservableObject
{
    private readonly AiReviewGroupViewModel _group;
    private readonly AiReviewViewModel _parent;

    public AiReviewChipViewModel(AiSuggestionItem model, AiReviewGroupViewModel group, AiReviewViewModel parent)
    {
        _group = group;
        _parent = parent;
        SuggestionId = model.SuggestionId;
        VideoItemId = model.VideoItemId;
        TagName = model.TagName;
        BestFrameSeconds = model.BestFrameSeconds;
        FilePath = model.FilePath;
        FileExists = model.FileExists;
        // Cosine similarity is roughly 0..1; surface it as a coarse match score.
        ConfidenceText = $"{Math.Round(model.Confidence * 100)}%";
    }

    public int SuggestionId { get; }
    public int VideoItemId { get; }
    public string TagName { get; }
    public string ConfidenceText { get; }
    public double? BestFrameSeconds { get; }
    public string FilePath { get; }
    public bool FileExists { get; }

    // Both preview paths need an online source to read a frame / play the clip.
    public bool CanPreview => FileExists && !string.IsNullOrWhiteSpace(FilePath);

    public bool IsOffline => !CanPreview;

    public string PreviewTooltip => CanPreview
        ? LocalizationManager.Instance["AiReview_PreviewTooltip"]
        : LocalizationManager.Instance["AiReview_OfflineTooltip"];

    // The best-scoring frame for this tag, lazily extracted on first hover.
    [ObservableProperty]
    private BitmapImage? _previewImage;

    [ObservableProperty]
    private bool _isPreviewLoading;

    private bool _previewRequested;

    // Lazy: triggered by the hover popup opening. Extracts/caches the still once,
    // then keeps it for the lifetime of the chip.
    public async Task EnsurePreviewAsync()
    {
        if (_previewRequested || !CanPreview) return;
        _previewRequested = true;

        IsPreviewLoading = true;
        try
        {
            PreviewImage = await _parent.GeneratePreviewAsync(SuggestionId, FilePath, BestFrameSeconds ?? 0);
        }
        finally
        {
            IsPreviewLoading = false;
        }
    }

    [RelayCommand]
    private Task Accept() => _parent.AcceptSuggestionAsync(_group, this);

    [RelayCommand]
    private Task Reject() => _parent.RejectSuggestionAsync(_group, this);

    // Verify in motion: open the parent clip and seek to this tag's best frame.
    [RelayCommand(CanExecute = nameof(CanPreview))]
    private void Jump() => _parent.RequestJump(VideoItemId, BestFrameSeconds ?? 0);
}
