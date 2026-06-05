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
using System.Collections.Generic;
using System.IO;
using System.Text;
using VideoArchiveManager.Core.Configuration;

namespace VideoArchiveManager.App.Services.Diagnostics;

// Default IDiagnosticsLog: a fixed-capacity in-memory ring buffer plus a
// best-effort append to a per-session rolling file on disk.
//
// Thread-safety: log entries arrive on arbitrary worker threads, so all
// buffer mutations take a lock and file writes are serialised through the
// same lock. The buffer is intentionally small (a few thousand lines) — it
// is a recent-activity view for troubleshooting, not an archive; the file
// on disk is the durable record.
public sealed class DiagnosticsLog : IDiagnosticsLog
{
    private const int Capacity = 5000;

    private readonly object _gate = new();
    private readonly Queue<LogEntry> _buffer = new(Capacity);
    private readonly StreamWriter? _writer;

    public string? LogFilePath { get; }
    public string LogDirectory { get; }

    public event Action<LogEntry>? EntryAdded;

    public DiagnosticsLog()
    {
        LogDirectory = AppSettings.DefaultLogDirectory;

        // File logging is best-effort: a locked-down or read-only profile
        // must not stop the app from running, so any failure here just
        // leaves LogFilePath null and the buffer working on its own.
        try
        {
            Directory.CreateDirectory(LogDirectory);
            LogFilePath = Path.Combine(LogDirectory, $"findthatshot-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            _writer = new StreamWriter(
                new FileStream(LogFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite),
                Encoding.UTF8)
            {
                AutoFlush = true
            };

            PruneOldLogs();
        }
        catch
        {
            _writer = null;
            LogFilePath = null;
        }
    }

    public void Add(LogEntry entry)
    {
        lock (_gate)
        {
            if (_buffer.Count >= Capacity) _buffer.Dequeue();
            _buffer.Enqueue(entry);

            try
            {
                _writer?.WriteLine(entry.ToLine());
            }
            catch
            {
                // A transient disk error must never crash the logging path.
            }
        }

        EntryAdded?.Invoke(entry);
    }

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate)
        {
            return _buffer.ToArray();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _buffer.Clear();
        }
    }

    // Keep only the most recent handful of session logs so the folder
    // doesn't grow without bound across launches.
    private void PruneOldLogs()
    {
        try
        {
            const int keep = 10;
            var files = new DirectoryInfo(LogDirectory)
                .GetFiles("findthatshot-*.log");
            if (files.Length <= keep) return;

            foreach (var stale in files
                         .OrderByDescending(f => f.LastWriteTimeUtc)
                         .Skip(keep))
            {
                try { stale.Delete(); }
                catch { /* ignore individual failures */ }
            }
        }
        catch
        {
            // Pruning is purely housekeeping; never let it surface.
        }
    }
}
