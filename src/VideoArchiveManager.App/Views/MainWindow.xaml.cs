using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using LibVLCSharp.Shared;
using VideoArchiveManager.App.ViewModels;

namespace VideoArchiveManager.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly LibVLC? _libVlc;
    private MediaPlayer? _mediaPlayer;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        if (App.IsPlayerAvailable)
        {
            try
            {
                _libVlc = App.GetService<LibVLC>();
                _mediaPlayer = new MediaPlayer(_libVlc);
                _mediaPlayer.TimeChanged += MediaPlayer_TimeChanged;
                _mediaPlayer.LengthChanged += MediaPlayer_LengthChanged;
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
                PlayerTimeText.Text = "--:-- / --:--";
            }
        }
        catch (Exception ex)
        {
            PlayerTimeText.Text = $"Player error: {ex.Message}";
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

    private void PlayerPause_Click(object sender, RoutedEventArgs e)
    {
        _mediaPlayer?.Pause();
    }

    private void PlayerResume_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaPlayer is null) return;
        if (!_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Play();
        }
    }

    private void PlayerStop_Click(object sender, RoutedEventArgs e)
    {
        _mediaPlayer?.Stop();
    }

    private void MediaPlayer_TimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(UpdatePlayerTime);
    }

    private void MediaPlayer_LengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(UpdatePlayerTime);
    }

    private void MediaPlayer_EndReached(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(UpdatePlayerTime);
    }

    private void MediaPlayer_EncounteredError(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            PlayerTimeText.Text = "Playback error";
        });
    }

    private void UpdatePlayerTime()
    {
        if (_mediaPlayer is null) return;
        var pos = TimeSpan.FromMilliseconds(Math.Max(0, _mediaPlayer.Time));
        var dur = TimeSpan.FromMilliseconds(Math.Max(0, _mediaPlayer.Length));
        PlayerTimeText.Text = $"{Format(pos)} / {Format(dur)}";

        static string Format(TimeSpan t)
            => t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
                : $"{t.Minutes:D2}:{t.Seconds:D2}";
    }
}
