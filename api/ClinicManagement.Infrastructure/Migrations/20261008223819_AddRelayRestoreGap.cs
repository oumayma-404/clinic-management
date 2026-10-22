using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRelayRestoreGap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "GapPendingSinceUtc",
                table: "ClinicRelays",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastGapAtUtc",
                table: "ClinicRelays",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastGapId",
                table: "ClinicRelays",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastGapRows",
                table: "ClinicRelays",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Epoch",
                table: "ClinicChangeCursors",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "EpochFromSeq",
                table: "ClinicChangeCursors",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GapPendingSinceUtc",
                table: "ClinicRelays");

            migrationBuilder.DropColumn(
                name: "LastGapAtUtc",
                table: "ClinicRelays");

            migrationBuilder.DropColumn(
                name: "LastGapId",
                table: "ClinicRelays");

            migrationBuilder.DropColumn(
                name: "LastGapRows",
                table: "ClinicRelays");

            migrationBuilder.DropColumn(
                name: "Epoch",
                table: "ClinicChangeCursors");

            migrationBuilder.DropColumn(
                name: "EpochFromSeq",
                table: "ClinicChangeCursors");
        }
    }
}
