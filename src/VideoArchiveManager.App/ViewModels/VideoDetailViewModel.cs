using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using VideoArchiveManager.App.Helpers;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Models.Enums;
using VideoArchiveManager.Core.Services;
using VideoArchiveManager.Data;

namespace VideoArchiveManager.App.ViewModels;

public partial class VideoDetailViewModel : ObservableObject
{
    private readonly IDbContextFactory<VideoArchiveDbContext> _contextFactory;
    private readonly ITagService _tagService;
    private readonly IFileSystemService _fileSystem;
    private readonly ISidecarService _sidecar;

    public VideoDetailViewModel(
        IDbContextFactory<VideoArchiveDbContext> contextFactory,
        ITagService tagService,
        IFileSystemService fileSystem,
        ISidecarService sidecar)
    {
        _contextFactory = contextFactory;
        _tagService = tagService;
        _fileSystem = fileSystem;
        _sidecar = sidecar;
    }

    [ObservableProperty]
    private VideoItemViewModel? _current;

    [ObservableProperty]
    private Uri? _mediaSource;

    [ObservableProperty]
    private bool _isPlayerVisible;

    public bool CanPlayInApp => App.IsPlayerAvailable;

    public string? PlayerUnavailableMessage => App.IsPlayerAvailable ? null : App.PlayerInitError;

    public BitmapImage? LargeThumbnail => Current is null ? null : ThumbnailLoader.LoadLarge(Current.Model.ThumbnailPath);

    public ObservableCollection<Tag> Tags { get; } = new();
    public ObservableCollection<TagType> AvailableTagTypes { get; } = new(Enum.GetValues<TagType>());
    public ObservableCollection<VideoStatus> AvailableStatuses { get; } = new(Enum.GetValues<VideoStatus>());
    public ObservableCollection<int> RatingValues { get; } = new(new[] { 0, 1, 2, 3, 4, 5 });

    [ObservableProperty]
    private string _newTagName = string.Empty;

    [ObservableProperty]
    private TagType _newTagType = TagType.Subject;

    [ObservableProperty]
    private string? _notes;

    [ObservableProperty]
    private int _rating;

    [ObservableProperty]
    private VideoStatus _status;

    public async Task LoadAsync(VideoItemViewModel? item)
    {
        ClosePlayer();

        Current = item;
        Tags.Clear();
        if (item is null)
        {
            Notes = null;
            Rating = 0;
            Status = VideoStatus.Unreviewed;
            OnPropertyChanged(nameof(LargeThumbnail));
            return;
        }

        Notes = item.Model.Notes;
        Rating = item.Model.Rating;
        Status = item.Model.Status;

        var tags = await _tagService.GetTagsForVideoAsync(item.Id);
        foreach (var t in tags) Tags.Add(t);
        OnPropertyChanged(nameof(LargeThumbnail));
    }

    [RelayCommand]
    private void PlayInApp()
    {
        if (Current is null) return;
        if (!App.IsPlayerAvailable) return;
        if (!File.Exists(Current.FilePath)) return;

        MediaSource = new Uri(Current.FilePath, UriKind.Absolute);
        IsPlayerVisible = true;
    }

    [RelayCommand]
    private void ClosePlayer()
    {
        MediaSource = null;
        IsPlayerVisible = false;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Current is null) return;

        VideoItem? entity;
        await using (var ctx = await _contextFactory.CreateDbContextAsync())
        {
            entity = await ctx.VideoItems.FirstOrDefaultAsync(v => v.Id == Current.Id);
            if (entity is null) return;

            entity.Notes = Notes;
            entity.Rating = Rating;
            entity.Status = Status;
            entity.UpdatedAt = DateTime.UtcNow;
            await ctx.SaveChangesAsync();
        }

        Current.Model.Notes = Notes;
        Current.Rating = Rating;
        Current.Status = Status;

        await TryWriteSidecarAsync(entity);
    }

    [RelayCommand]
    private async Task AddTagAsync()
    {
        if (Current is null || string.IsNullOrWhiteSpace(NewTagName)) return;
        var tag = await _tagService.GetOrCreateAsync(NewTagName.Trim(), NewTagType);
        await _tagService.AttachAsync(Current.Id, tag.Id);
        if (!Tags.Any(t => t.Id == tag.Id))
        {
            Tags.Add(tag);
        }
        NewTagName = string.Empty;
        Current.TagSummary = string.Join(", ", Tags.Select(t => t.Name));

        await TryWriteSidecarAsync();
    }

    [RelayCommand]
    private async Task RemoveTagAsync(Tag? tag)
    {
        if (tag is null || Current is null) return;
        await _tagService.DetachAsync(Current.Id, tag.Id);
        Tags.Remove(tag);
        Current.TagSummary = string.Join(", ", Tags.Select(t => t.Name));

        await TryWriteSidecarAsync();
    }

    private async Task TryWriteSidecarAsync(VideoItem? entity = null)
    {
        if (!_sidecar.IsEnabled || Current is null) return;

        if (entity is null)
        {
            await using var ctx = await _contextFactory.CreateDbContextAsync();
            entity = await ctx.VideoItems.FirstOrDefaultAsync(v => v.Id == Current.Id);
            if (entity is null) return;
        }

        await _sidecar.WriteAsync(entity, Tags.ToArray());
    }

    [RelayCommand]
    private void OpenFileLocation()
    {
        if (Current is null) return;
        _fileSystem.RevealInExplorer(Current.FilePath);
    }

    [RelayCommand]
    private void CopyFilePath()
    {
        if (Current is null) return;
        try
        {
            Clipboard.SetText(Current.FilePath);
        }
        catch
        {
            // Clipboard can intermittently fail; ignore.
        }
    }

    [RelayCommand]
    private void Play()
    {
        if (Current is null) return;
        _fileSystem.OpenWithDefaultPlayer(Current.FilePath);
    }
}
