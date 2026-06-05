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
namespace VideoArchiveManager.Core.Models.Enums;

public enum VideoStatus
{
    Unreviewed = 0,
    Keep = 1,
    Favorite = 2,
    ForStock = 3,
    UploadedPond5 = 4,
    UploadedShutterstock = 5,
    UploadedAdobe = 6,
    Rejected = 7,
    Archive = 8
}
