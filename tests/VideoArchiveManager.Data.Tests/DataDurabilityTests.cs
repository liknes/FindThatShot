// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Models.Enums;
using VideoArchiveManager.Core.Services;
using VideoArchiveManager.Core.Services.Ai;
using VideoArchiveManager.Data.Services;
using VideoArchiveManager.Data.Tests.TestSupport;
using Xunit;

namespace VideoArchiveManager.Data.Tests;

// P0 data-durability: user curation (tags, moments, notes) must only ever be
// destroyed by an explicit, scoped action — and the shared Tag vocabulary must
// survive clip deletion so other clips keep their tags.
public class DataDurabilityTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    private VideoLibraryService CreateLibrary()
    {
        var thumbnails = Substitute.For<IThumbnailService>();
        var backup = Substitute.For<ICatalogBackupService>();
        backup.BackupNowAsync(Arg.Any<CancellationToken>())
            .Returns(new CatalogBackupResult { Success = true });
        return new VideoLibraryService(_db, thumbnails, backup, NullLogger<VideoLibraryService>.Instance);
    }

    [Fact]
    public async Task Deleting_a_clip_cascades_its_join_rows_and_moments_but_keeps_the_tag_vocabulary()
    {
        int videoId, otherId, sharedTagId;
        using (var ctx = _db.CreateDbContext())
        {
            var v = ctx.AddVideo("/m/a.mp4");
            var other = ctx.AddVideo("/m/b.mp4");
            var birds = ctx.GetOrAddTag("birds");
            var ocean = ctx.GetOrAddTag("ocean", TagType.Location);

            ctx.AttachTag(v.Id, birds.Id);
            ctx.AttachTag(v.Id, ocean.Id, background: true);
            ctx.AttachTag(other.Id, birds.Id); // shared tag on a surviving clip

            var moment = ctx.AddMoment(v.Id, label: "gull dives");
            ctx.MomentTags.Add(new MomentTag { VideoMomentId = moment.Id, TagId = birds.Id });
            ctx.SaveChanges();

            videoId = v.Id;
            otherId = other.Id;
            sharedTagId = birds.Id;
        }

        var removed = await CreateLibrary().RemoveByIdsAsync(new[] { videoId });
        removed.Should().Be(1);

        using (var ctx = _db.CreateDbContext())
        {
            // The deleted clip's curation is gone...
            ctx.VideoTags.Count(vt => vt.VideoItemId == videoId).Should().Be(0);
            ctx.VideoMoments.Count(m => m.VideoItemId == videoId).Should().Be(0);
            ctx.MomentTags.Count().Should().Be(0, "the moment's tag join cascaded with the moment");

            // ...but the global Tag rows are never deleted...
            ctx.Tags.Count().Should().Be(2, "the tag vocabulary outlives any clip");

            // ...and the surviving clip keeps its tag.
            ctx.VideoTags.Count(vt => vt.VideoItemId == otherId && vt.TagId == sharedTagId)
                .Should().Be(1);
        }
    }

    [Fact]
    public async Task Removing_every_clip_still_leaves_the_tag_vocabulary_intact()
    {
        using (var ctx = _db.CreateDbContext())
        {
            var v = ctx.AddVideo("/m/only.mp4");
            var tag = ctx.GetOrAddTag("sunset", TagType.Mood);
            ctx.AttachTag(v.Id, tag.Id);
        }

        await CreateLibrary().RemoveOfflineAsync(); // none offline -> no-op
        var ids = new List<int>();
        using (var ctx = _db.CreateDbContext())
            ids.AddRange(ctx.VideoItems.Select(v => v.Id));

        await CreateLibrary().RemoveByIdsAsync(ids);

        using (var ctx = _db.CreateDbContext())
        {
            ctx.VideoItems.Count().Should().Be(0);
            ctx.Tags.Count().Should().Be(1, "no code path deletes Tag rows");
        }
    }

    [Fact]
    public async Task ClearAllAiData_deletes_only_AI_tables_and_preserves_user_tags_and_moments()
    {
        int videoId;
        using (var ctx = _db.CreateDbContext())
        {
            var v = ctx.AddVideo("/m/a.mp4", notes: "keeper");
            var tag = ctx.GetOrAddTag("birds");
            ctx.AttachTag(v.Id, tag.Id);
            ctx.AddMoment(v.Id, label: "moment");
            videoId = v.Id;

            // Seed AI-only data: embeddings + a pending suggestion.
            ctx.AiClipEmbeddings.Add(new AiClipEmbedding
            {
                VideoItemId = v.Id, Vector = new byte[] { 1, 2, 3, 4 }, Dim = 1, FrameCount = 1, ModelId = "test"
            });
            ctx.AiFrameEmbeddings.Add(new AiFrameEmbedding
            {
                VideoItemId = v.Id, TimeSeconds = 1, Vector = new byte[] { 1, 2, 3, 4 }, Dim = 1, ModelId = "test"
            });
            ctx.AiTagSuggestions.Add(new AiTagSuggestion
            {
                VideoItemId = v.Id, TagName = "boat", Confidence = 0.3, State = AiSuggestionState.Pending
            });
            ctx.SaveChanges();
        }

        var service = CreateAiTaggingService();
        var deleted = await service.ClearAllAiDataAsync();

        deleted.Should().Be(3, "one clip embedding + one frame embedding + one suggestion");

        using (var ctx = _db.CreateDbContext())
        {
            ctx.AiClipEmbeddings.Count().Should().Be(0);
            ctx.AiFrameEmbeddings.Count().Should().Be(0);
            ctx.AiTagSuggestions.Count().Should().Be(0);

            // User curation is untouched.
            ctx.VideoItems.Single(v => v.Id == videoId).Notes.Should().Be("keeper");
            ctx.VideoTags.Count(vt => vt.VideoItemId == videoId).Should().Be(1);
            ctx.VideoMoments.Count(m => m.VideoItemId == videoId).Should().Be(1);
            ctx.Tags.Count().Should().Be(1);
        }
    }

    private AiTaggingService CreateAiTaggingService()
    {
        var semanticSearch = Substitute.For<IAiSemanticSearchService>();
        return new AiTaggingService(
            _db,
            Substitute.For<IAiModelProvider>(),
            Substitute.For<IFrameSampler>(),
            Substitute.For<IFileSystemService>(),
            new FakeSettingsStore(),
            semanticSearch,
            NullLogger<AiTaggingService>.Instance);
    }

    public void Dispose() => _db.Dispose();
}
