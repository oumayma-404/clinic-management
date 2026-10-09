using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRelayClockCorrection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClockCorrectedAtUtc",
                table: "ClinicRelays",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClockCorrectedBySeconds",
                table: "ClinicRelays",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClockUnfixableSinceUtc",
                table: "ClinicRelays",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClockCorrectedAtUtc",
                table: "ClinicRelays");

            migrationBuilder.DropColumn(
                name: "ClockCorrectedBySeconds",
                table: "ClinicRelays");

            migrationBuilder.DropColumn(
                name: "ClockUnfixableSinceUtc",
                table: "ClinicRelays");
        }
    }
}
