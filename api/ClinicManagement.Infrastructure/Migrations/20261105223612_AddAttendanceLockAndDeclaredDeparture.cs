using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceLockAndDeclaredDeparture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AttendanceLockEnabled",
                table: "Clinics",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeclaredLastSaveUtc",
                table: "AttendanceEntries",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttendanceLockEnabled",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "DeclaredLastSaveUtc",
                table: "AttendanceEntries");
        }
    }
}
