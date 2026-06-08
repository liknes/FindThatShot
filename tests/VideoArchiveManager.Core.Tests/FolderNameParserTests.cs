// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
using FluentAssertions;
using VideoArchiveManager.Core.Helpers;
using Xunit;

namespace VideoArchiveManager.Core.Tests;

public class FolderNameParserTests
{
    [Fact]
    public void Parses_date_location_and_context()
    {
        var parsed = FolderNameParser.Parse("2026-05-27 - Lofoten - drone reveal");

        parsed.FolderDate.Should().Be(new DateTime(2026, 5, 27));
        parsed.LocationText.Should().Be("Lofoten");
        parsed.ContextText.Should().Be("drone reveal");
    }

    [Fact]
    public void Joins_multiple_context_segments()
    {
        var parsed = FolderNameParser.Parse("2026-05-27 - Lofoten - sunset - timelapse");

        parsed.LocationText.Should().Be("Lofoten");
        parsed.ContextText.Should().Be("sunset - timelapse");
    }

    [Fact]
    public void Date_only_folder_yields_just_a_date()
    {
        var parsed = FolderNameParser.Parse("2026-05-27");

        parsed.FolderDate.Should().Be(new DateTime(2026, 5, 27));
        parsed.LocationText.Should().BeNull();
        parsed.ContextText.Should().BeNull();
    }

    [Fact]
    public void Single_segment_with_date_is_treated_as_location()
    {
        var parsed = FolderNameParser.Parse("2026-05-27 - Lofoten");

        parsed.LocationText.Should().Be("Lofoten");
        parsed.ContextText.Should().BeNull();
    }

    [Fact]
    public void Single_segment_without_date_is_treated_as_context()
    {
        var parsed = FolderNameParser.Parse("RandomProjectFolder");

        parsed.FolderDate.Should().BeNull();
        parsed.LocationText.Should().BeNull();
        parsed.ContextText.Should().Be("RandomProjectFolder");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_input_yields_empty_result(string input)
    {
        var parsed = FolderNameParser.Parse(input);

        parsed.FolderDate.Should().BeNull();
        parsed.LocationText.Should().BeNull();
        parsed.ContextText.Should().BeNull();
    }
}
