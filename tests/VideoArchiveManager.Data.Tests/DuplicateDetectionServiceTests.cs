// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
using FluentAssertions;
using VideoArchiveManager.Data.Services;
using VideoArchiveManager.Data.Tests.TestSupport;
using Xunit;

namespace VideoArchiveManager.Data.Tests;

public class DuplicateDetectionServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();
    private readonly DuplicateDetectionService _service;

    public DuplicateDetectionServiceTests() => _service = new DuplicateDetectionService(_db);

    [Fact]
    public async Task Groups_clips_with_identical_size_duration_and_resolution()
    {
        using (var ctx = _db.CreateDbContext())
        {
            ctx.AddVideo("/a/clip.mp4", fileSize: 1_000_000, durationSeconds: 45.2, width: 1920, height: 1080);
            ctx.AddVideo("/b/clip.mp4", fileSize: 1_000_000, durationSeconds: 45.4, width: 1920, height: 1080); // rounds to 45
            ctx.AddVideo("/c/other.mp4", fileSize: 2_000_000, durationSeconds: 45.2, width: 1920, height: 1080); // different size
        }

        var groups = await _service.FindDuplicatesAsync();

        groups.Should().ContainSingle();
        groups[0].Videos.Should().HaveCount(2);
        groups[0].Videos.Count(v => v.IsSuggestedKeep).Should().Be(1, "exactly one member is flagged to keep");
    }

    [Fact]
    public async Task Suggested_keep_prefers_online_and_better_curated_copy()
    {
        using (var ctx = _db.CreateDbContext())
        {
            var offline = ctx.AddVideo("/x/a.mp4", fileSize: 500, durationSeconds: 10, width: 1280, height: 720, fileExists: false, rating: 5);
            var online = ctx.AddVideo("/y/a.mp4", fileSize: 500, durationSeconds: 10, width: 1280, height: 720, fileExists: true, rating: 3);
        }

        var groups = await _service.FindDuplicatesAsync();

        var keep = groups.Single().Videos.Single(v => v.IsSuggestedKeep);
        keep.FileExists.Should().BeTrue("online copies are preferred over offline ones");
    }

    [Fact]
    public async Task No_duplicates_returns_empty()
    {
        using (var ctx = _db.CreateDbContext())
        {
            ctx.AddVideo("/a.mp4", fileSize: 1, durationSeconds: 1, width: 10, height: 10);
            ctx.AddVideo("/b.mp4", fileSize: 2, durationSeconds: 2, width: 20, height: 20);
        }

        (await _service.FindDuplicatesAsync()).Should().BeEmpty();
    }

    public void Dispose() => _db.Dispose();
}
