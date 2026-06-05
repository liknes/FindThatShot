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
