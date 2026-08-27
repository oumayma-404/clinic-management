using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// The two inputs to a patient's CNAM <b>annual ceiling</b> (« plafond annuel », <c>adoption-qa-l</c> L10):
    /// how many dependants the insured person declares, and — when somebody knows it — the household's real ceiling.
    ///
    /// <para><b>What was missing.</b> <c>CnamReimbursementCalculator.Estimate</c> is
    /// <c>coefficient × VLC × rate</c> with no cap and no knowledge of what the patient has already claimed this
    /// year, so « Remboursement indicatif » told a patient who had exhausted their ceiling in March exactly what it
    /// told one who had never claimed. There were zero occurrences of <c>plafond</c> / <c>annualLimit</c> /
    /// <c>ceiling</c> anywhere in the product.</para>
    ///
    /// <para><b>Purely additive:</b> two nullable columns on <c>Patients</c> (the <c>CnamInfo</c> owned type is
    /// table-shared, so they land beside the eight <c>Cnam*</c> columns already there). No backfill and no row
    /// rewritten — a patient for whom nobody recorded a dependant count falls back to the barème's
    /// « assuré seul » figure, which is the correct reading of « not recorded ».</para>
    ///
    /// <para>⚠️ <b>The barème itself is not in the schema, and that is deliberate.</b> The 2024 amounts are sourced
    /// from two Tunisian outlets in agreement with no official CNAM publication retrieved, so they live in
    /// <c>Domain/Services/CnamPlafond</c> as a <b>default</b> that <c>CnamAnnualCeilingOverride</c> always beats.
    /// Freezing an unconfirmed table into rows would make correcting it a migration instead of an edit.</para>
    ///
    /// <para>⚠️ <b>Renumbered by hand to sort after <c>20260804120000_AddChequeDetailsToPayments</c>.</b> The EF tool
    /// stamped it with the wall clock (10:35), which is <i>earlier</i> than that migration's hand-chosen 12:00 —
    /// and its paired Designer snapshot describes the model <b>including</b> the cheque columns, so leaving it
    /// ordered first would have made the next <c>migrations add</c> diff against a model state that never existed.
    /// Nothing else changed: both migrations are additive and independent, so the applied order is immaterial to the
    /// database.</para>
    ///
    /// <para>⚠️ <b>The regenerated model snapshot recovered three entities a hand-written one had dropped.</b>
    /// <c>AuditEntries</c>, <c>BackupRuns</c> and <c>DocumentEmails</c> were present in the model and absent from
    /// the snapshot committed with the cheque migration, which is precisely the
    /// <c>ef-migration-scaffolding-hazards</c> failure mode — an incomplete snapshot makes the <i>next</i> migration
    /// re-create tables that already exist. This file's snapshot is EF-generated from the real model, so it is
    /// complete again.</para>
    /// </summary>
    /// <inheritdoc />
    public partial class AddCnamAnnualCeiling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CnamAnnualCeilingOverride",
                table: "Patients",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CnamDependantCount",
                table: "Patients",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CnamAnnualCeilingOverride",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "CnamDependantCount",
                table: "Patients");
        }
    }
}
