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
using Velopack;
using Velopack.Sources;
using VideoArchiveManager.Core.Configuration;
using VideoArchiveManager.Core.Services;

namespace VideoArchiveManager.App.Services;

// SAFETY CONTRACT:
//   * Delegates all binary-swap work to Velopack's UpdateManager, which
//     only touches files under its own packaged install root. Never reads
//     or writes user video files, the catalog DB, settings, or thumbnails.
//   * Refuses to attempt apply when not running from a packaged install
//     (e.g. `dotnet run`) — surfaces NotInstalledMode in the result so the
//     caller can show a friendly "test from installer" message instead.
public class VelopackUpdateService : IUpdateService
{
    private readonly ISettingsStore _settings;
    private readonly ILogger<VelopackUpdateService> _logger;

    // The UpdateInfo returned by CheckAsync is stashed here so the
    // subsequent DownloadAndApplyAsync call doesn't have to make another
    // HTTPS round-trip to GitHub. Cleared on apply / failure.
    private UpdateInfo? _pendingInfo;

    public VelopackUpdateService(ISettingsStore settings, ILogger<VelopackUpdateService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public string? CurrentVersion
    {
        get
        {
            try
            {
                var mgr = TryCreateManager();
                return mgr?.CurrentVersion?.ToString();
            }
            catch
            {
                return null;
            }
        }
    }

    public bool IsInstalled
    {
        get
        {
            try
            {
                var mgr = TryCreateManager();
                return mgr?.IsInstalled ?? false;
            }
            catch
            {
                return false;
            }
        }
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var repoUrl = _settings.Current.UpdateRepoUrl;
        if (string.IsNullOrWhiteSpace(repoUrl))
        {
            return new UpdateCheckResult
            {
                Success = false,
                ErrorMessage = "No update repository URL is configured (AppSettings.UpdateRepoUrl)."
            };
        }

        UpdateManager? mgr;
        try
        {
            mgr = TryCreateManager();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create UpdateManager");
            return new UpdateCheckResult
            {
                Success = false,
                ErrorMessage = $"Could not initialise the updater: {ex.Message}"
            };
        }

        if (mgr is null)
        {
            return new UpdateCheckResult
            {
                Success = false,
                ErrorMessage = "Could not initialise the updater."
            };
        }

        var current = mgr.CurrentVersion?.ToString();

        if (!mgr.IsInstalled)
        {
            // Running from `dotnet run` or a raw publish folder — we can't
            // apply updates here even if one is found. Surface this clearly
            // rather than silently saying "no updates".
            return new UpdateCheckResult
            {
                Success = true,
                UpdateAvailable = false,
                NotInstalledMode = true,
                CurrentVersion = current
            };
        }

        try
        {
            var info = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            _pendingInfo = info;

            return new UpdateCheckResult
            {
                Success = true,
                UpdateAvailable = info != null,
                CurrentVersion = current,
                AvailableVersion = info?.TargetFullRelease?.Version?.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CheckForUpdatesAsync failed");
            _pendingInfo = null;
            return new UpdateCheckResult
            {
                Success = false,
                CurrentVersion = current,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<UpdateApplyResult> DownloadAndApplyAsync(
        Action<int>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var info = _pendingInfo;
        if (info is null)
        {
            return new UpdateApplyResult
            {
                Success = false,
                ErrorMessage = "No pending update. Run Check for updates first."
            };
        }

        UpdateManager? mgr;
        try
        {
            mgr = TryCreateManager();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to re-create UpdateManager for apply");
            return new UpdateApplyResult
            {
                Success = false,
                ErrorMessage = $"Could not initialise the updater: {ex.Message}"
            };
        }

        if (mgr is null || !mgr.IsInstalled)
        {
            _pendingInfo = null;
            return new UpdateApplyResult
            {
                Success = false,
                ErrorMessage = "Updates can only be applied to an installed build. Install with the Setup.exe and try again."
            };
        }

        try
        {
            await mgr.DownloadUpdatesAsync(info, onProgress, cancellationToken).ConfigureAwait(false);

            // This exits the current process and hands control to Velopack's
            // updater. The new version launches automatically once the swap
            // completes. Anything after this line will not run.
            mgr.ApplyUpdatesAndRestart(info);

            return new UpdateApplyResult { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update download/apply failed");
            _pendingInfo = null;
            return new UpdateApplyResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private UpdateManager? TryCreateManager()
    {
        var repoUrl = _settings.Current.UpdateRepoUrl;
        if (string.IsNullOrWhiteSpace(repoUrl)) return null;

        // accessToken is null because we're targeting a PUBLIC repo; if the
        // user later moves to a private repo they would need to plumb a
        // personal access token in. prerelease=false means only published
        // (non-prerelease) GitHub releases are considered.
        var source = new GithubSource(repoUrl, accessToken: null, prerelease: false);
        return new UpdateManager(source);
    }
}
