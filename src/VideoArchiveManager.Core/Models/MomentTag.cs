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

// Join row linking a VideoMoment to a global Tag. Mirrors VideoTag so moments
// share the same tag vocabulary as whole clips — a "birds" tag means the same
// thing whether it's on a file or on a single shot inside it.
public class MomentTag
{
    public int VideoMomentId { get; set; }
    public VideoMoment VideoMoment { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;

    // Prominence of this tag on this moment. false = primary (default); true =
    // incidental/background. Mirrors VideoTag.IsBackground so moments and whole
    // clips share the same primary-vs-background semantics.
    public bool IsBackground { get; set; }
}
