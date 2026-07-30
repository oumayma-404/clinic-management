using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>Adds <c>Patient.ReferredBy</c> (« recommandé par »).</summary>
    /// <remarks>
    /// The generated body also re-emitted <c>Appointments.BookedWithOverlap</c>, the
    /// <c>UserDashboardPreferences</c> table and the <c>AppointmentProcedures</c> table, because the model-snapshot
    /// state of the sibling migrations that own them was not committed and EF's differ therefore saw them as new.
    /// They are removed: each has its own migration, and the startup <c>Database.Migrate()</c> fails outright on a
    /// repeated <c>CREATE TABLE</c> / <c>ADD COLUMN</c>.
    /// </remarks>
    public partial class AddPatientReferredBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReferredBy",
                table: "Patients",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReferredBy",
                table: "Patients");
        }
    }
}
