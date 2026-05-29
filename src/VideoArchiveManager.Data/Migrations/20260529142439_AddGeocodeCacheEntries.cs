using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoArchiveManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGeocodeCacheEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GeocodeCacheEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LatRounded = table.Column<double>(type: "REAL", nullable: false),
                    LonRounded = table.Column<double>(type: "REAL", nullable: false),
                    LocationShort = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Country = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Region = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    LookedUpAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeocodeCacheEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GeocodeCacheEntries_Provider_LatRounded_LonRounded",
                table: "GeocodeCacheEntries",
                columns: new[] { "Provider", "LatRounded", "LonRounded" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GeocodeCacheEntries");
        }
    }
}
