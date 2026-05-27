using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoArchiveManager.Core.Models;

namespace VideoArchiveManager.Data.Configurations;

public class VideoTagConfiguration : IEntityTypeConfiguration<VideoTag>
{
    public void Configure(EntityTypeBuilder<VideoTag> builder)
    {
        builder.ToTable("VideoTags");
        builder.HasKey(vt => new { vt.VideoItemId, vt.TagId });

        builder.HasOne(vt => vt.VideoItem)
            .WithMany(v => v.VideoTags)
            .HasForeignKey(vt => vt.VideoItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(vt => vt.Tag)
            .WithMany(t => t.VideoTags)
            .HasForeignKey(vt => vt.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(vt => vt.TagId);
    }
}
