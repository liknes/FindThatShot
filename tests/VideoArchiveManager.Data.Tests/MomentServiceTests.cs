// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VideoArchiveManager.Core.Services;
using VideoArchiveManager.Data.Services;
using VideoArchiveManager.Data.Tests.TestSupport;
using Xunit;

namespace VideoArchiveManager.Data.Tests;

public class MomentServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();
    private readonly IThumbnailService _thumbnails = Substitute.For<IThumbnailService>();
    private readonly MomentService _service;

    public MomentServiceTests()
    {
        _thumbnails.GenerateAtAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        _service = new MomentService(_db, _thumbnails, NullLogger<MomentService>.Instance);
    }

    [Fact]
    public async Task AddAsync_normalizes_a_reversed_in_out_range()
    {
        int videoId;
        using (var ctx = _db.CreateDbContext())
            videoId = ctx.AddVideo("/m/a.mp4").Id;

        var moment = await _service.AddAsync(videoId, startSeconds: 10, endSeconds: 4, label: "reversed");

        moment.StartSeconds.Should().Be(4);
        moment.EndSeconds.Should().Be(10);
    }

    [Fact]
    public async Task DeleteAsync_removes_the_moment_and_cleans_its_cached_thumbnail()
    {
        int videoId, momentId;
        using (var ctx = _db.CreateDbContext())
        {
            videoId = ctx.AddVideo("/m/a.mp4").Id;
            momentId = ctx.AddMoment(videoId).Id;
        }

        await _service.DeleteAsync(momentId);

        using (var ctx = _db.CreateDbContext())
            ctx.VideoMoments.Count().Should().Be(0);

        _thumbnails.Received(1).DeleteForMoments(Arg.Is<IEnumerable<int>>(ids => ids.Contains(momentId)));
    }

    [Fact]
    public async Task DetachTagAsync_removes_only_that_moment_tag()
    {
        int momentId, tagA, tagB;
        using (var ctx = _db.CreateDbContext())
        {
            var v = ctx.AddVideo("/m/a.mp4");
            var m = ctx.AddMoment(v.Id);
            tagA = ctx.GetOrAddTag("a").Id;
            tagB = ctx.GetOrAddTag("b").Id;
            momentId = m.Id;
        }

        await _service.AttachTagAsync(momentId, tagA);
        await _service.AttachTagAsync(momentId, tagB);
        await _service.DetachTagAsync(momentId, tagA);

        using var ctx2 = _db.CreateDbContext();
        ctx2.MomentTags.Where(mt => mt.VideoMomentId == momentId).Select(mt => mt.TagId)
            .Should().ContainSingle().Which.Should().Be(tagB);
        ctx2.Tags.Count().Should().Be(2, "detaching a tag never deletes the tag vocabulary");
    }

    public void Dispose() => _db.Dispose();
}
