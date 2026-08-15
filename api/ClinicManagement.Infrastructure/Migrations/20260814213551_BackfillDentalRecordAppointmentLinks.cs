using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Links each existing fiche de soins to the visit it documents, where exactly one visit can be meant.
    ///
    /// <para><b>Why this is required rather than tidy.</b> <c>DentalRecords.AppointmentId</c> has existed and been
    /// indexed since <c>AddDentalRecordAppointmentId</c>, but only one door ever populated it — the post-visit
    /// prompt's deep link. Every fiche charted the ordinary way, from the patient's page, stored NULL. The
    /// « À clôturer » worklist reads the absence of that link as « pas de fiche », so without this backfill its
    /// very first screen reports a missing fiche for most visits that have one, on every clinic, on day one.</para>
    ///
    /// <para><b>The rule is the resolver's, in SQL.</b> Exactly one non-cancelled, non-missed appointment for that
    /// patient on the fiche's own clinic-local day: <c>HAVING COUNT(*) = 1</c> is what refuses ambiguity, and it is
    /// deliberate that two visits in a day link neither. A missing link costs one row on a worklist; a wrong link
    /// attaches a séance to the wrong visit — a claim about a patient's day that nobody made.</para>
    ///
    /// <para><b>Tunisia is UTC+1</b>, so both sides are compared in the clinic's own day
    /// (<c>AT TIME ZONE 'Africa/Tunis'</c>) and not in UTC. Grouping by the raw UTC date would file an evening
    /// séance against the following day and match nothing — silently, and only for the last hour of the evening.</para>
    ///
    /// <para>This writes the column and nothing else: no status is changed and no post-visit prompt is withdrawn.
    /// Those are side effects of <i>recording</i> a fiche, and re-running them for history would mark visits
    /// « Terminé » that nobody closed.</para>
    ///
    /// <para><b>Re-runnable</b>: gated on <c>"AppointmentId" IS NULL</c>, so a fiche linked since (or by hand) is
    /// left alone. <c>Down</c> is deliberately empty — un-setting the column would also clear the links the
    /// application has written since, which the migration did not create and cannot tell apart.</para>
    /// </summary>
    public partial class BackfillDentalRecordAppointmentLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""DentalRecords"" dr
                SET ""AppointmentId"" = m.""AppointmentId""
                FROM (
                    SELECT
                        r.""Id""                AS ""DentalRecordId"",
                        MIN(a.""Id"")           AS ""AppointmentId""
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
                    -- The whole guard: exactly one candidate, or none. MIN() above is reached only when the count
                    -- is 1, so it selects the single row rather than choosing among several.
                    HAVING COUNT(*) = 1
                ) AS m
                WHERE dr.""Id"" = m.""DentalRecordId""
                  AND dr.""AppointmentId"" IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty — see the class remarks. Nulling the column would also erase the links written by
            // the application since this ran, and nothing distinguishes those from the ones this created.
        }
    }
}
