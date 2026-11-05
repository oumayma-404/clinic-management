using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttendanceSchemaHash",
                table: "Users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AttendanceTrackedOverride",
                table: "Users",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SchemaFailedAttempts",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "SchemaLockedUntilUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AttendanceEnabled",
                table: "Clinics",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AttendanceEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    LocalDay = table.Column<DateOnly>(type: "date", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: true),
                    TerminalName = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    PunchedArrivalUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PunchedDepartureUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArrivalUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DepartureUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DepartureSource = table.Column<int>(type: "integer", nullable: true),
                    CorrectedByUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CorrectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CorrectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    // No xmin here: Entity<T>.Version maps onto PostgreSQL's system column, which CREATE TABLE refuses.
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceEntries_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceTerminals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SecretHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceTerminals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceTerminals_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceEntries_ClinicId_LocalDay",
                table: "AttendanceEntries",
                columns: new[] { "ClinicId", "LocalDay" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceEntries_ClinicId_UserId_LocalDay",
                table: "AttendanceEntries",
                columns: new[] { "ClinicId", "UserId", "LocalDay" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceTerminals_ClinicId",
                table: "AttendanceTerminals",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceTerminals_SecretHash",
                table: "AttendanceTerminals",
                column: "SecretHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceEntries");

            migrationBuilder.DropTable(
                name: "AttendanceTerminals");

            migrationBuilder.DropColumn(
                name: "AttendanceSchemaHash",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AttendanceTrackedOverride",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SchemaFailedAttempts",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SchemaLockedUntilUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AttendanceEnabled",
                table: "Clinics");
        }
    }
}
