using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRelayBellAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RelayAlert",
                table: "StaffNotifications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetRole",
                table: "StaffNotifications",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CopyStoppedSinceUtc",
                table: "ClinicRelays",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RelayAlert",
                table: "StaffNotifications");

            migrationBuilder.DropColumn(
                name: "TargetRole",
                table: "StaffNotifications");

            migrationBuilder.DropColumn(
                name: "CopyStoppedSinceUtc",
                table: "ClinicRelays");
        }
    }
}
