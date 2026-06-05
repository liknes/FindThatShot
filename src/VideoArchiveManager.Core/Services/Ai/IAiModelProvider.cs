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

public enum AiModelStatus
{
    // The feature is switched off in Settings.
    Disabled,
    // Enabled, but no model files are present yet (offer to download).
    NotInstalled,
    // A download / extraction is in progress.
    Downloading,
    // Model files are present and loadable.
    Ready,
    // Something went wrong loading or downloading.
    Error
}

// Resolves where the CLIP model bundle lives, reports whether it's installed,
// downloads it on demand, and hands out a loaded (cached) IClipModel. Honours a
// user-supplied drop-in folder first, then a managed app-data location.
public interface IAiModelProvider
{
    // Directory the model bundle is expected in (drop-in if configured and
    // present, otherwise the managed app-data location).
    string ModelDirectory { get; }

    AiModelStatus GetStatus();

    bool IsModelInstalled();

    // Downloads + extracts the model bundle into the managed directory. No-op
    // (returns true) if already installed. Reports 0..1 progress.
    Task<bool> EnsureDownloadedAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    // Returns a loaded, cached model. Throws if not installed / load fails.
    IClipModel GetModel();

    // Releases the loaded model + its native sessions (e.g. on disable).
    void Unload();
}
