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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Services;

namespace VideoArchiveManager.Data.Services;

public sealed class VideoLocationService : IVideoLocationService
{
    private readonly IDbContextFactory<VideoArchiveDbContext> _contextFactory;
    private readonly IReverseGeocodingService _geocoder;
    private readonly ILogger<VideoLocationService>? _logger;

    public VideoLocationService(
        IDbContextFactory<VideoArchiveDbContext> contextFactory,
        IReverseGeocodingService geocoder,
        ILogger<VideoLocationService>? logger = null)
    {
        _contextFactory = contextFactory;
        _geocoder = geocoder;
        _logger = logger;
    }

    public async Task<int> FillMissingLocationsAsync(
        IProgress<GeocodeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        int processed = 0;
        int filled = 0;
        int skipped = 0;
        int failed = 0;

        List<int> candidateIds;
        await using (var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            candidateIds = await ctx.VideoItems
                .Where(v => v.GpsLatitude.HasValue
                            && v.GpsLongitude.HasValue
                            && (v.LocationText == null || v.LocationText == ""))
                .Select(v => v.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        int total = candidateIds.Count;
        progress?.Report(new GeocodeProgress
        {
            TotalCandidates = total,
            Processed = 0,
            Message = total == 0
                ? "No videos need a location lookup."
                : $"Looking up {total} location(s)…"
        });

        if (total == 0)
        {
            progress?.Report(new GeocodeProgress
            {
                TotalCandidates = 0,
                IsComplete = true,
                Message = "No videos need a location lookup."
            });
            return 0;
        }

        foreach (var id in candidateIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var video = await ctx.VideoItems
                .FirstOrDefaultAsync(v => v.Id == id, cancellationToken)
                .ConfigureAwait(false);

            processed++;

            if (video is null ||
                !video.GpsLatitude.HasValue ||
                !video.GpsLongitude.HasValue ||
                !string.IsNullOrWhiteSpace(video.LocationText))
            {
                skipped++;
                ReportProgress();
                continue;
            }

            GeocodeResult? result;
            try
            {
                result = await _geocoder
                    .LookupAsync(video.GpsLatitude.Value, video.GpsLongitude.Value, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Reverse-geocoding failed for video {Id}", video.Id);
                failed++;
                ReportProgress();
                continue;
            }

            if (result is null || string.IsNullOrWhiteSpace(result.LocationShort))
            {
                failed++;
                ReportProgress();
                continue;
            }

            video.LocationText = result.LocationShort.Trim();
            video.UpdatedAt = DateTime.UtcNow;
            await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            filled++;
            ReportProgress(video.FileName);

            void ReportProgress(string? currentFile = null)
            {
                progress?.Report(new GeocodeProgress
                {
                    TotalCandidates = total,
                    Processed = processed,
                    Filled = filled,
                    Skipped = skipped,
                    Failed = failed,
                    CurrentFile = currentFile,
                    Message = $"Located {filled} of {total}…"
                });
            }
        }

        progress?.Report(new GeocodeProgress
        {
            TotalCandidates = total,
            Processed = processed,
            Filled = filled,
            Skipped = skipped,
            Failed = failed,
            IsComplete = true,
            Message = $"Filled {filled} location(s). {failed} failed, {skipped} skipped."
        });

        return filled;
    }
}
