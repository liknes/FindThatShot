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
using System.ComponentModel.DataAnnotations;

namespace VideoArchiveManager.Core.Models;

// A timestamped sub-clip ("moment" / "the shot") inside a VideoItem. The whole
// point of the app's name: the natural unit of search is a moment in a clip,
// not just the whole file. A moment carries its own in/out range, label,
// rating, notes, tags, and a thumbnail captured at its in-point — so search can
// return the actual shot with a "jump to" action rather than the parent file.
//
// Source video files are never touched; like everything else here a moment is
// pure catalog metadata. Its thumbnail JPG lives in the app's thumbnail cache,
// not next to the video.
public class VideoMoment
{
    public int Id { get; set; }

    public int VideoItemId { get; set; }

    public VideoItem VideoItem { get; set; } = null!;

    // In-point, in seconds from the start of the clip. Always set.
    public double StartSeconds { get; set; }

    // Out-point, in seconds. Null means a single-point marker (an instant the
    // reviewer flagged with just an in-point); the UI treats it as a zero-length
    // moment that still has a thumbnail and metadata.
    public double? EndSeconds { get; set; }

    // Short human label for the shot, e.g. "hero drone reveal" or "gull dives".
    public string? Label { get; set; }

    public string? Notes { get; set; }

    public int Rating { get; set; }

    // Full path to the cached JPG captured at StartSeconds. Generated on demand
    // (mirrors VideoItem.ThumbnailPath); null until the frame has been grabbed.
    public string? ThumbnailPath { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<MomentTag> MomentTags { get; set; } = new List<MomentTag>();
}
