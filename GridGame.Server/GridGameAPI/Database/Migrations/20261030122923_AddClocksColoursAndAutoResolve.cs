using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GridGameAPI.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddClocksColoursAndAutoResolve : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClockBaseSeconds",
                table: "GridGames",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ClockIncrementSeconds",
                table: "GridGames",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ClockMode",
                table: "GridGames",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ClockRemainingMs",
                table: "GridGames",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ResignedOnTime",
                table: "GridGames",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "TurnStartedUtc",
                table: "GridGames",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ColourPrimary",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ColourSecondary",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseDefaultColours",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClockBaseSeconds",
                table: "GridGames");

            migrationBuilder.DropColumn(
                name: "ClockIncrementSeconds",
                table: "GridGames");

            migrationBuilder.DropColumn(
                name: "ClockMode",
                table: "GridGames");

            migrationBuilder.DropColumn(
                name: "ClockRemainingMs",
                table: "GridGames");

            migrationBuilder.DropColumn(
                name: "ResignedOnTime",
                table: "GridGames");

            migrationBuilder.DropColumn(
                name: "TurnStartedUtc",
                table: "GridGames");

            migrationBuilder.DropColumn(
                name: "ColourPrimary",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ColourSecondary",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "UseDefaultColours",
                table: "AspNetUsers");
        }
    }
}
