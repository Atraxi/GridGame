using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GridGameAPI.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddResignation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ResignedPlayerNumber",
                table: "GridGames",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResignedPlayerNumber",
                table: "GridGames");
        }
    }
}
