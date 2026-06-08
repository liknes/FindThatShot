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
namespace VideoArchiveManager.Core.Services.Ai;

// Turns a caption / query into the fixed-length (token id, attention mask) pair
// a CLIP text encoder ONNX session expects. Different model bundles ship
// different tokenizers (OpenAI CLIP's byte-level BPE for the English model, a
// BERT WordPiece vocabulary for the multilingual model), so the concrete
// implementation is chosen at load time from the model manifest.
public interface IClipTokenizer
{
    // Returns input ids and the matching attention mask, both padded to the
    // tokenizer's configured context length.
    (long[] InputIds, long[] AttentionMask) Tokenize(string text);
}
