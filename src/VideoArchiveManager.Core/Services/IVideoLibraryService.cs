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

// SAFETY CONTRACT (do not relax):
//
//   * Source video files (anything under user-configured root folders or any
//     path that did not originate from this app) MUST NEVER be deleted, moved,
//     renamed, or altered in any way. "Removing" a video here means forgetting
//     it from the catalog database, not touching the file on disk.
//
//   * App-generated thumbnail cache files inside the configured thumbnail
//     directory ARE allowed to be cleaned up when their video record is
//     removed. The thumbnail service is responsible for verifying any file it
//     deletes resolves inside that cache directory.
public interface IVideoLibraryService
{
    Task<int> RemoveByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    Task<int> RemoveOfflineAsync(CancellationToken cancellationToken = default);

    Task<int> CountUnderRootAsync(string rootPath, CancellationToken cancellationToken = default);

    Task<int> RemoveUnderRootAsync(string rootPath, CancellationToken cancellationToken = default);

    // Atomically removes a root folder row AND every video record imported from
    // under it in a single database transaction, so a partial failure can never
    // leave the catalog half-deleted (orphaned video rows with no root, or a
    // root with no videos). Returns the number of video records removed. As with
    // every method here, source video files on disk are NEVER touched.
    Task<int> RemoveRootFolderAsync(int rootFolderId, string rootPath, CancellationToken cancellationToken = default);
}
