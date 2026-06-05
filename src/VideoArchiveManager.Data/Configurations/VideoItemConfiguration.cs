// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.
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
