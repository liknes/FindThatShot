using Microsoft.Extensions.Logging;

namespace VideoArchiveManager.App.Services.Diagnostics;

// A single captured log line. Immutable so it can be handed to the UI
// thread without further synchronisation once created on whatever worker
// thread emitted the log.
public sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message,
    string? Exception)
{
    // Short, fixed-width level token used in the file log and the panel's
    // copy-to-clipboard output (e.g. "INFO", "WARN", "ERR ").
    public string LevelTag => Level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "CRIT",
        _ => "LOG"
    };

    // Just the trailing type name of the category (e.g. "VideoScannerService"
    // out of "VideoArchiveManager.Data.Services.VideoScannerService") so the
    // panel column stays readable.
    public string ShortCategory
    {
        get
        {
            if (string.IsNullOrEmpty(Category)) return string.Empty;
            var lastDot = Category.LastIndexOf('.');
            return lastDot >= 0 && lastDot < Category.Length - 1
                ? Category[(lastDot + 1)..]
                : Category;
        }
    }

    // One-line rendering used by the file writer and the "Copy" action.
    public string ToLine()
    {
        var line = $"{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{LevelTag,-5}] {ShortCategory}: {Message}";
        return Exception is null ? line : line + Environment.NewLine + Exception;
    }
}
