using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Gives the three aggregates that answer « qui a produit ceci ? » a nullable <c>DoctorId</c>, and promotes
    /// <c>WaitingListEntries.PreferredDoctorId</c> to a real foreign key (<c>adoption-qa-l</c> L9).
    ///
    /// <para><b>What was missing.</b> <c>DoctorId</c> existed on exactly three entities in the entire model —
    /// <c>Appointment</c> (the <b>only</b> FK to <c>Doctors</c>), <c>RecurringAppointment</c>, and
    /// <c>WaitingListEntry.PreferredDoctorId</c>, which was a bare <c>Property</c> — and on nothing carrying money or
    /// clinical work. <c>Invoice</c>, <c>InvoiceLine</c>, <c>Payment</c>, <c>TreatmentPlan</c>, <c>Installment</c>,
    /// <c>InstallmentPayment</c>, <c>DentalRecord</c> and <c>Expense</c> had none, <c>Patient</c> carried no doctor
    /// assignment, and <c>Features/Dashboard/</c> contained <b>zero</b> occurrences of <c>Doctor</c> across all four
    /// readers. So a practice with two dentists could not answer what either of them earned.</para>
    ///
    /// <para><b>Nullable, and nullable means nullable.</b> A visit booked with no practitioner is a real thing (a
    /// « créneau occupé », a walk-in recorded by reception), and every historical row predates the column. The
    /// backfill below therefore attributes only what is <i>knowable</i> and leaves the rest null — inventing a
    /// practitioner would silently credit one dentist with another's work, which is worse than admitting ignorance.
    /// Every read tolerates null.</para>
    ///
    /// <para>⚠️ <b>The orphan cleanup before the waiting-list FK is required, not defensive.</b>
    /// <c>PreferredDoctorId</c> has been an unconstrained <c>uuid</c> for the whole life of the product, so nothing
    /// prevented it holding an id from another clinic or one whose <c>Doctor</c> row has since been deleted. Adding
    /// the FK over such a row fails the migration outright — on the operator's database, after the schema is already
    /// half-applied. Nulling them first is the difference between « three queue entries forget a preference » and
    /// « the upgrade will not install ». This is precisely the cost of the bare Guid that the spec cites this column
    /// to illustrate.</para>
    ///
    /// <para>⚠️ <b>Attribution, not authorization.</b> It answers who earned a figure. Per-practitioner data scoping
    /// (« this dentist sees only their own patients ») is a separate decision with its own blast radius and is
    /// deliberately not here.</para>
    ///
    /// <para>⚠️ Run <c>dotnet run -- reconcile-money</c> before and after and diff the output: no figure should move.
    /// Attribution adds a dimension to the money reads and changes no arithmetic, so a drift would mean a filter
    /// leaked into an unfiltered total. Then run <c>dotnet run -- verify-schema</c> and read the new
    /// <c>practitioner-attribution-backfill</c> line — a backfill that covered zero rows is the failure mode only
    /// that verb can see.</para>
    /// </summary>
    /// <inheritdoc />
    public partial class AddPractitionerAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DoctorId",
                table: "TreatmentPlans",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DoctorId",
                table: "Invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DoctorId",
                table: "DentalRecords",
                type: "uuid",
                nullable: true);


            // ── Backfill: attribute what is knowable, leave the rest null ────────────────────────────────
            //
            // A fiche de soins and a note d'honoraires each carry the visit they document, and that visit already
            // records its practitioner. That is the one reliable historical source, so it is the only one used.
            migrationBuilder.Sql("""
                UPDATE "DentalRecords" r
                SET "DoctorId" = a."DoctorId"
                FROM "Appointments" a
                WHERE r."AppointmentId" = a."Id" AND a."DoctorId" IS NOT NULL;
                """);

            migrationBuilder.Sql("""
                UPDATE "Invoices" i
                SET "DoctorId" = a."DoctorId"
                FROM "Appointments" a
                WHERE i."AppointmentId" = a."Id" AND a."DoctorId" IS NOT NULL;
                """);

            // A devis has no appointment of its own, so it is attributed through the visits booked against its acts.
            // ⚠️ `MIN("AppointmentDateTime")` picks the EARLIEST such visit deterministically — the séance that
            // started the treatment. Without an ordering the result would depend on the plan's row order, so two
            // runs of the same migration on the same data could attribute the same devis to two different dentists.
            migrationBuilder.Sql("""
                UPDATE "TreatmentPlans" p
                SET "DoctorId" = src."DoctorId"
                FROM (
                    SELECT DISTINCT ON (i."TreatmentPlanId")
                           i."TreatmentPlanId" AS plan_id, a."DoctorId"
                    FROM "TreatmentPlanItems" i
                    JOIN "Appointments" a ON a."TreatmentPlanItemId" = i."Id"
                    WHERE a."DoctorId" IS NOT NULL
                    ORDER BY i."TreatmentPlanId", a."AppointmentDateTime"
                ) src
                WHERE p."Id" = src.plan_id;
                """);

            // Finally, an invoice raised from a devis inherits that devis's practitioner — but only where the
            // invoice has none of its own, so a direct appointment link (attributed above) always wins. Runs after
            // the plan pass so a plan attributed a moment ago propagates in the same migration.
            migrationBuilder.Sql("""
                UPDATE "Invoices" i
                SET "DoctorId" = p."DoctorId"
                FROM "TreatmentPlans" p
                WHERE i."TreatmentPlanId" = p."Id"
                  AND i."DoctorId" IS NULL
                  AND p."DoctorId" IS NOT NULL;
                """);

            // ── Orphan cleanup, REQUIRED before the waiting-list FK (see the type remarks) ──────────────
            migrationBuilder.Sql("""
                UPDATE "WaitingListEntries" w
                SET "PreferredDoctorId" = NULL
                WHERE w."PreferredDoctorId" IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM "Doctors" d WHERE d."Id" = w."PreferredDoctorId");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_WaitingListEntries_PreferredDoctorId",
                table: "WaitingListEntries",
                column: "PreferredDoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlans_DoctorId",
                table: "TreatmentPlans",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_DoctorId",
                table: "Invoices",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_DentalRecords_DoctorId",
                table: "DentalRecords",
                column: "DoctorId");

            migrationBuilder.AddForeignKey(
                name: "FK_DentalRecords_Doctors_DoctorId",
                table: "DentalRecords",
                column: "DoctorId",
                principalTable: "Doctors",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_Doctors_DoctorId",
                table: "Invoices",
                column: "DoctorId",
                principalTable: "Doctors",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TreatmentPlans_Doctors_DoctorId",
                table: "TreatmentPlans",
                column: "DoctorId",
                principalTable: "Doctors",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_WaitingListEntries_Doctors_PreferredDoctorId",
                table: "WaitingListEntries",
                column: "PreferredDoctorId",
                principalTable: "Doctors",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DentalRecords_Doctors_DoctorId",
                table: "DentalRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Doctors_DoctorId",
                table: "Invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_TreatmentPlans_Doctors_DoctorId",
                table: "TreatmentPlans");

            migrationBuilder.DropForeignKey(
                name: "FK_WaitingListEntries_Doctors_PreferredDoctorId",
                table: "WaitingListEntries");

            migrationBuilder.DropIndex(
                name: "IX_WaitingListEntries_PreferredDoctorId",
                table: "WaitingListEntries");

            migrationBuilder.DropIndex(
                name: "IX_TreatmentPlans_DoctorId",
                table: "TreatmentPlans");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_DoctorId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_DentalRecords_DoctorId",
                table: "DentalRecords");

            migrationBuilder.DropColumn(
                name: "DoctorId",
                table: "TreatmentPlans");

            migrationBuilder.DropColumn(
                name: "DoctorId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "DoctorId",
                table: "DentalRecords");
        }
    }
}
