using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GridGameAPI.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddBotNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BotNames",
                table: "GridGames",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BotNames",
                table: "GridGames");
        }
    }
}
