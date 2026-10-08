using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRelayReclaim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CutOverruledAtUtc",
                table: "ClinicRelays",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReclaimedAtAckSeq",
                table: "ClinicRelays",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReclaimedAtUtc",
                table: "ClinicRelays",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReclaimedByUserId",
                table: "ClinicRelays",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CutOverruledAtUtc",
                table: "ClinicRelays");

            migrationBuilder.DropColumn(
                name: "ReclaimedAtAckSeq",
                table: "ClinicRelays");

            migrationBuilder.DropColumn(
                name: "ReclaimedAtUtc",
                table: "ClinicRelays");

            migrationBuilder.DropColumn(
                name: "ReclaimedByUserId",
                table: "ClinicRelays");
        }
    }
}
