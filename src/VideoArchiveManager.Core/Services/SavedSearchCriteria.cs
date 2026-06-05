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
using System.Text.Json;
using System.Text.Json.Serialization;
using VideoArchiveManager.Core.Models.Enums;

namespace VideoArchiveManager.Core.Services;

// Serialisable snapshot of the sidebar filter state behind a SavedSearch.
// Mirrors the subset of SearchQuery the UI lets the user drive, plus the
// two view-mode toggles (availability / unreviewed) that aren't first-class
// SearchQuery inputs but matter when restoring a saved view. Persisted as
// JSON inside SavedSearch.CriteriaJson.
public class SavedSearchCriteria
{
    public string? Text { get; set; }

    public VideoStatus? Status { get; set; }

    public int MinRating { get; set; }

    public string? Camera { get; set; }

    public IReadOnlyList<int> TagIds { get; set; } = Array.Empty<int>();

    public DateTime? DateFrom { get; set; }

    public DateTime? DateTo { get; set; }

    // Path prefix matched in SearchService (a folder-tree node's FullPath).
    public string? RootFolderPath { get; set; }

    public bool ShowOnlyAvailable { get; set; } = true;

    public bool OnlyUnreviewed { get; set; }

    // When set, a tag filter only matches clips where the tag is a primary
    // (non-background) subject. Defaults to false so older saved searches
    // restore unchanged.
    public bool MainSubjectOnly { get; set; }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions);

    // Tolerant of malformed / empty payloads — a saved search whose JSON
    // can't be read falls back to an empty criteria (i.e. "show everything")
    // rather than throwing and breaking the sidebar list.
    public static SavedSearchCriteria Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new SavedSearchCriteria();
        try
        {
            return JsonSerializer.Deserialize<SavedSearchCriteria>(json, SerializerOptions)
                   ?? new SavedSearchCriteria();
        }
        catch (JsonException)
        {
            return new SavedSearchCriteria();
        }
    }
}
