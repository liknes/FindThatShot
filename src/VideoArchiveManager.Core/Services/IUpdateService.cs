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
namespace VideoArchiveManager.Core.Services;

// SAFETY CONTRACT:
//   * This service is the ONLY component allowed to swap the app's own
//     binaries on disk. It does so via Velopack's well-defined apply step
//     which runs after the app process exits (the app restarts into the
//     new version). It must NEVER touch the user's video files, the
//     catalog database, settings, or thumbnail cache.
public interface IUpdateService
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);

    Task<UpdateApplyResult> DownloadAndApplyAsync(
        Action<int>? onProgress = null,
        CancellationToken cancellationToken = default);

    // Convenience for the UI to show the currently-running version even
    // when no update check has happened yet.
    string? CurrentVersion { get; }

    // When true, the app is running from an installer / packaged build
    // and updates can actually be applied. When false (e.g. dotnet run,
    // direct exe from publish folder), checks may still work but apply
    // will be refused with a friendly message.
    bool IsInstalled { get; }
}

public class UpdateCheckResult
{
    public bool Success { get; init; }

    // True iff the remote source advertises a different version that
    // CheckForUpdatesAsync considers an update from the current build.
    public bool UpdateAvailable { get; init; }

    public string? CurrentVersion { get; init; }

    public string? AvailableVersion { get; init; }

    // True when the running app isn't an installed build (e.g. dotnet run
    // from the dev box). The UI uses this to show "checks only work for
    // installed builds" instead of pretending an update is being applied.
    public bool NotInstalledMode { get; init; }

    public string? ErrorMessage { get; init; }
}

public class UpdateApplyResult
{
    public bool Success { get; init; }

    public string? ErrorMessage { get; init; }
}
