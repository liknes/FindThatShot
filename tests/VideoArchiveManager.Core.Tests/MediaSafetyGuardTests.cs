// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
using FluentAssertions;
using VideoArchiveManager.Core.Services;
using VideoArchiveManager.Core.Tests.TestSupport;
using Xunit;

namespace VideoArchiveManager.Core.Tests;

// P0 guardrail: the central refuse-to-delete check that protects original media.
public class MediaSafetyGuardTests
{
    [Theory]
    [InlineData("clip.mp4")]
    [InlineData("clip.MP4")]
    [InlineData("a.mov")]
    [InlineData("b.mxf")]
    [InlineData("c.mkv")]
    [InlineData("d.avi")]
    [InlineData("companion.srt")]
    [InlineData("meta.findthatshot.json")]
    public void IsProtectedMedia_flags_media_and_sidecar_extensions(string name)
    {
        MediaSafetyGuard.IsProtectedMedia(name).Should().BeTrue();
    }

    [Theory]
    [InlineData("123.jpg")]
    [InlineData("thumb.png")]
    [InlineData("notes.txt")]
    [InlineData("")]
    public void IsProtectedMedia_allows_non_media(string name)
    {
        MediaSafetyGuard.IsProtectedMedia(name).Should().BeFalse();
    }

    [Fact]
    public void EnsureSafeToDelete_throws_for_media_file_even_inside_cache()
    {
        using var cache = new TempDirectory();
        // A video file that somehow ended up inside the cache dir must STILL be
        // refused: the extension check is independent of location.
        var media = cache.CreateFile("oops.mp4");

        var act = () => MediaSafetyGuard.EnsureSafeToDelete(media, cache.Path);

        act.Should().Throw<MediaSafetyException>();
    }

    [Fact]
    public void EnsureSafeToDelete_throws_for_path_outside_cache()
    {
        using var cache = new TempDirectory();
        using var elsewhere = new TempDirectory();
        var outside = elsewhere.CreateFile("123.jpg");

        var act = () => MediaSafetyGuard.EnsureSafeToDelete(outside, cache.Path);

        act.Should().Throw<MediaSafetyException>();
    }

    [Fact]
    public void EnsureSafeToDelete_allows_cache_jpg()
    {
        using var cache = new TempDirectory();
        var thumb = cache.CreateFile("42.jpg");

        var act = () => MediaSafetyGuard.EnsureSafeToDelete(thumb, cache.Path);

        act.Should().NotThrow();
    }

    [Fact]
    public void IsWithin_rejects_sibling_with_shared_prefix()
    {
        // "C:\Photos2" must not be considered inside "C:\Photos".
        MediaSafetyGuard.IsWithin(@"C:\Photos2\a.jpg", @"C:\Photos").Should().BeFalse();
        MediaSafetyGuard.IsWithin(@"C:\Photos\a.jpg", @"C:\Photos").Should().BeTrue();
    }
}
