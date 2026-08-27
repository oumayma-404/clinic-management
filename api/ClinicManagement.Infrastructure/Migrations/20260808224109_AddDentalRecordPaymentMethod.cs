using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// How a fiche de soins was settled — the method, and for a cheque its number, bank and due date.
    ///
    /// <para><b>Why.</b> Saving a fiche with a « Montant payé » raises the note d'honoraires and records that
    /// payment, and the payment was booked as <c>Cash</c> unconditionally (a hard-coded literal at the one call
    /// site). So a séance settled by a post-dated cheque produced a payment indistinguishable from notes in the
    /// drawer: absent from « Chèques à encaisser », and counted under « dont espèces » in the till's own
    /// breakdown.</para>
    ///
    /// <para>⚠️ <b>All four columns are nullable and nothing is backfilled, deliberately.</b> A null method reads
    /// as cash everywhere, which is the truth about every row written before this migration — those payments
    /// really were recorded as cash. Writing an explicit <c>Cash</c> into them would be a migration inventing a
    /// fact nobody recorded, and would make a historical row indistinguishable from one where a dentist
    /// deliberately chose « Espèces ».</para>
    ///
    /// <para>The three cheque columns mirror <c>Payments</c>' and <c>InstallmentPayments</c>' exactly (same
    /// lengths, same types) — they are the same three facts about the same piece of paper. No index: unlike those
    /// two ledgers a fiche is never the thing « chèques à encaisser » lists, because the cheque reaches that view
    /// through the <c>Payment</c> row this fiche produces.</para>
    ///
    /// <para>The « only on a cheque » invariant is <c>ChequeDetails.For</c>'s, not a CHECK constraint — same
    /// decision as L8, and <c>verify-schema</c> verifies it rather than the database enforcing it twice.</para>
    /// </summary>
    public partial class AddDentalRecordPaymentMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChequeBankName",
                table: "DentalRecords",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ChequeDueDate",
                table: "DentalRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChequeNumber",
                table: "DentalRecords",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentMethod",
                table: "DentalRecords",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChequeBankName",
                table: "DentalRecords");

            migrationBuilder.DropColumn(
                name: "ChequeDueDate",
                table: "DentalRecords");

            migrationBuilder.DropColumn(
                name: "ChequeNumber",
                table: "DentalRecords");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "DentalRecords");
        }
    }
}
