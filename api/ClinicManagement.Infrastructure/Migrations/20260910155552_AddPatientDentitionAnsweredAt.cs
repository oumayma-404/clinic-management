using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Records when a human last answered « quelle denture ? » — see <c>Patient.DentitionAnsweredAtUtc</c>.
    /// <c>Dentition</c> is NOT NULL defaulting to <c>Adult</c>, so it cannot tell an answer from a default, and
    /// the odontogramme's prompt was therefore re-asking on every reload of a patient with no date of birth.
    /// </summary>
    public partial class AddPatientDentitionAnsweredAt : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            /*
             * ⚠️ **Deliberately NOT backfilled**, unlike `AddInstallmentIsAutoRaised` beside it — and the two
             * cases are opposites, which is why. There, every pre-existing row HAD a discriminator in the data
             * (`DueDate = AcceptedDate` to the instant), so leaving it null was the lossy choice. Here there is
             * nothing to read: a stored `Adult` is exactly what an un-asked patient carries, so any backfill
             * would be inventing an answer — and it would invent it precisely for the undated walk-ins the
             * prompt exists to catch, silencing it for them for ever. Null means « ask once », answering
             * settles it, and the cost is one prompt per already-decided patient.
             *
             * ⚠️ No `xmin` in this migration, and that is checked rather than assumed: `Entity<TId>.Version`
             * maps onto PostgreSQL's system column, so the differ emits `AddColumn<uint>("xmin")` for all 38
             * entities whenever the model snapshot is stale. It did not here.
             */
            migrationBuilder.AddColumn<DateTime>(
                name: "DentitionAnsweredAtUtc",
                table: "Patients",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DentitionAnsweredAtUtc",
                table: "Patients");
        }
    }
}
