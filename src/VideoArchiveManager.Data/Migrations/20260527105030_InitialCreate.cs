using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoArchiveManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RootFolders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Path = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    LastScannedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RootFolders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VideoItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Extension = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    FolderPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtFile = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAtFile = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DurationSeconds = table.Column<double>(type: "REAL", nullable: true),
                    Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    FrameRate = table.Column<double>(type: "REAL", nullable: true),
                    Codec = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Camera = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    GpsLatitude = table.Column<double>(type: "REAL", nullable: true),
                    GpsLongitude = table.Column<double>(type: "REAL", nullable: true),
                    FolderDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LocationText = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ContextText = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    Rating = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ThumbnailPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    FileExists = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiTagSuggestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VideoItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    TagName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Confidence = table.Column<double>(type: "REAL", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Approved = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiTagSuggestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiTagSuggestions_VideoItems_VideoItemId",
                        column: x => x.VideoItemId,
                        principalTable: "VideoItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VideoTags",
                columns: table => new
                {
                    VideoItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    TagId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoTags", x => new { x.VideoItemId, x.TagId });
                    table.ForeignKey(
                        name: "FK_VideoTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VideoTags_VideoItems_VideoItemId",
                        column: x => x.VideoItemId,
                        principalTable: "VideoItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiTagSuggestions_TagName",
                table: "AiTagSuggestions",
                column: "TagName");

            migrationBuilder.CreateIndex(
                name: "IX_AiTagSuggestions_VideoItemId",
                table: "AiTagSuggestions",
                column: "VideoItemId");

            migrationBuilder.CreateIndex(
                name: "IX_RootFolders_Path",
                table: "RootFolders",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Name_Type",
                table: "Tags",
                columns: new[] { "Name", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Type",
                table: "Tags",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_VideoItems_Camera",
                table: "VideoItems",
                column: "Camera");

            migrationBuilder.CreateIndex(
                name: "IX_VideoItems_FileExists",
                table: "VideoItems",
                column: "FileExists");

            migrationBuilder.CreateIndex(
                name: "IX_VideoItems_FilePath",
                table: "VideoItems",
                column: "FilePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VideoItems_FolderDate",
                table: "VideoItems",
                column: "FolderDate");

            migrationBuilder.CreateIndex(
                name: "IX_VideoItems_FolderPath",
                table: "VideoItems",
                column: "FolderPath");

            migrationBuilder.CreateIndex(
                name: "IX_VideoItems_Rating",
                table: "VideoItems",
                column: "Rating");

            migrationBuilder.CreateIndex(
                name: "IX_VideoItems_Status",
                table: "VideoItems",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_VideoTags_TagId",
                table: "VideoTags",
                column: "TagId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiTagSuggestions");

            migrationBuilder.DropTable(
                name: "RootFolders");

            migrationBuilder.DropTable(
                name: "VideoTags");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "VideoItems");
        }
    }
}
