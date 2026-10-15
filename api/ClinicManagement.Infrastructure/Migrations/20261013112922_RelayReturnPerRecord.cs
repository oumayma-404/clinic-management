using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// <c>clinic-pc-copy</c> review fixes (2026-10-13), both additive.
    ///
    /// <para><b><c>ClinicChanges.Automatic</c></b> — a change a background job made. At the return a job's write never
    /// outranks the cabinet's work: counting the cloud's agenda job as « changed in the cloud » kept the cloud's version of
    /// every visit the cabinet cancelled or moved during a cut. <c>false</c> for every existing row is right — nobody
    /// knows, and « a person did it » is the safe reading (the cloud's version is kept and the PC's listed).</para>
    ///
    /// <para><b><c>EX_Appointments_NoDoubleBooking</c> becomes <c>DEFERRABLE INITIALLY IMMEDIATE</c></b>, predicate
    /// unchanged (carried verbatim from <c>FreeTheSlotOfARetiredAppointment</c>). Every ordinary save behaves exactly as
    /// before — an immediate constraint is checked at the end of each statement. Only the relay's applies defer it
    /// (<c>SET CONSTRAINTS ALL DEFERRED</c>), so a visit moved into a slot another one left, or two visits swapped, land
    /// as a whole instead of failing on whichever row happened to come first: that refused the whole return, for ever,
    /// and kept the PC de secours's copy from ever catching up.</para>
    /// </summary>
    public partial class RelayReturnPerRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Automatic",
                table: "ClinicChanges",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                ALTER TABLE "Appointments" DROP CONSTRAINT IF EXISTS "EX_Appointments_NoDoubleBooking";

                ALTER TABLE "Appointments"
                ADD CONSTRAINT "EX_Appointments_NoDoubleBooking"
                EXCLUDE USING gist (
                    "DoctorId" WITH =,
                    tstzrange("AppointmentDateTime", "AppointmentEndDateTime", '[)') WITH &&
                )
                WHERE ("DoctorId" IS NOT NULL
                       AND "Status" NOT IN (5, 6)
                       AND "DisregardedAtUtc" IS NULL
                       AND NOT "BookedWithOverlap")
                DEFERRABLE INITIALLY IMMEDIATE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Appointments" DROP CONSTRAINT IF EXISTS "EX_Appointments_NoDoubleBooking";

                ALTER TABLE "Appointments"
                ADD CONSTRAINT "EX_Appointments_NoDoubleBooking"
                EXCLUDE USING gist (
                    "DoctorId" WITH =,
                    tstzrange("AppointmentDateTime", "AppointmentEndDateTime", '[)') WITH &&
                )
                WHERE ("DoctorId" IS NOT NULL
                       AND "Status" NOT IN (5, 6)
                       AND "DisregardedAtUtc" IS NULL
                       AND NOT "BookedWithOverlap");
                """);

            migrationBuilder.DropColumn(
                name: "Automatic",
                table: "ClinicChanges");
        }
    }
}
