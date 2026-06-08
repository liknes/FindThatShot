// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveManager.Core.Configuration;
using VideoArchiveManager.Core.Services;
using VideoArchiveManager.Core.Tests.TestSupport;
using Xunit;

namespace VideoArchiveManager.Core.Tests;

// P0 data-durability: backups are the safety net for the catalog. These verify
// that a backup is a faithful copy, that restore is staged (not destructive) and
// snapshots the current catalog first, and that the staged restore is applied
// at startup — so a mistaken removal is always recoverable.
public class CatalogBackupServiceTests
{
    private static (CatalogBackupService Service, string DbPath, string BackupDir, TempDirectory Root)
        Create()
    {
        var root = new TempDirectory();
        var dbPath = root.Combine("catalog.db");
        var backupDir = root.Combine("Backups");
        var settings = new FakeSettingsStore(new AppSettings
        {
            DatabasePath = dbPath,
            BackupDirectory = backupDir,
            BackupRetentionCount = 7,
        });
        var service = new CatalogBackupService(settings, NullLogger<CatalogBackupService>.Instance);
        return (service, dbPath, backupDir, root);
    }

    [Fact]
    public async Task BackupNowAsync_creates_a_faithful_copy()
    {
        var (service, dbPath, backupDir, root) = Create();
        using var _ = root;
        File.WriteAllText(dbPath, "catalog-v1");

        var result = await service.BackupNowAsync();

        result.Success.Should().BeTrue();
        result.BackupPath.Should().NotBeNull();
        File.ReadAllText(result.BackupPath!).Should().Be("catalog-v1");
        Directory.GetParent(result.BackupPath!)!.FullName
            .Should().Be(Path.GetFullPath(backupDir));
    }

    [Fact]
    public async Task BackupNowAsync_fails_cleanly_when_no_catalog_exists()
    {
        var (service, _, _, root) = Create();
        using var _ = root;

        var result = await service.BackupNowAsync();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RestoreAsync_stages_pending_and_snapshots_current_catalog_without_destroying_it()
    {
        var (service, dbPath, _, root) = Create();
        using var _ = root;
        File.WriteAllText(dbPath, "live-catalog");

        var backupPath = root.Combine("Backups", "external-backup.db");
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.WriteAllText(backupPath, "older-catalog");

        var result = await service.RestoreAsync(backupPath);

        result.Success.Should().BeTrue();

        // Live catalog is untouched until the next startup applies the swap.
        File.ReadAllText(dbPath).Should().Be("live-catalog");

        // The restore is staged beside the catalog.
        result.PendingPath.Should().Be(dbPath + CatalogBackupService.PendingRestoreSuffix);
        File.ReadAllText(result.PendingPath!).Should().Be("older-catalog");

        // A pre-restore safety snapshot of the live catalog was captured so the
        // user can roll back even after the restart.
        result.SafetyBackupPath.Should().NotBeNull();
        File.ReadAllText(result.SafetyBackupPath!).Should().Be("live-catalog");
    }

    [Fact]
    public async Task Backup_then_restore_round_trip_recovers_the_earlier_catalog()
    {
        var (service, dbPath, _, root) = Create();
        using var _ = root;

        // v1 -> back up
        File.WriteAllText(dbPath, "curation-v1");
        var backup = await service.BackupNowAsync();
        backup.Success.Should().BeTrue();

        // User keeps working -> v2 (imagine an accidental mass-delete here)
        File.WriteAllText(dbPath, "curation-v2-DAMAGED");

        // Restore the v1 backup and apply it the way startup does.
        var restore = await service.RestoreAsync(backup.BackupPath!);
        restore.Success.Should().BeTrue();

        var applied = CatalogBackupService.ApplyPendingRestoreIfAny(dbPath);

        applied.Should().BeTrue();
        File.ReadAllText(dbPath).Should().Be("curation-v1", "the earlier catalog is fully recovered");
        File.Exists(dbPath + CatalogBackupService.PendingRestoreSuffix).Should().BeFalse();

        // The damaged state was preserved as a pre-restore safety copy.
        File.ReadAllText(restore.SafetyBackupPath!).Should().Be("curation-v2-DAMAGED");
    }

    [Fact]
    public void ApplyPendingRestoreIfAny_is_a_noop_when_nothing_is_staged()
    {
        var (_, dbPath, _, root) = Create();
        using var _ = root;
        File.WriteAllText(dbPath, "live", Encoding.UTF8);

        var applied = CatalogBackupService.ApplyPendingRestoreIfAny(dbPath);

        applied.Should().BeFalse();
        File.ReadAllText(dbPath).Should().Be("live");
    }
}
