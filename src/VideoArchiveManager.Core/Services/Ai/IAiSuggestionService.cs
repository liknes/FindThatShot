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

namespace VideoArchiveManager.Core.Services.Ai;

// A pending AI tag suggestion projected for the review queue, carrying enough
// of the parent clip to render a card without a second query.
public class AiSuggestionItem
{
    public int SuggestionId { get; init; }
    public int VideoItemId { get; init; }
    public string TagName { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public double? BestFrameSeconds { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string FolderPath { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string? ThumbnailPath { get; init; }
    public bool FileExists { get; init; }
}

// One clip's worth of pending suggestions for the grouped review UI.
public class AiSuggestionGroup
{
    public int VideoItemId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string FolderPath { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string? ThumbnailPath { get; init; }
    public bool FileExists { get; init; }
    public IReadOnlyList<AiSuggestionItem> Suggestions { get; init; } = Array.Empty<AiSuggestionItem>();
}

public interface IAiSuggestionService
{
    Task<int> CountPendingAsync(CancellationToken cancellationToken = default);

    // Pending suggestions grouped by clip, highest-confidence clips first.
    Task<IReadOnlyList<AiSuggestionGroup>> GetPendingGroupedAsync(int maxClips, CancellationToken cancellationToken = default);

    // Accepts a suggestion: gets-or-creates the real Tag, links it to the clip,
    // and marks the suggestion Accepted. Returns the created/linked tag id.
    Task<int> AcceptAsync(int suggestionId, TagType tagType, CancellationToken cancellationToken = default);

    Task RejectAsync(int suggestionId, CancellationToken cancellationToken = default);

    // Accept / reject every pending suggestion for a clip in one go.
    Task AcceptAllForClipAsync(int videoItemId, TagType tagType, CancellationToken cancellationToken = default);

    Task RejectAllForClipAsync(int videoItemId, CancellationToken cancellationToken = default);
}
