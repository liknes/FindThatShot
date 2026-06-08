// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
using FluentAssertions;
using VideoArchiveManager.Core.Models.Enums;
using VideoArchiveManager.Core.Services;
using VideoArchiveManager.Data.Services;
using VideoArchiveManager.Data.Tests.TestSupport;
using Xunit;

namespace VideoArchiveManager.Data.Tests;

public class SearchServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();
    private readonly SearchService _search;

    public SearchServiceTests() => _search = new SearchService(_db);

    [Fact]
    public async Task Text_search_uses_AND_semantics_across_tokens()
    {
        using (var ctx = _db.CreateDbContext())
        {
            ctx.AddVideo("/m/lofoten-sunset.mp4", notes: "lofoten sunset drone");
            ctx.AddVideo("/m/lofoten-rain.mp4", notes: "lofoten rain");
        }

        var result = await _search.SearchAsync(new SearchQuery { Text = "lofoten sunset" });

        result.TotalCount.Should().Be(1);
        result.Items.Single().FileName.Should().Be("lofoten-sunset.mp4");
    }

    [Fact]
    public async Task Filters_by_status_camera_and_min_rating()
    {
        using (var ctx = _db.CreateDbContext())
        {
            ctx.AddVideo("/m/a.mp4", camera: "DJI", rating: 5, status: VideoStatus.Keep);
            ctx.AddVideo("/m/b.mp4", camera: "DJI", rating: 2, status: VideoStatus.Keep);
            ctx.AddVideo("/m/c.mp4", camera: "Sony", rating: 5, status: VideoStatus.Keep);
        }

        var byCamera = await _search.SearchAsync(new SearchQuery { Camera = "DJI" });
        byCamera.TotalCount.Should().Be(2);

        var byRating = await _search.SearchAsync(new SearchQuery { Camera = "DJI", MinRating = 4 });
        byRating.Items.Single().FileName.Should().Be("a.mp4");

        var byStatus = await _search.SearchAsync(new SearchQuery { Status = VideoStatus.Keep });
        byStatus.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task Tag_filter_with_main_subject_only_excludes_background_attachments()
    {
        int primaryClip, backgroundClip, tagId;
        using (var ctx = _db.CreateDbContext())
        {
            var a = ctx.AddVideo("/m/a.mp4");
            var b = ctx.AddVideo("/m/b.mp4");
            var birds = ctx.GetOrAddTag("birds");
            ctx.AttachTag(a.Id, birds.Id, background: false);
            ctx.AttachTag(b.Id, birds.Id, background: true);
            primaryClip = a.Id; backgroundClip = b.Id; tagId = birds.Id;
        }

        var any = await _search.SearchAsync(new SearchQuery { TagIds = new[] { tagId } });
        any.TotalCount.Should().Be(2);

        var mainOnly = await _search.SearchAsync(new SearchQuery { TagIds = new[] { tagId }, MainSubjectOnly = true });
        mainOnly.Items.Select(v => v.Id).Should().BeEquivalentTo(new[] { primaryClip });
        mainOnly.Items.Should().NotContain(v => v.Id == backgroundClip);
    }

    [Fact]
    public async Task Filters_by_root_folder_prefix_without_matching_siblings()
    {
        using (var ctx = _db.CreateDbContext())
        {
            ctx.AddVideo(@"C:\Photos\a.mp4");
            ctx.AddVideo(@"C:\Photos2\b.mp4"); // shared prefix, must NOT match
        }

        var result = await _search.SearchAsync(new SearchQuery { RootFolderPath = @"C:\Photos\" });

        result.Items.Select(v => v.FileName).Should().ContainSingle().Which.Should().Be("a.mp4");
    }

    [Fact]
    public async Task Filters_by_file_availability()
    {
        using (var ctx = _db.CreateDbContext())
        {
            ctx.AddVideo("/m/online.mp4", fileExists: true);
            ctx.AddVideo("/m/offline.mp4", fileExists: false);
        }

        var offline = await _search.SearchAsync(new SearchQuery { FileExists = false });
        offline.Items.Single().FileName.Should().Be("offline.mp4");
    }

    [Fact]
    public async Task Paging_returns_total_count_and_page_window()
    {
        using (var ctx = _db.CreateDbContext())
        {
            for (var i = 0; i < 10; i++)
                ctx.AddVideo($"/m/clip{i:00}.mp4", modifiedAtFile: new DateTime(2026, 1, 1).AddMinutes(i));
        }

        var page = await _search.SearchAsync(new SearchQuery { Skip = 0, Take = 3 });

        page.TotalCount.Should().Be(10, "total reflects the whole match set, not the page");
        page.Items.Should().HaveCount(3);
    }

    public void Dispose() => _db.Dispose();
}
