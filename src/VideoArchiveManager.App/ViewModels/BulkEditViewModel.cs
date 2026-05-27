using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Models.Enums;
using VideoArchiveManager.Core.Services;
using VideoArchiveManager.Data;

namespace VideoArchiveManager.App.ViewModels;

public partial class BulkEditViewModel : ObservableObject
{
    private readonly IDbContextFactory<VideoArchiveDbContext> _contextFactory;
    private readonly ITagService _tagService;

    public BulkEditViewModel(
        IDbContextFactory<VideoArchiveDbContext> contextFactory,
        ITagService tagService)
    {
        _contextFactory = contextFactory;
        _tagService = tagService;
    }

    public IReadOnlyList<int> TargetIds { get; private set; } = Array.Empty<int>();

    public ObservableCollection<VideoStatus> AvailableStatuses { get; } = new(Enum.GetValues<VideoStatus>());
    public ObservableCollection<TagType> AvailableTagTypes { get; } = new(Enum.GetValues<TagType>());

    [ObservableProperty]
    private bool _applyStatus;

    [ObservableProperty]
    private VideoStatus _status = VideoStatus.Unreviewed;

    [ObservableProperty]
    private bool _applyRating;

    [ObservableProperty]
    private int _rating;

    [ObservableProperty]
    private bool _applyNotesAppend;

    [ObservableProperty]
    private string _notesToAppend = string.Empty;

    [ObservableProperty]
    private bool _applyTag;

    [ObservableProperty]
    private string _newTagName = string.Empty;

    [ObservableProperty]
    private TagType _newTagType = TagType.Subject;

    public event Action? Completed;
    public event Action? Cancelled;

    public void Initialize(IEnumerable<int> ids)
    {
        TargetIds = ids.ToArray();
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (TargetIds.Count == 0)
        {
            Completed?.Invoke();
            return;
        }

        await using (var ctx = await _contextFactory.CreateDbContextAsync())
        {
            var entities = await ctx.VideoItems
                .Where(v => TargetIds.Contains(v.Id))
                .ToListAsync();

            foreach (var entity in entities)
            {
                if (ApplyStatus) entity.Status = Status;
                if (ApplyRating) entity.Rating = Math.Clamp(Rating, 0, 5);
                if (ApplyNotesAppend && !string.IsNullOrWhiteSpace(NotesToAppend))
                {
                    var text = NotesToAppend.Trim();
                    entity.Notes = string.IsNullOrWhiteSpace(entity.Notes)
                        ? text
                        : entity.Notes + Environment.NewLine + text;
                }
                entity.UpdatedAt = DateTime.UtcNow;
            }

            await ctx.SaveChangesAsync();
        }

        if (ApplyTag && !string.IsNullOrWhiteSpace(NewTagName))
        {
            var tag = await _tagService.GetOrCreateAsync(NewTagName.Trim(), NewTagType);
            await _tagService.BulkAttachAsync(TargetIds, tag.Id);
        }

        Completed?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();
}
