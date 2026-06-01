using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using VideoArchiveManager.App.Helpers;
using VideoArchiveManager.Core.Configuration;
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
    private readonly IProxyResolver _proxyResolver;
    private readonly ISettingsStore _settings;

    public VideoDetailViewModel(
        IDbContextFactory<VideoArchiveDbContext> contextFactory,
        ITagService tagService,
        IFileSystemService fileSystem,
        ISidecarService sidecar,
        IProxyResolver proxyResolver,
        ISettingsStore settings)
    {
        _contextFactory = contextFactory;
        _tagService = tagService;
        _fileSystem = fileSystem;
        _sidecar = sidecar;
        _proxyResolver = proxyResolver;
        _settings = settings;
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

    // Snapshot of every Tag in the catalog, used to populate the
    // AutoSuggestBox dropdown. Refreshed when a video is loaded, when
    // the user adds a tag via this editor, and explicitly via
    // RefreshTagCatalogAsync (called by the main window after a bulk
    // edit, which can also mint new tags). Keeps the suggestion list
    // off the hot path — filtering happens in-memory.
    private List<Tag> _allTagsCache = new();

    // Filtered, type-aware, attached-tag-excluded view of _allTagsCache
    // matching the user's current NewTagName / NewTagType. Bound to
    // ui:AutoSuggestBox.ItemsSource so each keystroke updates the dropdown.
    public ObservableCollection<string> TagSuggestions { get; } = new();

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

    // Surface the result of the most recent save / tag-add / tag-remove,
    // including whether a sidecar was written, skipped, or failed. The main
    // window subscribes to this and mirrors it into the status bar so the
    // user always knows what just happened.
    [ObservableProperty]
    private string? _lastSaveStatus;

    // Fires every time the editor attaches a tag to the current video.
    // MainViewModel listens so the sidebar's tag picker can show newly
    // created tags immediately, without requiring an app restart or
    // a re-scan. The Tag may already exist in the catalog (Add typed an
    // existing name) — subscribers deduplicate by Id.
    public event EventHandler<Tag>? TagCatalogChanged;

    // Fires when the user asks to see the full info popup for the current
    // clip (right-click on the editor thumbnail / catalog card, or the
    // Alt+Enter shortcut). MainWindow listens and opens VideoInfoWindow.
    // The VM doesn't construct the window itself — that would couple the
    // VM to a specific UI host and to the sidecar service's view-side
    // formatting concerns.
    public event EventHandler<VideoItemViewModel>? ShowInfoRequested;

    [RelayCommand]
    private void ShowInfo()
    {
        if (Current is null) return;
        ShowInfoRequested?.Invoke(this, Current);
    }

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

        // Refresh the suggestion cache for the AutoSuggestBox. Cheap
        // single-table query and the user is likely about to start
        // tagging this clip.
        await RefreshTagCatalogAsync();
    }

    // Reloads the in-memory tag catalog used by the AutoSuggestBox.
    // Public so the host (MainViewModel) can call it after BulkEdit,
    // which can mint new tags the detail editor wouldn't otherwise
    // know about until the next video selection.
    public async Task RefreshTagCatalogAsync(CancellationToken cancellationToken = default)
    {
        var all = await _tagService.GetAllAsync(cancellationToken);
        _allTagsCache = all.ToList();
        UpdateTagSuggestions();
    }

    partial void OnNewTagNameChanged(string value) => UpdateTagSuggestions();

    partial void OnNewTagTypeChanged(TagType value) => UpdateTagSuggestions();

    // Rebuilds TagSuggestions from _allTagsCache using a case-insensitive
    // "contains" match against NewTagName, filtered to the currently
    // selected NewTagType (matches the DB's (Name, Type) uniqueness
    // contract), and excluding tags already attached to the current
    // video. Capped to 20 entries so the dropdown stays scannable.
    private void UpdateTagSuggestions()
    {
        TagSuggestions.Clear();
        if (_allTagsCache.Count == 0) return;
        if (string.IsNullOrWhiteSpace(NewTagName)) return;

        var attachedIds = Tags.Select(t => t.Id).ToHashSet();
        var query = NewTagName.Trim();

        var matches = _allTagsCache
            .Where(t => t.Type == NewTagType)
            .Where(t => !attachedIds.Contains(t.Id))
            .Where(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => t.Name)
            .Take(20);

        foreach (var name in matches) TagSuggestions.Add(name);
    }

    [RelayCommand]
    private void PlayInApp()
    {
        if (Current is null) return;
        if (!App.IsPlayerAvailable) return;
        if (!File.Exists(Current.FilePath)) return;

        // Honour the user's "Prefer DaVinci Resolve proxies for in-app
        // playback" setting (default off). When enabled and a proxy file
        // is present in a sibling "Proxy" folder, swap to it — much faster
        // decode on heavy 4K 60p sources. Falls back to the hero file
        // unconditionally when no proxy exists, so this is always safe.
        var sourcePath = Current.FilePath;
        if (_settings.Current.PreferProxyForPlayback)
        {
            var proxyPath = _proxyResolver.TryResolveProxy(Current.FilePath);
            if (!string.IsNullOrEmpty(proxyPath) && File.Exists(proxyPath))
            {
                sourcePath = proxyPath;
            }
        }

        MediaSource = new Uri(sourcePath, UriKind.Absolute);
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

        var sidecarStatus = await WriteSidecarStatusAsync(entity);
        LastSaveStatus = $"Saved · {sidecarStatus}";
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

        // Keep the suggestion cache fresh if this was a brand-new tag —
        // otherwise the next "Add" would miss it until a video reload.
        if (!_allTagsCache.Any(t => t.Id == tag.Id))
        {
            _allTagsCache.Add(tag);
        }

        // Notify the main window so the sidebar tag picker can pick up a
        // brand-new tag without waiting for the next ReloadFiltersAsync.
        TagCatalogChanged?.Invoke(this, tag);

        // Tagging is the clearest behavioural signal that a clip has been
        // looked at, so the first tag promotes the default Unreviewed status
        // to Keep automatically. This keeps the catalog's review queue
        // (StartReviewSession / OnlyUnreviewed filter) honest without
        // forcing the user to remember to flip Status manually.
        var statusBumped = await TryAutoBumpFromUnreviewedAsync();

        var sidecarStatus = await WriteSidecarStatusAsync();
        LastSaveStatus = statusBumped
            ? $"Tag added · status → Keep · {sidecarStatus}"
            : $"Tag added · {sidecarStatus}";
    }

    // Promotes Status from Unreviewed → Keep on the very first tag for a
    // clip. Returns true if a bump happened so the caller can surface it.
    // Re-reads from the DB to avoid racing with a stale in-memory Status,
    // and writes the bump in the same transaction as the timestamp update.
    private async Task<bool> TryAutoBumpFromUnreviewedAsync()
    {
        if (Current is null) return false;
        if (Status != VideoStatus.Unreviewed) return false;

        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var entity = await ctx.VideoItems.FirstOrDefaultAsync(v => v.Id == Current.Id);
        if (entity is null) return false;
        if (entity.Status != VideoStatus.Unreviewed) return false;

        entity.Status = VideoStatus.Keep;
        entity.UpdatedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync();

        // VideoItemViewModel.Status writes through to Model.Status and
        // raises change notifications, so the catalog grid + binding
        // listeners both see the new value with a single setter call.
        Current.Status = VideoStatus.Keep;
        Status = VideoStatus.Keep;
        return true;
    }

    [RelayCommand]
    private async Task RemoveTagAsync(Tag? tag)
    {
        if (tag is null || Current is null) return;
        await _tagService.DetachAsync(Current.Id, tag.Id);
        Tags.Remove(tag);
        Current.TagSummary = string.Join(", ", Tags.Select(t => t.Name));

        var sidecarStatus = await WriteSidecarStatusAsync();
        LastSaveStatus = $"Tag removed · {sidecarStatus}";
    }

    // Writes a sidecar (if enabled) and returns a short human-readable
    // status of what happened. NEVER throws; the caller is responsible for
    // user-visible labelling around the returned text.
    private async Task<string> WriteSidecarStatusAsync(VideoItem? entity = null)
    {
        if (!_sidecar.IsEnabled)
        {
            return "sidecar disabled";
        }
        if (Current is null)
        {
            return "sidecar skipped (no video selected)";
        }

        if (entity is null)
        {
            await using var ctx = await _contextFactory.CreateDbContextAsync();
            entity = await ctx.VideoItems.FirstOrDefaultAsync(v => v.Id == Current.Id);
            if (entity is null) return "sidecar skipped (video not found in catalog)";
        }

        var result = await _sidecar.WriteAsync(entity, Tags.ToArray());
        if (result.Written && !string.IsNullOrEmpty(result.Path))
        {
            return $"sidecar written: {result.Path}";
        }
        if (result.Skipped)
        {
            return "sidecar disabled";
        }
        return $"sidecar FAILED: {result.ErrorMessage ?? "unknown error"}";
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
