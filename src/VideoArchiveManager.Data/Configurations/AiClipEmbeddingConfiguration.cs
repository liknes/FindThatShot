using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoArchiveManager.Core.Models;

namespace VideoArchiveManager.Data.Configurations;

public class AiClipEmbeddingConfiguration : IEntityTypeConfiguration<AiClipEmbedding>
{
    public void Configure(EntityTypeBuilder<AiClipEmbedding> builder)
    {
        builder.ToTable("AiClipEmbeddings");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Vector).IsRequired();
        builder.Property(e => e.ModelId).IsRequired().HasMaxLength(64);

        builder.HasOne(e => e.VideoItem)
            .WithMany()
            .HasForeignKey(e => e.VideoItemId)
            .OnDelete(DeleteBehavior.Cascade);

        // One pooled embedding per clip.
        builder.HasIndex(e => e.VideoItemId).IsUnique();
    }
}
