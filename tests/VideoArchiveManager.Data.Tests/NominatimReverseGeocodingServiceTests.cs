// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
using FluentAssertions;
using VideoArchiveManager.Data.Services;
using VideoArchiveManager.Data.Tests.TestSupport;
using Xunit;

namespace VideoArchiveManager.Data.Tests;

public class NominatimReverseGeocodingServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    private NominatimReverseGeocodingService Create(StubHttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://nominatim.example/") };
        return new NominatimReverseGeocodingService(() => http, _db);
    }

    [Fact]
    public async Task LookupAsync_maps_the_most_specific_place_name_and_caches_it()
    {
        const string json = """
        {
          "display_name": "Reine, Moskenes, Nordland, Norway",
          "name": "Reine",
          "address": { "village": "Reine", "county": "Nordland", "country": "Norway" }
        }
        """;
        var handler = new StubHttpMessageHandler(json);
        var service = Create(handler);

        var first = await service.LookupAsync(67.93, 13.09);

        first.Should().NotBeNull();
        first!.LocationShort.Should().Be("Reine");
        first.Country.Should().Be("Norway");
        first.Region.Should().Be("Nordland");
        handler.RequestCount.Should().Be(1);

        // Second lookup for the same (rounded) coordinate is served from the DB
        // cache without another network call.
        var second = await service.LookupAsync(67.93, 13.09);
        second!.LocationShort.Should().Be("Reine");
        handler.RequestCount.Should().Be(1, "the cached entry must short-circuit the network call");

        using var ctx = _db.CreateDbContext();
        ctx.GeocodeCacheEntries.Count().Should().Be(1);
    }

    public void Dispose() => _db.Dispose();
}
