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
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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

// Severity tiers for the transient inline tag-feedback line in the editor's
// TAGS panel. Drives the message colour: Success (green) for a clean add /
// remove, Warning (amber) when the catalog write succeeded but the sidecar
// couldn't be written, Error (red) when the catalog write itself failed.
public enum FeedbackSeverity
{
    Success,
    Warning,
    Error
}

public partial class VideoDetailViewModel : ObservableObject
{
    private readonly IDbContextFactory<VideoArchiveDbContext> _contextFactory;
    private readonly ITagService _tagService;
    private readonly IMomentService _momentService;
    private readonly IFileSystemService _fileSystem;
    private readonly ISidecarService _sidecar;
    private readonly IProxyResolver _proxyResolver;
    private readonly ISettingsStore _settings;
    private readonly IReverseGeocodingService _reverseGeocoder;
    private readonly IDjiSrtTelemetryReader _srtReader;

    // Drives the auto-clear of the inline TAGS-panel feedback line. 2s after the
    // last add / remove the message fades out (see ShowTagFeedback). Created on
    // the UI thread with the VM, so its Tick fires on the dispatcher.
    private readonly DispatcherTimer _tagFeedbackTimer =
        new() { Interval = TimeSpan.FromSeconds(2) };

    // Same idea for the moment editor's "Save moment" confirmation.
    private readonly DispatcherTimer _momentFeedbackTimer =
        new() { Interval = TimeSpan.FromSeconds(2) };

    public VideoDetailViewModel(
        IDbContextFactory<VideoArchiveDbContext> contextFactory,
        ITagService tagService,
        IMomentService momentService,
        IFileSystemService fileSystem,
        ISidecarService sidecar,
        IProxyResolver proxyResolver,
        ISettingsStore settings,
        IReverseGeocodingService reverseGeocoder,
        IDjiSrtTelemetryReader srtReader)
    {
        _contextFactory = contextFactory;
        _tagService = tagService;
        _momentService = momentService;
        _fileSystem = fileSystem;
        _sidecar = sidecar;
        _proxyResolver = proxyResolver;
        _settings = settings;
        _reverseGeocoder = reverseGeocoder;
        _srtReader = srtReader;

        // Seed the live toggle from the persisted setting so the strip starts
        // in the user's last-chosen state. Assigning the backing field directly
        // (rather than the property) avoids the change handler firing a
        // redundant save during construction.
        _showTelemetryOverlay = settings.Current.ShowPlayerTelemetry;

        _tagFeedbackTimer.Tick += (_, _) =>
        {
            _tagFeedbackTimer.Stop();
            TagFeedback = null;
        };

        _momentFeedbackTimer.Tick += (_, _) =>
        {
            _momentFeedbackTimer.Stop();
            MomentFeedback = null;
        };
    }

    [ObservableProperty]
    private VideoItemViewModel? _current;

