using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVisitClosureWaiver : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NothingToBillAtUtc",
                table: "Appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NothingToBillByUserId",
                table: "Appointments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NothingToBillReason",
                table: "Appointments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_NothingToBillAtUtc",
                table: "Appointments",
                column: "NothingToBillAtUtc",
                filter: "\"NothingToBillAtUtc\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_NothingToBillAtUtc",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "NothingToBillAtUtc",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "NothingToBillByUserId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "NothingToBillReason",
                table: "Appointments");
        }
    }
}
