namespace VideoArchiveManager.App.Services.Diagnostics;

// In-process diagnostics buffer. A bounded ring of the most recent log
// entries that the Diagnostics window reads on open and subscribes to for
// live updates, plus the path of the rolling on-disk log file so users can
// attach it to a bug report. Registered as a singleton and fed by
// DiagnosticsLoggerProvider, which bridges Microsoft.Extensions.Logging
// into here.
public interface IDiagnosticsLog
{
    // Absolute path of the current session's log file (may be null if file
    // logging could not be initialised — the in-memory buffer still works).
    string? LogFilePath { get; }

    // Folder the log files live in.
    string LogDirectory { get; }

    // Raised whenever a new entry is appended. Handlers may run on a worker
    // thread; the UI marshals to the dispatcher itself.
    event Action<LogEntry>? EntryAdded;

    // Records a new entry (ring-buffered + written to disk).
    void Add(LogEntry entry);

    // A point-in-time copy of the buffered entries, oldest first.
    IReadOnlyList<LogEntry> Snapshot();

    // Empties the in-memory buffer (does not touch the on-disk log).
    void Clear();
}
