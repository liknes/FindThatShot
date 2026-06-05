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

public class AiTagSuggestionConfiguration : IEntityTypeConfiguration<AiTagSuggestion>
{
    public void Configure(EntityTypeBuilder<AiTagSuggestion> builder)
    {
        builder.ToTable("AiTagSuggestions");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.TagName).IsRequired().HasMaxLength(128);
        builder.Property(a => a.Source).HasMaxLength(64);
        builder.Property(a => a.State).HasConversion<int>();

        builder.HasOne(a => a.VideoItem)
            .WithMany()
            .HasForeignKey(a => a.VideoItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.VideoItemId);
        builder.HasIndex(a => a.TagName);
        builder.HasIndex(a => a.State);

        // A clip never carries the same suggested label twice; lets the
        // tagging pass upsert (skip already-known suggestions, including
        // ones the user previously rejected) without scanning.
        builder.HasIndex(a => new { a.VideoItemId, a.TagName }).IsUnique();
    }
}
