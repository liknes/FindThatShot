using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoArchiveManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoMoments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VideoMoments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VideoItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartSeconds = table.Column<double>(type: "REAL", nullable: false),
                    EndSeconds = table.Column<double>(type: "REAL", nullable: true),
                    Label = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    Rating = table.Column<int>(type: "INTEGER", nullable: false),
                    ThumbnailPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoMoments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VideoMoments_VideoItems_VideoItemId",
                        column: x => x.VideoItemId,
                        principalTable: "VideoItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MomentTags",
                columns: table => new
                {
                    VideoMomentId = table.Column<int>(type: "INTEGER", nullable: false),
                    TagId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MomentTags", x => new { x.VideoMomentId, x.TagId });
                    table.ForeignKey(
                        name: "FK_MomentTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MomentTags_VideoMoments_VideoMomentId",
                        column: x => x.VideoMomentId,
                        principalTable: "VideoMoments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MomentTags_TagId",
                table: "MomentTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoMoments_Rating",
                table: "VideoMoments",
                column: "Rating");

            migrationBuilder.CreateIndex(
                name: "IX_VideoMoments_StartSeconds",
                table: "VideoMoments",
                column: "StartSeconds");

            migrationBuilder.CreateIndex(
                name: "IX_VideoMoments_VideoItemId",
                table: "VideoMoments",
                column: "VideoItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MomentTags");

            migrationBuilder.DropTable(
                name: "VideoMoments");
        }
    }
}
