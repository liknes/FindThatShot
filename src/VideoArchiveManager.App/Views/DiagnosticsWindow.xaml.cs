using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using VideoArchiveManager.App.Services.Diagnostics;
using VideoArchiveManager.Core.Configuration;
using VideoArchiveManager.Core.Services;

namespace VideoArchiveManager.App.Views;

// Self-support diagnostics panel: a snapshot of the environment (versions,
// paths, player/ffmpeg availability) over a live tail of the app's log,
// with copy / open-file / clear actions so a user can troubleshoot or
// produce a paste-able bug report without leaving the app.
public partial class DiagnosticsWindow : Window
{
    private readonly IDiagnosticsLog _log;
    private readonly ISettingsStore _settings;
    private readonly IFfprobeService _ffprobe;

    // Master list of everything we know about (snapshot + live appends);
    // the ListBox shows whatever currently passes the level + text filter.
    private readonly List<LogEntry> _all = new();
    private readonly System.Collections.ObjectModel.ObservableCollection<LogEntry> _shown = new();

    private LogLevel _minLevel = LogLevel.Information;
    private string _searchText = string.Empty;
    private bool _loaded;

    public DiagnosticsWindow(IDiagnosticsLog log, ISettingsStore settings, IFfprobeService ffprobe)
    {
        InitializeComponent();
        _log = log;
        _settings = settings;
        _ffprobe = ffprobe;

        LogList.ItemsSource = _shown;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _loaded = true;
        BuildEnvironment();

        _all.Clear();
        _all.AddRange(_log.Snapshot());
        ReapplyFilter();

        // Live tail. The sink can raise on a worker thread, so marshal.
        _log.EntryAdded += OnEntryAdded;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _log.EntryAdded -= OnEntryAdded;
    }

