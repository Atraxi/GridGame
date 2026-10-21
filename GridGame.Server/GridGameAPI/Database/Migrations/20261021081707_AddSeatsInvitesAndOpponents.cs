using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GridGameAPI.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSeatsInvitesAndOpponents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsInviteOnly",
                table: "GridGames",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "GameInvites",
                columns: table => new
                {
                    GridGameId = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameInvites", x => new { x.GridGameId, x.PlayerId });
                    table.ForeignKey(
                        name: "FK_GameInvites_AspNetUsers_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameInvites_GridGames_GridGameId",
                        column: x => x.GridGameId,
                        principalTable: "GridGames",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameSeats",
                columns: table => new
                {
                    GridGameId = table.Column<int>(type: "int", nullable: false),
                    PlayerNumber = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSeats", x => new { x.GridGameId, x.PlayerNumber });
                    table.ForeignKey(
                        name: "FK_GameSeats_AspNetUsers_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameSeats_GridGames_GridGameId",
                        column: x => x.GridGameId,
                        principalTable: "GridGames",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PastOpponents",
                columns: table => new
                {
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OpponentId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LastPlayedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PastOpponents", x => new { x.PlayerId, x.OpponentId });
                    table.ForeignKey(
                        name: "FK_PastOpponents_AspNetUsers_OpponentId",
                        column: x => x.OpponentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PastOpponents_AspNetUsers_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameInvites_PlayerId",
                table: "GameInvites",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_GameSeats_GridGameId_PlayerId",
                table: "GameSeats",
                columns: new[] { "GridGameId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameSeats_PlayerId",
                table: "GameSeats",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PastOpponents_OpponentId",
                table: "PastOpponents",
                column: "OpponentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameInvites");

            migrationBuilder.DropTable(
                name: "GameSeats");

            migrationBuilder.DropTable(
                name: "PastOpponents");

            migrationBuilder.DropColumn(
                name: "IsInviteOnly",
                table: "GridGames");
        }
    }
}
