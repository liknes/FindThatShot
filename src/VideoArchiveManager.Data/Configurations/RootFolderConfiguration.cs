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
