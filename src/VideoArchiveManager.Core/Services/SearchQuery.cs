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

namespace VideoArchiveManager.Core.Services;

public class SearchQuery
{
    public string? Text { get; set; }
    public VideoStatus? Status { get; set; }
    public int? MinRating { get; set; }
    public string? Camera { get; set; }
    public IReadOnlyCollection<int>? TagIds { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string? RootFolderPath { get; set; }
    public bool? FileExists { get; set; }

    // Restrict the result to an explicit set of catalog ids. Drives the
    // "scope the grid to this map cluster / viewport" flow from the global
    // map browse view — the map hands back the ids of the clips the user
    // clicked / framed and the grid query narrows to exactly those.
    public IReadOnlyCollection<int>? VideoIds { get; set; }

    // True ⇒ restrict to videos that look untouched: either still have the
    // default Status of Unreviewed, or carry no tags. Defined as a UNION so a
    // forgetful reviewer (didn't update Status) and a behavioural reviewer
    // (no tags yet) both surface the same way.
    public bool? OnlyUnreviewed { get; set; }

    // True ⇒ when filtering by tags, only match clips where the tag is a
    // primary (non-background) subject. Hides clips where the tag is merely
    // incidental (e.g. distant islands behind a beach). Has no effect unless
    // TagIds is set.
    public bool MainSubjectOnly { get; set; }

    public int Skip { get; set; }
    public int Take { get; set; } = 200;

    public SearchSortField SortBy { get; set; } = SearchSortField.ModifiedDescending;
}

public enum SearchSortField
{
    ModifiedDescending,
    ModifiedAscending,
    FileNameAscending,
    FileNameDescending,
    RatingDescending,
    FolderDateDescending
}
