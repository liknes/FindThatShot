using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Navigation;
using VideoArchiveManager.App.ViewModels;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Services;

namespace VideoArchiveManager.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ISidecarService _sidecar;

    // Set while the user is dragging the seek slider so the periodic
    // MediaElement.PropertyChanged "Position" updates don't fight the
    // user's input.
    private bool _isUserSeeking;

    // Our own source of truth for whether the FFME video is meant to be
    // playing, used to drive BOTH the play/pause toggle decision and the
    // button label. FFME's own signals are ambiguous here: during playback
    // the engine drops into the transient `Manual` state to refill its block
    // buffer, and a paused clip also settles into `Manual` — so neither
    // `MediaState` nor `IsPlaying` reliably distinguishes "playing" from
    // "paused". Tracking intent at every deliberate transition (open/play,
    // pause, stop, end-of-media) makes the button deterministic and flicker
    // free. (mpv has a reliable GetPaused(), so this flag is FFME-only.)
    private bool _playerPlaying;

    // Cached so we can restore the original 3-column layout when leaving review mode.
    private GridLength _normalLeftWidth;
    private GridLength _normalSplitterWidth;
    private GridLength _normalListWidth;
    private GridLength _normalEditorWidth;

    // Single non-modal "Get info" popup shared across right-clicks. We
    // bring the existing instance to the front instead of opening a
    // second window — duplicate properties popups for the same clip
    // would just be visual clutter.
    private VideoInfoWindow? _infoWindow;

    public MainWindow(MainViewModel viewModel, ISidecarService sidecar)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _sidecar = sidecar;
        DataContext = viewModel;
        Title = BuildWindowTitle();

        // Apply the user's last-saved sidebar width BEFORE caching it as the
        // "normal mode" width below, so the cached value reflects the user's
        // preferences (review mode then restores to that, not to the XAML
        // default 260px). Skip silently if the settings store says nothing
        // sensible — the column XAML default 260px stays in place.
        try
        {
            var persisted = _viewModel.InitialSidebarWidth;
            if (persisted >= 200d && persisted <= 600d)
            {
                LeftSidebarColumn.Width = new GridLength(persisted);
            }
        }
        catch
        {
            // Defensive: any exception here just means we use the XAML default.
        }

        // Remember the normal-mode column widths so we can restore them after
        // the user closes the in-app player.
        _normalLeftWidth = LeftSidebarColumn.Width;
        _normalSplitterWidth = LeftSplitterColumn.Width;
        _normalListWidth = VideoListColumn.Width;
        _normalEditorWidth = EditorColumn.Width;

        if (App.UseMpvPlayer)
        {
            // EXPERIMENTAL GPU player: mpv renders into its own child window,
            // so we hide the FFME element and don't subscribe its events. A
            // poll timer drives the seek slider + time readout since mpv
            // surfaces position via property queries rather than WPF events.
            ConfigureMpvPlayer();
        }
        else if (App.IsPlayerAvailable)
        {
            // FFME's MediaElement is its own player — no separate MediaPlayer
            // object to wire up. Subscribe to MediaElement events directly.
            // MediaOpening fires BEFORE the stream is fully decoded so it's
            // the only spot where MediaOptions (hardware acceleration, stream
            // selection, codec params) can still be mutated — see the
            // MediaElement_MediaOpening rationale for why we care.
            VideoPlayer.MediaOpening += MediaElement_MediaOpening;
            VideoPlayer.MediaOpened += MediaElement_MediaOpened;
            VideoPlayer.MediaEnded += MediaElement_MediaEnded;
            VideoPlayer.PropertyChanged += MediaElement_PropertyChanged;
            VideoPlayer.MessageLogged += MediaElement_MessageLogged;
        }

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        _viewModel.Detail.PropertyChanged += Detail_PropertyChanged;
        _viewModel.Detail.ShowInfoRequested += Detail_ShowInfoRequested;
    }

    private async void MainWindow_Closed(object? sender, EventArgs e)
    {
        try
        {
            _mpvTimer?.Stop();
            _mpvTimer = null;

            _viewModel.Detail.ShowInfoRequested -= Detail_ShowInfoRequested;

            if (_infoWindow is not null)
            {
                try { _infoWindow.Close(); }
                catch { /* already closing */ }
                _infoWindow = null;
            }

            if (App.IsPlayerAvailable)
            {
                VideoPlayer.MediaOpening -= MediaElement_MediaOpening;
                VideoPlayer.MediaOpened -= MediaElement_MediaOpened;
                VideoPlayer.MediaEnded -= MediaElement_MediaEnded;
                VideoPlayer.PropertyChanged -= MediaElement_PropertyChanged;
                VideoPlayer.MessageLogged -= MediaElement_MessageLogged;

                // Close the current media before the visual tree is torn down.
                // FFME's Close() is async and idempotent.
                await VideoPlayer.Close();
                VideoPlayer.Dispose();
            }
        }
        catch
        {
            // window is going away
        }
    }

    // Monotonic ticket for coalescing player-sync requests, plus a gate that
    // serializes the actual FFME commands. See Detail_PropertyChanged.
    private int _playerSyncRequest;
    private readonly SemaphoreSlim _playerSyncGate = new(1, 1);

    // --- EXPERIMENTAL mpv player state (only used when App.UseMpvPlayer) ---
    // mpv exposes position/duration via property polling, not WPF events, so we
    // tick a timer to refresh the transport bar. _pendingMpvSource covers the
    // race where a clip is selected before the HwndHost child window (and thus
    // the mpv instance) has been created — we stash the path and load it the
    // moment the player signals ready.
    private System.Windows.Threading.DispatcherTimer? _mpvTimer;
    private string? _pendingMpvSource;

    private void ConfigureMpvPlayer()
    {
        VideoPlayer.Visibility = Visibility.Collapsed;
        MpvPlayer.Visibility = Visibility.Visible;

        MpvPlayer.PlayerReady += (_, _) =>
        {
            if (_pendingMpvSource is { } pending)
            {
                MpvPlayer.Player?.Load(pending);
                _pendingMpvSource = null;
            }
            UpdatePlayPauseLabel();
        };

        MpvPlayer.PlayerFailed += (_, message) =>
        {
            PlayerCurrentTimeText.Text = "error";
            PlayerDurationText.Text = message.Length > 20 ? message[..20] : message;
        };

        _mpvTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _mpvTimer.Tick += (_, _) => UpdateMpvTime();
        _mpvTimer.Start();
    }

    private void UpdateMpvTime()
    {
        var player = MpvPlayer.Player;
        if (player is null) return;

        var pos = player.GetTimePosition();
        if (pos < 0) pos = 0;
        var dur = player.GetDuration();

        PlayerCurrentTimeText.Text = FormatTimecode(TimeSpan.FromSeconds(pos));
        PlayerDurationText.Text = dur > 0 ? FormatTimecode(TimeSpan.FromSeconds(dur)) : "00:00";

        // Refresh the DJI telemetry readout for the new position (no-op when the
        // clip has no telemetry track or the matched sample hasn't changed).
        _viewModel.Detail.UpdateTelemetryForPosition(TimeSpan.FromSeconds(pos));

        if (!_isUserSeeking && dur > 0)
        {
            var fraction = Math.Clamp(pos / dur, 0.0, 1.0);
            var newValue = fraction * PlayerSeekSlider.Maximum;
            if (Math.Abs(PlayerSeekSlider.Value - newValue) > 0.5)
                PlayerSeekSlider.Value = newValue;
        }

        UpdatePlayPauseLabel();
    }

    private static string FormatTimecode(TimeSpan t)
        => t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
            : $"{t.Minutes:D2}:{t.Seconds:D2}";

    private void Detail_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not VideoDetailViewModel detail) return;

        if (e.PropertyName == nameof(VideoDetailViewModel.IsPlayerVisible))
        {
            ApplyReviewModeLayout(detail.IsPlayerVisible);
            UpdateClosePlayerOverlay();
        }

        if (!App.IsPlayerAvailable) return;

        if (e.PropertyName is not (nameof(VideoDetailViewModel.MediaSource) or nameof(VideoDetailViewModel.IsPlayerVisible)))
        {
            return;
        }

        // Prev/next navigation fires a burst of these in one synchronous beat:
        // ClosePlayer nulls MediaSource + clears IsPlayerVisible, then PlayInApp
        // sets the new MediaSource + IsPlayerVisible. Acting on each one
        // immediately races a "close" against the following "open" on FFME's
        // async command queue and the player can settle closed on a black
        // frame. Instead we stamp each change with a ticket and defer the work
        // to Background priority, so the whole synchronous burst lands first
        // and only the final ticket actually drives the player to its
        // now-current desired state.
        var request = ++_playerSyncRequest;
        _ = Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => _ = SyncPlayerToDetailAsync(detail, request)));
    }

    private async Task SyncPlayerToDetailAsync(VideoDetailViewModel detail, int request)
    {
        // Superseded by a newer change that arrived in the same burst — skip.
        if (request != _playerSyncRequest) return;

        // Serialize media commands so a still-running Open/Close from a prior
        // (rapid) navigation can't interleave with this one.
        await _playerSyncGate.WaitAsync();
        try
        {
            // Re-check after acquiring the gate: a newer request may have
            // queued while we waited, in which case let that one win.
            if (request != _playerSyncRequest) return;

            if (App.UseMpvPlayer)
            {
                SyncMpvToDetail(detail);
                return;
            }

            if (detail.IsPlayerVisible && detail.MediaSource is not null)
            {
                // Open() handles tear-down of any previously-loaded media
                // internally, so we don't need a manual Close() between
                // clips. After Open returns, FFME has decoded the first
                // frame and NaturalDuration is final, so MediaOpened has
                // already fired by the time await returns.
                await VideoPlayer.Open(detail.MediaSource);
                await VideoPlayer.Play();
                _playerPlaying = true;
                UpdatePlayPauseLabel();
            }
            else
            {
                await VideoPlayer.Close();
                _playerPlaying = false;
                PlayerCurrentTimeText.Text = "00:00";
                PlayerDurationText.Text = "00:00";
                PlayerSeekSlider.Value = 0;
                PlayPauseButton.Content = "Play";
                if (TryFindResource("Icon.Play") is string playGlyph)
                {
                    Helpers.Controls.Theme.SetIcon(PlayPauseButton, playGlyph);
                }
            }
        }
        catch (Exception ex)
        {
            PlayerCurrentTimeText.Text = "error";
            PlayerDurationText.Text = ex.Message.Length > 20 ? ex.Message[..20] : ex.Message;
        }
        finally
        {
            _playerSyncGate.Release();
        }
    }

    // mpv equivalent of the FFME open/close branch above. mpv commands are
    // synchronous and cheap, so there's no async command queue to coalesce
    // against — the request-ticket gate in SyncPlayerToDetailAsync already
    // ensured only the latest desired state reaches us.
    private void SyncMpvToDetail(VideoDetailViewModel detail)
    {
        if (detail.IsPlayerVisible && detail.MediaSource is not null)
        {
            var path = detail.MediaSource.IsFile
                ? detail.MediaSource.LocalPath
                : detail.MediaSource.ToString();

            var player = MpvPlayer.Player;
            if (player is null)
            {
                // Child window / mpv not built yet — load on PlayerReady.
                _pendingMpvSource = path;
            }
            else
            {
                player.Load(path);
            }
            UpdatePlayPauseLabel();
        }
        else
        {
            _pendingMpvSource = null;
            MpvPlayer.Player?.Stop();
            PlayerCurrentTimeText.Text = "00:00";
            PlayerDurationText.Text = "00:00";
            PlayerSeekSlider.Value = 0;
            PlayPauseButton.Content = "Play";
            if (TryFindResource("Icon.Play") is string playGlyph)
            {
                Helpers.Controls.Theme.SetIcon(PlayPauseButton, playGlyph);
            }
        }
    }

    // Swap the main row's column widths so the sidebar + list collapse and the
    // player takes the central area when review mode opens. Restores the cached
    // original widths when leaving. Cheap (just sets four GridLength values).
    // The corner close (X) lives in a Popup (its own top-level HWND) so it
    // always composites above the video surface — including the experimental
    // mpv child window, which would hide any in-grid WPF overlay (airspace).
    // We open it only while the player is visible AND this window is active, so
    // the floating button never lingers on top of other apps after an alt-tab.
    private void Window_ActivationChanged(object? sender, EventArgs e) => UpdateClosePlayerOverlay();

    private void UpdateClosePlayerOverlay()
    {
        if (ClosePlayerPopup is null) return;
        ClosePlayerPopup.IsOpen = _viewModel.Detail.IsPlayerVisible && IsActive;
    }

    private void ApplyReviewModeLayout(bool reviewMode)
    {
        if (reviewMode)
        {
            // Snapshot the current sidebar width here (instead of relying on
            // _normalLeftWidth from construction) so a user-resized rail is
            // round-tripped correctly when leaving review mode. Same for
            // the splitter column — it MUST collapse to 0 in review mode,
            // otherwise the 5px hairline floats as dead chrome between the
            // player and the editor.
            _normalLeftWidth = LeftSidebarColumn.Width;
            _normalSplitterWidth = LeftSplitterColumn.Width;
            LeftSidebarColumn.Width = new GridLength(0);
            LeftSidebarColumn.MinWidth = 0;
            LeftSplitterColumn.Width = new GridLength(0);
            VideoListColumn.Width = new GridLength(0);
            PlayerColumn.Width = new GridLength(1, GridUnitType.Star);
            EditorColumn.Width = new GridLength(380);
            EditorColumn.MinWidth = 340;
        }
        else
        {
            LeftSidebarColumn.Width = _normalLeftWidth;
            LeftSidebarColumn.MinWidth = 200;
            LeftSplitterColumn.Width = _normalSplitterWidth;
            VideoListColumn.Width = _normalListWidth;
            PlayerColumn.Width = new GridLength(0);
            EditorColumn.Width = _normalEditorWidth;
            EditorColumn.MinWidth = 420;
        }
    }

    // Persist the user's last-dragged sidebar width so the rail starts in
    // the same shape next launch. We only fire on DragCompleted (not on
    // every layout pass) so the disk write is gated by user intent, not
    // animation / window-resize churn. Width clamping happens in
    // MainViewModel.PersistSidebarWidthAsync.
    private void LeftSplitter_OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_viewModel.Detail.IsPlayerVisible) return;
        var width = LeftSidebarColumn.ActualWidth;
        if (width <= 0) return;
        // Update the cached "normal mode" width too so review mode round-trips
        // to the user's drag rather than to the XAML default.
        _normalLeftWidth = new GridLength(width);
        _ = _viewModel.PersistSidebarWidthAsync(width);
    }

    private void VideoList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox) return;

        _viewModel.SelectedVideos.Clear();
        foreach (var obj in listBox.SelectedItems)
        {
            if (obj is VideoItemViewModel vm)
            {
                _viewModel.SelectedVideos.Add(vm);
            }
        }
    }

    // Double-clicking a catalog thumbnail starts in-app playback — the same
    // action as the sidebar "Play in app" button. The preceding single click
    // already selected the card (which synchronously sets Detail.Current via
    // LoadAsync), but we set the selection explicitly so playback always
    // targets the clip the user double-clicked, even on edge cases.
    private void Thumbnail_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if (sender is not FrameworkElement element) return;
        if (element.DataContext is not VideoItemViewModel vm) return;

        _viewModel.SelectedVideo = vm;

        var command = _viewModel.Detail.PlayInAppCommand;
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    // Re-entrancy guard for the tag picker. Adding a chip mutates
    // SelectedTagFilters, which synchronously rebuilds FilteredTags
    // (Clear + Add). That mutation re-fires SelectionChanged on the
    // ListBox with whatever item now sits at the previously-selected
    // index, which used to cascade into adding several chips per click.
    //
    // Two echo paths to defend against:
    //   1. SYNCHRONOUS — the lb.SelectedItem = null assignment, and the
    //      Clear/Add side effects, raise SelectionChanged on the same call
    //      stack. The guard is set before either, so they bail out immediately.
    //   2. DEFERRED — VirtualizingPanel.IsVirtualizing="True" makes the picker
    //      recycle containers asynchronously when its ItemsSource changes.
    //      WPF dispatches a focus/selection restore at a lower priority that
    //      fires SelectionChanged AFTER our finally block returns, with
    //      e.AddedItems[0] set to whatever item now sits at the previously-
    //      selected index. We have to keep the guard set until that deferred
    //      work has drained, so we reset it via Dispatcher.BeginInvoke at
    //      DispatcherPriority.Background instead of synchronously.
    private bool _isHandlingTagFilterSelection;

    // Sidebar tag picker: clicking a tag promotes it to a chip and immediately
    // clears the ListBox selection so the same row can be re-used after we
    // re-add the tag (e.g. user removes the chip, the tag reappears in the
    // list, click works again). Treating it as a "single-shot picker" instead
    // of a stateful selection keeps the VM simple — no orphaned SelectedItem
    // to manage.
    private void TagFilterList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isHandlingTagFilterSelection) return;
        if (sender is not ListBox lb) return;
        if (e.AddedItems.Count == 0) return;
        if (e.AddedItems[0] is not Tag tag) return;

        _isHandlingTagFilterSelection = true;
        // Clear the selection BEFORE the chip-add triggers a rebuild,
        // so any SelectionChanged echoes during the rebuild see a null
        // SelectedItem and exit early via the guard above.
        lb.SelectedItem = null;
        _viewModel.AddTagFilterCommand.Execute(tag);

        // Reset the guard at Background priority so the deferred SelectionChanged
        // raised by the virtualizing panel after FilteredTags is rebuilt arrives
        // while the guard is still set — otherwise the picker auto-selects index 0
        // (now the next tag in alphabetical order) and adds that as a second chip.
        Dispatcher.BeginInvoke(
            new Action(() => _isHandlingTagFilterSelection = false),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    // TreeView.SelectedItem is read-only — you can only set selection via
    // the user clicking or via the data model's IsSelected TwoWay binding
    // (see App.TreeViewItem in Resources/Components/FolderTree.xaml). The
    // VM's filter pipeline reads SelectedFolderNode, so we mirror the
    // event-driven selection onto it here. Selecting null clears the
    // folder filter, matching what "Clear filters" does.
    private void FolderTreeView_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _viewModel.SelectedFolderNode = e.NewValue as FolderNode;
    }

    // --- Drag-and-drop folders into the FOLDERS sidebar panel ---------
    //
    // Familiar from Lightroom / Bridge: drop one or more folders from
    // Windows Explorer onto the FOLDERS panel to register them as catalog
    // roots (the same outcome as the "Add folder…" picker / Ctrl+O). We
    // only accept the drop when the payload contains at least one real
    // directory; dropping plain files is rejected so the cursor shows the
    // "no-drop" affordance rather than silently doing nothing.

    private static IReadOnlyList<string> GetDroppedDirectories(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop)) return System.Array.Empty<string>();
        if (data.GetData(DataFormats.FileDrop) is not string[] paths) return System.Array.Empty<string>();
        return paths.Where(System.IO.Directory.Exists).ToList();
    }

    private void FoldersPanel_OnDragEnter(object sender, DragEventArgs e)
    {
        if (GetDroppedDirectories(e.Data).Count > 0)
        {
            FolderDropOverlay.Visibility = Visibility.Visible;
        }
        FoldersPanel_OnDragOver(sender, e);
    }

    private void FoldersPanel_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = GetDroppedDirectories(e.Data).Count > 0
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void FoldersPanel_OnDragLeave(object sender, DragEventArgs e)
    {
        FolderDropOverlay.Visibility = Visibility.Collapsed;
    }

    private async void FoldersPanel_OnDrop(object sender, DragEventArgs e)
    {
        FolderDropOverlay.Visibility = Visibility.Collapsed;
        var directories = GetDroppedDirectories(e.Data);
        if (directories.Count == 0) return;
        e.Handled = true;
        await _viewModel.AddRootFoldersByPathsAsync(directories);
    }

    // ModernWpf's AutoSuggestBox raises QuerySubmitted on Enter (or when
    // the user picks a suggestion with Enter). The Text binding has
    // already pushed NewTagName, so we just kick the existing
    // AddTagCommand on the detail VM. Keeps the Enter-to-add UX that the
    // old TextBox + KeyBinding gave us, plus dropdown-driven completion.
    private void NewTagSuggest_QuerySubmitted(
        ModernWpf.Controls.AutoSuggestBox sender,
        ModernWpf.Controls.AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_viewModel.Detail.AddTagCommand.CanExecute(null))
        {
            _viewModel.Detail.AddTagCommand.Execute(null);
        }
    }

    private void PlayerPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (App.UseMpvPlayer)
        {
            MpvTogglePlayPause();
            return;
        }

        ToggleFfmePlayPause();
    }

    // Single FFME play/pause toggle shared by the transport button and the
    // Space shortcut. CRITICAL ordering: we flip our `_playerPlaying` intent
    // flag and repaint the button label *synchronously, up front*, THEN fire
    // the FFME command. FFME's async Play()/Pause() can fault or stall while
    // the engine is mid-transition (the slow 4K refill churn is the usual
    // culprit); doing the flag/label update only *after* awaiting it meant a
    // faulting Pause() skipped the flip and left the button stuck on "Pause"
    // and unresponsive. Driving the UI from intent makes the button
    // deterministic regardless of how the engine command settles.
    private void ToggleFfmePlayPause()
    {
        if (!App.IsPlayerAvailable) return;
        _playerPlaying = !_playerPlaying;
        UpdatePlayPauseLabel();
        _ = ApplyFfmePlayingStateAsync();
    }

    private async Task ApplyFfmePlayingStateAsync()
    {
        try
        {
            if (_playerPlaying)
            {
                await VideoPlayer.Play();
            }
            else
            {
                await VideoPlayer.Pause();
            }
        }
        catch
        {
            // FFME can be in a transient state during media open/close; the
            // button label already reflects intent, so just swallow.
        }
    }

    private void MpvTogglePlayPause()
    {
        var player = MpvPlayer.Player;
        if (player is null) return;
        if (player.GetPaused()) player.Play();
        else player.Pause();
        UpdatePlayPauseLabel();
    }

    // Pause + seek-to-0 instead of Stop() so the first frame stays on screen.
    // FFME's Stop() closes the underlying decoder, which would tear down
    // the WriteableBitmap and leave a momentary black gap until reopen —
    // not strictly a regression vs the previous VLC behaviour, but the
    // "freeze at first frame" UX is cleaner.
    private async void PlayerStop_Click(object sender, RoutedEventArgs e)
    {
        if (App.UseMpvPlayer)
        {
            var player = MpvPlayer.Player;
            if (player is not null)
            {
                player.Pause();
                player.SeekAbsolute(0);
                UpdatePlayPauseLabel();
            }
            return;
        }

        if (!App.IsPlayerAvailable) return;

        // Update intent + label first (see ToggleFfmePlayPause), then drive the
        // engine — so a faulting Pause() can't leave the button mislabelled.
        _playerPlaying = false;
        UpdatePlayPauseLabel();
        try
        {
            await VideoPlayer.Pause();
            if (VideoPlayer.IsSeekable)
            {
                await VideoPlayer.Seek(TimeSpan.Zero);
            }
        }
        catch
        {
            // see PlayerPlayPause_Click rationale.
        }
    }

    private void PlayerSkipBack_Click(object sender, RoutedEventArgs e)
    {
        _ = SeekRelativeAsync(TimeSpan.FromSeconds(-5));
    }

    private void PlayerSkipForward_Click(object sender, RoutedEventArgs e)
    {
        _ = SeekRelativeAsync(TimeSpan.FromSeconds(5));
    }

    private async Task SeekRelativeAsync(TimeSpan delta)
    {
        if (App.UseMpvPlayer)
        {
            var player = MpvPlayer.Player;
            if (player is not null)
            {
                var target = player.GetTimePosition() + delta.TotalSeconds;
                if (target < 0) target = 0;
                player.SeekAbsolute(target);
            }
            return;
        }

        if (!App.IsPlayerAvailable) return;
        if (!VideoPlayer.IsSeekable) return;
        try
        {
            var current = VideoPlayer.Position;
            var duration = VideoPlayer.NaturalDuration ?? TimeSpan.Zero;
            var target = current + delta;
            if (target < TimeSpan.Zero) target = TimeSpan.Zero;
            // Stop ~100ms shy of the duration so we don't accidentally hit
            // EndOfStream and trigger MediaEnded for a deliberate +5s skip.
            if (duration > TimeSpan.Zero && target >= duration)
            {
                target = duration - TimeSpan.FromMilliseconds(100);
                if (target < TimeSpan.Zero) target = TimeSpan.Zero;
            }
            await VideoPlayer.Seek(target);
        }
        catch
        {
            // see PlayerPlayPause_Click rationale.
        }
    }

    private void PlayerSeekSlider_DragStarted(object sender, DragStartedEventArgs e)
    {
        _isUserSeeking = true;
    }

    private void PlayerSeekSlider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _ = ApplySliderPositionAsync();
        _isUserSeeking = false;
    }

    private void PlayerSeekSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Fires on a click directly on the track (no thumb drag). Apply once.
        _ = ApplySliderPositionAsync();
    }

    private async Task ApplySliderPositionAsync()
    {
        if (App.UseMpvPlayer)
        {
            var player = MpvPlayer.Player;
            if (player is not null)
            {
                var dur = player.GetDuration();
                if (dur > 0)
                {
                    var f = Math.Clamp(PlayerSeekSlider.Value / PlayerSeekSlider.Maximum, 0.0, 1.0);
                    player.SeekAbsolute(dur * f);
                }
            }
            return;
        }

        if (!App.IsPlayerAvailable) return;
        if (!VideoPlayer.IsSeekable) return;
        var duration = VideoPlayer.NaturalDuration ?? TimeSpan.Zero;
        if (duration <= TimeSpan.Zero) return;
        var fraction = Math.Clamp(PlayerSeekSlider.Value / PlayerSeekSlider.Maximum, 0.0, 1.0);
        try
        {
            await VideoPlayer.Seek(TimeSpan.FromTicks((long)(duration.Ticks * fraction)));
        }
        catch
        {
            // see PlayerPlayPause_Click rationale.
        }
    }

    private void ExitMenu_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AboutWindow
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    // Single shared, non-modal Diagnostics window. Non-modal so its live log
    // tail keeps updating while the user reproduces an issue in the main
    // window; resolved from DI for its IDiagnosticsLog / settings / ffprobe
    // dependencies. Re-clicking the menu brings the existing window forward
    // rather than stacking duplicates.
    private DiagnosticsWindow? _diagnosticsWindow;

    private void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (_diagnosticsWindow is not null && _diagnosticsWindow.IsLoaded)
        {
            if (_diagnosticsWindow.WindowState == WindowState.Minimized)
                _diagnosticsWindow.WindowState = WindowState.Normal;
            _diagnosticsWindow.Activate();
            return;
        }

        _diagnosticsWindow = App.GetService<DiagnosticsWindow>();
        _diagnosticsWindow.Owner = this;
        _diagnosticsWindow.Closed += (_, _) => _diagnosticsWindow = null;
        _diagnosticsWindow.Show();
    }

    // Single shared, non-modal catalog statistics window. Non-modal so the
    // user can keep it open beside the catalog while they curate; resolved
    // from DI for its statistics service + viewmodel. Re-clicking the menu
    // brings the existing window forward (and refreshes it) rather than
    // stacking duplicates.
    private CatalogStatsWindow? _statsWindow;

    private void CatalogStats_Click(object sender, RoutedEventArgs e)
    {
        if (_statsWindow is not null && _statsWindow.IsLoaded)
        {
            if (_statsWindow.WindowState == WindowState.Minimized)
                _statsWindow.WindowState = WindowState.Normal;
            _statsWindow.Activate();
            return;
        }

        _statsWindow = App.GetService<CatalogStatsWindow>();
        _statsWindow.Owner = this;
        _statsWindow.Closed += (_, _) => _statsWindow = null;
        _statsWindow.Show();
    }

    // Opens the non-modal clip-info popup. Reuses an existing window if one
    // is already open (refreshes its contents by recreating + replacing) so
    // power users mashing Alt+Enter don't end up with a stack of dialogs.
    // The popup is purely a viewer, so a recreate-on-reopen is cheap.
    private void Detail_ShowInfoRequested(object? sender, VideoItemViewModel item)
    {
        var tags = _viewModel.Detail.Tags.ToArray();

        // If a popup is already open for the same clip, just bring it to
        // the front rather than respawning identical content.
        if (_infoWindow is not null)
        {
            try
            {
                if (_infoWindow.IsLoaded
                    && string.Equals(_infoWindow.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    _infoWindow.Activate();
                    return;
                }
                _infoWindow.Close();
            }
            catch
            {
                // existing window is in a bad state; fall through and respawn.
            }
            _infoWindow = null;
        }

        var dialog = new VideoInfoWindow(item, tags, _sidecar)
        {
            Owner = this
        };
        dialog.Closed += (_, _) =>
        {
            if (ReferenceEquals(_infoWindow, dialog)) _infoWindow = null;
        };
        _infoWindow = dialog;
        dialog.Show();
    }

    private static string BuildWindowTitle()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version is null) return "Find That Shot";
        // Build is the third component; skip the trailing .0 that AssemblyVersion always adds.
        return $"Find That Shot \u2014 {version.Major}.{version.Minor}.{version.Build}";
    }

    private void FocusSearchMenu_Click(object sender, RoutedEventArgs e)
    {
        FocusSearchBox();
    }

    private void FocusSearch_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        FocusSearchBox();
        e.Handled = true;
    }

    private void FocusSearchBox()
    {
        if (SearchBox is null) return;
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    // Window-level shortcuts active only while review mode is open AND focus is
    // not inside a text input (so typing tag names / notes still works):
    //   Esc          close the player and return to the gallery
    //   Space        toggle play / pause
    //   Left / Right  jump to the previous / next clip and keep playing
    //   1-9, 0       toggle the matching pinned tag on the current clip
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_viewModel.Detail.IsPlayerVisible) return;

        // Esc closes the player regardless of which engine is active or where
        // focus is — it's the natural "get me out of here" key, and unlike the
        // play controls below it doesn't interfere with typing notes/tags.
        if (e.Key == Key.Escape)
        {
            if (_viewModel.Detail.ClosePlayerCommand.CanExecute(null))
            {
                _viewModel.Detail.ClosePlayerCommand.Execute(null);
            }
            e.Handled = true;
            return;
        }

        // Number keys 1-9 then 0 toggle the pinned tag bound to that slot on
        // the current clip. Plain digits only (a modifier means it's some other
        // shortcut), and never while a text input has focus so typing tag
        // names / notes / coordinates still inserts digits normally. Handled
        // independently of the player engine — tagging doesn't need playback.
        if (Keyboard.Modifiers == ModifierKeys.None && TryGetPinnedTagSlot(e.Key, out var slot))
        {
            var digitFocus = Keyboard.FocusedElement;
            if (digitFocus is TextBoxBase or PasswordBox or ComboBox) return;
            _ = _viewModel.Detail.ToggleTagBySlotAsync(slot);
            e.Handled = true;
            return;
        }

        if (!App.IsPlayerAvailable && !App.UseMpvPlayer) return;
        if (e.Key is not (Key.Space or Key.Left or Key.Right)) return;

        var focused = Keyboard.FocusedElement;
        if (focused is TextBoxBase or PasswordBox or ComboBox) return;

        if (e.Key == Key.Left)
        {
            if (_viewModel.PlayPreviousCommand.CanExecute(null))
            {
                _viewModel.PlayPreviousCommand.Execute(null);
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Right)
        {
            if (_viewModel.PlayNextCommand.CanExecute(null))
            {
                _viewModel.PlayNextCommand.Execute(null);
            }
            e.Handled = true;
            return;
        }

        if (App.UseMpvPlayer)
        {
            MpvTogglePlayPause();
            e.Handled = true;
            return;
        }

        ToggleFfmePlayPause();
        e.Handled = true;
    }

    // Maps a digit key to a pinned-tag slot index. The printed digit is the
    // visible hotkey; slots run 0-9 with "1" → slot 0 … "9" → slot 8 and "0"
    // → slot 9 (so the tenth pin lands on the 0 key at the end of the number
    // row). Both the top-row digits and the numpad are accepted. Returns false
    // (slot = -1) for any non-digit key.
    private static bool TryGetPinnedTagSlot(Key key, out int slot)
    {
        slot = key switch
        {
            Key.D1 or Key.NumPad1 => 0,
            Key.D2 or Key.NumPad2 => 1,
            Key.D3 or Key.NumPad3 => 2,
            Key.D4 or Key.NumPad4 => 3,
            Key.D5 or Key.NumPad5 => 4,
            Key.D6 or Key.NumPad6 => 5,
            Key.D7 or Key.NumPad7 => 6,
            Key.D8 or Key.NumPad8 => 7,
            Key.D9 or Key.NumPad9 => 8,
            Key.D0 or Key.NumPad0 => 9,
            _ => -1
        };
        return slot >= 0;
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        var url = e.Uri?.ToString();
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            e.Handled = true;
        }
        catch
        {
            // Browser launch can fail on locked-down systems; swallow silently.
        }
    }

    // Opt FFME into GPU-accelerated decoding before the stream is fully
    // opened. Without this, FFME defaults to pure software FFmpeg decode,
    // which can't keep up with high-bitrate 4K/60p sources (DJI drone clips
    // are the canonical offender): the FrameDecodingWorker falls behind the
    // audio clock so video drifts into slow motion, and the engine repeatedly
    // toggles MediaState between Play and Manual/Pause to refill its block
    // buffer — which our PropertyChanged → UpdatePlayPauseLabel pipeline
    // surfaces as the play/pause button label flickering between "Play"
    // and "Pause" several times per second.
    //
    // We hand FFME a priority-ordered list of hardware devices (D3D11VA and
    // DXVA2 cover essentially every modern Windows GPU; CUDA is included as
    // a fallthrough for systems where the NVIDIA driver exposes it cleanly
    // ahead of the generic DX paths) and let it try each one until one
    // succeeds. If none of them work, FFmpeg automatically falls back to
    // software decoding, so this is a strict improvement on the previous
    // software-only behaviour.
    //
    // Failures to enumerate / configure hardware devices are swallowed so
    // a quirky GPU driver never blocks playback — worst case we land back
    // on software decode, which is exactly what we had before.
    private void MediaElement_MediaOpening(object? sender, Unosquare.FFME.Common.MediaOpeningEventArgs e)
    {
        try
        {
            if (e.Options.VideoStream is not Unosquare.FFME.Common.StreamInfo videoStream)
                return;

            if (videoStream.HardwareDevices.Count == 0)
                return; // No GPU devices for this stream → FFmpeg uses software decode.

            var preferred = new[]
            {
                FFmpeg.AutoGen.AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
                FFmpeg.AutoGen.AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2,
                FFmpeg.AutoGen.AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA,
            };

            var devices = new List<Unosquare.FFME.Common.HardwareDeviceInfo>(preferred.Length);
            foreach (var type in preferred)
            {
                var device = videoStream.HardwareDevices.FirstOrDefault(d => d.DeviceType == type);
                if (device != null) devices.Add(device);
            }

            if (devices.Count > 0)
                e.Options.VideoHardwareDevices = devices.ToArray();

            // FFME-FALLBACK ONLY. This handler runs solely on the FFME path; the
            // primary engine is now mpv (GPU-rendered, full-res). FFME presents
            // every frame through a CPU-side WriteableBitmap (see
            // docs/in-app-player.md), so a 3840x2160@60p stream means ~33MB per
            // frame * 60 ≈ 2GB/s of color-convert + copy on the UI thread, which
            // drags 4K60 into slow motion even with working GPU decode. Capping
            // the rendered height to 1080 keeps the fallback usable; mpv (when
            // libmpv is present) renders these sources at full resolution and
            // never reaches this code. scale=-2 keeps aspect ratio at an even
            // width. Preview-quality tradeoff only — the source file is untouched.
            if (videoStream.PixelHeight > 1080)
                e.Options.VideoFilter = "scale=-2:1080";
        }
        catch
        {
            // Defensive: any failure here just means we keep FFME's default
            // software-only decode path. Never let a hardware-accel probe
            // throw into the dispatcher during media open.
        }
    }

    // FFME's MediaElement raises MediaOpened on the UI thread once the
    // first frame is decoded and NaturalDuration is final; this is the
    // FFME analogue of VLC's LengthChanged.
    private void MediaElement_MediaOpened(object? sender, EventArgs e)
    {
        UpdatePlayerTime();
        UpdatePlayPauseLabel();
    }

    private void MediaElement_MediaEnded(object? sender, EventArgs e)
    {
        // Playback reached the natural end — the clip is no longer playing, so
        // reset intent to flip the button back to "Play".
        _playerPlaying = false;
        UpdatePlayerTime();
        UpdatePlayPauseLabel();
    }

    // FFME surfaces playback state and position changes via standard
    // INotifyPropertyChanged on the MediaElement. We only react to Position
    // here, to drive the seek slider + current-time readout. The Play/Pause
    // label is NOT driven from here: FFME's MediaState toggles through the
    // transient `Manual` refill state several times a second during playback
    // (and also reports `Manual` while paused), so reacting to it both
    // flickered the label and got it stuck after a pause. The label is now
    // driven by the explicit `_playerPlaying` intent flag, updated at each
    // deliberate transition. Filtering by name keeps this off the hot path
    // for the dozens of other properties FFME notifies on.
    private void MediaElement_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Unosquare.FFME.MediaElement.Position):
                Dispatcher.BeginInvoke(UpdatePlayerTime);
                break;
        }
    }

    // FFME doesn't raise a MediaFailed event; failures surface as exceptions
    // on Open()/Play() (handled in Detail_PropertyChanged) and as MessageLogged
    // entries with elevated severity. Show a minimal inline error if anything
    // FFME considers an error gets logged.
    private void MediaElement_MessageLogged(object? sender, Unosquare.FFME.Common.MediaLogMessageEventArgs e)
    {
        if (e.MessageType != Unosquare.FFME.Common.MediaLogMessageType.Error) return;
        Dispatcher.BeginInvoke(() =>
        {
            PlayerCurrentTimeText.Text = "error";
            PlayerDurationText.Text = "--:--";
        });
    }

    private void UpdatePlayerTime()
    {
        if (!App.IsPlayerAvailable) return;
        var pos = VideoPlayer.Position;
        if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
        var dur = VideoPlayer.NaturalDuration ?? TimeSpan.Zero;
        PlayerCurrentTimeText.Text = Format(pos);
        PlayerDurationText.Text = Format(dur);
        SyncSliderFromPlayer();
        _viewModel.Detail.UpdateTelemetryForPosition(pos);

        static string Format(TimeSpan t)
            => t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
                : $"{t.Minutes:D2}:{t.Seconds:D2}";
    }

    private void SyncSliderFromPlayer()
    {
        if (!App.IsPlayerAvailable) return;
        if (_isUserSeeking) return;
        var duration = VideoPlayer.NaturalDuration ?? TimeSpan.Zero;
        if (duration <= TimeSpan.Zero) return;
        var fraction = Math.Clamp(VideoPlayer.Position.TotalSeconds / duration.TotalSeconds, 0.0, 1.0);
        var newValue = fraction * PlayerSeekSlider.Maximum;
        if (Math.Abs(PlayerSeekSlider.Value - newValue) > 0.5)
        {
            PlayerSeekSlider.Value = newValue;
        }
    }

    private void UpdatePlayPauseLabel()
    {
        if (App.UseMpvPlayer)
        {
            var player = MpvPlayer.Player;
            var mpvPlaying = player is not null && !player.GetPaused();
            PlayPauseButton.Content = mpvPlaying ? "Pause" : "Play";
            var mpvGlyphKey = mpvPlaying ? "Icon.Pause" : "Icon.Play";
            if (TryFindResource(mpvGlyphKey) is string mpvGlyph)
            {
                Helpers.Controls.Theme.SetIcon(PlayPauseButton, mpvGlyph);
            }
            return;
        }

        if (!App.IsPlayerAvailable) return;
        // Drive the label from our own intent flag, NOT from FFME's MediaState
        // or IsPlaying. During playback FFME drops into the transient `Manual`
        // state to refill its block buffer, and a paused clip also settles into
        // `Manual` — so reading either signal made the label both flicker
        // (during refills) and get stuck on "Pause" after a real pause. The
        // intent flag is set at every deliberate transition and is unambiguous.
        var playing = _playerPlaying;
        PlayPauseButton.Content = playing ? "Pause" : "Play";
        // Swap the Segoe Fluent glyph carried via the Theme.Icon attached
        // property so the button shows the *next* action visually instead
        // of the current state. Resource keys are defined in
        // Resources/Theme/Icons.xaml.
        var glyphKey = playing ? "Icon.Pause" : "Icon.Play";
        if (TryFindResource(glyphKey) is string glyph)
        {
            Helpers.Controls.Theme.SetIcon(PlayPauseButton, glyph);
        }
    }

    // Manual GPS picker: forward the picked coords from the embedded
    // Leaflet click handler into the VM. The control raises this on the
    // UI thread (CoreWebView2.WebMessageReceived dispatches via the WPF
    // synchronization context), so no marshalling is needed here.
    private void LocationMap_OnLocationPicked(object? sender, Helpers.Controls.LocationPickedEventArgs e)
    {
        _viewModel.Detail.ApplyPickedLocation(e.Latitude, e.Longitude);
    }
}
