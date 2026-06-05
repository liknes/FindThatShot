using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoArchiveManager.Core.Models;

namespace VideoArchiveManager.Data.Configurations;

public class MomentTagConfiguration : IEntityTypeConfiguration<MomentTag>
{
    public void Configure(EntityTypeBuilder<MomentTag> builder)
    {
        builder.ToTable("MomentTags");
        builder.HasKey(mt => new { mt.VideoMomentId, mt.TagId });

        builder.HasOne(mt => mt.VideoMoment)
            .WithMany(m => m.MomentTags)
            .HasForeignKey(mt => mt.VideoMomentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(mt => mt.Tag)
            .WithMany(t => t.MomentTags)
            .HasForeignKey(mt => mt.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(mt => mt.TagId);

        builder.Property(mt => mt.IsBackground).HasDefaultValue(false);
    }
}
