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
    public DbSet<GeocodeCacheEntry> GeocodeCacheEntries => Set<GeocodeCacheEntry>();
    public DbSet<SavedSearch> SavedSearches => Set<SavedSearch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(VideoArchiveDbContext).Assembly);
    }
}
