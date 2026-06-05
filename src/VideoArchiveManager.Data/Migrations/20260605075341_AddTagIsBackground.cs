using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoArchiveManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTagIsBackground : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBackground",
                table: "VideoTags",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsBackground",
                table: "MomentTags",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsBackground",
                table: "VideoTags");

            migrationBuilder.DropColumn(
                name: "IsBackground",
                table: "MomentTags");
        }
    }
}
