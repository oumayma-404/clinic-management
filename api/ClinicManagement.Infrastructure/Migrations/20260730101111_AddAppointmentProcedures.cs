using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// A séance can hold several acts. Adds the <c>AppointmentProcedures</c> child table and **backfills one row
    /// per existing appointment that has an act**, so the collection is authoritative from the first request
    /// instead of the agenda showing « aucun acte » for every visit ever booked.
    /// </summary>
    /// <remarks>
    /// The generated body also carried <c>Patients.ReferredBy</c>, <c>Appointments.BookedWithOverlap</c> and the
    /// <c>UserDashboardPreferences</c> table: those belong to three sibling migrations whose model-snapshot state
    /// was not committed, so EF's differ re-emitted them here. They are removed — each is created by its own
    /// migration, and applying the same <c>CREATE TABLE</c> / <c>ADD COLUMN</c> twice fails the startup
    /// <c>Database.Migrate()</c>.
    /// </remarks>
    public partial class AddAppointmentProcedures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppointmentProcedures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcedureTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcedureName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    ColorHex = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    TreatmentPlanItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    SequenceNumber = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentProcedures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppointmentProcedures_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AppointmentProcedures_ProcedureTypes_ProcedureTypeId",
                        column: x => x.ProcedureTypeId,
                        principalTable: "ProcedureTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentProcedures_AppointmentId",
                table: "AppointmentProcedures",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentProcedures_ProcedureTypeId",
                table: "AppointmentProcedures",
                column: "ProcedureTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentProcedures_TreatmentPlanItemId",
                table: "AppointmentProcedures",
                column: "TreatmentPlanItemId");

            // Backfill: every appointment that already names an act becomes a one-act séance.
            //
            // Not optional. The collection is what the agenda, the edit dialog and the devis read-back read from
            // now on, so without this every historical appointment would render as having no act at all — and the
            // first edit of one would then *save* that emptiness back over its ProcedureTypeId.
            //
            // Name/duration/colour are read from the appointment's own snapshot where it has one and from the
            // catalog otherwise, in that order: the snapshot is what the visit was booked with, and preferring the
            // live catalog value would silently rewrite the colour of a visit booked before a procedure was
            // recoloured. TreatmentPlanItemId is carried across so a plan-scheduled visit keeps speaking for its
            // act through the new path as well as the old scalar.
            migrationBuilder.Sql("""
                INSERT INTO "AppointmentProcedures" (
                    "Id", "AppointmentId", "ProcedureTypeId", "ProcedureName",
                    "DurationMinutes", "ColorHex", "TreatmentPlanItemId", "SequenceNumber")
                SELECT
                    gen_random_uuid(),
                    a."Id",
                    a."ProcedureTypeId",
                    pt."Name",
                    COALESCE(a."ProcedureDurationMinutes", pt."DefaultDurationMinutes"),
                    COALESCE(a."ProcedureColorHex", pt."ColorHex"),
                    a."TreatmentPlanItemId",
                    0
                FROM "Appointments" a
                LEFT JOIN "ProcedureTypes" pt ON pt."Id" = a."ProcedureTypeId"
                WHERE a."ProcedureTypeId" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The parent's scalars were never dropped, so the one-act case survives the rollback unaided; the
            // second and third acts of a grouped séance do not, and cannot — there is nowhere to put them.
            migrationBuilder.DropTable(
                name: "AppointmentProcedures");
        }
    }
}
