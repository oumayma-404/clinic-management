using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPerClinicReminderOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LeadTimeHours",
                table: "ClinicReminderSettings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MessageTemplateBody",
                table: "ClinicReminderSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmsApiUrl",
                table: "ClinicReminderSettings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppApiUrl",
                table: "ClinicReminderSettings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LeadTimeHours",
                table: "ClinicReminderSettings");

            migrationBuilder.DropColumn(
                name: "MessageTemplateBody",
                table: "ClinicReminderSettings");

            migrationBuilder.DropColumn(
                name: "SmsApiUrl",
                table: "ClinicReminderSettings");

            migrationBuilder.DropColumn(
                name: "WhatsAppApiUrl",
                table: "ClinicReminderSettings");
        }
    }
}
