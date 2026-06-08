// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
using FluentAssertions;
using VideoArchiveManager.Core.Services;
using VideoArchiveManager.Data.Services;
using VideoArchiveManager.Data.Tests.TestSupport;
using Xunit;

namespace VideoArchiveManager.Data.Tests;

public class SavedSearchServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();
    private readonly SavedSearchService _service;

    public SavedSearchServiceTests() => _service = new SavedSearchService(_db);

    [Fact]
    public async Task SaveAsync_creates_then_upserts_by_name()
    {
        await _service.SaveAsync("Best aerials", new SavedSearchCriteria { MinRating = 4 });
        await _service.SaveAsync("best aerials", new SavedSearchCriteria { MinRating = 5 }); // case-insensitive upsert

        var all = await _service.GetAllAsync();
        all.Should().ContainSingle("a re-save under the same name updates rather than duplicates");

        var restored = SavedSearchCriteria.Deserialize(all[0].CriteriaJson);
        restored.MinRating.Should().Be(5);
    }

    [Fact]
    public async Task RenameAsync_changes_the_name()
    {
        var saved = await _service.SaveAsync("old", new SavedSearchCriteria());
        await _service.RenameAsync(saved.Id, "new");

        var all = await _service.GetAllAsync();
        all.Single().Name.Should().Be("new");
    }

    [Fact]
    public async Task DeleteAsync_removes_the_saved_search()
    {
        var saved = await _service.SaveAsync("temp", new SavedSearchCriteria());
        await _service.DeleteAsync(saved.Id);

        (await _service.GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_rejects_a_blank_name()
    {
        var act = async () => await _service.SaveAsync("   ", new SavedSearchCriteria());
        await act.Should().ThrowAsync<ArgumentException>();
    }

    public void Dispose() => _db.Dispose();
}
