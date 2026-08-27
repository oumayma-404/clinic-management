using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Group B — six nullable columns, three per payment ledger: when a cheque was taken to the bank, by whom,
    /// and their name. Purely additive, all null for every existing row: a cheque received before today has no
    /// banked date, which is a different statement from « it was never banked » and is why they are nullable
    /// rather than defaulted.
    ///
    /// <para>⚠️ <b>No CHECK constraint.</b> « Only a cheque can be marked banked » lives in
    /// <c>ChequeBankedStamp.For</c>; a copy here would surface as a 500 instead of the French refusal, so
    /// <c>verify-schema</c> gained <c>cheque-banked-only-on-cheques</c> to verify it over both ledgers instead —
    /// exactly the arrangement <c>AddChequeDetailsToPayments</c> established one migration earlier.</para>
    ///
    /// <para>⚠️ <b>An <c>AlterColumn Patients.DateOfBirth → nullable</c> was scaffolded into this migration and
    /// removed by hand.</b> That model change belongs to Part 4's <c>NullableDobLabOrderAppointment</c>, which
    /// also has to reorder its own drops below its backfill; it was merely in the working tree, unmigrated, when
    /// this was generated. The paired <c>.Designer.cs</c> and the model snapshot were reverted to
    /// <c>DateTime</c> for the same reason — leaving them nullable would make the differ believe the change had
    /// already shipped and silently emit nothing for it, leaving the column <c>NOT NULL</c> in every database
    /// while the model said otherwise.</para>
    /// </summary>
    public partial class AddChequeBankedStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChequeBankedByName",
                table: "Payments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChequeBankedByUserId",
                table: "Payments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ChequeBankedOn",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChequeBankedByName",
                table: "InstallmentPayments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChequeBankedByUserId",
                table: "InstallmentPayments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ChequeBankedOn",
                table: "InstallmentPayments",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChequeBankedByName",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ChequeBankedByUserId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ChequeBankedOn",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ChequeBankedByName",
                table: "InstallmentPayments");

            migrationBuilder.DropColumn(
                name: "ChequeBankedByUserId",
                table: "InstallmentPayments");

            migrationBuilder.DropColumn(
                name: "ChequeBankedOn",
                table: "InstallmentPayments");
        }
    }
}
