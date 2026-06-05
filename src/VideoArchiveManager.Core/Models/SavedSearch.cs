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

// A named, reusable filter — the app's equivalent of Lightroom's Smart
// Collections. The user's current sidebar filter state (text, status,
// rating, camera, tags, dates, folder scope, availability, unreviewed)
// is captured into SavedSearchCriteria, serialised to CriteriaJson, and
// re-applied on demand. Membership is *dynamic*: clicking a saved search
// re-runs the query against the live catalog, so newly-scanned clips that
// match show up automatically without any manual bucketing.
public class SavedSearch
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    // System.Text.Json serialisation of SavedSearchCriteria. Stored as a
    // blob rather than a wide column set so the criteria shape can evolve
    // without a schema migration each time SearchQuery grows a field.
    public string CriteriaJson { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    // Ascending display order in the sidebar. Defaults to creation order;
    // reserved for future drag-to-reorder without another migration.
    public int SortOrder { get; set; }
}
