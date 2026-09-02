using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// A séance somebody <b>retired</b> stops holding its slot.
    ///
    /// <para>« Supprimer (créé par erreur) » writes <c>DisregardedAtUtc</c>: the séance leaves the agenda, the
    /// patient's history and every figure. What it could not do was give the hour back —
    /// <c>EX_Appointments_NoDoubleBooking</c> is partial on <c>Status NOT IN (5, 6)</c>, and a retired séance is
    /// still <c>Scheduled</c>, so PostgreSQL went on reserving a slot for a rendez-vous nobody could see. Booking
    /// over it was refused by a collision naming an appointment that is not on the calendar, which is the worst
    /// shape a refusal can take: correct, and unanswerable.</para>
    ///
    /// <para>One term added to the predicate, exactly as <c>AllowAcknowledgedOverlap</c> added
    /// <c>BookedWithOverlap</c> before it. Dropped and re-added rather than altered, because PostgreSQL has no
    /// ALTER for an exclusion constraint's predicate.</para>
    ///
    /// <para>⚠️ <b>This cannot fail where the current constraint stands.</b> The new predicate is strictly
    /// narrower — it exempts every row the old one exempted, plus the retired ones — so it can only ever be
    /// satisfied by a database the old one already satisfied. The <c>Down</c> is the half that can fail, and
    /// deliberately: see below.</para>
    ///
    /// <para>⚠️ <b>The application guard moves with it.</b> <c>AppointmentScheduling.OccupiesSlot</c> is the same
    /// rule in C#, and the two are one decision in two places: widened here alone, the app would wave a booking
    /// through and the INSERT would fail on a raw constraint violation; widened there alone, the app would refuse
    /// a slot the database considers free. It now takes the appointment rather than a bare status, so no call site
    /// can ask the question without the mark.</para>
    /// </summary>
    public partial class FreeTheSlotOfARetiredAppointment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Statuses 5 and 6 are Cancelled and NoShow (the enum is 1-based), carried over verbatim from
            // AddAppointmentBookingIntegrity and AllowAcknowledgedOverlap.
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restoring the wider predicate puts every retired séance back in scope, so a slot rebooked over one
            // while this migration was applied makes the ADD CONSTRAINT fail. That is correct and is the same
            // contract AllowAcknowledgedOverlap's own Down carries: a down-migration must not silently discard
            // bookings, and failing loud names the pair to resolve first.
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
    }
}
