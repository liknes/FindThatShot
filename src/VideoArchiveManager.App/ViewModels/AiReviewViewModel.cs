using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoArchiveManager.App.Helpers;
using VideoArchiveManager.Core.Models.Enums;
using VideoArchiveManager.Core.Services.Ai;

namespace VideoArchiveManager.App.ViewModels;

// Backs the AI suggestion review queue. Pending AiTagSuggestion rows are shown
// grouped by clip as accept/reject chips. Accepting promotes a suggestion to a
// real tag; rejecting remembers the dismissal. Source files are never touched.
public partial class AiReviewViewModel : ObservableObject
{
    private readonly IAiSuggestionService _suggestions;

    public AiReviewViewModel(IAiSuggestionService suggestions)
    {
        _suggestions = suggestions;
    }

    // Raised after any accept (which creates a real tag) so the owner can
    // refresh the main grid's tag chips / sidebar tag list.
    public event EventHandler? TagsChanged;

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
                ? "No pending suggestions."
                : $"{pending:N0} suggestion(s) across {Groups.Count:N0} clip(s)";
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
            ? "No pending suggestions."
            : $"{remaining:N0} suggestion(s) across {Groups.Count:N0} clip(s)";
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
        TagName = model.TagName;
        // Cosine similarity is roughly 0..1; surface it as a coarse match score.
        ConfidenceText = $"{Math.Round(model.Confidence * 100)}%";
    }

    public int SuggestionId { get; }
    public string TagName { get; }
    public string ConfidenceText { get; }

    [RelayCommand]
    private Task Accept() => _parent.AcceptSuggestionAsync(_group, this);

    [RelayCommand]
    private Task Reject() => _parent.RejectSuggestionAsync(_group, this);
}
