using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEInvoicing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Clinic TTN « El Fatoora » settings (non-secret).
            migrationBuilder.AddColumn<bool>(
                name: "TtnEInvoicingEnabled",
                table: "Clinics",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TtnEnvironment",
                table: "Clinics",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Sandbox");

            // Invoice e-invoicing state (additive; existing invoices default to NotSubmitted / 0 attempts).
            migrationBuilder.AddColumn<int>(
                name: "EInvoiceAttemptCount",
                table: "Invoices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "EInvoiceLastError",
                table: "Invoices",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EInvoiceNextAttemptAt",
                table: "Invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EInvoiceStatus",
                table: "Invoices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "EInvoiceSubmittedAt",
                table: "Invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EInvoiceValidatedAt",
                table: "Invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QrPayload",
                table: "Invoices",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignedXmlStorageKey",
                table: "Invoices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TtnIdentifier",
                table: "Invoices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TtnReceiptStorageKey",
                table: "Invoices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_EInvoiceStatus_EInvoiceNextAttemptAt",
                table: "Invoices",
                columns: new[] { "EInvoiceStatus", "EInvoiceNextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_EInvoiceStatus_EInvoiceNextAttemptAt",
                table: "Invoices");

            migrationBuilder.DropColumn(name: "EInvoiceAttemptCount", table: "Invoices");
            migrationBuilder.DropColumn(name: "EInvoiceLastError", table: "Invoices");
            migrationBuilder.DropColumn(name: "EInvoiceNextAttemptAt", table: "Invoices");
            migrationBuilder.DropColumn(name: "EInvoiceStatus", table: "Invoices");
            migrationBuilder.DropColumn(name: "EInvoiceSubmittedAt", table: "Invoices");
            migrationBuilder.DropColumn(name: "EInvoiceValidatedAt", table: "Invoices");
            migrationBuilder.DropColumn(name: "QrPayload", table: "Invoices");
            migrationBuilder.DropColumn(name: "SignedXmlStorageKey", table: "Invoices");
            migrationBuilder.DropColumn(name: "TtnIdentifier", table: "Invoices");
            migrationBuilder.DropColumn(name: "TtnReceiptStorageKey", table: "Invoices");

            migrationBuilder.DropColumn(name: "TtnEInvoicingEnabled", table: "Clinics");
            migrationBuilder.DropColumn(name: "TtnEnvironment", table: "Clinics");
        }
    }
}
