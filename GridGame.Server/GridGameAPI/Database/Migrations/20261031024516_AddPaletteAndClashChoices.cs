using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GridGameAPI.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPaletteAndClashChoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultPalette",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClashChoices",
                columns: table => new
                {
                    GridGameId = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SwitchToDefaults = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClashChoices", x => new { x.GridGameId, x.PlayerId });
                    table.ForeignKey(
                        name: "FK_ClashChoices_AspNetUsers_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClashChoices_GridGames_GridGameId",
                        column: x => x.GridGameId,
                        principalTable: "GridGames",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClashChoices_PlayerId",
                table: "ClashChoices",
                column: "PlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClashChoices");

            migrationBuilder.DropColumn(
                name: "DefaultPalette",
                table: "AspNetUsers");
        }
    }
}
