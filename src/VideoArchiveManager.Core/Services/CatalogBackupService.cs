using Microsoft.Extensions.Logging;
using VideoArchiveManager.Core.Configuration;

namespace VideoArchiveManager.Core.Services;

// SAFETY CONTRACT:
//   * This service only ever reads the SQLite catalog file and writes new
//     copies of it into the configured Backups directory. Restoring is done
//     via a staged file ("<catalog>.restore-pending") that is swapped into
//     place at the next app startup, so the live catalog is never replaced
//     while connections may be open. It never touches source video files.
public class CatalogBackupService : ICatalogBackupService
{
    private const string BackupPrefix = "catalog";
    private const string BackupExtension = ".db";
    public const string PendingRestoreSuffix = ".restore-pending";
    public const string PreRestoreSafetyPrefix = "catalog-pre-restore-";
    // Timestamp format that is safe for Windows filenames: yyyyMMdd-HHmmss.
    private const string TimestampFormat = "yyyyMMdd-HHmmss";

    private readonly ISettingsStore _settings;
    private readonly ILogger<CatalogBackupService> _logger;

    public CatalogBackupService(ISettingsStore settings, ILogger<CatalogBackupService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<CatalogBackupResult> BackupNowAsync(CancellationToken cancellationToken = default)
    {
        var current = _settings.Current;
        var dbPath = current.EffectiveDatabasePath;
        var backupDir = current.EffectiveBackupDirectory;

        if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
        {
            return new CatalogBackupResult
            {
                Success = false,
                ErrorMessage = $"Catalog database not found at '{dbPath}'."
            };
        }

        try
        {
            Directory.CreateDirectory(backupDir);

            var nowUtc = DateTime.UtcNow;
            var stamp = nowUtc.ToLocalTime().ToString(TimestampFormat);
            var backupPath = Path.Combine(backupDir, $"{BackupPrefix}-{stamp}{BackupExtension}");

            await using (var source = new FileStream(
                dbPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1 << 16,
                useAsync: true))
            await using (var dest = new FileStream(
                backupPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1 << 16,
                useAsync: true))
            {
                await source.CopyToAsync(dest, cancellationToken).ConfigureAwait(false);
            }

            var bytes = new FileInfo(backupPath).Length;

            var pruned = await PruneAsync(current.BackupRetentionCount, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Backed up catalog to {Path} ({Bytes} bytes); pruned {Pruned} old backup(s).",
                backupPath, bytes, pruned);

            return new CatalogBackupResult
            {
                Success = true,
                BackupPath = backupPath,
                Bytes = bytes,
                CreatedAtUtc = nowUtc,
                Pruned = pruned
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Catalog backup failed.");
            return new CatalogBackupResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public Task<IReadOnlyList<CatalogBackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        var dir = _settings.Current.EffectiveBackupDirectory;
        IReadOnlyList<CatalogBackupInfo> list = !Directory.Exists(dir)
            ? Array.Empty<CatalogBackupInfo>()
            : EnumerateBackups(dir)
                .OrderByDescending(b => b.CreatedAtUtc)
                .ToArray();
        return Task.FromResult(list);
    }

    public Task<int> PruneAsync(int keep, CancellationToken cancellationToken = default)
    {
        var dir = _settings.Current.EffectiveBackupDirectory;
        if (keep < 1 || !Directory.Exists(dir)) return Task.FromResult(0);

        var ordered = EnumerateBackups(dir)
            .OrderByDescending(b => b.CreatedAtUtc)
            .ToList();

        var removed = 0;
        foreach (var old in ordered.Skip(keep))
        {
            try
            {
                File.Delete(old.Path);
                removed++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete old backup {Path}", old.Path);
            }
        }

        return Task.FromResult(removed);
    }

    public async Task<CatalogRestoreResult> RestoreAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
        {
            return new CatalogRestoreResult
            {
                Success = false,
                ErrorMessage = $"Backup file not found: '{backupPath}'."
            };
        }

        var current = _settings.Current;
        var dbPath = current.EffectiveDatabasePath;
        var backupDir = current.EffectiveBackupDirectory;
        var pendingPath = dbPath + PendingRestoreSuffix;

        string? safetyPath = null;

        try
        {
            // Snapshot the live catalog into the backup folder as a
            // "pre-restore" safety copy so the user can roll back even after
            // restart.
            if (File.Exists(dbPath))
            {
                Directory.CreateDirectory(backupDir);
                var stamp = DateTime.UtcNow.ToLocalTime().ToString(TimestampFormat);
                safetyPath = Path.Combine(backupDir, $"{PreRestoreSafetyPrefix}{stamp}{BackupExtension}");

                await using var src = new FileStream(
                    dbPath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 1 << 16, useAsync: true);
                await using var dst = new FileStream(
                    safetyPath, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, bufferSize: 1 << 16, useAsync: true);
                await src.CopyToAsync(dst, cancellationToken).ConfigureAwait(false);
            }

            // Stage the restore. We don't touch dbPath here — the swap
            // happens at next startup before any DB connection is opened.
            if (File.Exists(pendingPath))
            {
                File.Delete(pendingPath);
            }

            var dbDir = Path.GetDirectoryName(pendingPath);
            if (!string.IsNullOrEmpty(dbDir)) Directory.CreateDirectory(dbDir);

            await using (var src = new FileStream(
                backupPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1 << 16, useAsync: true))
            await using (var dst = new FileStream(
                pendingPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, bufferSize: 1 << 16, useAsync: true))
            {
                await src.CopyToAsync(dst, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "Staged restore from {Backup} -> {Pending} (safety copy at {Safety}).",
                backupPath, pendingPath, safetyPath ?? "(none)");

            return new CatalogRestoreResult
            {
                Success = true,
                PendingPath = pendingPath,
                SafetyBackupPath = safetyPath
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Restore staging failed for {Backup}", backupPath);
            return new CatalogRestoreResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                SafetyBackupPath = safetyPath
            };
        }
    }

    // Applies a previously-staged restore by atomically replacing the live
    // catalog with the staged file. MUST be called BEFORE any database
    // connection is opened (i.e. before EF Core / DbContextFactory is used)
    // so that the live catalog file is never locked when this runs.
    //
    // No-op when no staged file is present. Returns true if a restore was
    // applied; logs and returns false on failure (leaving the live catalog
    // untouched so the app can still start with the previous data).
    public static bool ApplyPendingRestoreIfAny(string dbPath, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(dbPath)) return false;
        var pendingPath = dbPath + PendingRestoreSuffix;
        if (!File.Exists(pendingPath)) return false;

        try
        {
            if (File.Exists(dbPath))
            {
                File.Replace(pendingPath, dbPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(pendingPath, dbPath);
            }
            logger?.LogInformation("Applied pending catalog restore at {DbPath}", dbPath);
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to apply pending restore at {DbPath}", dbPath);
            return false;
        }
    }

    private static IEnumerable<CatalogBackupInfo> EnumerateBackups(string dir)
    {
        foreach (var path in Directory.EnumerateFiles(dir, $"{BackupPrefix}-*{BackupExtension}"))
        {
            FileInfo info;
            try
            {
                info = new FileInfo(path);
            }
            catch
            {
                continue;
            }

            yield return new CatalogBackupInfo
            {
                Path = path,
                FileName = info.Name,
                Bytes = info.Length,
                CreatedAtUtc = info.CreationTimeUtc
            };
        }
    }
}