    [ObservableProperty]
    private Uri? _mediaSource;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTelemetryStripVisible))]
    [NotifyPropertyChangedFor(nameof(IsMapVisible))]
    private bool _isPlayerVisible;

    // DJI flight track for the selected clip, read lazily from a sibling
    // ".SRT" companion on selection. Null when the clip has no companion (or
    // the read is still in flight); the sidebar map draws it as a polyline
    // with takeoff / landing dots when present. Not persisted — it's derived
    // on demand from the file on disk, so a multi-thousand-point track never
    // touches the catalog DB.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMapVisible))]
    private IReadOnlyList<GeoPoint>? _flightPath;

    // Live drone position for the sidebar map, updated every player tick from
    // the time-indexed telemetry track (see UpdateTelemetryForPosition) so a
    // marker tracks the drone along its flight path as the video plays. Null
    // when there's no current sample (before the first cue, or no telemetry).
    [ObservableProperty]
    private GeoPoint? _dronePosition;

    // Cancels the in-flight SRT read when the selection changes again before
    // the previous parse finished, so a slow read for an earlier clip can't
    // land its track on the clip the user has since moved to.
    private CancellationTokenSource? _flightPathCts;

    // Time-indexed DJI telemetry for the clip being played, parsed from the
    // sibling ".SRT" companion when playback starts. The player's 250ms tick
    // calls UpdateTelemetryForPosition to pick the sample matching the current
    // position and surface it through CurrentTelemetry. Held as an array so the
    // per-tick binary search over it is allocation-free.
    private DjiTelemetrySample[]? _telemetryTrack;

    private CancellationTokenSource? _telemetryCts;

    // True once a non-empty telemetry track has been loaded for the current
    // clip; gates both the strip and its inline toggle so neither appears for
    // clips without DJI telemetry.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTelemetryStripVisible))]
    private bool _hasTelemetry;

    // The telemetry frame matching the current playback position. All the
    // formatted chip strings below derive from it, so changing it refreshes the
    // whole strip in one notification batch.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TelemetryIso))]
    [NotifyPropertyChangedFor(nameof(TelemetryShutter))]
    [NotifyPropertyChangedFor(nameof(TelemetryAperture))]
    [NotifyPropertyChangedFor(nameof(TelemetryFocalLength))]
    [NotifyPropertyChangedFor(nameof(TelemetryAltitude))]
    [NotifyPropertyChangedFor(nameof(TelemetryGps))]
    private DjiTelemetrySample? _currentTelemetry;

    // User toggle for the strip, mirrored to AppSettings.ShowPlayerTelemetry so
    // the choice persists across restarts (see OnShowTelemetryOverlayChanged).
    // Initialised from settings in the constructor.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTelemetryStripVisible))]
    private bool _showTelemetryOverlay;

    public bool IsTelemetryStripVisible => IsPlayerVisible && HasTelemetry && ShowTelemetryOverlay;

    // The sidebar map shows in normal mode (as always), and ALSO stays visible
    // during review when there's a flight path to animate the drone marker
    // along — that's the whole point of the playback-synced map. With no
    // track, review mode keeps the map hidden so the editor focuses on
    // tags / notes (the original behaviour).
    public bool IsMapVisible => !IsPlayerVisible || FlightPath is not null;

    public string? TelemetryIso =>
        CurrentTelemetry?.Iso is { } iso ? iso.ToString(CultureInfo.InvariantCulture) : null;

    public string? TelemetryShutter =>
        string.IsNullOrWhiteSpace(CurrentTelemetry?.Shutter) ? null : CurrentTelemetry!.Value.Shutter;

    public string? TelemetryAperture =>
        CurrentTelemetry?.FNumber is { } f ? $"f/{f.ToString("0.0", CultureInfo.InvariantCulture)}" : null;

    public string? TelemetryFocalLength =>
        CurrentTelemetry?.FocalLength is { } fl ? $"{fl.ToString("0.#", CultureInfo.InvariantCulture)} mm" : null;

    public string? TelemetryAltitude =>
        CurrentTelemetry?.RelativeAltitude is { } alt ? $"{alt.ToString("0.0", CultureInfo.InvariantCulture)} m" : null;

    public string? TelemetryGps =>
        CurrentTelemetry is { Latitude: { } lat, Longitude: { } lon }
            ? $"{lat.ToString("0.00000", CultureInfo.InvariantCulture)}, {lon.ToString("0.00000", CultureInfo.InvariantCulture)}"
            : null;

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

    public ObservableCollection<AttachedTag> Tags { get; } = new();
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

    // Transient, colour-coded confirmation shown inline in the editor's TAGS
    // panel right after an add / remove (the bottom status bar keeps the
    // persistent, detailed record via LastSaveStatus). Null when nothing is
    // showing; the 2-second _tagFeedbackTimer clears it. Severity drives the
    // message colour in the XAML (green / amber / red).
    [ObservableProperty]
    private string? _tagFeedback;

    [ObservableProperty]
    private FeedbackSeverity _tagFeedbackSeverity;

    // Transient confirmation shown inline under the moment editor's Save moment
    // button. Same auto-clearing, colour-coded treatment as the tag feedback.
    [ObservableProperty]
    private string? _momentFeedback;

    [ObservableProperty]
    private FeedbackSeverity _momentFeedbackSeverity;

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

    // Raised when the user jumps to a moment while the player is already open on
    // the current clip. The host (MainWindow code-behind) seeks the active
    // engine — the VM has no reference to the player surface itself.
    public event EventHandler<double>? SeekRequested;

    // === Timestamped moments (sub-clips / "the shot") ========================

    // Moments attached to the currently-selected clip, ordered by in-point.
    public ObservableCollection<MomentViewModel> Moments { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMoments))]
    [NotifyPropertyChangedFor(nameof(MomentCountText))]
    private int _momentCount;

    public bool HasMoments => MomentCount > 0;

    public string MomentCountText => MomentCount == 1 ? "1 moment" : $"{MomentCount} moments";

    // The moment whose compact editor (label / rating / notes / tags) is shown
    // below the list. Null when nothing is selected.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedMoment))]
    private MomentViewModel? _selectedMoment;

    public bool HasSelectedMoment => SelectedMoment is not null;

    // The pending in-point captured by the "I" key (or the Mark in button),
    // awaiting an out-point. Null when no in-point is staged.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingInPoint))]
    private double? _pendingInPoint;

    public bool HasPendingInPoint => PendingInPoint.HasValue;

    // Transient one-line status for the marker workflow ("In point 00:01:12 —
    // press O to set the out point", "Moment added", etc.).
    [ObservableProperty]
    private string? _momentMarkStatus;

    [ObservableProperty]
    private string _newMomentTagName = string.Empty;

    [ObservableProperty]
    private TagType _newMomentTagType = TagType.Subject;

    // Consumed by the player host after a clip is opened via JumpToMoment so it
    // can seek to the moment's in-point once the media is ready. Read-and-clear.
    private double? _pendingSeekSeconds;

    public double? ConsumePendingSeek()
    {
        var v = _pendingSeekSeconds;
        _pendingSeekSeconds = null;
        return v;
    }

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

        // Cancel any flight-path read still running for the previous clip and
        // clear the old track immediately so the map doesn't show a stale
        // polyline while the new clip's SRT (if any) is parsed.
        _flightPathCts?.Cancel();
        _flightPathCts?.Dispose();
        _flightPathCts = null;
        FlightPath = null;

        // Drop any telemetry track from the previous clip immediately (the
        // fresh one is loaded lazily on PlayInApp, not here).
        ResetTelemetry();

        ClearMomentState();

        if (item is null)
        {
            Notes = null;
            Rating = 0;
            Status = VideoStatus.Unreviewed;
            OnPropertyChanged(nameof(LargeThumbnail));
            return;
        }

        // Fire-and-forget: a sibling DJI ".SRT" companion, when present, is
        // parsed off the UI thread and the resulting track is applied back
        // on this context once it completes (guarded against a since-changed
        // selection inside LoadFlightPathAsync). Gated behind the display-only
        // "Show drone flight paths" setting — when off we simply never read
        // the track, so FlightPath stays null and the map falls back to the
        // single-fix pin (geotags are unaffected; they're read at scan time).
        if (_settings.Current.ShowDroneFlightPaths)
        {
            _ = LoadFlightPathAsync(item);
        }

        Notes = item.Model.Notes;
        Rating = item.Model.Rating;
        Status = item.Model.Status;

        var videoTags = await _tagService.GetVideoTagsForVideoAsync(item.Id);
        foreach (var vt in videoTags) Tags.Add(new AttachedTag(vt.Tag, vt.IsBackground));
        OnPropertyChanged(nameof(LargeThumbnail));

        // Refresh the suggestion cache for the AutoSuggestBox. Cheap
        // single-table query and the user is likely about to start
        // tagging this clip.
        await RefreshTagCatalogAsync();

        await LoadMomentsAsync(item.Id);
    }

    // Loads the moments for a clip into the editor list. Cheap join query;
    // re-run whenever the selection changes or a moment is added/removed.
    private async Task LoadMomentsAsync(int videoItemId)
    {
        var moments = await _momentService.GetForVideoAsync(videoItemId);

        // Guard against a selection that moved on while we were loading.
        if (Current is null || Current.Id != videoItemId) return;

        Moments.Clear();
        foreach (var m in moments)
        {
            Moments.Add(new MomentViewModel(m, m.MomentTags));
        }
        MomentCount = Moments.Count;
        Current.MomentCount = MomentCount;
    }

    // Resets all per-clip moment UI state. Called on every selection change so
    // a pending in-point or selected moment can't leak across clips.
    private void ClearMomentState()
    {
        Moments.Clear();
        MomentCount = 0;
        SelectedMoment = null;
        PendingInPoint = null;
        MomentMarkStatus = null;
        NewMomentTagName = string.Empty;
    }

    // Parses the clip's sibling DJI ".SRT" companion (if any) into a flight
    // track on a background thread, then applies it to FlightPath — but only
    // if the user hasn't moved to a different clip in the meantime. All
    // failures are swallowed: a missing / unreadable / non-DJI SRT simply
    // leaves FlightPath null and the map falls back to the single-fix marker.
    private async Task LoadFlightPathAsync(VideoItemViewModel item)
    {
        var cts = new CancellationTokenSource();
        _flightPathCts = cts;
        try
        {
            var path = await _srtReader
                .TryReadFlightPathAsync(item.FilePath, cancellationToken: cts.Token)
                .ConfigureAwait(true);

            // Ignore a result that arrived after the selection moved on, or a
            // path with nothing worth drawing.
            if (cts.IsCancellationRequested) return;
            if (!ReferenceEquals(Current, item)) return;
            if (path is null || path.Count < 2) return;

            FlightPath = path;
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer selection; nothing to do.
        }
        catch
        {
            // Defensive: never let a telemetry-read failure surface to the UI.
        }
    }

    // Parses the clip's sibling DJI ".SRT" into a time-indexed telemetry track
    // for the player overlay. Kicked off from PlayInApp (not selection) so we
    // only pay the full-file parse when the user actually opens the player, and
    // guarded against a since-changed selection the same way LoadFlightPathAsync
    // is. Reads from the *original* clip path (Current.FilePath) because the
    // SRT sits next to the source, not next to any proxy we may be playing.
    private async Task LoadTelemetryTrackAsync(VideoItemViewModel item)
    {
        var cts = new CancellationTokenSource();
        _telemetryCts = cts;
        try
        {
            var track = await _srtReader
                .TryReadTelemetryTrackAsync(item.FilePath, cancellationToken: cts.Token)
                .ConfigureAwait(true);

            if (cts.IsCancellationRequested) return;
            if (!ReferenceEquals(Current, item)) return;
            if (track is null || track.Count == 0) return;

            _telemetryTrack = track as DjiTelemetrySample[] ?? track.ToArray();
            HasTelemetry = true;

            // Prime the readout with the first frame so the strip shows content
            // immediately instead of a blank bar until the first position tick.
            CurrentTelemetry = _telemetryTrack[0];
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer selection; nothing to do.
        }
        catch
        {
            // Defensive: a missing / unreadable / non-DJI SRT just leaves the
            // overlay hidden.
        }
    }

    // Cancels any in-flight telemetry read and clears the loaded track + live
    // sample. Called whenever the played clip changes or the player closes so a
    // stale readout can't linger over a different clip.
    private void ResetTelemetry()
    {
        _telemetryCts?.Cancel();
        _telemetryCts?.Dispose();
        _telemetryCts = null;
        _telemetryTrack = null;
        HasTelemetry = false;
        CurrentTelemetry = null;
        DronePosition = null;
    }

    // Picks the telemetry sample whose [Start, End) window contains the given
    // playback position and publishes it through CurrentTelemetry. Called from
    // the player's position tick (both the mpv and FFME paths). Cheap: a binary
    // search over the sorted track, and a no-op when the matched sample hasn't
    // changed since the last tick.
    public void UpdateTelemetryForPosition(TimeSpan position)
    {
        var track = _telemetryTrack;
        if (track is null || track.Length == 0) return;

        // Find the last cue whose Start is <= position.
        int lo = 0, hi = track.Length - 1, idx = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (track[mid].Start <= position)
            {
                idx = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        var match = track[idx];

        // Before the first cue starts there's no valid sample yet.
        if (position < match.Start)
        {
            if (CurrentTelemetry is not null) CurrentTelemetry = null;
            if (DronePosition is not null) DronePosition = null;
            return;
        }

        if (CurrentTelemetry is { } cur && cur.Start == match.Start) return;
        CurrentTelemetry = match;

        // Mirror the matched sample's GPS into the live map marker. Samples
        // without coordinates (rare gaps in the SRT) clear the marker rather
        // than freezing it at a stale spot.
        DronePosition = match is { Latitude: { } mlat, Longitude: { } mlon }
            ? new GeoPoint(mlat, mlon, match.RelativeAltitude)
            : null;
    }

    // Persist the live toggle to settings (fire-and-forget) so the player's
    // inline show/hide choice survives a restart and stays in sync with the
    // Settings-window checkbox.
    partial void OnShowTelemetryOverlayChanged(bool value)
    {
        _settings.Current.ShowPlayerTelemetry = value;
        _ = _settings.SaveAsync(_settings.Current);
    }

    [RelayCommand]
    private void ToggleTelemetryOverlay() => ShowTelemetryOverlay = !ShowTelemetryOverlay;

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

        // Lazily parse the DJI telemetry track for the player overlay now that
        // we're actually opening the player. Fire-and-forget against the
        // original clip path (the SRT lives next to the source, not the proxy).
        ResetTelemetry();
        _ = LoadTelemetryTrackAsync(Current);
    }

    [RelayCommand]
    private void ClosePlayer()
    {
        MediaSource = null;
        ActiveProxyPath = null;
        IsPlayingProxy = false;
        IsPlayerVisible = false;
        ResetTelemetry();

        // A staged in-point only makes sense while the player is open; drop it
        // so reopening the player doesn't resurrect a stale half-marked range.
        PendingInPoint = null;
        MomentMarkStatus = null;
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

        var (_, sidecarText) = await WriteSidecarStatusAsync(entity);
        LastSaveStatus = $"Saved · {sidecarText}";
    }

    [RelayCommand]
    private async Task AddTagAsync()
    {
        if (Current is null || string.IsNullOrWhiteSpace(NewTagName)) return;
        var tag = await _tagService.GetOrCreateAsync(NewTagName.Trim(), NewTagType);
        NewTagName = string.Empty;
        await AttachTagCoreAsync(tag);
    }

    // Shared attach pipeline behind both the Add button and the review-mode
    // pinned-tag hotkeys: persists the join row, mirrors the tag into the
    // editor's chip list + suggestion cache, notifies the sidebar picker of a
    // possibly-new tag, auto-promotes an Unreviewed clip to Keep on its first
    // tag, and writes the sidecar — reporting the outcome through LastSaveStatus.
    private async Task AttachTagCoreAsync(Tag tag)
    {
        if (Current is null) return;

        try
        {
            await _tagService.AttachAsync(Current.Id, tag.Id);
            if (!Tags.Any(t => t.Id == tag.Id))
            {
                Tags.Add(new AttachedTag(tag, isBackground: false));
            }
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

            var (sidecarFailed, sidecarText) = await WriteSidecarStatusAsync();
            LastSaveStatus = statusBumped
                ? $"Tag added · status → Keep · {sidecarText}"
                : $"Tag added · {sidecarText}";

            // The catalog write succeeded by the time we get here; a sidecar
            // failure is a partial success (curation is safe in the DB), so it's
            // surfaced as a warning rather than a hard error.
            if (sidecarFailed)
            {
                ShowTagFeedback($"Tag added: {tag.Name} — sidecar write failed", FeedbackSeverity.Warning);
            }
            else
            {
                ShowTagFeedback(
                    statusBumped ? $"Tag added: {tag.Name} · status → Keep" : $"Tag added: {tag.Name}",
                    FeedbackSeverity.Success);
            }
        }
        catch (Exception ex)
        {
            LastSaveStatus = $"Tag add failed: {ex.Message}";
            ShowTagFeedback($"Couldn't add tag: {ex.Message}", FeedbackSeverity.Error);
        }
    }

    // Toggles a tag (identified by name + type, the catalog's natural key) on
    // the current clip. Already attached → detach; otherwise create-if-needed
    // and attach. Routed to by the review-mode number-key hotkeys, and reuses
    // the same attach / detach pipelines as the editor so auto-Keep, sidecars
    // and the catalog picker all stay in sync regardless of entry point.
    public async Task ToggleTagAsync(string name, TagType type)
    {
        if (Current is null || string.IsNullOrWhiteSpace(name)) return;
        var trimmed = name.Trim();

        var attached = Tags.FirstOrDefault(
            t => t.Type == type && string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (attached is not null)
        {
            await RemoveTagAsync(attached);
            return;
        }

        var tag = await _tagService.GetOrCreateAsync(trimmed, type);
        await AttachTagCoreAsync(tag);
    }

    // Routes a review-mode number-key press to the pinned tag bound to that
    // slot (slot 0 = "1" key … slot 9 = "0" key), reading the live mapping
    // from settings so a Settings-dialog edit takes effect on the next
    // keypress without any reload wiring. A no-op when the slot is empty /
    // unconfigured, so an unbound digit does nothing.
    public Task ToggleTagBySlotAsync(int slotIndex)
    {
        if (Current is null) return Task.CompletedTask;
        var pinned = _settings.Current.PinnedTags;
        if (pinned is null || pinned.Count == 0) return Task.CompletedTask;

        var entry = pinned.FirstOrDefault(p => p is not null && p.Slot == slotIndex);
        if (entry is null || string.IsNullOrWhiteSpace(entry.Name)) return Task.CompletedTask;

        return ToggleTagAsync(entry.Name, entry.Type);
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
    private async Task RemoveTagAsync(AttachedTag? tag)
    {
        if (tag is null || Current is null) return;
        try
        {
            await _tagService.DetachAsync(Current.Id, tag.Id);
            Tags.Remove(tag);
            Current.TagSummary = string.Join(", ", Tags.Select(t => t.Name));

            var (sidecarFailed, sidecarText) = await WriteSidecarStatusAsync();
            LastSaveStatus = $"Tag removed · {sidecarText}";
            ShowTagFeedback(
                sidecarFailed ? $"Tag removed: {tag.Name} — sidecar write failed" : $"Tag removed: {tag.Name}",
                sidecarFailed ? FeedbackSeverity.Warning : FeedbackSeverity.Success);
        }
        catch (Exception ex)
        {
            LastSaveStatus = $"Tag remove failed: {ex.Message}";
            ShowTagFeedback($"Couldn't remove tag: {ex.Message}", FeedbackSeverity.Error);
        }
    }

    // Flips a clip tag between primary and background (incidental) prominence
    // from the chip's right-click menu, persists it, and rewrites the sidecar so
    // the portable record stays in sync. A no-op when nothing is attached.
    [RelayCommand]
    private async Task ToggleTagProminenceAsync(AttachedTag? tag)
    {
        if (tag is null || Current is null) return;
        var next = !tag.IsBackground;
        try
        {
            await _tagService.SetTagProminenceAsync(Current.Id, tag.Id, next);
            tag.IsBackground = next;

            var (sidecarFailed, _) = await WriteSidecarStatusAsync();
            ShowTagFeedback(
                next ? $"Marked as background: {tag.Name}" : $"Marked as main subject: {tag.Name}",
                sidecarFailed ? FeedbackSeverity.Warning : FeedbackSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowTagFeedback($"Couldn't update tag: {ex.Message}", FeedbackSeverity.Error);
        }
    }

    // Shows a short, colour-coded message inline in the editor's TAGS panel and
    // starts (or restarts) the 2-second auto-clear timer. Reused by every tag
    // entry point — the Add button, chip remove, and the review-mode pinned-tag
    // hotkeys — since they all funnel through AttachTagCoreAsync / RemoveTagAsync.
    private void ShowTagFeedback(string message, FeedbackSeverity severity)
    {
        TagFeedbackSeverity = severity;
        TagFeedback = message;
        _tagFeedbackTimer.Stop();
        _tagFeedbackTimer.Start();
    }

    // Writes a sidecar and returns a short human-readable status of what
    // happened. A sidecar is written when the setting is on OR when one
    // already exists for the clip (so an existing file stays in sync even
    // when "write new sidecars" is off). NEVER throws; the caller is
    // responsible for user-visible labelling around the returned text.
    private async Task<(bool Failed, string Text)> WriteSidecarStatusAsync(VideoItem? entity = null)
    {
        if (Current is null)
        {
            return (false, "sidecar skipped (no video selected)");
        }
        if (!_sidecar.ShouldWriteFor(Current.FilePath))
        {
            return (false, "sidecar disabled");
        }

        if (entity is null)
        {
            await using var ctx = await _contextFactory.CreateDbContextAsync();
            entity = await ctx.VideoItems.FirstOrDefaultAsync(v => v.Id == Current.Id);
            if (entity is null) return (true, "sidecar skipped (video not found in catalog)");
        }

        // Always carry the clip's current moments into the sidecar so the
        // portable record stays complete and in sync with the catalog. Pull the
        // join rows (not the in-memory chip list) so each tag's prominence
        // (IsBackground) is written from the catalog's source of truth.
        var moments = await _momentService.GetForVideoAsync(Current.Id);
        var videoTags = await _tagService.GetVideoTagsForVideoAsync(Current.Id);
        var result = await _sidecar.WriteAsync(entity, videoTags, moments);
        if (result.Written && !string.IsNullOrEmpty(result.Path))
        {
            return (false, $"sidecar written: {result.Path}");
        }
        if (result.Skipped)
        {
            return (false, "sidecar disabled");
        }
        return (true, $"sidecar FAILED: {result.ErrorMessage ?? "unknown error"}");
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

        var (_, sidecarText) = await WriteSidecarStatusAsync(entity);
        LastSaveStatus = $"Location saved · {geoStatus} · {sidecarText}";

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

        var (_, sidecarText) = await WriteSidecarStatusAsync(entity);
        LastSaveStatus = $"Location cleared · {sidecarText}";

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

    // === Moment capture, jump, and editing ===================================

    // "I" key / Mark in button: stage the current playback position as the
    // in-point of a moment-to-be. The out-point arrives with "O".
    public void MarkInPoint(TimeSpan position)
    {
        if (Current is null) return;
        PendingInPoint = Math.Max(0, position.TotalSeconds);
        MomentMarkStatus = $"In point {FormatSeconds(PendingInPoint!.Value)} — press O to set the out point";
    }

    // "O" key / Mark out button: close the staged range into a saved moment. If
    // no in-point is staged, capture a single-point marker at the current
    // position instead (so a lone "O" still flags the instant).
    public async Task MarkOutPointAsync(TimeSpan position)
    {
        if (Current is null) return;
        var outSeconds = Math.Max(0, position.TotalSeconds);

        if (PendingInPoint is double inSeconds)
        {
            await CreateMomentAsync(inSeconds, outSeconds);
            PendingInPoint = null;
        }
        else
        {
            await CreateMomentAsync(outSeconds, null);
        }
    }

    // Drops a single-point marker at the current position (toolbar button), with
    // no range. Handy for "mark this frame" without an in/out gesture.
    public Task AddPointMomentAsync(TimeSpan position)
    {
        if (Current is null) return Task.CompletedTask;
        return CreateMomentAsync(Math.Max(0, position.TotalSeconds), null);
    }

    private async Task CreateMomentAsync(double startSeconds, double? endSeconds)
    {
        if (Current is null) return;
        var videoId = Current.Id;

        var moment = await _momentService.AddAsync(videoId, startSeconds, endSeconds, null);

        // The selection may have moved while ffmpeg grabbed the frame.
        if (Current is null || Current.Id != videoId) return;

        var vm = new MomentViewModel(moment);
        InsertMomentSorted(vm);
        MomentCount = Moments.Count;
        SelectedMoment = vm;
        Current.MomentCount = MomentCount;

        MomentMarkStatus = endSeconds is null
            ? $"Marker added at {vm.StartText}"
            : $"Moment added {vm.TimeRangeText}";

        await WriteSidecarStatusAsync();
    }

    // Keeps the Moments collection sorted by in-point as new ones arrive, so the
    // list always reads chronologically regardless of capture order.
    private void InsertMomentSorted(MomentViewModel vm)
    {
        int i = 0;
        while (i < Moments.Count && Moments[i].StartSeconds <= vm.StartSeconds) i++;
        Moments.Insert(i, vm);
    }

    // Open the player on the current clip and seek to an arbitrary position.
    // Used by the moment-search window's "jump to" after the clip is selected.
    public void PlayAt(double startSeconds)
    {
        if (Current is null) return;
        if (IsPlayerVisible && MediaSource is not null)
        {
            SeekRequested?.Invoke(this, startSeconds);
        }
        else
        {
            _pendingSeekSeconds = startSeconds;
            PlayInApp();
        }
    }

    // Seek to a moment. If the player is already open on this clip, just seek;
    // otherwise open it and let the host apply the pending seek once ready.
    [RelayCommand]
    private void PlayMoment(MomentViewModel? moment)
    {
        if (moment is null || Current is null) return;
        SelectedMoment = moment;

        if (IsPlayerVisible && MediaSource is not null)
        {
            SeekRequested?.Invoke(this, moment.StartSeconds);
        }
        else
        {
            _pendingSeekSeconds = moment.StartSeconds;
            PlayInApp();
        }
    }

    [RelayCommand]
    private async Task SaveMomentAsync(MomentViewModel? moment)
    {
        moment ??= SelectedMoment;
        if (moment is null) return;

        try
        {
            moment.Model.Label = moment.Label;
            moment.Model.Notes = moment.Notes;
            moment.Model.Rating = moment.Rating;
            await _momentService.UpdateAsync(moment.Model);
            moment.RefreshTagSummary();

            var (sidecarFailed, _) = await WriteSidecarStatusAsync();
            MomentMarkStatus = $"Changes saved · {moment.DisplayLabel}";
            ShowMomentFeedback(
                sidecarFailed ? "Changes saved — sidecar write failed" : "Changes saved",
                sidecarFailed ? FeedbackSeverity.Warning : FeedbackSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowMomentFeedback($"Couldn't save changes: {ex.Message}", FeedbackSeverity.Error);
        }
    }

    // Inline, colour-coded confirmation under the moment editor's Save button,
    // auto-cleared after 2s (mirrors ShowTagFeedback).
    private void ShowMomentFeedback(string message, FeedbackSeverity severity)
    {
        MomentFeedbackSeverity = severity;
        MomentFeedback = message;
        _momentFeedbackTimer.Stop();
        _momentFeedbackTimer.Start();
    }

    [RelayCommand]
    private async Task DeleteMomentAsync(MomentViewModel? moment)
    {
        moment ??= SelectedMoment;
        if (moment is null) return;

        await _momentService.DeleteAsync(moment.Id);
        Moments.Remove(moment);
        if (ReferenceEquals(SelectedMoment, moment)) SelectedMoment = null;
        MomentCount = Moments.Count;
        if (Current is not null)
        {
            Current.MomentCount = MomentCount;
        }
        MomentMarkStatus = "Moment deleted";
        await WriteSidecarStatusAsync();
    }

    [RelayCommand]
    private async Task AddMomentTagAsync()
    {
        if (SelectedMoment is null || string.IsNullOrWhiteSpace(NewMomentTagName)) return;
        var tag = await _tagService.GetOrCreateAsync(NewMomentTagName.Trim(), NewMomentTagType);
        NewMomentTagName = string.Empty;

        await _momentService.AttachTagAsync(SelectedMoment.Id, tag.Id);
        if (!SelectedMoment.Tags.Any(t => t.Id == tag.Id))
        {
            SelectedMoment.Tags.Add(new AttachedTag(tag, isBackground: false));
            SelectedMoment.RefreshTagSummary();
        }

        // A moment tag can mint a brand-new catalog tag; surface it like the
        // clip-tag path so the sidebar picker stays in sync.
        TagCatalogChanged?.Invoke(this, tag);
        MomentMarkStatus = $"Tag added to moment · {tag.Name}";
        await WriteSidecarStatusAsync();
    }

    [RelayCommand]
    private async Task RemoveMomentTagAsync(AttachedTag? tag)
    {
        if (tag is null || SelectedMoment is null) return;
        await _momentService.DetachTagAsync(SelectedMoment.Id, tag.Id);
        SelectedMoment.Tags.Remove(tag);
        SelectedMoment.RefreshTagSummary();
        MomentMarkStatus = $"Tag removed from moment · {tag.Name}";
        await WriteSidecarStatusAsync();
    }

    // Moment-tag analogue of ToggleTagProminenceAsync: flips a moment tag
    // between primary and background and rewrites the sidecar.
    [RelayCommand]
    private async Task ToggleMomentTagProminenceAsync(AttachedTag? tag)
    {
        if (tag is null || SelectedMoment is null) return;
        var next = !tag.IsBackground;
        await _momentService.SetTagProminenceAsync(SelectedMoment.Id, tag.Id, next);
        tag.IsBackground = next;
        MomentMarkStatus = next
            ? $"Moment tag marked as background · {tag.Name}"
            : $"Moment tag marked as main subject · {tag.Name}";
        await WriteSidecarStatusAsync();
    }

    private static string FormatSeconds(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
            : $"{t.Minutes:D2}:{t.Seconds:D2}";
    }
}
