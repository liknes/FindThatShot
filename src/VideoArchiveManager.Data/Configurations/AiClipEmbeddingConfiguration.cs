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
