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

/// <summary>
/// A read-only snapshot of aggregate catalog metrics, computed on demand for
/// the statistics dashboard. Every value is derived purely from the catalog
/// database — no source video files are read or touched to build it.
/// </summary>
public class CatalogStatistics
{
    public int TotalClips { get; init; }
    public int OnlineClips { get; init; }
    public int OfflineClips { get; init; }

    public long TotalSizeBytes { get; init; }
    public double TotalDurationSeconds { get; init; }

    public int RootFolderCount { get; init; }
    public int DistinctFolders { get; init; }
    public int DistinctCameras { get; init; }
    public int TotalTags { get; init; }

    // Curation progress signals.
    public int UnreviewedClips { get; init; }
    public int TaggedClips { get; init; }
    public int RatedClips { get; init; }
    public int GeotaggedClips { get; init; }

    public IReadOnlyList<StatCount> ByStatus { get; init; } = Array.Empty<StatCount>();
    public IReadOnlyList<StatCount> ByRating { get; init; } = Array.Empty<StatCount>();
    public IReadOnlyList<StatCount> ByResolution { get; init; } = Array.Empty<StatCount>();
    public IReadOnlyList<StatCount> ByYear { get; init; } = Array.Empty<StatCount>();
    public IReadOnlyList<StatCount> TopCameras { get; init; } = Array.Empty<StatCount>();
    public IReadOnlyList<StatCount> TopCodecs { get; init; } = Array.Empty<StatCount>();
    public IReadOnlyList<StatCount> TopTags { get; init; } = Array.Empty<StatCount>();
}

/// <summary>
/// A single labelled bucket in a breakdown (e.g. one status, one camera, one
/// year). <see cref="Count"/> is the number of clips in that bucket.
/// </summary>
public class StatCount
{
    public StatCount(string label, int count)
    {
        Label = label;
        Count = count;
    }

    public string Label { get; }
    public int Count { get; }
}
