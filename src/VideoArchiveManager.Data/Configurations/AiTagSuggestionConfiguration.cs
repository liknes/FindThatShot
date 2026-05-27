using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoArchiveManager.Core.Models;

namespace VideoArchiveManager.Data.Configurations;

public class AiTagSuggestionConfiguration : IEntityTypeConfiguration<AiTagSuggestion>
{
    public void Configure(EntityTypeBuilder<AiTagSuggestion> builder)
    {
        builder.ToTable("AiTagSuggestions");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.TagName).IsRequired().HasMaxLength(128);
        builder.Property(a => a.Source).HasMaxLength(64);

        builder.HasOne(a => a.VideoItem)
            .WithMany()
            .HasForeignKey(a => a.VideoItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.VideoItemId);
        builder.HasIndex(a => a.TagName);
    }
}
