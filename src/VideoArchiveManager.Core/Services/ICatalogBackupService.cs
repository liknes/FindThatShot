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
