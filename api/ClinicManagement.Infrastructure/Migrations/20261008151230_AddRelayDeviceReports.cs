using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRelayDeviceReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeviceReportsLockSinceUtc",
                table: "ClinicRelays",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DevicesReachedPcAtUtc",
                table: "ClinicRelays",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DevicesUnreachableFirstAtUtc",
                table: "ClinicRelays",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DevicesUnreachableLastAtUtc",
                table: "ClinicRelays",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GatewayAddress",
                table: "ClinicRelays",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HttpsPort",
                table: "ClinicRelays",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicAddress",
                table: "ClinicRelays",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeviceReportsLockSinceUtc",
                table: "ClinicRelays");

            migrationBuilder.DropColumn(
                name: "DevicesReachedPcAtUtc",
                table: "ClinicRelays");

            migrationBuilder.DropColumn(
                name: "DevicesUnreachableFirstAtUtc",
                table: "ClinicRelays");

            migrationBuilder.DropColumn(
                name: "DevicesUnreachableLastAtUtc",
                table: "ClinicRelays");

            migrationBuilder.DropColumn(
                name: "GatewayAddress",
                table: "ClinicRelays");

            migrationBuilder.DropColumn(
                name: "HttpsPort",
                table: "ClinicRelays");

            migrationBuilder.DropColumn(
                name: "PublicAddress",
                table: "ClinicRelays");
        }
    }
}
