using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// The arrival lock starts at the next opening after it was armed and a borne went live. A null armed-at means
    /// « armed long ago », so no cabinet's lock moves; a borne already held by a device went live when it was created.
    /// </summary>
    public partial class AddAttendanceLockStart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AttendanceLockArmedAtUtc",
                table: "Clinics",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ActivatedAtUtc",
                table: "AttendanceTerminals",
                type: "timestamp with time zone",
                nullable: true);

            // Below the DDL that creates the column it writes; a waiting borne stays null until its device claims it.
            migrationBuilder.Sql(
                "UPDATE \"AttendanceTerminals\" SET \"ActivatedAtUtc\" = \"CreatedAt\" " +
                "WHERE \"SecretHash\" IS NOT NULL AND \"ActivatedAtUtc\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttendanceLockArmedAtUtc",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "ActivatedAtUtc",
                table: "AttendanceTerminals");
        }
    }
}
