using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBorneAssignedToDevice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "SecretHash",
                table: "AttendanceTerminals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AddColumn<string>(
                name: "DeviceDescription",
                table: "AttendanceTerminals",
                type: "character varying(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PendingSessionFamilyId",
                table: "AttendanceTerminals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceTerminals_PendingSessionFamilyId",
                table: "AttendanceTerminals",
                column: "PendingSessionFamilyId",
                filter: "\"PendingSessionFamilyId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AttendanceTerminals_PendingSessionFamilyId",
                table: "AttendanceTerminals");

            migrationBuilder.DropColumn(
                name: "DeviceDescription",
                table: "AttendanceTerminals");

            migrationBuilder.DropColumn(
                name: "PendingSessionFamilyId",
                table: "AttendanceTerminals");

            // A borne still waiting for its device has no secret, and the column cannot take it back as NOT NULL.
            migrationBuilder.Sql("DELETE FROM \"AttendanceTerminals\" WHERE \"SecretHash\" IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "SecretHash",
                table: "AttendanceTerminals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);
        }
    }
}
