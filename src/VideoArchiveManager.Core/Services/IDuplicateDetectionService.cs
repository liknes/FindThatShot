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

/// <summary>
/// Finds likely-duplicate clips in the catalog by metadata fingerprint
/// (exact file size + duration + resolution). Read-only: it only queries the
/// catalog database and never reads, hashes, moves, or modifies any source
/// video file. Acting on the results (removing redundant catalog entries) is
/// done through <see cref="IVideoLibraryService"/>, which only ever forgets
/// rows from the database — never deletes files on disk.
/// </summary>
public interface IDuplicateDetectionService
{
    Task<IReadOnlyList<DuplicateGroup>> FindDuplicatesAsync(CancellationToken cancellationToken = default);
}
