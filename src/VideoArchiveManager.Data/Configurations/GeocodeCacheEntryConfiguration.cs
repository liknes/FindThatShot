using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoArchiveManager.Core.Models;

namespace VideoArchiveManager.Data.Configurations;

public class GeocodeCacheEntryConfiguration : IEntityTypeConfiguration<GeocodeCacheEntry>
{
    public void Configure(EntityTypeBuilder<GeocodeCacheEntry> builder)
    {
        builder.ToTable("GeocodeCacheEntries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.LocationShort).IsRequired().HasMaxLength(256);
        builder.Property(e => e.DisplayName).HasMaxLength(1024);
        builder.Property(e => e.Country).HasMaxLength(128);
        builder.Property(e => e.Region).HasMaxLength(256);
        builder.Property(e => e.Provider).IsRequired().HasMaxLength(64);

        // Unique key per (provider, rounded coordinate) so multiple providers
        // could co-exist in the future without colliding.
        builder.HasIndex(e => new { e.Provider, e.LatRounded, e.LonRounded }).IsUnique();
    }
}
