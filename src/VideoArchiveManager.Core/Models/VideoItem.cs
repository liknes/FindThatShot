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
using VideoArchiveManager.Core.Models.Enums;

namespace VideoArchiveManager.Core.Models;

public class VideoItem
{
    public int Id { get; set; }

    [Required]
    public string FilePath { get; set; } = string.Empty;

    [Required]
    public string FileName { get; set; } = string.Empty;

    public string Extension { get; set; } = string.Empty;

    public string FolderPath { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public DateTime CreatedAtFile { get; set; }

    public DateTime ModifiedAtFile { get; set; }

    public double? DurationSeconds { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public double? FrameRate { get; set; }

    public string? Codec { get; set; }

    public string? Camera { get; set; }

    public double? GpsLatitude { get; set; }

    public double? GpsLongitude { get; set; }

    public DateTime? FolderDate { get; set; }

    public string? LocationText { get; set; }

    public string? ContextText { get; set; }

    public string? Notes { get; set; }

    public int Rating { get; set; }

    public VideoStatus Status { get; set; } = VideoStatus.Unreviewed;

    public string? ThumbnailPath { get; set; }

    public bool FileExists { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<VideoTag> VideoTags { get; set; } = new List<VideoTag>();

    public ICollection<VideoMoment> Moments { get; set; } = new List<VideoMoment>();
}
