// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VideoArchiveManager.Core.Models.Enums;
using VideoArchiveManager.Data.Tests.TestSupport;
using Xunit;

namespace VideoArchiveManager.Data.Tests;

// P0 data-durability: schema upgrades must be additive. Applying all migrations
// to a fresh DB must succeed, and re-running Migrate over a populated catalog
// must never wipe user data.
public class MigrationsTests
{
    [Fact]
    public void All_migrations_apply_to_a_fresh_database()
    {
        using var db = new SqliteTestDatabase(); // ctor runs Database.Migrate()
        using var ctx = db.CreateDbContext();

        ctx.Database.GetPendingMigrations().Should().BeEmpty("every migration is applied on creation");
        ctx.Database.GetAppliedMigrations().Should().NotBeEmpty();
    }

    [Fact]
    public void Re_running_migrations_preserves_existing_user_data()
    {
        using var db = new SqliteTestDatabase();

        int videoId;
        using (var ctx = db.CreateDbContext())
        {
            var v = ctx.AddVideo("/m/a.mp4", notes: "irreplaceable", rating: 5, status: VideoStatus.Favorite);
            var tag = ctx.GetOrAddTag("birds");
            ctx.AttachTag(v.Id, tag.Id);
            ctx.AddMoment(v.Id, label: "the shot");
            videoId = v.Id;
        }

        // Simulate an app restart applying migrations again over the live catalog.
        using (var ctx = db.CreateDbContext())
            ctx.Database.Migrate();

        using (var ctx = db.CreateDbContext())
        {
            var v = ctx.VideoItems.Single(x => x.Id == videoId);
            v.Notes.Should().Be("irreplaceable");
            v.Rating.Should().Be(5);
            ctx.VideoTags.Count(vt => vt.VideoItemId == videoId).Should().Be(1);
            ctx.VideoMoments.Count(m => m.VideoItemId == videoId).Should().Be(1);
            ctx.Tags.Count().Should().Be(1);
        }
    }
}
