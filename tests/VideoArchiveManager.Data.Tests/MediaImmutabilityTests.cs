// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VideoArchiveManager.Core.Services;
using VideoArchiveManager.Data.Services;
using VideoArchiveManager.Data.Tests.TestSupport;
using Xunit;

namespace VideoArchiveManager.Data.Tests;

// P0 media-immutability: removing entries from the catalog must NEVER delete,
// move, or alter the underlying source video files on disk.
public class MediaImmutabilityTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();
    private readonly string _mediaRoot;

    public MediaImmutabilityTests()
    {
        _mediaRoot = Path.Combine(Path.GetTempPath(), "fts-media-immutability", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_mediaRoot);
    }

    private string CreateMediaFile(string relative)
    {
        relative = relative.Replace('/', Path.DirectorySeparatorChar);
        var full = Path.Combine(_mediaRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var rng = new Random(relative.GetHashCode());
        var bytes = new byte[2048];
        rng.NextBytes(bytes);
        File.WriteAllBytes(full, bytes);
        return full;
    }

    private static Dictionary<string, string> HashTree(string root)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            map[path] = Convert.ToHexString(sha.ComputeHash(stream));
        }
        return map;
    }

    private VideoLibraryService CreateLibrary(out IThumbnailService thumbnails)
    {
        thumbnails = Substitute.For<IThumbnailService>();
        var backup = Substitute.For<ICatalogBackupService>();
        backup.BackupNowAsync(Arg.Any<CancellationToken>())
            .Returns(new CatalogBackupResult { Success = true });
        return new VideoLibraryService(_db, thumbnails, backup, NullLogger<VideoLibraryService>.Instance);
    }

    [Fact]
    public async Task RemoveByIdsAsync_removes_catalog_rows_but_never_the_source_files()
    {
        var f1 = CreateMediaFile("trip/a.mp4");
        var f2 = CreateMediaFile("trip/b.mov");
        var companion = CreateMediaFile("trip/b.SRT");

        int id1, id2;
        using (var ctx = _db.CreateDbContext())
        {
            id1 = ctx.AddVideo(f1).Id;
            id2 = ctx.AddVideo(f2).Id;
        }

        var before = HashTree(_mediaRoot);

        var library = CreateLibrary(out _);
        var removed = await library.RemoveByIdsAsync(new[] { id1, id2 });

        removed.Should().Be(2);

        using (var ctx = _db.CreateDbContext())
        {
            ctx.VideoItems.Count().Should().Be(0, "catalog rows are forgotten");
        }

        // Every source file (including the .SRT companion) is byte-for-byte intact.
        HashTree(_mediaRoot).Should().BeEquivalentTo(before);
        File.Exists(f1).Should().BeTrue();
        File.Exists(f2).Should().BeTrue();
        File.Exists(companion).Should().BeTrue();
    }

    [Fact]
    public async Task RemoveUnderRootAsync_only_forgets_rows_under_that_root_and_touches_no_files()
    {
        var underRoot = CreateMediaFile("keep/clip.mp4");
        var elsewhere = CreateMediaFile("other/clip.mp4");

        using (var ctx = _db.CreateDbContext())
        {
            ctx.AddVideo(underRoot);
            ctx.AddVideo(elsewhere);
        }

        var before = HashTree(_mediaRoot);

        var library = CreateLibrary(out _);
        var removed = await library.RemoveUnderRootAsync(Path.Combine(_mediaRoot, "keep"));

        removed.Should().Be(1);
        using (var ctx = _db.CreateDbContext())
        {
            ctx.VideoItems.Select(v => v.FilePath).Should().ContainSingle()
                .Which.Should().Be(elsewhere);
        }

        HashTree(_mediaRoot).Should().BeEquivalentTo(before, "no source file is ever deleted");
    }

    public void Dispose()
    {
        _db.Dispose();
        try { if (Directory.Exists(_mediaRoot)) Directory.Delete(_mediaRoot, true); } catch { }
    }
}
