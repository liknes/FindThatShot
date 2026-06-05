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
