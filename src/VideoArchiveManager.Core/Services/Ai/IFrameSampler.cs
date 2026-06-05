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

// One decoded frame: its position in the clip plus tightly-packed RGB24 pixels
// at the model's square input size (size*size*3 bytes).
public readonly record struct SampledFrame(double TimeSeconds, byte[] Rgb24);

// Decodes N evenly-spaced frames across a clip via ffmpeg, scaled+center-cropped
// to the requested square size and handed back as raw RGB. The source file is
// only ever read, never modified.
public interface IFrameSampler
{
    Task<IReadOnlyList<SampledFrame>> SampleAsync(
        string videoFilePath,
        double? durationSeconds,
        int frameCount,
        int size,
        CancellationToken cancellationToken = default);
}
