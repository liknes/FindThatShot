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

public interface IThumbnailService
{
    bool IsAvailable();

    string ResolveExecutablePath();

    string GetThumbnailPath(int videoId);

    Task<string?> GenerateAsync(int videoId, string videoFilePath, double? durationSeconds, CancellationToken cancellationToken = default);

    // Deletes app-generated thumbnail cache files for the given video ids.
    // Only canonical "{id}.jpg" files inside the configured thumbnail
    // directory are touched. Returns the number of files actually deleted.
    int DeleteForVideos(IEnumerable<int> videoIds);

    // Canonical cache path for a moment thumbnail: a "{momentId}.jpg" file
    // inside a "Moments" subfolder of the configured thumbnail directory, kept
    // separate from whole-clip thumbnails so the two id spaces can't collide.
    string GetMomentThumbnailPath(int momentId);

    // Extracts a single frame at an explicit timestamp (seconds from the start
    // of the clip) for a moment, writing it to GetMomentThumbnailPath(momentId).
    // Unlike GenerateAsync this seeks to a caller-supplied position rather than
    // a fixed 15% of the duration. Returns the output path on success, null on
    // failure. Source video file is never modified.
    Task<string?> GenerateAtAsync(int momentId, string videoFilePath, double seekSeconds, CancellationToken cancellationToken = default);

    // Deletes cached "{momentId}.jpg" files for the given moment ids from the
    // "Moments" subfolder. Returns the number of files actually deleted.
    int DeleteForMoments(IEnumerable<int> momentIds);

    // Canonical cache path for an AI-suggestion preview frame: a
    // "{suggestionId}.jpg" file inside an "AiPreview" subfolder, kept separate
    // from clip/moment thumbnails so id spaces can't collide.
    string GetAiPreviewThumbnailPath(int suggestionId);

    // Extracts the single frame that drove a tag suggestion (seekSeconds is the
    // suggestion's BestFrameSeconds) so the reviewer can verify it, writing it to
    // GetAiPreviewThumbnailPath(suggestionId). Idempotent: returns the cached file
    // if it already exists. Returns the output path on success, null on failure.
    // The source video file is only ever read, never modified.
    Task<string?> GenerateAtPathAsync(int suggestionId, string videoFilePath, double seekSeconds, CancellationToken cancellationToken = default);

    // Per-video cache directory for lazily-generated hover-scrub frames: a
    // "Scrub/{videoId}" subfolder of the configured thumbnail directory, kept
    // separate from whole-clip and moment thumbnails.
    string GetScrubDirectory(int videoId);

    // Lazily generates up to frameCount evenly-spaced preview frames across the
    // clip into GetScrubDirectory(videoId) as "0.jpg".."(n-1).jpg" for hover
    // scrubbing on the catalog card. Idempotent: if the expected frames are
    // already cached they're returned as-is without re-running ffmpeg. Returns
    // the ordered list of frame paths that exist on disk (may be shorter than
    // frameCount on partial failure). The source video file is only ever read,
    // never modified.
    //
    // frameProduced (optional) is invoked with each frame path the moment it's
    // available (cached or freshly extracted), in index order, so the UI can
    // reveal the scrub strip progressively instead of waiting for the whole set.
    Task<IReadOnlyList<string>> GenerateScrubFramesAsync(
        int videoId,
        string videoFilePath,
        double? durationSeconds,
        int frameCount,
        IProgress<string>? frameProduced = null,
        CancellationToken cancellationToken = default);
}
