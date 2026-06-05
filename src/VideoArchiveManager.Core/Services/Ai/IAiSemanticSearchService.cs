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
namespace VideoArchiveManager.Core.Services.Ai;

public class SemanticSearchHit
{
    public int VideoItemId { get; init; }
    public double Score { get; init; }
    // Best-matching timestamp (seconds) within the clip, when frame-level
    // resolution is available — lets the UI jump to where the subject appears.
    public double? BestFrameSeconds { get; init; }
}

// Natural-language ("drone shot over snowy mountains at sunset") search over the
// stored CLIP clip embeddings. Returns clip ids ranked by similarity; the
// caller hydrates them into VideoItems.
public interface IAiSemanticSearchService
{
    bool IsAvailable { get; }

    Task<IReadOnlyList<SemanticSearchHit>> SearchAsync(
        string query,
        int maxResults,
        double minScore,
        CancellationToken cancellationToken = default);

    // Drops the in-memory embedding cache (e.g. after a tagging pass adds rows).
    void InvalidateCache();
}
