using System.Collections.ObjectModel;
using System.Globalization;
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
    private readonly IReverseGeocodingService _reverseGeocoder;

    public VideoDetailViewModel(
        IDbContextFactory<VideoArchiveDbContext> contextFactory,
        ITagService tagService,
        IFileSystemService fileSystem,
        ISidecarService sidecar,
        IProxyResolver proxyResolver,
        ISettingsStore settings,
        IReverseGeocodingService reverseGeocoder)
    {
        _contextFactory = contextFactory;
        _tagService = tagService;
        _fileSystem = fileSystem;
        _sidecar = sidecar;
        _proxyResolver = proxyResolver;
        _settings = settings;
        _reverseGeocoder = reverseGeocoder;
    }

    [ObservableProperty]
    private VideoItemViewModel? _current;

    [ObservableProperty]
    private Uri? _mediaSource;

    [ObservableProperty]
    private bool _isPlayerVisible;

    // True when PlayInApp substituted a DaVinci-style proxy for the hero
    // clip's MediaSource. Surfaces a "PROXY" badge in the player toolbar
    // so the user can tell at a glance whether they're watching the
    // original file or a transcoded preview. ActiveProxyPath carries the
    // resolved absolute path for the chip's tooltip.
    [ObservableProperty]
    private bool _isPlayingProxy;

    [ObservableProperty]
    private string? _activeProxyPath;

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

    // Manual GPS picker state. The right-sidebar map enters pick mode when
    // the user clicks the "+ Set location" CTA (or "Edit location" on a
    // clip that already has GPS). While IsPickingLocation is true, the
    // map installs a click handler that posts back lat/lon, and the
    // sidebar reveals a small bottom form with a coords text input and
    // Save / Cancel buttons. Pending* hold the in-progress coordinates
    // until the user saves; on Save they are written to the entity, the
    // result is reverse-geocoded to refresh LocationText, and a sidecar
    // is written.
    [ObservableProperty]
    private bool _isPickingLocation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveLocationCommand))]
    private double? _pendingLatitude;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveLocationCommand))]
    private double? _pendingLongitude;

    // Two-way bound to a TextBox in the picker overlay. Accepts "lat, lon"
    // or "lat,lon" using invariant-culture decimal separators (matches
    // the format VideoItemViewModel.GpsText produces, so paste round-trips
    // cleanly). Parsing happens in OnPendingCoordsTextChanged.
    [ObservableProperty]
    private string _pendingCoordsText = string.Empty;

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

    // Selection change resets any half-started pick — otherwise the user
    // would see the bottom picker form hovering over a different clip's
    // map, which would be both confusing and a footgun (Save would
    // persist the previous clip's pending coords to the new clip).
    partial void OnCurrentChanged(VideoItemViewModel? value)
    {
        if (IsPickingLocation)
        {
            ResetPickState();
        }
    }

    // Entering review mode (in-app player visible) collapses the whole
    // map block, so any in-flight pick is no longer visible and would
    // strand the user in a state with no way to Save / Cancel.
    partial void OnIsPlayerVisibleChanged(bool value)
    {
        if (value && IsPickingLocation)
        {
            ResetPickState();
        }
    }

    // Two-way bound to the picker TextBox. Accepts either "lat, lon" or
    // "lat,lon" using invariant-culture decimals; rejects anything that
    // doesn't parse to two finite doubles inside [-90, 90] / [-180, 180].
    // Invalid input clears Pending* so SaveLocationCommand's CanExecute
    // stays false — the disabled Save button is the visible signal.
    partial void OnPendingCoordsTextChanged(string value)
    {
        if (TryParseCoords(value, out var lat, out var lon))
        {
            PendingLatitude = lat;
            PendingLongitude = lon;
        }
        else
        {
            PendingLatitude = null;
            PendingLongitude = null;
        }
    }

    private static bool TryParseCoords(string? text, out double lat, out double lon)
    {
        lat = 0;
        lon = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim();
        var commaIdx = trimmed.IndexOf(',');
        if (commaIdx <= 0 || commaIdx == trimmed.Length - 1) return false;

        var latPart = trimmed[..commaIdx].Trim();
        var lonPart = trimmed[(commaIdx + 1)..].Trim();

        if (!double.TryParse(latPart, NumberStyles.Float, CultureInfo.InvariantCulture, out lat)) return false;
        if (!double.TryParse(lonPart, NumberStyles.Float, CultureInfo.InvariantCulture, out lon)) return false;
        if (!double.IsFinite(lat) || !double.IsFinite(lon)) return false;
        if (lat < -90 || lat > 90 || lon < -180 || lon > 180) return false;

        return true;
    }

    private void ResetPickState()
    {
        IsPickingLocation = false;
        PendingLatitude = null;
        PendingLongitude = null;
        PendingCoordsText = string.Empty;
    }

    // Called by the host (MainWindow code-behind) when LocationMapView
    // raises LocationPicked. We update both Pending* and PendingCoordsText
    // so the TextBox reflects the pin position, and the user can still
    // tweak the value manually before hitting Save.
    public void ApplyPickedLocation(double latitude, double longitude)
    {
        if (!IsPickingLocation) return;
        PendingLatitude = latitude;
        PendingLongitude = longitude;
        PendingCoordsText = string.Format(
            CultureInfo.InvariantCulture,
            "{0:0.######}, {1:0.######}",
            latitude, longitude);
    }

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
        string? activeProxy = null;
        if (_settings.Current.PreferProxyForPlayback)
        {
            var proxyPath = _proxyResolver.TryResolveProxy(Current.FilePath);
            if (!string.IsNullOrEmpty(proxyPath) && File.Exists(proxyPath))
            {
                sourcePath = proxyPath;
                activeProxy = proxyPath;
            }
        }

        ActiveProxyPath = activeProxy;
        IsPlayingProxy = activeProxy is not null;
        MediaSource = new Uri(sourcePath, UriKind.Absolute);
        IsPlayerVisible = true;
    }

    [RelayCommand]
    private void ClosePlayer()
    {
        MediaSource = null;
        ActiveProxyPath = null;
        IsPlayingProxy = false;
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

    // === Manual GPS location picker ===========================================

    // Enter pick mode. When the clip already has GPS, seed the TextBox with
    // the current coords so the user is editing rather than starting blank;
    // otherwise start empty so they can type or click freshly.
    [RelayCommand]
    private void BeginPickLocation()
    {
        if (Current is null) return;
        if (Current.GpsLatitude is double curLat && Current.GpsLongitude is double curLon)
        {
            PendingLatitude = curLat;
            PendingLongitude = curLon;
            PendingCoordsText = string.Format(
                CultureInfo.InvariantCulture,
                "{0:0.######}, {1:0.######}",
                curLat, curLon);
        }
        else
        {
            PendingLatitude = null;
            PendingLongitude = null;
            PendingCoordsText = string.Empty;
        }
        IsPickingLocation = true;
    }

    [RelayCommand]
    private void CancelPickLocation()
    {
        ResetPickState();
    }

    // SaveLocation is enabled only when both Pending* are valid.
    private bool CanSaveLocation()
        => Current is not null
           && PendingLatitude.HasValue
           && PendingLongitude.HasValue;

    [RelayCommand(CanExecute = nameof(CanSaveLocation))]
    private async Task SaveLocationAsync()
    {
        if (Current is null) return;
        if (PendingLatitude is not double lat) return;
        if (PendingLongitude is not double lon) return;

        VideoItem? entity;
        await using (var ctx = await _contextFactory.CreateDbContextAsync())
        {
            entity = await ctx.VideoItems.FirstOrDefaultAsync(v => v.Id == Current.Id);
            if (entity is null) return;

            entity.GpsLatitude = lat;
            entity.GpsLongitude = lon;
            entity.UpdatedAt = DateTime.UtcNow;
            await ctx.SaveChangesAsync();
        }

        // Mirror onto the in-memory model so the map + the clip-info
        // popup pick up the new coords immediately. VideoItemViewModel's
        // GPS getters proxy directly to Model so we just need the model
        // fields plus a property-change ping for the map binding.
        Current.Model.GpsLatitude = lat;
        Current.Model.GpsLongitude = lon;
        Current.RefreshLocation();

        // Reverse-geocode in the background; failures are non-fatal —
        // LocationText simply stays as-is. Reuses the same Nominatim
        // service the "Fill missing locations from GPS…" command uses,
        // including its on-disk geocode cache.
        var geoStatus = "geocode skipped";
        try
        {
            var geo = await _reverseGeocoder.LookupAsync(lat, lon);
            if (geo is not null && !string.IsNullOrWhiteSpace(geo.LocationShort))
            {
                await using var ctx = await _contextFactory.CreateDbContextAsync();
                var fresh = await ctx.VideoItems.FirstOrDefaultAsync(v => v.Id == Current.Id);
                if (fresh is not null)
                {
                    fresh.LocationText = geo.LocationShort;
                    fresh.UpdatedAt = DateTime.UtcNow;
                    await ctx.SaveChangesAsync();
                    Current.Model.LocationText = geo.LocationShort;
                    Current.RefreshLocation();
                    entity = fresh;
                    geoStatus = $"location → {geo.LocationShort}";
                }
            }
            else
            {
                geoStatus = "geocode: no match";
            }
        }
        catch
        {
            // Network blip / rate limit / parse error — keep the GPS save
            // but signal geocode didn't run. The user can retry via the
            // existing "Fill missing locations from GPS…" catalog command.
            geoStatus = "geocode failed";
        }

        var sidecarStatus = await WriteSidecarStatusAsync(entity);
        LastSaveStatus = $"Location saved · {geoStatus} · {sidecarStatus}";

        ResetPickState();
    }

    // Clear the GPS coords on the current clip. LocationText is left alone
    // on purpose: it can be folder-derived (FolderNameParser) rather than
    // strictly GPS-derived, so blanking it here would discard user-visible
    // context that isn't actually about the dropped pin.
    [RelayCommand]
    private async Task ClearLocationAsync()
    {
        if (Current is null) return;
        if (Current.GpsLatitude is null && Current.GpsLongitude is null) return;

        VideoItem? entity;
        await using (var ctx = await _contextFactory.CreateDbContextAsync())
        {
            entity = await ctx.VideoItems.FirstOrDefaultAsync(v => v.Id == Current.Id);
            if (entity is null) return;

            entity.GpsLatitude = null;
            entity.GpsLongitude = null;
            entity.UpdatedAt = DateTime.UtcNow;
            await ctx.SaveChangesAsync();
        }

        Current.Model.GpsLatitude = null;
        Current.Model.GpsLongitude = null;
        Current.RefreshLocation();

        var sidecarStatus = await WriteSidecarStatusAsync(entity);
        LastSaveStatus = $"Location cleared · {sidecarStatus}";

        ResetPickState();
    }

    [RelayCommand]
    private void CopyLocation()
    {
        var text = Current?.GpsText;
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // Clipboard can intermittently fail on locked / RDP sessions; ignore.
        }
    }
}
