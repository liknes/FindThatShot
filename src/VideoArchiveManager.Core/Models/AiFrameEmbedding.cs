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

// One L2-normalized CLIP image embedding for a single frame sampled from a
// clip at TimeSeconds. Multiple rows per clip. Tag scoring max-pools across
// these (so "does this clip ever show snow?" is answered by the best frame,
// not the average), and they let search resolve the exact second a subject
// appears for jump-to / future Moment creation.
public class AiFrameEmbedding
{
    public int Id { get; set; }

    public int VideoItemId { get; set; }
    public VideoItem VideoItem { get; set; } = null!;

    // Position (seconds) in the clip the frame was decoded from.
    public double TimeSeconds { get; set; }

    // Float32 vector, little-endian, length == Dim. Stored as a BLOB.
    public byte[] Vector { get; set; } = Array.Empty<byte>();

    public int Dim { get; set; }

    public string ModelId { get; set; } = string.Empty;
}
