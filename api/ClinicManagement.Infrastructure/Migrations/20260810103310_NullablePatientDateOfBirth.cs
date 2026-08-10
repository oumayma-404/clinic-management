using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Makes <c>Patients.DateOfBirth</c> nullable (AC-18). A walk-in registered at the desk with nothing but a name
    /// genuinely has no date of birth, and the NOT NULL column is what forced <c>PatientFromRequest</c> to
    /// substitute « thirty years ago » — which stored a birthday nobody gave us and, through
    /// <c>DentitionRules</c>, charted every undated patient on adult teeth.
    ///
    /// <para><b>Purely a widening — no backfill, and deliberately none (D-1).</b> Existing rows keep whatever they
    /// hold, including the fabricated dates already written: this migration cannot tell a real 1994 birthday from a
    /// substituted one, and guessing would destroy real data to tidy up invented data. Nothing is dropped either,
    /// so the drops-below-the-backfill ordering trap (R-12) does not arise here.</para>
    ///
    /// <para>⚠️ <b><c>Down()</c> is lossy and cannot be otherwise.</b> Narrowing back to NOT NULL has to give the
    /// rows that are legitimately null *something*, and EF's scaffolded choice is <c>0001-01-01</c> — a sentinel of
    /// exactly the kind this change exists to retire. Export before reverting.</para>
    /// </summary>
    public partial class NullablePatientDateOfBirth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "DateOfBirth",
                table: "Patients",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "DateOfBirth",
                table: "Patients",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }
    }
}
