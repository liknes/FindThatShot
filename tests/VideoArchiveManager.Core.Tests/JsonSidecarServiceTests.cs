// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveManager.Core.Configuration;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Models.Enums;
using VideoArchiveManager.Core.Services;
using VideoArchiveManager.Core.Tests.TestSupport;
using Xunit;

namespace VideoArchiveManager.Core.Tests;

// P0 media-immutability: the sidecar is the ONLY thing the app deliberately
// writes into a media folder, and even then only an auxiliary ".findthatshot.json"
// file. These tests pin that contract: default-off writes nothing, on-writes
// only the sidecar, and the source video bytes never change either way.
public class JsonSidecarServiceTests
{
    private static JsonSidecarService Create(bool writeSidecars, out FakeSettingsStore settings)
    {
        settings = new FakeSettingsStore(new AppSettings { WriteSidecarFiles = writeSidecars });
        return new JsonSidecarService(settings, NullLogger<JsonSidecarService>.Instance);
    }

    private static (VideoItem Video, VideoTag[] Tags) MakeClip(string filePath)
    {
        var video = new VideoItem
        {
            Id = 1,
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            Rating = 4,
            Status = VideoStatus.Keep,
            Notes = "hero shot",
        };
        var tags = new[]
        {
            new VideoTag { Tag = new Tag { Name = "birds", Type = TagType.Subject }, IsBackground = false },
            new VideoTag { Tag = new Tag { Name = "ocean", Type = TagType.Location }, IsBackground = true },
        };
        return (video, tags);
    }

    [Fact]
    public async Task When_disabled_and_no_existing_sidecar_writes_nothing_and_leaves_media_untouched()
    {
        using var media = new TempDirectory();
        var clipPath = media.CreateFile("trip/hero.mp4");
        var before = MediaSnapshot.Capture(media.Path);

        var service = Create(writeSidecars: false, out _);
        var (video, tags) = MakeClip(clipPath);

        var result = await service.WriteAsync(video, tags);

        result.Skipped.Should().BeTrue();
        File.Exists(clipPath + JsonSidecarService.SidecarSuffix).Should().BeFalse();
        before.DiffAgainstCurrent(media.Path).Should().BeEmpty("no file should appear and the video must be untouched");
    }

    [Fact]
    public async Task When_enabled_writes_only_the_sidecar_and_leaves_the_video_bytes_unchanged()
    {
        using var media = new TempDirectory();
        var clipPath = media.CreateFile("trip/hero.mp4");

        // Fingerprint just the video file up front.
        var videoBefore = MediaSnapshot.Capture(media.Path);

        var service = Create(writeSidecars: true, out _);
        var (video, tags) = MakeClip(clipPath);

        var result = await service.WriteAsync(video, tags);

        result.Written.Should().BeTrue();

        var sidecarPath = clipPath + JsonSidecarService.SidecarSuffix;
        File.Exists(sidecarPath).Should().BeTrue();

        // The temp file used for the atomic write must be cleaned up.
        File.Exists(sidecarPath + ".tmp").Should().BeFalse();

        // The ONLY change in the media folder is the new sidecar; the video is
        // byte-for-byte identical (no CONTENT/LENGTH/MTIME/REMOVED diffs).
        var diffs = videoBefore.DiffAgainstCurrent(media.Path);
        diffs.Should().ContainSingle()
            .Which.Should().StartWith("ADDED:").And.EndWith(JsonSidecarService.SidecarSuffix);

        // And the sidecar round-trips the user's curation.
        var read = await service.TryReadAsync(clipPath);
        read.Should().NotBeNull();
        read!.Rating.Should().Be(4);
        read.Notes.Should().Be("hero shot");
        read.Tags.Select(t => t.Name).Should().BeEquivalentTo(new[] { "birds", "ocean" });
    }

    [Fact]
    public async Task When_disabled_but_a_sidecar_already_exists_it_is_kept_in_sync()
    {
        using var media = new TempDirectory();
        var clipPath = media.CreateFile("trip/hero.mp4");
        var sidecarPath = clipPath + JsonSidecarService.SidecarSuffix;

        // Pre-existing sidecar = the user's opt-in for this clip.
        var enabled = Create(writeSidecars: true, out _);
        var (video, tags) = MakeClip(clipPath);
        (await enabled.WriteAsync(video, tags)).Written.Should().BeTrue();

        // Now writing is globally disabled, but the existing file must still
        // track catalog edits rather than going stale.
        var disabled = Create(writeSidecars: false, out _);
        video.Rating = 1;
        var result = await disabled.WriteAsync(video, Array.Empty<VideoTag>());

        result.Written.Should().BeTrue();
        var read = await disabled.TryReadAsync(clipPath);
        read!.Rating.Should().Be(1);
        read.Tags.Should().BeEmpty();
    }
}
