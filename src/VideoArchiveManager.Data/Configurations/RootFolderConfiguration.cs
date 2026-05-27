using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoArchiveManager.Core.Models;

namespace VideoArchiveManager.Data.Configurations;

public class RootFolderConfiguration : IEntityTypeConfiguration<RootFolder>
{
    public void Configure(EntityTypeBuilder<RootFolder> builder)
    {
        builder.ToTable("RootFolders");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Path).IsRequired().HasMaxLength(1024);
        builder.Property(r => r.Name).HasMaxLength(256);

        builder.HasIndex(r => r.Path).IsUnique();
    }
}
