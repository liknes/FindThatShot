using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoArchiveManager.Core.Models;

namespace VideoArchiveManager.Data.Configurations;

public class VideoMomentConfiguration : IEntityTypeConfiguration<VideoMoment>
{
    public void Configure(EntityTypeBuilder<VideoMoment> builder)
    {
        builder.ToTable("VideoMoments");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Label).HasMaxLength(256);
        builder.Property(m => m.Notes).HasMaxLength(4096);
        builder.Property(m => m.ThumbnailPath).HasMaxLength(1024);

        builder.HasOne(m => m.VideoItem)
            .WithMany(v => v.Moments)
            .HasForeignKey(m => m.VideoItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => m.VideoItemId);
        builder.HasIndex(m => m.Rating);
        builder.HasIndex(m => m.StartSeconds);
    }
}
