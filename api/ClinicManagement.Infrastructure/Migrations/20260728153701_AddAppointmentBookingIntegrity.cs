using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Booking integrity for appointments (AC-P1.14–1.18, AC-P1.22, AC-P1.31).
    ///
    /// <para>
    /// Two things: the <c>BookedOutsideWorkingHours</c> marker, and the piece that cannot be done in C# — a
    /// PostgreSQL <b>exclusion constraint</b> making a double-booking impossible at the database. A
    /// check-then-insert cannot be made safe by widening the check, so the application guard in
    /// <c>AppointmentScheduling</c> exists to produce a readable French refusal, and this constraint is what
    /// actually guarantees it.
    /// </para>
    ///
    /// <para>
    /// <b>No precedent for any of this in the repo</b> — first `CREATE EXTENSION`, first generated column, first
    /// exclusion constraint — hence raw <c>Sql(...)</c> throughout, with the reason EF cannot express each step.
    /// </para>
    ///
    /// <para>
    /// <b>Operational notes.</b> Building the GiST index takes <c>ACCESS EXCLUSIVE</c> on <c>Appointments</c>
    /// (plan risk <b>R-6</b>); Cloud applies migrations before Kestrel serves, but in Local they run
    /// fire-and-forget *after* it is already serving — so schedule this batch for a quiet window. Every step is
    /// written to be re-runnable (<c>IF NOT EXISTS</c> / <c>WHERE NOT EXISTS</c>) because a throw in Local calls
    /// <c>StopApplication()</c> (<b>R-7</b>).
    /// </para>
    /// </summary>
    public partial class AddAppointmentBookingIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: the differ also emitted `AddColumn<int>("TokenVersion", "Users")` here. Removed on purpose —
            // 20260727174753_AddUserTokenVersion already creates that column, and it is present in the head
            // model snapshot, so re-adding it would fail with «column "TokenVersion" ... already exists» on
            // every existing database. The model snapshot is unaffected by removing the statement.

            // AC-P1.31: records a booking that was made outside the practitioner's hours with an explicit,
            // confirmed override, so the exception is auditable rather than silent.
            migrationBuilder.AddColumn<bool>(
                name: "BookedOutsideWorkingHours",
                table: "Appointments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // ---- AC-P1.22 (R-4): pre-flight ------------------------------------------------------------
            //
            // Refuse to add the constraint if data that already exists would violate it, naming the offending
            // pairs. It counts ONLY the pairs the *partial* constraint would reject — a cancelled-then-rebooked
            // slot is legitimate history and must not abort the migration. It never deletes a row: the whole
            // point is that an operator decides, with the ids in hand.
            //
            // Raw SQL because this is an assertion, not a schema change: EF has no way to express "fail with a
            // computed message".
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    offending text;
                    offending_count int;
                BEGIN
                    SELECT count(*), string_agg(pair, ', ')
                      INTO offending_count, offending
                    FROM (
                        SELECT a."Id" || ' ↔ ' || b."Id" AS pair
                        FROM "Appointments" a
                        JOIN "Appointments" b
                          ON a."Id" < b."Id"
                         AND a."DoctorId" IS NOT NULL
                         AND a."DoctorId" = b."DoctorId"
                         -- Cancelled (5) and NoShow (6) free the slot, exactly as the constraint below and the
                         -- application guard both have it.
                         AND a."Status" NOT IN (5, 6)
                         AND b."Status" NOT IN (5, 6)
                         AND tstzrange(
                                 a."AppointmentDateTime",
                                 a."AppointmentDateTime" + (a."Duration" * interval '1 microsecond' / 10),
                                 '[)')
                          && tstzrange(
                                 b."AppointmentDateTime",
                                 b."AppointmentDateTime" + (b."Duration" * interval '1 microsecond' / 10),
                                 '[)')
                        LIMIT 50
                    ) pairs;

                    IF offending_count > 0 THEN
                        RAISE EXCEPTION
                            'Impossible d''ajouter la contrainte anti-double-réservation : % paire(s) de rendez-vous se chevauchent déjà pour un même praticien. Corrigez-les puis relancez la migration. Paires concernées : %',
                            offending_count, offending;
                    END IF;
                END $$;
                """);

            // ---- The extension -------------------------------------------------------------------------
            //
            // `btree_gist` is required because the constraint mixes an equality operator (=, on the uuid
            // DoctorId) with an overlap operator (&&, on the range) in one GiST index. It ships
            // `trusted = true` in PostgreSQL 16, so the database owner can create it without superuser — the
            // Local installer's `clinic_user` owns the database, and Cloud runs Postgres in-stack.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

            // ---- The end column, maintained by a trigger ------------------------------------------------
            //
            // `Duration` is stored as `bigint` **ticks** (100 ns), so there is no end-time column to build a
            // range from, and the arithmetic cannot live in the constraint expression either: a GiST index needs
            // an IMMUTABLE expression.
            //
            // A `GENERATED ALWAYS AS ... STORED` column was the obvious answer and PostgreSQL rejects it —
            // `42P17: generation expression is not immutable`. `timestamptz + interval` is only **STABLE**,
            // because interval addition can involve day/month components whose meaning depends on the session
            // timezone. That rules out a generated column AND an expression index, so the end time has to be a
            // plain materialised column.
            //
            // It is kept correct by a trigger rather than by the application, deliberately: AC-P1.15 requires the
            // DATABASE to be the guarantee, and `GoogleCalendarSyncService` writes appointments straight through
            // the repository. A column maintained in C# would be silently wrong for any writer that forgot it —
            // and a wrong end time means the constraint guards the wrong window, which is worse than no
            // constraint because it looks like it works. The trigger cannot be bypassed by any writer.
            //
            // Not mapped on the entity: nothing in C# reads or writes it.
            migrationBuilder.Sql("""
                ALTER TABLE "Appointments"
                ADD COLUMN IF NOT EXISTS "AppointmentEndDateTime" timestamptz;
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION "fn_appointments_sync_end"()
                RETURNS trigger AS $$
                BEGIN
                    NEW."AppointmentEndDateTime" :=
                        NEW."AppointmentDateTime" + (NEW."Duration" * interval '1 microsecond' / 10);
                    RETURN NEW;
                END $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS "trg_appointments_sync_end" ON "Appointments";
                CREATE TRIGGER "trg_appointments_sync_end"
                BEFORE INSERT OR UPDATE OF "AppointmentDateTime", "Duration"
                ON "Appointments"
                FOR EACH ROW EXECUTE FUNCTION "fn_appointments_sync_end"();
                """);

            // Backfill existing rows. Idempotent (`WHERE ... IS NULL`) because a throw in Local mode calls
            // StopApplication() (R-7), and NOT NULL is asserted afterwards so the constraint can never see a
            // null range bound.
            migrationBuilder.Sql("""
                UPDATE "Appointments"
                SET "AppointmentEndDateTime" =
                    "AppointmentDateTime" + ("Duration" * interval '1 microsecond' / 10)
                WHERE "AppointmentEndDateTime" IS NULL;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "Appointments" ALTER COLUMN "AppointmentEndDateTime" SET NOT NULL;
                """);

            // ---- The constraint ------------------------------------------------------------------------
            //
            // AC-P1.16 — PARTIAL, on `Status NOT IN (5, 6)`. Without the predicate a cancelled slot would become
            // permanently unbookable, and rebooking a cancelled slot is the single most common scheduling action
            // in a clinic.
            //
            // AC-P1.17 (A-4) — the `DoctorId IS NOT NULL` predicate makes the NULL behaviour a STATED rule
            // rather than an accident. PostgreSQL's `=` never matches NULL, so an unassigned appointment would be
            // silently exempt anyway; declaring it explicitly says why that is intended: an appointment with no
            // practitioner is a "busy slot" belonging to nobody, so there is no one for it to double-book. The
            // application guard in `AppointmentScheduling.FindCollisionAsync` short-circuits on the same
            // condition, so guard and constraint agree.
            //
            // `WITH (...)` is omitted deliberately — the default fillfactor is right for a table this size.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint WHERE conname = 'EX_Appointments_NoDoubleBooking'
                    ) THEN
                        ALTER TABLE "Appointments"
                        ADD CONSTRAINT "EX_Appointments_NoDoubleBooking"
                        EXCLUDE USING gist (
                            "DoctorId" WITH =,
                            tstzrange("AppointmentDateTime", "AppointmentEndDateTime", '[)') WITH &&
                        )
                        WHERE ("DoctorId" IS NOT NULL AND "Status" NOT IN (5, 6));
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Appointments" DROP CONSTRAINT IF EXISTS "EX_Appointments_NoDoubleBooking";
                """);

            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS "trg_appointments_sync_end" ON "Appointments";
                DROP FUNCTION IF EXISTS "fn_appointments_sync_end"();
                ALTER TABLE "Appointments" DROP COLUMN IF EXISTS "AppointmentEndDateTime";
                """);

            // The extension is deliberately NOT dropped: it may be in use by something else, and dropping a
            // shared extension on a rollback is a far bigger side effect than leaving it installed.

            migrationBuilder.DropColumn(
                name: "BookedOutsideWorkingHours",
                table: "Appointments");
        }
    }
}
