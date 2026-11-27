using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentWaitingRoom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArrivedAtUtc",
                table: "Appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CalledInAtUtc",
                table: "Appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsWalkIn",
                table: "Appointments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeftChairAtUtc",
                table: "Appointments",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArrivedAtUtc",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "CalledInAtUtc",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "IsWalkIn",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "LeftChairAtUtc",
                table: "Appointments");
        }
    }
}
