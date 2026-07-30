using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Reconciles the EF model with a column that already exists: <c>DentalRecords.AppointmentId</c> and its index.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both methods are deliberately empty.</b> This migration is committed for its <b>model snapshot</b> only —
    /// the same construct, and the same reason, as <c>AddConcurrencyToken</c>.
    /// </para>
    /// <para>
    /// The column and <c>IX_DentalRecords_AppointmentId</c> have existed since
    /// <c>20260717113419_AddDentalRecordAppointmentId</c>, which still creates both. What was missing was the *model*:
    /// the <c>DentalRecord</c> entity had no such property, so EF never declared it, nothing ever populated it (every
    /// row held NULL), and the next scaffold would have emitted <c>DropColumn</c> — silently deleting a link the
    /// application had just started to depend on. Adding the property is therefore a pure model change: the schema is
    /// already correct on **every** database, fresh or existing, because the 2026-07-17 migration builds it.
    /// </para>
    /// <para>
    /// Letting the generated body stand would have been wrong in both directions: on an existing database the
    /// <c>AddColumn</c> fails outright (« column "AppointmentId" of relation "DentalRecords" already exists »), and on
    /// a fresh one it would run *after* the migration that already created the column, failing identically. Guarded
    /// <c>IF NOT EXISTS</c> SQL would work but would assert that the schema might be missing, which it never is.
    /// </para>
    /// </remarks>
    public partial class ReconcileDentalRecordAppointmentLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty — see the class remarks. Snapshot-only migration.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty — nothing was applied, so there is nothing to revert.
        }
    }
}
