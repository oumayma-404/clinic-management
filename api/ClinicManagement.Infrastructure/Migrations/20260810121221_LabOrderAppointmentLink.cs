using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Two unrelated changes that land together (AC-23 + AC-25).
    ///
    /// <para><b>Adds</b> <c>LabWorkOrders.AppointmentId</c> — nullable, indexed, with a real FK — so a bon de
    /// prothèse can name the séance it belongs to. ⚠️ The FK is <b><c>SetNull</c>, not <c>Cascade</c></b> (D-3):
    /// deleting the appointment must clear the link and leave the bon standing, because the piece is at the
    /// laboratory either way and cascading would delete the record of work already paid for.</para>
    ///
    /// <para><b>Drops</b> <c>Appointments.BookedOutsideWorkingHours</c> — four write sites, <b>zero readers</b> for
    /// the column's whole life (no query, no DTO, no screen, no constraint), so the « audited exception » it
    /// claimed to record was never auditable. The out-of-hours *permission* is untouched; it travels on the
    /// commands as <c>AllowOutsideWorkingHours</c> and now persists nothing.</para>
    ///
    /// <para>⚠️ <b>The drop sits last, below the additive statements, deliberately.</b> EF's differ emitted it
    /// first — it orders by schema dependency, not by data safety. Nothing here reads the dropped column, so that
    /// order was already harmless; it is reordered anyway so the additive-then-destructive shape is what a future
    /// edit inherits, because the moment a backfill is added above it the original order would silently destroy
    /// the data being copied (R-12).</para>
    ///
    /// <para>⚠️ <c>Down()</c> restores the boolean at <c>false</c> for every row. Lossless in substance rather than
    /// by luck: the column was write-only, so nothing ever distinguished its two values.</para>
    /// </summary>
    public partial class LabOrderAppointmentLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AppointmentId",
                table: "LabWorkOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LabWorkOrders_AppointmentId",
                table: "LabWorkOrders",
                column: "AppointmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_LabWorkOrders_Appointments_AppointmentId",
                table: "LabWorkOrders",
                column: "AppointmentId",
                principalTable: "Appointments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Destructive, and therefore last — see the note above.
            migrationBuilder.DropColumn(
                name: "BookedOutsideWorkingHours",
                table: "Appointments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LabWorkOrders_Appointments_AppointmentId",
                table: "LabWorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_LabWorkOrders_AppointmentId",
                table: "LabWorkOrders");

            migrationBuilder.DropColumn(
                name: "AppointmentId",
                table: "LabWorkOrders");

            migrationBuilder.AddColumn<bool>(
                name: "BookedOutsideWorkingHours",
                table: "Appointments",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
