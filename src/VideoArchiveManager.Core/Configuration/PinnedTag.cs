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
using VideoArchiveManager.Core.Models.Enums;

namespace VideoArchiveManager.Core.Configuration;

// A single tag bound to a review-mode number-key hotkey. Persisted in
// settings.json rather than the catalog DB, so the binding survives a
// catalog restore / rebuild — it's identified by Name + Type (matching the
// catalog's (Name, Type) uniqueness contract) and resolved against the live
// tag catalog at runtime instead of by a (potentially stale) DB id.
public class PinnedTag
{
    // Number-key slot this tag is bound to. Slot 0 is the "1" key, slot 1
    // the "2" key … slot 8 the "9" key, and slot 9 the "0" key (so the
    // tenth pin sits on the 0 key at the end of the number row). Range 0-9.
    public int Slot { get; set; }

    public string Name { get; set; } = string.Empty;

    public TagType Type { get; set; } = TagType.Subject;
}
