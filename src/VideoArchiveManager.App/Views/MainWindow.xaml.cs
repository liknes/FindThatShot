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

        if (App.IsPlayerAvailable)
        {
            // FFME's MediaElement is its own player — no separate MediaPlayer
            // object to wire up. Subscribe to MediaElement events directly.
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
            _viewModel.Detail.ShowInfoRequested -= Detail_ShowInfoRequested;

            if (_infoWindow is not null)
            {
                try { _infoWindow.Close(); }
                catch { /* already closing */ }
                _infoWindow = null;
            }

            if (App.IsPlayerAvailable)
            {
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

    private async void Detail_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not VideoDetailViewModel detail) return;

        if (e.PropertyName == nameof(VideoDetailViewModel.IsPlayerVisible))
        {
            ApplyReviewModeLayout(detail.IsPlayerVisible);
        }

        if (!App.IsPlayerAvailable) return;

        if (e.PropertyName is not (nameof(VideoDetailViewModel.MediaSource) or nameof(VideoDetailViewModel.IsPlayerVisible)))
        {
            return;
        }

        try
        {
            if (detail.IsPlayerVisible && detail.MediaSource is not null)
            {
                // Open() handles tear-down of any previously-loaded media
                // internally, so we don't need a manual Close() between
                // clips. After Open returns, FFME has decoded the first
                // frame and NaturalDuration is final, so MediaOpened has
                // already fired by the time await returns.
                await VideoPlayer.Open(detail.MediaSource);
                await VideoPlayer.Play();
            }
            else
            {
                await VideoPlayer.Close();
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
    }

    // Swap the main row's column widths so the sidebar + list collapse and the
    // player takes the central area when review mode opens. Restores the cached
    // original widths when leaving. Cheap (just sets four GridLength values).
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

    private async void PlayerPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (!App.IsPlayerAvailable) return;
        try
        {
            if (VideoPlayer.IsPlaying)
            {
                await VideoPlayer.Pause();
            }
            else
            {
                await VideoPlayer.Play();
            }
        }
        catch
        {
            // FFME state machine can be in a transient state during media
            // open/close; never let a play/pause click throw into the
            // dispatcher.
        }
    }

    // Pause + seek-to-0 instead of Stop() so the first frame stays on screen.
    // FFME's Stop() closes the underlying decoder, which would tear down
    // the WriteableBitmap and leave a momentary black gap until reopen —
    // not strictly a regression vs the previous VLC behaviour, but the
    // "freeze at first frame" UX is cleaner.
    private async void PlayerStop_Click(object sender, RoutedEventArgs e)
    {
        if (!App.IsPlayerAvailable) return;

        try
        {
            if (VideoPlayer.IsPlaying)
            {
                await VideoPlayer.Pause();
            }
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

    // Window-level shortcut: Space toggles play/pause when review mode is open
    // AND focus is not inside a text input (so typing tag names / notes still works).
    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_viewModel.Detail.IsPlayerVisible) return;
        if (!App.IsPlayerAvailable) return;
        if (e.Key != Key.Space) return;

        var focused = Keyboard.FocusedElement;
        if (focused is TextBoxBase or PasswordBox or ComboBox) return;

        try
        {
            if (VideoPlayer.IsPlaying)
            {
                await VideoPlayer.Pause();
            }
            else
            {
                await VideoPlayer.Play();
            }
        }
        catch
        {
            // see PlayerPlayPause_Click rationale.
        }
        e.Handled = true;
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
        UpdatePlayerTime();
        UpdatePlayPauseLabel();
    }

    // FFME surfaces playback state and position changes via standard
    // INotifyPropertyChanged on the MediaElement. We only act on the
    // properties we actually render in the toolbar — Position drives the
    // seek slider + current-time readout, MediaState drives the Play/Pause
    // button label. Filtering by name keeps this off the hot path for the
    // dozens of other properties FFME notifies on.
    private void MediaElement_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Unosquare.FFME.MediaElement.Position):
                Dispatcher.BeginInvoke(UpdatePlayerTime);
                break;
            case nameof(Unosquare.FFME.MediaElement.MediaState):
            case nameof(Unosquare.FFME.MediaElement.IsPlaying):
            case nameof(Unosquare.FFME.MediaElement.IsPaused):
                Dispatcher.BeginInvoke(UpdatePlayPauseLabel);
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
        if (!App.IsPlayerAvailable) return;
        var playing = VideoPlayer.IsPlaying;
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
}
