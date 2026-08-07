using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Gives both payment ledgers a cheque's identity: its number, the drawing bank, and the day it may be banked
    /// (<c>adoption-qa-l</c> L8).
    ///
    /// <para><b>What was missing.</b> Post-dated cheques are ubiquitous in Tunisian private practice and
    /// <c>PaymentMethod.Cheque</c> was a <b>bare enum value</b> — <c>Payments</c>, <c>InstallmentPayments</c>,
    /// <c>CreditNotes</c> and <c>Expenses</c> each carried an amount, a method and a date and nothing else. For
    /// money <i>out</i> the cheque number could at least go in an expense's description; for money <b>in</b> there
    /// was no free-text field of any kind, so « quel chèque, de quelle banque, encaissable quand ? » had nowhere to
    /// live. A post-dated cheque nobody banks is simply money lost.</para>
    ///
    /// <para><b>Purely additive.</b> Six nullable columns and two partial indexes; no backfill, no data migration,
    /// no row rewritten. Every existing payment keeps reading exactly as it did — a cheque recorded before today
    /// legitimately has no number, which is different from « we have no cheques » and is why the columns are
    /// nullable rather than defaulted to <c>''</c>.</para>
    ///
    /// <para>⚠️ <b>The invariant is not a CHECK constraint, deliberately.</b> Cheque details only make sense when
    /// the method is <c>Cheque</c>, and that rule lives in <c>ChequeDetails.For</c> — one place, reached by every
    /// write path. Expressing it here as well would be a second copy of it, and the copy that fired would surface
    /// as a 500 instead of the French refusal the domain already returns.</para>
    ///
    /// <para>⚠️ <b>Both index filters key on <c>ChequeDueDate IS NOT NULL</c>, not on <c>Method = 1</c>.</b> By the
    /// invariant above only a cheque can carry a due date, so the two are equally selective — and the method form
    /// would bake <c>PaymentMethod.Cheque</c>'s ordinal into SQL, a magic number in the one place no compiler
    /// checks it.</para>
    ///
    /// <para>⚠️ <b>Hand-written, not scaffolded.</b> <c>dotnet ef</c> cannot load a freshly-built assembly on this
    /// development machine (Smart App Control / WDAC, <c>0x800711C7</c>), so this file, the paired Designer and the
    /// model snapshot were written by hand to match the two entity configurations. The delta is six columns and two
    /// indexes — small enough to verify by eye — and the result is checked against PostgreSQL's own catalog by
    /// <c>dotnet run -- verify-schema</c>, which matches indexes on <b>table + ordered columns</b> rather than on
    /// name, so a hand-chosen index name cannot produce a false failure. It must still be regenerated with the EF
    /// tool in an unrestricted environment before merge.</para>
    /// </summary>
    /// <inheritdoc />
    public partial class AddChequeDetailsToPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChequeBankName",
                table: "InstallmentPayments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ChequeDueDate",
                table: "InstallmentPayments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChequeNumber",
                table: "InstallmentPayments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChequeBankName",
                table: "Payments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ChequeDueDate",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChequeNumber",
                table: "Payments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            // « Chèques à encaisser » must see BOTH ledgers — an échéancier settled with a book of post-dated
            // cheques is the archetypal case — so the index exists on each.
            migrationBuilder.CreateIndex(
                name: "IX_InstallmentPayments_ChequeDueDate",
                table: "InstallmentPayments",
                column: "ChequeDueDate",
                filter: "\"ChequeDueDate\" IS NOT NULL AND NOT \"IsVoided\"");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ChequeDueDate",
                table: "Payments",
                column: "ChequeDueDate",
                filter: "\"ChequeDueDate\" IS NOT NULL AND NOT \"IsVoided\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_ChequeDueDate",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_InstallmentPayments_ChequeDueDate",
                table: "InstallmentPayments");

            migrationBuilder.DropColumn(
                name: "ChequeNumber",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ChequeDueDate",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ChequeBankName",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ChequeNumber",
                table: "InstallmentPayments");

            migrationBuilder.DropColumn(
                name: "ChequeDueDate",
                table: "InstallmentPayments");

            migrationBuilder.DropColumn(
                name: "ChequeBankName",
                table: "InstallmentPayments");
        }
    }
}
