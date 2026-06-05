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
namespace VideoArchiveManager.Core.Models;

/// <summary>
/// A set of two or more catalog entries that share the same metadata
/// fingerprint (exact file size + duration + resolution) and are therefore
/// almost certainly the same clip living in more than one place — e.g. the
/// same file copied to a backup drive.
///
/// This is "Tier 1" duplicate detection: it is computed purely from catalog
/// metadata the scanner already stored, so it is instant and works even for
/// offline clips. No source video file is opened, hashed, or touched to build
/// a group.
/// </summary>
public class DuplicateGroup
{
    /// <summary>Exact file size in bytes shared by every member.</summary>
    public long FileSizeBytes { get; init; }

    /// <summary>Duration in seconds shared by every member (rounded), or null when unknown.</summary>
    public double? DurationSeconds { get; init; }

    public int? Width { get; init; }
    public int? Height { get; init; }

    public IReadOnlyList<DuplicateVideo> Videos { get; init; } = Array.Empty<DuplicateVideo>();

    /// <summary>Number of members beyond the one suggested copy to keep.</summary>
    public int RedundantCount => Math.Max(0, Videos.Count - 1);

    /// <summary>Bytes that could be reclaimed if every redundant copy were removed from disk.</summary>
    public long RedundantBytes => (long)RedundantCount * FileSizeBytes;
}

/// <summary>
/// One member of a <see cref="DuplicateGroup"/>: a thin, read-only projection
/// of the catalog row, carrying just what the duplicate finder needs to show
/// the clip and to act on it (remove from catalog by id).
/// </summary>
public class DuplicateVideo
{
    public int Id { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string FolderPath { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public double? DurationSeconds { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? Codec { get; init; }
    public string? Camera { get; init; }
    public int Rating { get; init; }
    public int TagCount { get; init; }
    public bool FileExists { get; init; }
    public string? ThumbnailPath { get; init; }

    /// <summary>
    /// True for the single member the finder suggests keeping (online, then
    /// best curated, then oldest). Surfaced as a hint only — the user always
    /// chooses what, if anything, to remove.
    /// </summary>
    public bool IsSuggestedKeep { get; set; }
}
