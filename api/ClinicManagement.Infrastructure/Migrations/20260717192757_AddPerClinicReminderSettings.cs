using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPerClinicReminderSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClinicId",
                table: "Notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClinicReminderSettings",
                columns: table => new
                {
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    SmsEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    WhatsAppEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    SmsSenderId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    WhatsAppPhoneNumberId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    WhatsAppTemplateName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    WhatsAppTemplateLanguage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SmsApiKeyEncrypted = table.Column<string>(type: "text", nullable: true),
                    WhatsAppAccessTokenEncrypted = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicReminderSettings", x => x.ClinicId);
                    table.ForeignKey(
                        name: "FK_ClinicReminderSettings_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicReminderSettings");

            migrationBuilder.DropColumn(
                name: "ClinicId",
                table: "Notifications");
        }
    }
}
