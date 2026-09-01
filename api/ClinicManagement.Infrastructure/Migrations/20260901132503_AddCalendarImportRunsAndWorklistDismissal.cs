using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarImportRunsAndWorklistDismissal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ⚠️ KEPT, not a scaffolder accident to undo. The new `(ClinicId, DisregardedAtUtc)` index below is
            // a strict superset: `ClinicId` leads it, so every query that used the single-column index — which is
            // most reads in the product — is served by the composite one, and PostgreSQL needs no hint to do it.
            // Leaving the old index in place would also put the catalog permanently ahead of the EF model, which
            // is exactly the drift `verify-schema` exists to report.
            migrationBuilder.DropIndex(
                name: "IX_Appointments_ClinicId",
                table: "Appointments");

            migrationBuilder.AddColumn<Guid>(
                name: "CalendarImportRunId",
                table: "Patients",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CalendarReviewDismissedAtUtc",
                table: "Patients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CalendarImportRunId",
                table: "Appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DisregardedAtUtc",
                table: "Appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisregardedByUserId",
                table: "Appointments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisregardedReason",
                table: "Appointments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CalendarImportRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TriggeredByUserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    WindowFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WindowToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AppointmentsCreated = table.Column<int>(type: "integer", nullable: false),
                    PatientsCreated = table.Column<int>(type: "integer", nullable: false),
                    AppointmentsUpdated = table.Column<int>(type: "integer", nullable: false),
                    AppointmentsLinked = table.Column<int>(type: "integer", nullable: false),
                    RevertedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevertedByUserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AppointmentsDeleted = table.Column<int>(type: "integer", nullable: true),
                    PatientsDeleted = table.Column<int>(type: "integer", nullable: true),
                    RowsKept = table.Column<int>(type: "integer", nullable: true)
                    // ⚠️ The scaffolder emitted `xmin = table.Column<uint>(type: "xid", …)` here and it was
                    // removed by hand — the same line `AddClinicSubscriptions` and `AddSuppliers` each had to
                    // delete. `Entity<T>.Version` maps onto PostgreSQL's *system* column, so the differ writes it
                    // out as a real one and the migration dies with « column name "xmin" conflicts with a system
                    // column name ». Every row still gets its concurrency token from the system column.
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarImportRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalendarImportRuns_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Patients_CalendarImportRunId",
                table: "Patients",
                column: "CalendarImportRunId",
                filter: "\"CalendarImportRunId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Patients_ClinicId_CalendarReviewDismissedAtUtc",
                table: "Patients",
                columns: new[] { "ClinicId", "CalendarReviewDismissedAtUtc" },
                filter: "\"CalendarReviewDismissedAtUtc\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_CalendarImportRunId",
                table: "Appointments",
                column: "CalendarImportRunId",
                filter: "\"CalendarImportRunId\" IS NOT NULL");

            // ⚠️ Deliberately NOT partial, unlike the three filtered indexes around it. `CountByStatusBetweenAsync`
            // and `GetStatusTimelineAsync` read this column as `IS NULL` — i.e. over the overwhelming majority of a
            // clinic's agenda — so a `WHERE "DisregardedAtUtc" IS NOT NULL` filter would exclude exactly the rows
            // those queries want.
            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ClinicId_DisregardedAtUtc",
                table: "Appointments",
                columns: new[] { "ClinicId", "DisregardedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarImportRuns_ClinicId_StartedAtUtc",
                table: "CalendarImportRuns",
                columns: new[] { "ClinicId", "StartedAtUtc" });

            // ────────────────────────────────────────────────────────────────────────────────────────────────
            // The backfill. Required, not tidy — `BackfillDentalRecordAppointmentLinks`' own words.
            //
            // ⚠️ WITHOUT THIS the undo only ever covers imports run from today onward, which is precisely the
            // cabinet that does not need it: the practice this feature exists for already pressed the button, has
            // a worklist full of phantom séances and a hundred placeholder fiches, and nothing on any of those
            // rows says which pass created them. So history is reconstructed here from the evidence that survives.
            //
            // ⚠️ It sits BELOW every DDL statement above, so a later edit inherits that order — the
            // `AddSuppliers` hazard, where the scaffolder put a `DropColumn` above the backfill that read it.
            //
            // THREE SIGNALS, and the third is what makes it safe:
            //
            //   · a PLACEHOLDER PATIENT (`CalendarImportPendingReviewSince IS NOT NULL`) is exact — only the
            //     calendar import writes that stamp, and it clears the moment a human completes the fiche, so a
            //     record the practice has already adopted is correctly left alone.
            //
            //   · an IMPORTED APPOINTMENT looks like `DoctorId IS NULL` with a Google event id — but so does an
            //     ordinary booking made without a practitioner and pushed to Google, so that pair ALONE would
            //     offer to delete the cabinet's own work.
            //
            //   · so a run is only synthesised around a BURST that contains at least one placeholder patient
            //     (the `HAVING` below). A lone hand-booked appointment is never in one; an import creates its
            //     rows seconds apart. This is the discriminator, and it is deliberately conservative: a burst
            //     with no placeholder is skipped whole rather than guessed at.
            //
            // ⚠️ The appointment predicate also takes rows whose `GoogleCalendarEventId` is NULL when they belong
            // to a placeholder patient. That is not laxity — it is the only way to recover the visits a practice
            // ALREADY CANCELLED while trying to tidy up, because cancelling nulls that column (and deletes the
            // Google event). Those are the rows inflating the taux d'absence, so missing them would leave the
            // complaint that started this feature unfixed.
            //
            // ⚠️ The run id is DERIVED (`md5(clinic || first row's instant)`), never `gen_random_uuid()`: that
            // makes `Up()` re-runnable and, with the `NOT EXISTS` guard, safe to re-run on a populated database.
            // Every write is additionally gated on `CalendarImportRunId IS NULL`, so a row already attributed to
            // a real run is never re-stamped.
            //
            // The recorded window is the one the pass actually read (−7 days … +90 days from its own first row):
            // stated as it was, not as it now is, since the window is narrowed by this same release.
            migrationBuilder.Sql(@"
                CREATE TEMP TABLE _cal_import_backfill_rows AS
                WITH placeholders AS (
                    SELECT ""Id"", ""ClinicId"", ""CreatedAt""
                    FROM ""Patients""
                    WHERE ""CalendarImportPendingReviewSince"" IS NOT NULL
                      AND ""CalendarImportRunId"" IS NULL
                ),
                candidates AS (
                    SELECT 'P'::text AS kind, p.""Id"" AS row_id, p.""ClinicId"" AS clinic_id,
                           p.""CreatedAt"" AS created_at
                    FROM placeholders p
                    UNION ALL
                    SELECT 'A'::text, a.""Id"", a.""ClinicId"", a.""CreatedAt""
                    FROM ""Appointments"" a
                    WHERE a.""CalendarImportRunId"" IS NULL
                      AND a.""DoctorId"" IS NULL
                      AND (
                            a.""GoogleCalendarEventId"" IS NOT NULL
                         OR a.""PatientId"" IN (SELECT ""Id"" FROM placeholders)
                      )
                ),
                marked AS (
                    SELECT kind, row_id, clinic_id, created_at,
                           CASE
                               WHEN LAG(created_at) OVER w IS NULL
                                 OR created_at - LAG(created_at) OVER w > INTERVAL '10 minutes'
                               THEN 1 ELSE 0
                           END AS starts_run
                    FROM candidates
                    WINDOW w AS (PARTITION BY clinic_id ORDER BY created_at)
                )
                SELECT kind, row_id, clinic_id, created_at,
                       SUM(starts_run) OVER (
                           PARTITION BY clinic_id ORDER BY created_at ROWS UNBOUNDED PRECEDING
                       ) AS burst
                FROM marked;

                CREATE TEMP TABLE _cal_import_backfill_runs AS
                SELECT clinic_id,
                       burst,
                       MD5(clinic_id::text || '|' || MIN(created_at)::text)::uuid AS run_id,
                       MIN(created_at) AS started_at,
                       MAX(created_at) AS finished_at,
                       COUNT(*) FILTER (WHERE kind = 'A') AS appointments_created,
                       COUNT(*) FILTER (WHERE kind = 'P') AS patients_created
                FROM _cal_import_backfill_rows
                GROUP BY clinic_id, burst
                HAVING COUNT(*) FILTER (WHERE kind = 'P') > 0;

                INSERT INTO ""CalendarImportRuns"" (
                    ""Id"", ""ClinicId"", ""StartedAtUtc"", ""CompletedAtUtc"", ""TriggeredByUserId"",
                    ""WindowFromUtc"", ""WindowToUtc"",
                    ""AppointmentsCreated"", ""PatientsCreated"", ""AppointmentsUpdated"", ""AppointmentsLinked""
                )
                SELECT r.run_id, r.clinic_id, r.started_at, r.finished_at, 'job|CalendarImportBackfill',
                       r.started_at - INTERVAL '7 days', r.started_at + INTERVAL '90 days',
                       r.appointments_created, r.patients_created, 0, 0
                FROM _cal_import_backfill_runs r
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""CalendarImportRuns"" e WHERE e.""Id"" = r.run_id
                );

                UPDATE ""Appointments"" a
                SET ""CalendarImportRunId"" = r.run_id
                FROM _cal_import_backfill_rows b
                JOIN _cal_import_backfill_runs r
                  ON r.clinic_id = b.clinic_id AND r.burst = b.burst
                WHERE b.kind = 'A'
                  AND a.""Id"" = b.row_id
                  AND a.""CalendarImportRunId"" IS NULL;

                UPDATE ""Patients"" p
                SET ""CalendarImportRunId"" = r.run_id
                FROM _cal_import_backfill_rows b
                JOIN _cal_import_backfill_runs r
                  ON r.clinic_id = b.clinic_id AND r.burst = b.burst
                WHERE b.kind = 'P'
                  AND p.""Id"" = b.row_id
                  AND p.""CalendarImportRunId"" IS NULL;

                DROP TABLE _cal_import_backfill_rows;
                DROP TABLE _cal_import_backfill_runs;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The backfill needs no inverse: every row it wrote is either in `CalendarImportRuns` (dropped whole
            // below) or a `CalendarImportRunId` on a column that is itself dropped below. Nothing it touched
            // survives this method, so an explicit un-stamp would be writing to columns on their way out.
            migrationBuilder.DropTable(
                name: "CalendarImportRuns");

            migrationBuilder.DropIndex(
                name: "IX_Patients_CalendarImportRunId",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_Patients_ClinicId_CalendarReviewDismissedAtUtc",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_CalendarImportRunId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_ClinicId_DisregardedAtUtc",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "CalendarImportRunId",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "CalendarReviewDismissedAtUtc",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "CalendarImportRunId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "DisregardedAtUtc",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "DisregardedByUserId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "DisregardedReason",
                table: "Appointments");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ClinicId",
                table: "Appointments",
                column: "ClinicId");
        }
    }
}
