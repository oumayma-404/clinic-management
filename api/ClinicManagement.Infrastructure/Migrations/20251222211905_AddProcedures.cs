using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProcedures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProcedureColorHex",
                table: "Appointments",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcedureDurationMinutes",
                table: "Appointments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProcedureTypeId",
                table: "Appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProcedureTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DefaultDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    ColorHex = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcedureTypes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ProcedureTypeId",
                table: "Appointments",
                column: "ProcedureTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcedureTypes_Name",
                table: "ProcedureTypes",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_ProcedureTypes_ProcedureTypeId",
                table: "Appointments",
                column: "ProcedureTypeId",
                principalTable: "ProcedureTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_ProcedureTypes_ProcedureTypeId",
                table: "Appointments");

            migrationBuilder.DropTable(
                name: "ProcedureTypes");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_ProcedureTypeId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ProcedureColorHex",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ProcedureDurationMinutes",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ProcedureTypeId",
                table: "Appointments");
        }
    }
}
