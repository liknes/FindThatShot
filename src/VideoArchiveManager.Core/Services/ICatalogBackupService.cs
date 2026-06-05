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

public interface ICatalogBackupService
{
    Task<CatalogBackupResult> BackupNowAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CatalogBackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default);

    Task<int> PruneAsync(int keep, CancellationToken cancellationToken = default);

    // Stages a restore: copies the chosen backup to a sibling
    // "<catalog.db>.restore-pending" file. The actual swap happens at the
    // next app startup, before any database connection is opened, to avoid
    // SQLite file-lock conflicts. The caller is responsible for restarting
    // the app after this returns successfully.
    Task<CatalogRestoreResult> RestoreAsync(string backupPath, CancellationToken cancellationToken = default);
}

public class CatalogBackupResult
{
    public bool Success { get; init; }

    public string? BackupPath { get; init; }

    public long Bytes { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public int Pruned { get; init; }

    public string? ErrorMessage { get; init; }
}

public class CatalogBackupInfo
{
    public string Path { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public long Bytes { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}

public class CatalogRestoreResult
{
    public bool Success { get; init; }

    // Pre-restore safety copy of the current catalog, if one was created.
    // Lets the user roll back even after the restart.
    public string? SafetyBackupPath { get; init; }

    // The staged path on disk; once the app restarts this is moved into
    // place over the live catalog.
    public string? PendingPath { get; init; }

    public string? ErrorMessage { get; init; }
}
