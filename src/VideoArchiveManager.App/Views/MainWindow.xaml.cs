using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Navigation;
using LibVLCSharp.Shared;
using VideoArchiveManager.App.ViewModels;
using VideoArchiveManager.Core.Models;

namespace VideoArchiveManager.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly LibVLC? _libVlc;
    private MediaPlayer? _mediaPlayer;

    // Set while the user is dragging the seek slider so the periodic
    // MediaPlayer.PositionChanged updates don't fight the user's input.
    private bool _isUserSeeking;

    // Cached so we can restore the original 3-column layout when leaving review mode.
    private GridLength _normalLeftWidth;
    private GridLength _normalListWidth;
    private GridLength _normalEditorWidth;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Title = BuildWindowTitle();

        // Remember the normal-mode column widths so we can restore them after
        // the user closes the in-app player.
        _normalLeftWidth = LeftSidebarColumn.Width;
        _normalListWidth = VideoListColumn.Width;
        _normalEditorWidth = EditorColumn.Width;

        if (App.IsPlayerAvailable)
        {
            try
            {
                _libVlc = App.GetService<LibVLC>();
                _mediaPlayer = new MediaPlayer(_libVlc);
                _mediaPlayer.TimeChanged += MediaPlayer_TimeChanged;
                _mediaPlayer.LengthChanged += MediaPlayer_LengthChanged;
                _mediaPlayer.PositionChanged += MediaPlayer_PositionChanged;
                _mediaPlayer.Playing += MediaPlayer_PlayStateChanged;
                _mediaPlayer.Paused += MediaPlayer_PlayStateChanged;
                _mediaPlayer.Stopped += MediaPlayer_PlayStateChanged;
                _mediaPlayer.EndReached += MediaPlayer_EndReached;
                _mediaPlayer.EncounteredError += MediaPlayer_EncounteredError;
                VideoPlayer.MediaPlayer = _mediaPlayer;
            }
            catch
            {
                _mediaPlayer = null;
            }
        }

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        _viewModel.Detail.PropertyChanged += Detail_PropertyChanged;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        try
        {
            if (_mediaPlayer is not null)
            {
                _mediaPlayer.TimeChanged -= MediaPlayer_TimeChanged;
                _mediaPlayer.LengthChanged -= MediaPlayer_LengthChanged;
                _mediaPlayer.PositionChanged -= MediaPlayer_PositionChanged;
                _mediaPlayer.Playing -= MediaPlayer_PlayStateChanged;
                _mediaPlayer.Paused -= MediaPlayer_PlayStateChanged;
                _mediaPlayer.Stopped -= MediaPlayer_PlayStateChanged;
                _mediaPlayer.EndReached -= MediaPlayer_EndReached;
                _mediaPlayer.EncounteredError -= MediaPlayer_EncounteredError;
                if (_mediaPlayer.IsPlaying) _mediaPlayer.Stop();
                var oldMedia = _mediaPlayer.Media;
                _mediaPlayer.Media = null;
                oldMedia?.Dispose();
                _mediaPlayer.Dispose();
                _mediaPlayer = null;
            }
        }
        catch
        {
            // window is going away
        }
    }

    private void Detail_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not VideoDetailViewModel detail) return;

        if (e.PropertyName == nameof(VideoDetailViewModel.IsPlayerVisible))
        {
            ApplyReviewModeLayout(detail.IsPlayerVisible);
        }

        if (_mediaPlayer is null) return;

        if (e.PropertyName is not (nameof(VideoDetailViewModel.MediaSource) or nameof(VideoDetailViewModel.IsPlayerVisible)))
        {
            return;
        }

        try
        {
            if (detail.IsPlayerVisible && detail.MediaSource is not null && _libVlc is not null)
            {
                var oldMedia = _mediaPlayer.Media;
                var newMedia = new Media(_libVlc, detail.MediaSource);
                _mediaPlayer.Media = newMedia;
                _mediaPlayer.Play();
                oldMedia?.Dispose();
            }
            else
            {
                if (_mediaPlayer.IsPlaying) _mediaPlayer.Stop();
                var oldMedia = _mediaPlayer.Media;
                _mediaPlayer.Media = null;
                oldMedia?.Dispose();
                PlayerCurrentTimeText.Text = "00:00";
                PlayerDurationText.Text = "00:00";
                PlayerSeekSlider.Value = 0;
                PlayPauseButton.Content = "Play";
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
            LeftSidebarColumn.Width = new GridLength(0);
            LeftSidebarColumn.MinWidth = 0;
            VideoListColumn.Width = new GridLength(0);
            PlayerColumn.Width = new GridLength(1, GridUnitType.Star);
            EditorColumn.Width = new GridLength(380);
            EditorColumn.MinWidth = 340;
        }
        else
        {
            LeftSidebarColumn.Width = _normalLeftWidth;
            LeftSidebarColumn.MinWidth = 200;
            VideoListColumn.Width = _normalListWidth;
            PlayerColumn.Width = new GridLength(0);
            EditorColumn.Width = _normalEditorWidth;
            EditorColumn.MinWidth = 420;
        }
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
    // We capture the clicked tag from e.AddedItems, clear the selection
    // BEFORE the rebuild, and refuse to re-enter while the click is being
    // processed.
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
        try
        {
            // Clear the selection BEFORE the chip-add triggers a rebuild,
            // so any SelectionChanged echoes during the rebuild see a null
            // SelectedItem and exit early via the guard above.
            lb.SelectedItem = null;
            _viewModel.AddTagFilterCommand.Execute(tag);
        }
        finally
        {
            _isHandlingTagFilterSelection = false;
        }
    }

    private void PlayerPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaPlayer is null) return;
        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Pause();
        }
        else
        {
            _mediaPlayer.Play();
        }
    }

    // We deliberately do NOT call MediaPlayer.Stop() here. Stop() releases
    // the VLC decoder, after which the underlying HwndHost surface is
    // repainted by Windows using the window class's default WhiteBrush —
    // a hard white flash against the cinematic black backdrop. Pausing and
    // seeking to 0 gives the same "stopped at the beginning" user model
    // while keeping the first frame on screen.
    private void PlayerStop_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaPlayer is null) return;

        try
        {
            if (_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.Pause();
            }
            if (_mediaPlayer.IsSeekable)
            {
                _mediaPlayer.Time = 0;
            }
        }
        catch
        {
            // VLC's state machine can be fussy mid-transition; never let
            // this button throw into the dispatcher.
        }
    }

    private void PlayerSkipBack_Click(object sender, RoutedEventArgs e)
    {
        SeekRelative(TimeSpan.FromSeconds(-5));
    }

    private void PlayerSkipForward_Click(object sender, RoutedEventArgs e)
    {
        SeekRelative(TimeSpan.FromSeconds(5));
    }

    private void SeekRelative(TimeSpan delta)
    {
        if (_mediaPlayer is null) return;
        if (!_mediaPlayer.IsSeekable) return;
        var target = Math.Max(0, _mediaPlayer.Time + (long)delta.TotalMilliseconds);
        if (_mediaPlayer.Length > 0 && target >= _mediaPlayer.Length)
        {
            target = Math.Max(0, _mediaPlayer.Length - 100);
        }
        _mediaPlayer.Time = target;
    }

    private void PlayerSeekSlider_DragStarted(object sender, DragStartedEventArgs e)
    {
        _isUserSeeking = true;
    }

    private void PlayerSeekSlider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        ApplySliderPosition();
        _isUserSeeking = false;
    }

    private void PlayerSeekSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Fires on a click directly on the track (no thumb drag). Apply once.
        ApplySliderPosition();
    }

    private void ApplySliderPosition()
    {
        if (_mediaPlayer is null) return;
        if (!_mediaPlayer.IsSeekable) return;
        var position = (float)Math.Clamp(PlayerSeekSlider.Value / PlayerSeekSlider.Maximum, 0.0, 1.0);
        _mediaPlayer.Position = position;
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

    private static string BuildWindowTitle()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version is null) return "Video Archive Manager";
        // Build is the third component; skip the trailing .0 that AssemblyVersion always adds.
        return $"Video Archive Manager \u2014 {version.Major}.{version.Minor}.{version.Build}";
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
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_viewModel.Detail.IsPlayerVisible) return;
        if (_mediaPlayer is null) return;
        if (e.Key != Key.Space) return;

        var focused = Keyboard.FocusedElement;
        if (focused is TextBoxBase or PasswordBox or ComboBox) return;

        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Pause();
        }
        else
        {
            _mediaPlayer.Play();
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

    private void MediaPlayer_TimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(UpdatePlayerTime);
    }

    private void MediaPlayer_LengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(UpdatePlayerTime);
    }

    private void MediaPlayer_PositionChanged(object? sender, MediaPlayerPositionChangedEventArgs e)
    {
        // VLC raises this several times a second; cheap UI update only.
        Dispatcher.BeginInvoke(SyncSliderFromPlayer);
    }

    private void MediaPlayer_PlayStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(UpdatePlayPauseLabel);
    }

    private void MediaPlayer_EndReached(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            UpdatePlayerTime();
            UpdatePlayPauseLabel();
        });
    }

    private void MediaPlayer_EncounteredError(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            PlayerCurrentTimeText.Text = "error";
            PlayerDurationText.Text = "--:--";
        });
    }

    private void UpdatePlayerTime()
    {
        if (_mediaPlayer is null) return;
        var pos = TimeSpan.FromMilliseconds(Math.Max(0, _mediaPlayer.Time));
        var dur = TimeSpan.FromMilliseconds(Math.Max(0, _mediaPlayer.Length));
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
        if (_mediaPlayer is null) return;
        if (_isUserSeeking) return;
        var position = Math.Clamp(_mediaPlayer.Position, 0.0f, 1.0f);
        var newValue = position * PlayerSeekSlider.Maximum;
        // Avoid feedback loops with bound ValueChanged events.
        if (Math.Abs(PlayerSeekSlider.Value - newValue) > 0.5)
        {
            PlayerSeekSlider.Value = newValue;
        }
    }

    private void UpdatePlayPauseLabel()
    {
        if (_mediaPlayer is null) return;
        PlayPauseButton.Content = _mediaPlayer.IsPlaying ? "Pause" : "Play";
    }
}
