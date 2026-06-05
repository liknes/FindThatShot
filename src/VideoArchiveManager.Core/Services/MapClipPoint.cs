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

/// <summary>
/// Lightweight projection of a geotagged clip for the global map browse view.
/// Carries only what the map and its side preview panel need (coordinates,
/// identity, thumbnail, online state) so plotting thousands of points doesn't
/// drag the full <see cref="Models.VideoItem"/> graph (tags, moments) into
/// memory. The catalog is read only — no source file is touched to build it.
/// </summary>
public sealed class MapClipPoint
{
    public int Id { get; init; }

    public double Latitude { get; init; }

    public double Longitude { get; init; }

    public string FileName { get; init; } = string.Empty;

    public string FolderPath { get; init; } = string.Empty;

    public string? LocationText { get; init; }

    public string? ThumbnailPath { get; init; }

    public bool FileExists { get; init; }

    public int Rating { get; init; }
}
