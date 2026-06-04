using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoArchiveManager.Core.Models;

namespace VideoArchiveManager.Data.Configurations;

public class AiFrameEmbeddingConfiguration : IEntityTypeConfiguration<AiFrameEmbedding>
{
    public void Configure(EntityTypeBuilder<AiFrameEmbedding> builder)
    {
        builder.ToTable("AiFrameEmbeddings");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Vector).IsRequired();
        builder.Property(e => e.ModelId).IsRequired().HasMaxLength(64);

        builder.HasOne(e => e.VideoItem)
            .WithMany()
            .HasForeignKey(e => e.VideoItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.VideoItemId);
    }
}
