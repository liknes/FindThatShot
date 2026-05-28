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
                    File.Delete(candidate);
                    deleted++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete thumbnail for video id {Id}", id);
            }
        }
        return deleted;
    }

    public async Task<string?> GenerateAsync(int videoId, string videoFilePath, double? durationSeconds, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoFilePath) || !File.Exists(videoFilePath))
        {
            return null;
        }

        var outputPath = GetThumbnailPath(videoId);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var seekSeconds = ComputeSeek(durationSeconds);
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
        psi.ArgumentList.Add("scale=640:-2");
        psi.ArgumentList.Add("-q:v");
        psi.ArgumentList.Add("4");
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
