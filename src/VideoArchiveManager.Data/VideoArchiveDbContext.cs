// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.
using Microsoft.EntityFrameworkCore;
using VideoArchiveManager.Core.Models;

namespace VideoArchiveManager.Data;

public class VideoArchiveDbContext : DbContext
{
    public VideoArchiveDbContext(DbContextOptions<VideoArchiveDbContext> options)
        : base(options)
    {
    }

    public DbSet<VideoItem> VideoItems => Set<VideoItem>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<VideoTag> VideoTags => Set<VideoTag>();
    public DbSet<VideoMoment> VideoMoments => Set<VideoMoment>();
    public DbSet<MomentTag> MomentTags => Set<MomentTag>();
    public DbSet<RootFolder> RootFolders => Set<RootFolder>();
    public DbSet<AiTagSuggestion> AiTagSuggestions => Set<AiTagSuggestion>();
    public DbSet<AiClipEmbedding> AiClipEmbeddings => Set<AiClipEmbedding>();
    public DbSet<AiFrameEmbedding> AiFrameEmbeddings => Set<AiFrameEmbedding>();
    public DbSet<GeocodeCacheEntry> GeocodeCacheEntries => Set<GeocodeCacheEntry>();
    public DbSet<SavedSearch> SavedSearches => Set<SavedSearch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(VideoArchiveDbContext).Assembly);
    }
}
