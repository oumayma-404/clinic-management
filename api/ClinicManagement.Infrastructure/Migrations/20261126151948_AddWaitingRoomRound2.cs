using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWaitingRoomRound2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "WorksTwoChairs",
                table: "Doctors",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "CallSkippedAtUtc",
                table: "Appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeftUnseenAtUtc",
                table: "Appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PlannedDuration",
                table: "Appointments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlannedStartUtc",
                table: "Appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UrgentAtUtc",
                table: "Appointments",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WorksTwoChairs",
                table: "Doctors");

            migrationBuilder.DropColumn(
                name: "CallSkippedAtUtc",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "LeftUnseenAtUtc",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "PlannedDuration",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "PlannedStartUtc",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "UrgentAtUtc",
                table: "Appointments");
        }
    }
}
