using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Drops the two columns « Importer depuis Google »'s retirement left behind.
    ///
    /// <para><c>Clinics.GoogleCalendarHoldsOnlyAppointments</c> was the practice's declaration that its calendar
    /// held nothing but appointments, and the only thing that ever read it was the import gate. Left standing it
    /// would have been a switch in the agenda's Google popover that changed nothing.</para>
    ///
    /// <para><c>Appointments.DisregardedReason</c> is a separate decision that landed in the same pass:
    /// « Retirer de la liste » no longer asks for a motif. It shipped demanding one on « Rien à facturer »'s
    /// reasoning, and the parallel does not hold — that mark is a claim about money, this one asserts nothing — so
    /// the column was write-only from the day it shipped (nothing ever read it back into a DTO or a screen).</para>
    ///
    /// <para>⚠️ <b>Data loss, and it is intended:</b> any motif already typed is discarded. The mark itself
    /// (<c>DisregardedAtUtc</c>, <c>DisregardedByUserId</c>) and <c>AuditSaveChangesInterceptor</c>'s own trail are
    /// untouched, so « qui a retiré cette séance, et quand » stays answerable. Nothing else read the column, so
    /// no worklist, figure or export changes.</para>
    ///
    /// <para>⚠️ No <c>CalendarImportRun</c> table, <c>CalendarImportRunId</c> or review-stamp column is touched.
    /// The undo deliberately outlived the importer: a cabinet whose worklist still holds an import it made has no
    /// other way back, and those rows are what the revert reads.</para>
    /// </summary>
    public partial class RetireGoogleCalendarImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoogleCalendarHoldsOnlyAppointments",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "DisregardedReason",
                table: "Appointments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "GoogleCalendarHoldsOnlyAppointments",
                table: "Clinics",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DisregardedReason",
                table: "Appointments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }
    }
}
