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

namespace VideoArchiveManager.Core.Services.Ai;

// Samples frames by spawning ffmpeg once per timestamp, piping a single
// scaled+cropped RGB24 frame to stdout. Reuses the same ffmpeg the thumbnail
// pipeline resolves, so it honours the user's configured path. Reading frames
// never writes to the source video.
public class FfmpegFrameSampler : IFrameSampler
{
    private readonly IThumbnailService _thumbnails;
    private readonly ILogger<FfmpegFrameSampler> _logger;

    public FfmpegFrameSampler(IThumbnailService thumbnails, ILogger<FfmpegFrameSampler> logger)
    {
        _thumbnails = thumbnails;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SampledFrame>> SampleAsync(
        string videoFilePath,
        double? durationSeconds,
        int frameCount,
        int size,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoFilePath) || !File.Exists(videoFilePath))
            return Array.Empty<SampledFrame>();

        var timestamps = ComputeTimestamps(durationSeconds, Math.Max(1, frameCount));
        var exe = _thumbnails.ResolveExecutablePath();
        var frameBytes = size * size * 3;

        var frames = new List<SampledFrame>(timestamps.Count);
        foreach (var t in timestamps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rgb = await ExtractRawFrameAsync(exe, videoFilePath, t, size, frameBytes, cancellationToken)
                .ConfigureAwait(false);
            if (rgb != null) frames.Add(new SampledFrame(t, rgb));
        }
        return frames;
    }

    // Evenly spaced sample points biased to clip interior (mid-bucket centers),
    // so we never lean on a black first frame or a fade-out tail.
    private static List<double> ComputeTimestamps(double? duration, int count)
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

    private async Task<byte[]?> ExtractRawFrameAsync(
        string exe, string videoFilePath, double seekSeconds, int size, int frameBytes, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-ss");
        psi.ArgumentList.Add(seekSeconds.ToString("F2", CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(videoFilePath);
        psi.ArgumentList.Add("-frames:v");
        psi.ArgumentList.Add("1");
        psi.ArgumentList.Add("-vf");
        psi.ArgumentList.Add(
            $"scale={size}:{size}:force_original_aspect_ratio=increase,crop={size}:{size}");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("rawvideo");
        psi.ArgumentList.Add("-pix_fmt");
        psi.ArgumentList.Add("rgb24");
        psi.ArgumentList.Add("pipe:1");

        try
        {
            using var process = new Process { StartInfo = psi };
            if (!process.Start()) return null;

            using var ms = new MemoryStream(frameBytes);
            var copyTask = process.StandardOutput.BaseStream.CopyToAsync(ms, cancellationToken);
            var errTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await copyTask.ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await errTask.ConfigureAwait(false);

            var data = ms.ToArray();
            if (data.Length < frameBytes)
            {
                // Short read = decode failure / seek past EOF; treat as no frame.
                return null;
            }
            // Trim any trailing bytes (shouldn't happen for a single frame).
            if (data.Length > frameBytes) Array.Resize(ref data, frameBytes);
            return data;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Frame sampling failed for {File} at {Seek}s", videoFilePath, seekSeconds);
            return null;
        }
    }
}
