// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VideoArchiveManager.Core.Configuration;
using VideoArchiveManager.Core.Services;
using VideoArchiveManager.Data.Services;
using VideoArchiveManager.Data.Tests.TestSupport;
using Xunit;

namespace VideoArchiveManager.Data.Tests;

// Guardrail: every bulk catalog-removal takes a safety backup first, so a
// mistaken mass-delete is always recoverable from a snapshot at most one
// destructive action old.
public class VideoLibrarySafetyBackupTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();
    private readonly string _backupDir;
    private readonly CatalogBackupService _backup;

    public VideoLibrarySafetyBackupTests()
    {
        _backupDir = Path.Combine(_db.RootDirectory, "Backups");
        var settings = new FakeSettingsStore(new AppSettings
        {
            DatabasePath = _db.DatabasePath,
            BackupDirectory = _backupDir,
        });
        _backup = new CatalogBackupService(settings, NullLogger<CatalogBackupService>.Instance);
    }

    private VideoLibraryService CreateLibrary()
        => new(_db, Substitute.For<IThumbnailService>(), _backup, NullLogger<VideoLibraryService>.Instance);

    private int BackupCount()
        => Directory.Exists(_backupDir)
            ? Directory.GetFiles(_backupDir, "catalog-*.db").Length
            : 0;

    [Fact]
    public async Task RemoveByIdsAsync_takes_a_safety_backup_first()
    {
        int id;
        using (var ctx = _db.CreateDbContext())
            id = ctx.AddVideo("/m/a.mp4").Id;

        BackupCount().Should().Be(0);

        await CreateLibrary().RemoveByIdsAsync(new[] { id });

        BackupCount().Should().BeGreaterThan(0, "a snapshot must exist before the removal");
    }

    [Fact]
    public async Task RemoveOfflineAsync_takes_a_safety_backup_first()
    {
        using (var ctx = _db.CreateDbContext())
            ctx.AddVideo("/m/gone.mp4", fileExists: false);

        await CreateLibrary().RemoveOfflineAsync();

        BackupCount().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RemoveRootFolderAsync_takes_a_safety_backup_first()
    {
        int rootId;
        using (var ctx = _db.CreateDbContext())
        {
            ctx.RootFolders.Add(new Core.Models.RootFolder { Path = "/m" });
            ctx.SaveChanges();
            rootId = ctx.RootFolders.Single().Id;
            ctx.AddVideo("/m/a.mp4");
        }

        await CreateLibrary().RemoveRootFolderAsync(rootId, "/m");

        BackupCount().Should().BeGreaterThan(0);
    }

    public void Dispose() => _db.Dispose();
}
