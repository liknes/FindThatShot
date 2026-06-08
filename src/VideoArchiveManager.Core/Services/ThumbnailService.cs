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
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using VideoArchiveManager.Core.Configuration;

namespace VideoArchiveManager.Core.Services;

public class ThumbnailService : IThumbnailService
{
    private readonly ISettingsStore _settings;
    private readonly ILogger<ThumbnailService> _logger;

    public ThumbnailService(ISettingsStore settings, ILogger<ThumbnailService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public bool IsAvailable()
    {
        var path = ResolveExecutablePath();
        if (string.IsNullOrEmpty(path)) return false;
        if (File.Exists(path)) return true;
        return IsOnPath(path);
    }

    public string ResolveExecutablePath()
    {
        var configured = _settings.Current.FfmpegPath;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }
        return "ffmpeg";
    }

    public string GetThumbnailPath(int videoId)
    {
        var dir = _settings.Current.EffectiveThumbnailDirectory;
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{videoId}.jpg");
    }

    public string GetMomentThumbnailPath(int momentId)
    {
        var dir = Path.Combine(_settings.Current.EffectiveThumbnailDirectory, "Moments");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{momentId}.jpg");
    }

    public string GetAiPreviewThumbnailPath(int suggestionId)
    {
        var dir = Path.Combine(_settings.Current.EffectiveThumbnailDirectory, "AiPreview");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{suggestionId}.jpg");
    }

    public string GetScrubDirectory(int videoId)
        => Path.Combine(_settings.Current.EffectiveThumbnailDirectory, "Scrub", videoId.ToString());

