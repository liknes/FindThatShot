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
/// Maps a hero clip path on disk to its NLE-generated proxy (DaVinci Resolve
/// convention: a sibling "Proxy" folder containing a file with the same base
/// name as the hero, typically a smaller ProRes/DNxHR/H.264 transcode used for
/// faster review and editing).
///
/// <para>
/// Used exclusively at in-app playback time when the user has opted into
/// <c>AppSettings.PreferProxyForPlayback</c>. Catalog entries, thumbnails,
/// ffprobe metadata and external playback all continue to point at the hero
/// file regardless of whether a proxy exists.
/// </para>
/// </summary>
public interface IProxyResolver
{
    /// <summary>
    /// Returns the absolute path to the proxy file for <paramref name="heroFilePath"/>
    /// if one exists on disk; <see langword="null"/> otherwise. Implementations
    /// must never throw — a missing folder, an inaccessible drive, or any I/O
    /// error all surface as a <see langword="null"/> result so callers can fall
    /// back to the hero file unconditionally.
    /// </summary>
    string? TryResolveProxy(string heroFilePath);
}
