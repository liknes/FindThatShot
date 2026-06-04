using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoArchiveManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAiEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "BestFrameSeconds",
                table: "AiTagSuggestions",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "State",
                table: "AiTagSuggestions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "AiClipEmbeddings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VideoItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Vector = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Dim = table.Column<int>(type: "INTEGER", nullable: false),
                    FrameCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiClipEmbeddings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiClipEmbeddings_VideoItems_VideoItemId",
                        column: x => x.VideoItemId,
                        principalTable: "VideoItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiFrameEmbeddings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VideoItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    TimeSeconds = table.Column<double>(type: "REAL", nullable: false),
                    Vector = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Dim = table.Column<int>(type: "INTEGER", nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiFrameEmbeddings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiFrameEmbeddings_VideoItems_VideoItemId",
                        column: x => x.VideoItemId,
                        principalTable: "VideoItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiClipEmbeddings_VideoItemId",
                table: "AiClipEmbeddings",
                column: "VideoItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiFrameEmbeddings_VideoItemId",
                table: "AiFrameEmbeddings",
                column: "VideoItemId");

            migrationBuilder.CreateIndex(
                name: "IX_AiTagSuggestions_State",
                table: "AiTagSuggestions",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_AiTagSuggestions_VideoItemId_TagName",
                table: "AiTagSuggestions",
                columns: new[] { "VideoItemId", "TagName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiClipEmbeddings");

            migrationBuilder.DropTable(
                name: "AiFrameEmbeddings");

            migrationBuilder.DropIndex(
                name: "IX_AiTagSuggestions_State",
                table: "AiTagSuggestions");

            migrationBuilder.DropIndex(
                name: "IX_AiTagSuggestions_VideoItemId_TagName",
                table: "AiTagSuggestions");

            migrationBuilder.DropColumn(
                name: "BestFrameSeconds",
                table: "AiTagSuggestions");

            migrationBuilder.DropColumn(
                name: "State",
                table: "AiTagSuggestions");
        }
    }
}
