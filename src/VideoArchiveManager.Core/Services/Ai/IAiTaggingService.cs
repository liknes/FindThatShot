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

public class AiTaggingProgress
{
    public int Total { get; init; }
    public int Processed { get; init; }
    public int Tagged { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public string? CurrentFile { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool IsComplete { get; init; }
}

// Runs the CLIP scoring pass over clips: samples frames, stores embeddings, and
// writes AiTagSuggestion rows. Never reads or modifies source files beyond the
// frame decode, and never auto-applies tags.
public interface IAiTaggingService
{
    // Number of online clips that still need embedding for the current model
    // (or all online clips when reprocessAll is true).
    Task<int> CountPendingAsync(bool reprocessAll, CancellationToken cancellationToken = default);

    Task GenerateAsync(
        bool reprocessAll,
        IProgress<AiTaggingProgress>? progress = null,
        CancellationToken cancellationToken = default);

    // Removes all AI-generated data (embeddings + suggestions) from the catalog.
    // Source files and real tags are untouched.
    Task<int> ClearAllAiDataAsync(CancellationToken cancellationToken = default);
}
