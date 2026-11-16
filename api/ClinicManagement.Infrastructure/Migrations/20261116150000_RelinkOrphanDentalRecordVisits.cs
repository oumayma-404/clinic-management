using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Links the fiches saved before their visit existed — once, for the rows already in the database.
    ///
    /// <para><b>Why the same statement runs a second time.</b> <c>BackfillDentalRecordAppointmentLinks</c> linked
    /// every fiche whose day held exactly one visit, once. But a walk-in is charted first and booked after, and
    /// nothing asked the question again when the visit appeared: the fiche stayed unlinked, « À clôturer » asked for
    /// a fiche the patient already had, and <c>verify-schema</c>'s <c>dental-record-visit-links-backfill</c> failed
    /// the deploy (production, November 2026). <c>DentalRecordVisitLinks</c> now asks inside every save, so no new
    /// row is left behind; this reaches the rows written before it, which no save will ever touch again.</para>
    ///
    /// <para><b>The statement is the original's, character for character</b> — the rule is
    /// <c>DentalRecordVisitLink</c>'s, and <c>DentalRecordVisitLinksTests</c> fails if the two migrations ever
    /// disagree. Same guarantees: exactly one non-cancelled, non-missed visit on the fiche's own Tunisian day; the
    /// column and nothing else (no status, no prompt withdrawn); re-runnable; an empty <c>Down</c>.</para>
    /// </summary>
    public partial class RelinkOrphanDentalRecordVisits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""DentalRecords"" dr
                SET ""AppointmentId"" = m.""AppointmentId""
                FROM (
                    SELECT
                        r.""Id""                     AS ""DentalRecordId"",
                        -- ⚠️ NOT `MIN(a.""Id"")`: PostgreSQL ships no `min`/`max` aggregate for `uuid`, so that
                        -- form is `42883: function min(uuid) does not exist` — a startup crash on every
                        -- deployment, not a wrong answer. `array_agg` has no such gap, and taking element 1 is
                        -- also the honest expression of the intent below: HAVING COUNT(*) = 1 means the group
                        -- holds exactly one candidate, so this unwraps the single row rather than choosing.
                        (array_agg(a.""Id""))[1]     AS ""AppointmentId""
                    FROM ""DentalRecords"" r
                    JOIN ""Appointments"" a
                      ON a.""PatientId"" = r.""PatientId""
                     AND a.""ClinicId""  = r.""ClinicId""
                     -- 5 = Cancelled, 6 = NoShow. Both are complete answers about a visit that did not happen, so
                     -- neither can be the séance a fiche documents. The ordinals are the enum's own, mapped
                     -- HasConversion<int>(); they are stated here because SQL has no access to the type.
                     AND a.""Status"" NOT IN (5, 6)
                     AND (a.""AppointmentDateTime"" AT TIME ZONE 'Africa/Tunis')::date
                       = (r.""InterventionDate""    AT TIME ZONE 'Africa/Tunis')::date
                    WHERE r.""AppointmentId"" IS NULL
                    GROUP BY r.""Id""
                    -- The whole guard: exactly one candidate, or none. The aggregate above is reached only when
                    -- the count is 1, so it unwraps the single row rather than choosing among several.
                    HAVING COUNT(*) = 1
                ) AS m
                WHERE dr.""Id"" = m.""DentalRecordId""
                  AND dr.""AppointmentId"" IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty, for the original's reason: nothing tells the links this wrote from the ones the
            // application has written since.
        }
    }
}
