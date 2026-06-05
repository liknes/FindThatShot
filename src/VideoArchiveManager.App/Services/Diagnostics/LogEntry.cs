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
