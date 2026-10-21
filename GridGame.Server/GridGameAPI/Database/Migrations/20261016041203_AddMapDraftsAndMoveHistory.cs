using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GridGameAPI.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddMapDraftsAndMoveHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDraft",
                table: "Maps",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MoveLog",
                table: "GridGames",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDraft",
                table: "Maps");

            migrationBuilder.DropColumn(
                name: "MoveLog",
                table: "GridGames");
        }
    }
}
