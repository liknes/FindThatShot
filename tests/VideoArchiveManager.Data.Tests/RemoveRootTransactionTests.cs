// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Services;
using VideoArchiveManager.Data.Services;
using VideoArchiveManager.Data.Tests.TestSupport;
using Xunit;

namespace VideoArchiveManager.Data.Tests;

// Guardrail: removing a root folder + its video records is a single atomic
// transaction. A failure partway through must leave the catalog exactly as it
// was — never half-deleted (orphaned videos, or a root with no videos).
public class RemoveRootTransactionTests
{
    private static VideoLibraryService CreateLibrary(SqliteTestDatabase db)
    {
        var backup = Substitute.For<ICatalogBackupService>();
        backup.BackupNowAsync(Arg.Any<CancellationToken>())
            .Returns(new CatalogBackupResult { Success = true });
        return new VideoLibraryService(db, Substitute.For<IThumbnailService>(), backup, NullLogger<VideoLibraryService>.Instance);
    }

    [Fact]
    public async Task RemoveRootFolderAsync_atomically_removes_root_and_its_videos()
    {
        using var db = new SqliteTestDatabase();
        int rootId;
        using (var ctx = db.CreateDbContext())
        {
            ctx.RootFolders.Add(new RootFolder { Path = @"C:\Media" });
            ctx.SaveChanges();
            rootId = ctx.RootFolders.Single().Id;
            ctx.AddVideo(@"C:\Media\a.mp4");
            ctx.AddVideo(@"C:\Media\sub\b.mp4");
            ctx.AddVideo(@"C:\Other\c.mp4"); // outside the root
        }

        var removed = await CreateLibrary(db).RemoveRootFolderAsync(rootId, @"C:\Media");

        removed.Should().Be(2);
        using (var ctx = db.CreateDbContext())
        {
            ctx.RootFolders.Count().Should().Be(0, "the root folder row is removed");
            ctx.VideoItems.Select(v => v.FilePath).Should().ContainSingle()
                .Which.Should().Be(@"C:\Other\c.mp4", "only clips under the root are forgotten");
        }
    }

    [Fact]
    public async Task RemoveRootFolderAsync_rolls_back_everything_when_the_second_step_fails()
    {
        var interceptor = new ThrowOnRootFolderDeleteInterceptor();
        using var db = new SqliteTestDatabase(new[] { interceptor });

        int rootId;
        using (var ctx = db.CreateDbContext())
        {
            ctx.RootFolders.Add(new RootFolder { Path = @"C:\Media" });
            ctx.SaveChanges();
            rootId = ctx.RootFolders.Single().Id;
            ctx.AddVideo(@"C:\Media\a.mp4");
            ctx.AddVideo(@"C:\Media\b.mp4");
        }

        var act = async () => await CreateLibrary(db).RemoveRootFolderAsync(rootId, @"C:\Media");

        await act.Should().ThrowAsync<ThrowOnRootFolderDeleteInterceptor.SimulatedFailureException>();

        // The video ExecuteDelete ran inside the transaction; the failure on the
        // root-folder step must have rolled it back, so NOTHING is lost.
        using (var ctx = db.CreateDbContext())
        {
            ctx.VideoItems.Count().Should().Be(2, "video deletions are rolled back with the failed transaction");
            ctx.RootFolders.Count().Should().Be(1, "the root folder row survives the rollback");
        }
    }
}