    public async Task<IReadOnlyList<string>> GenerateScrubFramesAsync(
        int videoId,
        string videoFilePath,
        double? durationSeconds,
        int frameCount,
        IProgress<string>? frameProduced = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoFilePath) || !File.Exists(videoFilePath))
            return Array.Empty<string>();

        var count = Math.Clamp(frameCount, 2, 60);
        var dir = GetScrubDirectory(videoId);
        var timestamps = ComputeScrubTimestamps(durationSeconds, count);

        // Fast cache-hit path: if every expected frame already exists, return
        // the cached set without spawning a single ffmpeg process.
        var expected = Enumerable.Range(0, timestamps.Count)
            .Select(i => Path.Combine(dir, $"{i}.jpg"))
            .ToList();
        if (expected.All(File.Exists))
        {
            foreach (var p in expected) frameProduced?.Report(p);
            return expected;
        }

        Directory.CreateDirectory(dir);

        var result = new List<string>(timestamps.Count);
        for (var i = 0; i < timestamps.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outputPath = Path.Combine(dir, $"{i}.jpg");
            if (File.Exists(outputPath))
            {
                result.Add(outputPath);
                frameProduced?.Report(outputPath);
                continue;
            }

            // Smaller + slightly lower quality than card thumbnails: scrub
            // frames are transient triage visuals, so we trade a little detail
            // for faster generation and a lighter cache footprint.
            var produced = await ExtractFrameAsync(
                videoFilePath, timestamps[i], outputPath, cancellationToken,
                scaleFilter: "scale=320:-2", quality: 5).ConfigureAwait(false);
            if (produced != null)
            {
                result.Add(produced);
                // Reveal this frame immediately so the card "fills in" as
                // extraction proceeds rather than blocking on the whole set.
                frameProduced?.Report(produced);
            }
        }
        return result;
    }

    // Evenly spaced sample points biased to the clip interior (mid-bucket
    // centers), mirroring the AI frame sampler so we never lean on a black
    // first frame or a fade-out tail.
    private static List<double> ComputeScrubTimestamps(double? duration, int count)
    {
        if (duration is null or <= 0)
            return new List<double> { 1.0 };

        var d = duration.Value;
        var result = new List<double>(count);
        for (var i = 0; i < count; i++)
        {
            var t = (i + 0.5) / count * d;
            if (t < 0) t = 0;
            if (t > d - 0.1) t = Math.Max(0, d - 0.1);
            result.Add(t);
        }
        return result;
    }

    // Deletes only canonical "{id}.jpg" files inside the configured thumbnail
    // cache directory. Source video files (anything outside this directory)
    // are NEVER touched, even if a malformed DB row tried to point us there.
    public int DeleteForVideos(IEnumerable<int> videoIds)
    {
        if (videoIds is null) return 0;

        string thumbDirFull;
        try
        {
            thumbDirFull = Path.GetFullPath(_settings.Current.EffectiveThumbnailDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve thumbnail directory; skipping thumbnail cleanup.");
            return 0;
        }

        if (!Directory.Exists(thumbDirFull)) return 0;

        var scrubRootFull = Path.GetFullPath(Path.Combine(thumbDirFull, "Scrub"));

        var deleted = 0;
        foreach (var id in videoIds)
        {
            try
            {
                var candidate = Path.GetFullPath(Path.Combine(thumbDirFull, $"{id}.jpg"));
                var candidateDir = Path.GetDirectoryName(candidate);

                // Defense in depth: only delete files that resolve directly inside the
                // configured thumbnail cache directory. This guards against any future
                // change to the path scheme accidentally escaping the cache dir.
                if (!string.Equals(candidateDir, thumbDirFull, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Skipping thumbnail outside cache dir: {Path}", candidate);
                    continue;
                }

                if (File.Exists(candidate))
                {
                    // Last-line-of-defence: refuse to delete anything that looks
                    // like media or escapes the cache dir, regardless of how the
                    // candidate path was derived.
                    MediaSafetyGuard.EnsureSafeToDelete(candidate, thumbDirFull);
                    File.Delete(candidate);
                    deleted++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete thumbnail for video id {Id}", id);
            }

            // Drop the lazily-generated hover-scrub frame cache for this clip
            // too. Guarded to resolve strictly under the "Scrub" subfolder so a
            // malformed id can never escape the cache directory.
            try
            {
                var scrubDir = Path.GetFullPath(Path.Combine(scrubRootFull, id.ToString()));
                if (string.Equals(Path.GetDirectoryName(scrubDir), scrubRootFull, StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(scrubDir))
                {
                    Directory.Delete(scrubDir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete scrub frames for video id {Id}", id);
            }
        }
        return deleted;
    }

    public Task<string?> GenerateAsync(int videoId, string videoFilePath, double? durationSeconds, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoFilePath) || !File.Exists(videoFilePath))
        {
            return Task.FromResult<string?>(null);
        }

        var outputPath = GetThumbnailPath(videoId);
        return ExtractFrameAsync(videoFilePath, ComputeSeek(durationSeconds), outputPath, cancellationToken);
    }

    public Task<string?> GenerateAtAsync(int momentId, string videoFilePath, double seekSeconds, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoFilePath) || !File.Exists(videoFilePath))
        {
            return Task.FromResult<string?>(null);
        }

        // Clamp negatives to 0; an out-of-range (past EOF) seek just yields no
        // frame and we return null, which the caller treats as "no thumbnail".
        var seek = seekSeconds < 0 ? 0 : seekSeconds;
        var outputPath = GetMomentThumbnailPath(momentId);
        return ExtractFrameAsync(videoFilePath, seek, outputPath, cancellationToken);
    }

    public Task<string?> GenerateAtPathAsync(int suggestionId, string videoFilePath, double seekSeconds, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoFilePath) || !File.Exists(videoFilePath))
        {
            return Task.FromResult<string?>(null);
        }

        // The best frame for a given suggestion never changes, so reuse the
        // cached still on a revisit instead of re-running ffmpeg.
        var outputPath = GetAiPreviewThumbnailPath(suggestionId);
        if (File.Exists(outputPath))
        {
            return Task.FromResult<string?>(outputPath);
        }

        var seek = seekSeconds < 0 ? 0 : seekSeconds;
        return ExtractFrameAsync(videoFilePath, seek, outputPath, cancellationToken);
    }

    // Shared single-frame extract used by whole-clip, moment, and hover-scrub
    // thumbnails. Seeks (fast seek, before -i) to seekSeconds and writes one
    // scaled JPEG to outputPath. The scaleFilter and JPEG quality are
    // overridable so scrub frames can be smaller/cheaper than card thumbnails.
    // The source file is only read, never written.
    private async Task<string?> ExtractFrameAsync(
        string videoFilePath,
        double seekSeconds,
        string outputPath,
        CancellationToken cancellationToken,
        string scaleFilter = "scale=640:-2",
        int quality = 4)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var exe = ResolveExecutablePath();

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-ss");
        psi.ArgumentList.Add(seekSeconds.ToString("F2", CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(videoFilePath);
        psi.ArgumentList.Add("-frames:v");
        psi.ArgumentList.Add("1");
        psi.ArgumentList.Add("-vf");
        psi.ArgumentList.Add(scaleFilter);
        psi.ArgumentList.Add("-q:v");
        psi.ArgumentList.Add(quality.ToString(CultureInfo.InvariantCulture));
        psi.ArgumentList.Add(outputPath);

        try
        {
            using var process = new Process { StartInfo = psi };
            if (!process.Start()) return null;

            _ = process.StandardOutput.ReadToEndAsync(cancellationToken);
            _ = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode == 0 && File.Exists(outputPath))
            {
                return outputPath;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Thumbnail generation failed for {File}", videoFilePath);
        }

        return null;
    }

    public int DeleteForMoments(IEnumerable<int> momentIds)
    {
        if (momentIds is null) return 0;

        string momentsDirFull;
        try
        {
            momentsDirFull = Path.GetFullPath(
                Path.Combine(_settings.Current.EffectiveThumbnailDirectory, "Moments"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve moment thumbnail directory; skipping cleanup.");
            return 0;
        }

        if (!Directory.Exists(momentsDirFull)) return 0;

        var deleted = 0;
        foreach (var id in momentIds)
        {
            try
            {
                var candidate = Path.GetFullPath(Path.Combine(momentsDirFull, $"{id}.jpg"));
                var candidateDir = Path.GetDirectoryName(candidate);

                // Same defense-in-depth as DeleteForVideos: only delete files
                // that resolve directly inside the Moments cache directory.
                if (!string.Equals(candidateDir, momentsDirFull, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Skipping moment thumbnail outside cache dir: {Path}", candidate);
                    continue;
                }

                if (File.Exists(candidate))
                {
                    MediaSafetyGuard.EnsureSafeToDelete(candidate, momentsDirFull);
                    File.Delete(candidate);
                    deleted++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete thumbnail for moment id {Id}", id);
            }
        }
        return deleted;
    }

    private static double ComputeSeek(double? duration)
    {
        if (!duration.HasValue || duration.Value <= 0) return 1.0;
        var seek = duration.Value * 0.15;
        if (seek < 0.5) seek = 0.5;
        if (seek > duration.Value - 0.1) seek = Math.Max(0.0, duration.Value - 0.1);
        return seek;
    }

    private static bool IsOnPath(string executable)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return false;

        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, executable);
                if (File.Exists(candidate)) return true;
                if (OperatingSystem.IsWindows() && File.Exists(candidate + ".exe")) return true;
            }
            catch
            {
                // ignore inaccessible PATH entries
            }
        }
        return false;
    }
}
