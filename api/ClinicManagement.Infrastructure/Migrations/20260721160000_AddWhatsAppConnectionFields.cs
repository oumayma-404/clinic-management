using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppConnectionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WhatsAppBusinessAccountId",
                table: "ClinicReminderSettings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WhatsAppConnectedAt",
                table: "ClinicReminderSettings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WhatsAppConnectionStatus",
                table: "ClinicReminderSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppLastError",
                table: "ClinicReminderSettings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WhatsAppBusinessAccountId",
                table: "ClinicReminderSettings");

            migrationBuilder.DropColumn(
                name: "WhatsAppConnectedAt",
                table: "ClinicReminderSettings");

            migrationBuilder.DropColumn(
                name: "WhatsAppConnectionStatus",
                table: "ClinicReminderSettings");

            migrationBuilder.DropColumn(
                name: "WhatsAppLastError",
                table: "ClinicReminderSettings");
        }
    }
}
