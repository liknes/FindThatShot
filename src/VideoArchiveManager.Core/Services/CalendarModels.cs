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
/// Aggregate count of clips whose effective shoot date
/// (<c>FolderDate ?? ModifiedAtFile</c>) falls in a given calendar month.
/// Drives the year-month heatmap of the calendar browse view. Read-only over
/// the catalog — no source file is touched to build it.
/// </summary>
public sealed record MonthShootCount(int Year, int Month, int Count);

/// <summary>
/// Lightweight projection of a clip for the calendar browse view's month clip
/// list + side preview panel. Carries only what the list and preview need
/// (identity, thumbnail, online state, rating, effective date) so listing a
/// busy month stays cheap — mirrors <see cref="MapClipPoint"/> for the map
/// browse view. The catalog is read only.
/// </summary>
public sealed class CalendarClip
{
    public int Id { get; init; }

    public string FileName { get; init; } = string.Empty;

    public string FolderPath { get; init; } = string.Empty;

    public string? ThumbnailPath { get; init; }

    public bool FileExists { get; init; }

    public int Rating { get; init; }

    /// <summary>The date the clip was bucketed under: FolderDate ?? ModifiedAtFile.</summary>
    public DateTime EffectiveDate { get; init; }
}
