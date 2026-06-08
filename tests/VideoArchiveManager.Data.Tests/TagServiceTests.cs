// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
using FluentAssertions;
using VideoArchiveManager.Core.Models.Enums;
using VideoArchiveManager.Data.Services;
using VideoArchiveManager.Data.Tests.TestSupport;
using Xunit;

namespace VideoArchiveManager.Data.Tests;

public class TagServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();
    private readonly TagService _service;

    public TagServiceTests() => _service = new TagService(_db);

    [Fact]
    public async Task GetOrCreateAsync_is_idempotent_per_name_and_type()
    {
        var a = await _service.GetOrCreateAsync("birds", TagType.Subject);
        var b = await _service.GetOrCreateAsync(" birds ", TagType.Subject); // trimmed, same

        b.Id.Should().Be(a.Id);
        using var ctx = _db.CreateDbContext();
        ctx.Tags.Count().Should().Be(1);
    }

    [Fact]
    public async Task AttachAsync_is_idempotent_and_DetachAsync_removes_only_that_join()
    {
        int videoId, tagA, tagB;
        using (var ctx = _db.CreateDbContext())
        {
            videoId = ctx.AddVideo("/m/a.mp4").Id;
            tagA = ctx.GetOrAddTag("birds").Id;
            tagB = ctx.GetOrAddTag("ocean", TagType.Location).Id;
        }

        await _service.AttachAsync(videoId, tagA);
        await _service.AttachAsync(videoId, tagA); // duplicate attach is a no-op
        await _service.AttachAsync(videoId, tagB);

        using (var ctx = _db.CreateDbContext())
            ctx.VideoTags.Count(vt => vt.VideoItemId == videoId).Should().Be(2);

        await _service.DetachAsync(videoId, tagA);

        using (var ctx = _db.CreateDbContext())
        {
            ctx.VideoTags.Count(vt => vt.VideoItemId == videoId).Should().Be(1);
            ctx.VideoTags.Single(vt => vt.VideoItemId == videoId).TagId.Should().Be(tagB);
            ctx.Tags.Count().Should().Be(2, "detaching a tag from a clip never deletes the tag itself");
        }
    }

    [Fact]
    public async Task BulkAttachAsync_attaches_to_all_without_duplicating_existing()
    {
        int v1, v2, v3, tagId;
        using (var ctx = _db.CreateDbContext())
        {
            v1 = ctx.AddVideo("/m/1.mp4").Id;
            v2 = ctx.AddVideo("/m/2.mp4").Id;
            v3 = ctx.AddVideo("/m/3.mp4").Id;
            tagId = ctx.GetOrAddTag("trip").Id;
            ctx.AttachTag(v1, tagId); // already attached
        }

        await _service.BulkAttachAsync(new[] { v1, v2, v3 }, tagId);

        using var ctx2 = _db.CreateDbContext();
        ctx2.VideoTags.Count(vt => vt.TagId == tagId).Should().Be(3, "no duplicate join for the already-attached clip");
    }

    public void Dispose() => _db.Dispose();
}
