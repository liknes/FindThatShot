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
namespace VideoArchiveManager.App.Localization;

// Resolves a localized display name for any enum value. The resx key is
// Enum_<TypeName>_<ValueName>; if the key is absent the raw enum name is
// returned (LocalizationManager already falls back to the key itself, which
// for our convention is human-readable enough during development).
public static class EnumDisplay
{
    public static string For(Enum value)
    {
        var key = $"Enum_{value.GetType().Name}_{value}";
        var text = LocalizationManager.Instance[key];
        // Fallback: if the key wasn't found the indexer echoes it back; show
        // the bare enum name in that case rather than the noisy key string.
        return text == key ? value.ToString() : text;
    }
}
