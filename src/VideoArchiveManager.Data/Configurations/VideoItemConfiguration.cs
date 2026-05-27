using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoArchiveManager.Core.Models;

namespace VideoArchiveManager.Data.Configurations;

public class VideoItemConfiguration : IEntityTypeConfiguration<VideoItem>
{
    public void Configure(EntityTypeBuilder<VideoItem> builder)
    {
        builder.ToTable("VideoItems");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.FilePath).IsRequired().HasMaxLength(1024);
        builder.Property(v => v.FileName).IsRequired().HasMaxLength(512);
        builder.Property(v => v.Extension).HasMaxLength(16);
        builder.Property(v => v.FolderPath).HasMaxLength(1024);
        builder.Property(v => v.Codec).HasMaxLength(64);
        builder.Property(v => v.Camera).HasMaxLength(256);
        builder.Property(v => v.LocationText).HasMaxLength(512);
        builder.Property(v => v.ContextText).HasMaxLength(1024);
        builder.Property(v => v.Notes).HasMaxLength(4096);
        builder.Property(v => v.ThumbnailPath).HasMaxLength(1024);

        builder.HasIndex(v => v.FilePath).IsUnique();
        builder.HasIndex(v => v.FolderPath);
        builder.HasIndex(v => v.Status);
        builder.HasIndex(v => v.Rating);
        builder.HasIndex(v => v.Camera);
        builder.HasIndex(v => v.FolderDate);
        builder.HasIndex(v => v.FileExists);
    }
}