    private void OnEntryAdded(LogEntry entry)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _all.Add(entry);
            if (Passes(entry))
            {
                _shown.Add(entry);
                UpdateCount();
                if (AutoScrollCheck.IsChecked == true && _shown.Count > 0)
                {
                    LogList.ScrollIntoView(_shown[^1]);
                }
            }
        });
    }

    // --- Filtering -----------------------------------------------------

    private bool Passes(LogEntry e)
    {
        if (e.Level < _minLevel) return false;
        if (_searchText.Length == 0) return true;
        return e.Message.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
            || e.ShortCategory.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
            || (e.Exception?.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void ReapplyFilter()
    {
        _shown.Clear();
        foreach (var e in _all)
        {
            if (Passes(e)) _shown.Add(e);
        }
        UpdateCount();
        if (AutoScrollCheck.IsChecked == true && _shown.Count > 0)
        {
            LogList.ScrollIntoView(_shown[^1]);
        }
    }

    private void UpdateCount()
    {
        LogCountText.Text = _shown.Count == _all.Count
            ? $"{_all.Count} entries"
            : $"{_shown.Count} of {_all.Count} entries";
    }

    private void LevelFilter_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        _minLevel = LevelFilter.SelectedIndex switch
        {
            0 => LogLevel.Trace,
            1 => LogLevel.Information,
            2 => LogLevel.Warning,
            3 => LogLevel.Error,
            _ => LogLevel.Information
        };
        ReapplyFilter();
    }

    private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        _searchText = SearchBox.Text?.Trim() ?? string.Empty;
        ReapplyFilter();
    }

    // --- Environment summary -------------------------------------------

    private void BuildEnvironment()
    {
        var s = _settings.Current;

        AddEnvRow(EnvLeftColumn, "App version", AppVersion());
        AddEnvRow(EnvLeftColumn, "Runtime", RuntimeInformation.FrameworkDescription);
        AddEnvRow(EnvLeftColumn, "OS", RuntimeInformation.OSDescription);
        AddEnvRow(EnvLeftColumn, "Architecture", RuntimeInformation.OSArchitecture.ToString());
        AddEnvRow(EnvLeftColumn, "Player engine", App.UseMpvPlayer ? "mpv (GPU)" : "FFME (CPU)");
        AddEnvRow(EnvLeftColumn, "Player available", App.IsPlayerAvailable ? "Yes" : $"No — {App.PlayerInitError ?? "unavailable"}");
        AddEnvRow(EnvLeftColumn, "ffprobe", _ffprobe.IsAvailable() ? "Found" : "Not found");

        AddEnvRow(EnvRightColumn, "Database", s.EffectiveDatabasePath);
        AddEnvRow(EnvRightColumn, "Thumbnails", s.EffectiveThumbnailDirectory);
        AddEnvRow(EnvRightColumn, "Backups", s.EffectiveBackupDirectory);
        AddEnvRow(EnvRightColumn, "Settings", AppSettings.UserSettingsPath);
        AddEnvRow(EnvRightColumn, "Logs", _log.LogFilePath ?? _log.LogDirectory);
    }

    private static void AddEnvRow(Panel host, string label, string value)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var key = new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["App.Foreground.Tertiary"]
        };
        Grid.SetColumn(key, 0);

        var val = new TextBlock
        {
            Text = value,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["App.Foreground.Primary"],
            ToolTip = value
        };
        Grid.SetColumn(val, 1);

        row.Children.Add(key);
        row.Children.Add(val);
        host.Children.Add(row);
    }

    private static string AppVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "unknown" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    private string BuildEnvironmentText()
    {
        var s = _settings.Current;
        var sb = new StringBuilder();
        sb.AppendLine("=== Find That Shot — diagnostics ===");
        sb.AppendLine($"App version:      {AppVersion()}");
        sb.AppendLine($"Runtime:          {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"OS:               {RuntimeInformation.OSDescription}");
        sb.AppendLine($"Architecture:     {RuntimeInformation.OSArchitecture}");
        sb.AppendLine($"Player engine:    {(App.UseMpvPlayer ? "mpv (GPU)" : "FFME (CPU)")}");
        sb.AppendLine($"Player available: {(App.IsPlayerAvailable ? "Yes" : $"No — {App.PlayerInitError ?? "unavailable"}")}");
        sb.AppendLine($"ffprobe:          {(_ffprobe.IsAvailable() ? "Found" : "Not found")}");
        sb.AppendLine($"Database:         {s.EffectiveDatabasePath}");
        sb.AppendLine($"Thumbnails:       {s.EffectiveThumbnailDirectory}");
        sb.AppendLine($"Backups:          {s.EffectiveBackupDirectory}");
        sb.AppendLine($"Settings:         {AppSettings.UserSettingsPath}");
        sb.AppendLine($"Log file:         {_log.LogFilePath ?? "(in-memory only)"}");
        return sb.ToString();
    }

    private string BuildVisibleLogText()
    {
        var sb = new StringBuilder();
        foreach (var e in _shown) sb.AppendLine(e.ToLine());
        return sb.ToString();
    }

    // --- Actions -------------------------------------------------------

    private void CopyLog_Click(object sender, RoutedEventArgs e) => TrySetClipboard(BuildVisibleLogText());

    private void CopyReport_Click(object sender, RoutedEventArgs e) =>
        TrySetClipboard(BuildEnvironmentText() + Environment.NewLine + "=== Recent log ===" + Environment.NewLine + BuildVisibleLogText());

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        _log.Clear();
        _all.Clear();
        _shown.Clear();
        UpdateCount();
    }

    private void OpenLogFile_Click(object sender, RoutedEventArgs e)
    {
        var path = _log.LogFilePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            MessageBox.Show(this, "No log file is available for this session.", "Diagnostics",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        TryStart(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = _log.LogDirectory;
        try
        {
            Directory.CreateDirectory(dir);
            // Select the current log file in Explorer when we have one.
            if (!string.IsNullOrEmpty(_log.LogFilePath) && File.Exists(_log.LogFilePath))
            {
                TryStart(new ProcessStartInfo("explorer.exe", $"/select,\"{_log.LogFilePath}\""));
            }
            else
            {
                TryStart(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            }
        }
        catch
        {
            // best-effort
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void TrySetClipboard(string text)
    {
        try
        {
            Clipboard.SetText(string.IsNullOrEmpty(text) ? " " : text);
        }
        catch
        {
            // Clipboard can be transiently locked by another process; ignore.
        }
    }

    private static void TryStart(ProcessStartInfo psi)
    {
        try { Process.Start(psi); }
        catch { /* ignore launch failures on locked-down systems */ }
    }
}
