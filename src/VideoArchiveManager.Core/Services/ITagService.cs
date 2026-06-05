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
using VideoArchiveManager.Core.Models.Enums;

namespace VideoArchiveManager.Core.Services;

public interface ITagService
{
    Task<IReadOnlyList<Tag>> GetAllAsync(CancellationToken cancellationToken = default);

    // Returns up to <paramref name="count"/> tags ordered by how many videos
    // carry them (most-used first). Used to bootstrap the review-mode pinned
    // tag hotkeys from the user's existing vocabulary. Tags with no usage are
    // excluded.
    Task<IReadOnlyList<Tag>> GetMostUsedAsync(int count, CancellationToken cancellationToken = default);

    Task<Tag> GetOrCreateAsync(string name, TagType type, CancellationToken cancellationToken = default);

    Task AttachAsync(int videoItemId, int tagId, CancellationToken cancellationToken = default);

    Task DetachAsync(int videoItemId, int tagId, CancellationToken cancellationToken = default);

    Task BulkAttachAsync(IEnumerable<int> videoItemIds, int tagId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Tag>> GetTagsForVideoAsync(int videoItemId, CancellationToken cancellationToken = default);

    // Like GetTagsForVideoAsync but returns the join rows so callers can see
    // each tag's per-clip prominence (VideoTag.IsBackground), with Tag loaded.
    Task<IReadOnlyList<VideoTag>> GetVideoTagsForVideoAsync(int videoItemId, CancellationToken cancellationToken = default);

    // Sets whether a tag is "background" (incidental) on a specific clip.
    // No-op if the tag isn't attached.
    Task SetTagProminenceAsync(int videoItemId, int tagId, bool isBackground, CancellationToken cancellationToken = default);
}
