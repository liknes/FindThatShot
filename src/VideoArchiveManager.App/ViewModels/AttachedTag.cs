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
using CommunityToolkit.Mvvm.ComponentModel;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Models.Enums;

namespace VideoArchiveManager.App.ViewModels;

// Display wrapper for a Tag attached to a specific clip or moment, carrying the
// per-attachment prominence (IsBackground) that the bare Tag can't express.
// Name / Id / Type are passed through so existing chip bindings ({Binding Name})
// and the attach/detach/toggle logic keep working unchanged.
public partial class AttachedTag : ObservableObject
{
    public Tag Tag { get; }

    // true ⇒ this tag is an incidental / background subject on the clip or
    // moment (e.g. distant islands behind a beach), false ⇒ primary subject.
    // Drives the dimmed chip styling and the context-menu toggle.
    [ObservableProperty]
    private bool _isBackground;

    public AttachedTag(Tag tag, bool isBackground)
    {
        Tag = tag;
        _isBackground = isBackground;
    }

    public int Id => Tag.Id;
    public string Name => Tag.Name;
    public TagType Type => Tag.Type;
}
