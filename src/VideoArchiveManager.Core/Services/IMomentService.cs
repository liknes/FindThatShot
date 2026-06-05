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
using VideoArchiveManager.Core.Models;

namespace VideoArchiveManager.Core.Services;

// Criteria for searching across moments (sub-clips) catalog-wide. Mirrors the
// shape of the whole-clip SearchQuery but scoped to the fields a moment owns.
public class MomentSearchQuery
{
    public string? Text { get; set; }
    public int? MinRating { get; set; }
    public IReadOnlyCollection<int>? TagIds { get; set; }
    public bool? FileExists { get; set; }

    // True ⇒ when filtering by tags, only match moments where the tag is a
    // primary (non-background) subject. Has no effect unless TagIds is set.
    public bool MainSubjectOnly { get; set; }

    public int Take { get; set; } = 500;
}

public class MomentSearchResult
{
    public IReadOnlyList<VideoMoment> Moments { get; init; } = Array.Empty<VideoMoment>();
    public int TotalCount { get; init; }
}

// CRUD + search for timestamped moments. Tags reuse the global Tag vocabulary
// via the MomentTag join. Thumbnails are captured at the moment's in-point
// through IThumbnailService and never touch the source file.
public interface IMomentService
{
    Task<IReadOnlyList<VideoMoment>> GetForVideoAsync(int videoItemId, CancellationToken cancellationToken = default);

    // Creates a moment, then best-effort captures its thumbnail at StartSeconds.
    // The returned entity has its Id (and ThumbnailPath if capture succeeded).
    Task<VideoMoment> AddAsync(int videoItemId, double startSeconds, double? endSeconds, string? label, CancellationToken cancellationToken = default);

    // Persists label / notes / rating / in-out edits. Regenerates the thumbnail
    // only when the in-point moved (regenerateThumbnail = true).
    Task UpdateAsync(VideoMoment moment, bool regenerateThumbnail = false, CancellationToken cancellationToken = default);

    Task DeleteAsync(int momentId, CancellationToken cancellationToken = default);

    Task AttachTagAsync(int momentId, int tagId, CancellationToken cancellationToken = default);

    Task DetachTagAsync(int momentId, int tagId, CancellationToken cancellationToken = default);

    // Sets whether a tag is "background" (incidental) on a specific moment.
    // No-op if the tag isn't attached.
    Task SetTagProminenceAsync(int momentId, int tagId, bool isBackground, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Tag>> GetTagsForMomentAsync(int momentId, CancellationToken cancellationToken = default);

    Task<MomentSearchResult> SearchAsync(MomentSearchQuery query, CancellationToken cancellationToken = default);

    // Total number of moments attached to a clip (for the catalog card badge).
    Task<int> CountForVideoAsync(int videoItemId, CancellationToken cancellationToken = default);
}
