using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// M4 — patient contact details become genuinely optional, and the four sentinel literals are retired.
    ///
    /// <para>
    /// <b>This must be the last migration of the batch, and the null-safe code must already be deployed.</b>
    /// In Local mode migrations run <i>after</i> Kestrel is serving (<c>DeferredStartupService</c>), so there is
    /// a guaranteed window in which the running build sees the migrated data. Blanking the sentinels before the
    /// null-safe reads shipped would take the patient list and the header search down for the clinic — the old
    /// <c>GetPatientsQuery</c> dereferenced <c>p.PhoneNumber.Value</c> in an in-memory filter over every patient
    /// in the clinic, so one blanked row 500s the whole page.
    /// </para>
    /// </summary>
    public partial class MakePatientContactOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PhoneNumber",
                table: "Patients",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Patients",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            // Retire the four sentinels, now that NULL is expressible. Two sources produced them:
            // CreatePatientCommand (noemail@example.com / 0000000000) for anyone who left the field blank, and
            // GoogleCalendarSyncService (unknown@example.com / 000-000-0000) for patients conjured out of a
            // calendar-event title. Matched by exact value on purpose — a real patient could plausibly own a
            // near-miss like "no-email@example.com", and guessing at one is worse than leaving it alone. The
            // reconcile-money report counts near-misses separately so an operator can review those by hand.
            //
            // Ordered nullable-FIRST: these statements can only run once the columns accept NULL.
            migrationBuilder.Sql(@"
                UPDATE ""Patients""
                SET ""Email"" = NULL
                WHERE ""Email"" IN ('noemail@example.com', 'unknown@example.com');");

            migrationBuilder.Sql(@"
                UPDATE ""Patients""
                SET ""PhoneNumber"" = NULL
                WHERE ""PhoneNumber"" IN ('0000000000', '000-000-0000');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty.
            //
            // The generated Down() re-imposed NOT NULL with defaultValue: "" — which would either fail outright
            // on the rows this migration just blanked, or quietly rewrite them to empty strings, swapping one
            // indistinguishable-from-real placeholder for another. It could not restore the sentinels either:
            // the blanking is lossy by design, since a row that was already blank beforehand is afterwards
            // indistinguishable from one this migration cleared.
            //
            // Rolling this back means restoring the backup taken before the batch — see packaging/README.md,
            // and diff the reconcile-money report captured on either side of the upgrade. Shipping a Down()
            // that throws at runtime would only surface that at the worst possible moment.
        }
    }
}
