using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveEInvoicing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_EInvoiceStatus_EInvoiceNextAttemptAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "EInvoiceAttemptCount",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "EInvoiceLastError",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "EInvoiceNextAttemptAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "EInvoiceStatus",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "EInvoiceSubmittedAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "EInvoiceValidatedAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "QrPayload",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "SignedXmlStorageKey",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "TtnIdentifier",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "TtnReceiptStorageKey",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "TtnApiSecretEncrypted",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "TtnCertificateKey",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "TtnCertificatePasswordEncrypted",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "TtnEInvoicingEnabled",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "TtnEnvironment",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "TtnUsername",
                table: "Clinics");
        }

        /// <summary>
        /// Deliberately irreversible. Restoring the sixteen columns would recreate empty ones: the values —
        /// every TTN identifier, signed-XML key and receipt key — are dropped by <c>Up</c> and no migration can
        /// bring them back, so a <c>Down</c> that "succeeded" would hand back a schema that looks whole and
        /// holds nothing. Restore from the pre-migration backup instead.
        ///
        /// <para>⚠️ The first throwing <c>Down()</c> in this repository — not a pattern to copy without the same
        /// argument behind it.</para>
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "La suppression de la facturation électronique « El Fatoora » est irréversible : les identifiants "
                + "TTN et les clés des documents signés ont été supprimés et aucune migration ne peut les "
                + "restaurer. Exportez les données avant de migrer, puis restaurez la sauvegarde antérieure à "
                + "cette migration.");
        }
    }
}
