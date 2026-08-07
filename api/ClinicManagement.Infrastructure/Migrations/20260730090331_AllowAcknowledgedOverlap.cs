using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Makes a <b>deliberate</b> double-booking possible without dropping the protection against an accidental one.
    ///
    /// <para><c>AddAppointmentBookingIntegrity</c> added <c>EX_Appointments_NoDoubleBooking</c>, which makes any
    /// overlap for the same practitioner impossible at the database. That is right for the accident it was written
    /// for and wrong for the day a clinic actually has: a second chair, an assistant preparing one patient while the
    /// dentist starts another, an emergency squeezed into a taken slot. With the constraint in place the refusal
    /// could not be overridden at any layer — the application guard and the UI block sat downstream of a hard
    /// database rule.</para>
    ///
    /// <para>Rather than dropping the constraint, its <b>predicate</b> gains one term: a row the user explicitly
    /// acknowledged as an overlap (<c>BookedWithOverlap</c>) falls outside the constraint's scope. Two unacknowledged
    /// bookings still cannot overlap, so every <i>accidental</i> double-booking is still refused by the database,
    /// while a deliberate one is recorded as deliberate. The flag is set only when a collision was actually detected
    /// (<c>CreateAppointmentCommand</c> / <c>UpdateAppointmentCommand</c>), and cleared when a booking moves to a
    /// slot that no longer collides — so no row keeps an exemption it no longer needs.</para>
    ///
    /// <para>Raw <c>Sql(...)</c> for the constraint because EF cannot express an exclusion constraint at all; the
    /// column above it is EF-generated. Verified by <c>dotnet run -- verify-schema</c>, which asserts the constraint
    /// exists <b>and is partial</b> — the added term keeps it partial, so that assertion still holds unchanged.</para>
    /// </summary>
    public partial class AllowAcknowledgedOverlap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BookedWithOverlap",
                table: "Appointments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Recreate the exclusion constraint with the acknowledgement term. Dropped and re-added rather than
            // altered because PostgreSQL has no ALTER for an exclusion constraint's predicate.
            //
            // Statuses 5 and 6 are Cancelled and NoShow (the enum is 1-based), carried over verbatim from
            // AddAppointmentBookingIntegrity: a cancelled slot must stay rebookable, which is why the constraint is
            // partial in the first place.
            migrationBuilder.Sql("""
                ALTER TABLE "Appointments" DROP CONSTRAINT IF EXISTS "EX_Appointments_NoDoubleBooking";

                ALTER TABLE "Appointments"
                ADD CONSTRAINT "EX_Appointments_NoDoubleBooking"
                EXCLUDE USING gist (
                    "DoctorId" WITH =,
                    tstzrange("AppointmentDateTime", "AppointmentEndDateTime", '[)') WITH &&
                )
                WHERE ("DoctorId" IS NOT NULL AND "Status" NOT IN (5, 6) AND NOT "BookedWithOverlap");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the original predicate BEFORE dropping the column it references — the reverse order would
            // leave the constraint referring to a column that no longer exists.
            //
            // Any row currently exempt via the flag becomes subject to the constraint again, so a genuinely
            // overlapping pair created under this migration makes the ADD CONSTRAINT fail. That is correct: a
            // down-migration must not silently discard bookings, and failing loud names the pair to resolve first.
            migrationBuilder.Sql("""
                ALTER TABLE "Appointments" DROP CONSTRAINT IF EXISTS "EX_Appointments_NoDoubleBooking";

                ALTER TABLE "Appointments"
                ADD CONSTRAINT "EX_Appointments_NoDoubleBooking"
                EXCLUDE USING gist (
                    "DoctorId" WITH =,
                    tstzrange("AppointmentDateTime", "AppointmentEndDateTime", '[)') WITH &&
                )
                WHERE ("DoctorId" IS NOT NULL AND "Status" NOT IN (5, 6));
                """);

            migrationBuilder.DropColumn(
                name: "BookedWithOverlap",
                table: "Appointments");
        }
    }
}
