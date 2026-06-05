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

// One pooled, L2-normalized CLIP embedding per clip (the mean of its sampled
// frame embeddings, re-normalized). Small and cacheable in memory, this is the
// vector natural-language search ranks against. The richer per-frame vectors
// live in AiFrameEmbedding for max-pool tagging and timestamp precision.
public class AiClipEmbedding
{
    public int Id { get; set; }

    public int VideoItemId { get; set; }
    public VideoItem VideoItem { get; set; } = null!;

    // Float32 vector, little-endian, length == Dim. Stored as a BLOB.
    public byte[] Vector { get; set; } = Array.Empty<byte>();

    public int Dim { get; set; }

    // Number of frames that were sampled and pooled into this vector.
    public int FrameCount { get; set; }

    // Identifies the model that produced the vector so a model upgrade can
    // invalidate / re-embed stale rows.
    public string ModelId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
