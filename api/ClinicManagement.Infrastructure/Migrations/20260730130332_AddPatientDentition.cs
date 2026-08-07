using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Adds <c>Patient.Dentition</c> — which set of teeth a patient is charted on, asked once on the patient instead
    /// of three times in the UI (the odontogram toggle, the fiche editor toggle, and the per-fiche badge).
    /// </summary>
    /// <remarks>
    /// The generated body was a single <c>AddColumn(nullable: false, defaultValue: 0)</c>, wrong in both halves and
    /// replaced here:
    /// <list type="number">
    /// <item><c>0</c> is <c>Child</c>, so <b>every existing patient</b> — adults included — would have been charted on
    /// baby teeth. No constant is safe: the answer depends on the row.</item>
    /// <item>It leaves a permanent <c>DEFAULT 0</c> on the column that the EF model does not declare, so model and
    /// catalog disagree from the moment it runs.</item>
    /// </list>
    /// Instead the column is added <b>nullable</b>, backfilled per row from the patient's own date of birth, then
    /// tightened to <c>NOT NULL</c> — the only version that lands a correct value on a populated table. The cutoff
    /// mirrors <c>DentitionRules.AdultFromAgeYears</c>; this migration is history, so if that constant ever moves it
    /// must NOT be edited to follow.
    /// </remarks>
    public partial class AddPatientDentition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Dentition",
                table: "Patients",
                type: "integer",
                nullable: true);

            // 0 = Child, 1 = Adult (DentitionType). Age is measured on the clinic's calendar — Tunisia is UTC+1 with
            // no DST, the same offset ClinicClock applies — so a patient whose 13th birthday is today is not counted
            // as 12 for the first hour of every Tunisian day.
            migrationBuilder.Sql(@"
                UPDATE ""Patients""
                SET ""Dentition"" = CASE
                    WHEN EXTRACT(YEAR FROM AGE(now() + interval '1 hour', ""DateOfBirth"")) < 13 THEN 0
                    ELSE 1
                END
                WHERE ""Dentition"" IS NULL;
            ");

            migrationBuilder.Sql(@"ALTER TABLE ""Patients"" ALTER COLUMN ""Dentition"" SET NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Dentition",
                table: "Patients");
        }
    }
}
