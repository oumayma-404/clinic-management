using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentEmailsAndSmtpSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SmtpFromAddress",
                table: "ClinicReminderSettings",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmtpFromName",
                table: "ClinicReminderSettings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmtpHost",
                table: "ClinicReminderSettings",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmtpPasswordEncrypted",
                table: "ClinicReminderSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SmtpPort",
                table: "ClinicReminderSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SmtpUseTls",
                table: "ClinicReminderSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmtpUsername",
                table: "ClinicReminderSettings",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocumentEmails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentKind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstallmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecipientEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    AttachmentStorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AttachmentFileName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    QueuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    RequestedByUserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentEmails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentEmails_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentEmails_Clinic_Kind_Document",
                table: "DocumentEmails",
                columns: new[] { "ClinicId", "DocumentKind", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentEmails_Status_QueuedAt",
                table: "DocumentEmails",
                columns: new[] { "Status", "QueuedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentEmails");

            migrationBuilder.DropColumn(
                name: "SmtpFromAddress",
                table: "ClinicReminderSettings");

            migrationBuilder.DropColumn(
                name: "SmtpFromName",
                table: "ClinicReminderSettings");

            migrationBuilder.DropColumn(
                name: "SmtpHost",
                table: "ClinicReminderSettings");

            migrationBuilder.DropColumn(
                name: "SmtpPasswordEncrypted",
                table: "ClinicReminderSettings");

            migrationBuilder.DropColumn(
                name: "SmtpPort",
                table: "ClinicReminderSettings");

            migrationBuilder.DropColumn(
                name: "SmtpUseTls",
                table: "ClinicReminderSettings");

            migrationBuilder.DropColumn(
                name: "SmtpUsername",
                table: "ClinicReminderSettings");
        }
    }
}
