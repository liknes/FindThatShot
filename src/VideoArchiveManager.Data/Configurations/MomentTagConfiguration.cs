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
