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

// P0 media-immutability: ffprobe is read-only metadata extraction. We can't
// run a real ffprobe in CI, but we can pin the contract that a missing/blank
// path is handled without touching disk (and never throws).
public class FfprobeServiceTests
{
    private static FfprobeService Create()
        => new(new FakeSettingsStore(), NullLogger<FfprobeService>.Instance);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProbeAsync_returns_null_for_blank_path(string path)
    {
        (await Create().ProbeAsync(path)).Should().BeNull();
    }

    [Fact]
    public async Task ProbeAsync_returns_null_for_missing_file_and_leaves_folder_untouched()
    {
        using var media = new TempDirectory();
        var before = MediaSnapshot.Capture(media.Path);

        var result = await Create().ProbeAsync(media.Combine("missing.mp4"));

        result.Should().BeNull();
        before.DiffAgainstCurrent(media.Path).Should().BeEmpty();
    }
}
