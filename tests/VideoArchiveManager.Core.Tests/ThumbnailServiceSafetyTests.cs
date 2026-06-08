// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveManager.Core.Configuration;
using VideoArchiveManager.Core.Services;
using VideoArchiveManager.Core.Tests.TestSupport;
using Xunit;

namespace VideoArchiveManager.Core.Tests;

// P0 media-immutability: the thumbnail service is the ONLY component that
// deletes files on disk, so it gets the strictest scrutiny. Derived artifact
// paths must live under the cache dir, and cache cleanup must never reach a
// source media file.
public class ThumbnailServiceSafetyTests
{
    private static (ThumbnailService Service, FakeSettingsStore Settings) CreateService(string thumbDir)
    {
        var settings = new FakeSettingsStore(new AppSettings { ThumbnailDirectory = thumbDir });
        var service = new ThumbnailService(settings, NullLogger<ThumbnailService>.Instance);
        return (service, settings);
    }

    [Fact]
    public void Thumbnail_paths_all_resolve_inside_the_cache_directory()
    {
        using var cache = new TempDirectory();
        var (service, _) = CreateService(cache.Path);

        MediaSafetyGuard.IsWithin(service.GetThumbnailPath(7), cache.Path).Should().BeTrue();
        MediaSafetyGuard.IsWithin(service.GetMomentThumbnailPath(7), cache.Path).Should().BeTrue();
        MediaSafetyGuard.IsWithin(service.GetAiPreviewThumbnailPath(7), cache.Path).Should().BeTrue();
        MediaSafetyGuard.IsWithin(service.GetScrubDirectory(7), cache.Path).Should().BeTrue();
    }

    [Fact]
    public async Task GenerateAsync_returns_null_for_missing_source_without_touching_disk()
    {
        using var cache = new TempDirectory();
        using var media = new TempDirectory();
        var (service, _) = CreateService(cache.Path);

        var before = MediaSnapshot.Capture(media.Path);
        var result = await service.GenerateAsync(1, media.Combine("does-not-exist.mp4"), 10);

        result.Should().BeNull();
        before.DiffAgainstCurrent(media.Path).Should().BeEmpty();
    }

    [Fact]
    public void DeleteForVideos_removes_cache_jpg_but_never_the_source_media()
    {
        using var cache = new TempDirectory();
        using var media = new TempDirectory();

        var (service, _) = CreateService(cache.Path);

        // Source footage lives in a completely separate tree.
        media.CreateFile("trip/hero.mp4");
        media.CreateFile("trip/hero.mp4.findthatshot.json");
        var mediaBefore = MediaSnapshot.Capture(media.Path);

        // A legitimate cache thumbnail for video id 5.
        var thumb = cache.CreateFile("5.jpg");

        var deleted = service.DeleteForVideos(new[] { 5 });

        deleted.Should().Be(1);
        File.Exists(thumb).Should().BeFalse("the cache thumbnail should be cleaned up");
        mediaBefore.DiffAgainstCurrent(media.Path).Should().BeEmpty("source media must be untouched");
    }

    [Fact]
    public void DeleteForVideos_is_a_noop_when_cache_directory_does_not_exist()
    {
        using var media = new TempDirectory();
        var nonexistentCache = Path.Combine(media.Path, "no-cache-here");
        var (service, _) = CreateService(nonexistentCache);

        var deleted = service.DeleteForVideos(new[] { 1, 2, 3 });

        deleted.Should().Be(0);
    }
}
