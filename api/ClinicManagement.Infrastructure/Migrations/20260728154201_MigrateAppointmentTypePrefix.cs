using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Retires the <c>Type: </c> prefix from <c>Appointment.Notes</c> in favour of <c>ProcedureTypeId</c>
    /// (§ 8.4, AC-P1.47–1.51).
    ///
    /// <para>
    /// The appointment dialogs wrote the chosen act's name into the notes as <c>"Type: &lt;name&gt;"</c> while
    /// *also* setting <c>ProcedureTypeId</c> from separate state, and the edit dialog parsed the prefix back out.
    /// The two could disagree, and the divergence was persisted forward on every save. The destination is the
    /// column that already exists — no new column (AC-P1.47).
    /// </para>
    ///
    /// <para>
    /// <b>Three explicit row classes, nothing discarded (AC-P1.48)</b>, reported as per-class counts in a
    /// <c>NOTICE</c> (AC-P1.50):
    /// </para>
    /// <list type="number">
    /// <item><b>prefix, no id</b> — match the name against that clinic's catalog and SET <c>ProcedureTypeId</c>.</item>
    /// <item><b>prefix + an id</b> — the existing id <b>wins</b>; only the prefix text is stripped. The id is the
    /// structured value; the note is free text a user may have edited.</item>
    /// <item><b>prefix matching no catalog row</b> — left completely untouched and counted, so a note a user
    /// legitimately began with "Type: " is never mangled (AC-P1.49).</item>
    /// </list>
    ///
    /// <para>
    /// <b>The detail the plan did not anticipate.</b> Real rows are not <c>"Type: &lt;name&gt;"</c> — they are
    /// <c>"Type: &lt;name&gt;&lt;trailing free text&gt;"</c>, e.g.
    /// <c>"Type: Prothèse amovible (partielle / complète) (dents 21, 22, 23, 24)"</c>. Stripping the whole first
    /// line would destroy the teeth list. So the match is on the <b>longest catalog name the note starts with</b>
    /// (longest, so « Radiographie panoramique » is not matched as « Radiographie rétro-alvéolaire »), and only
    /// that name is removed — whatever follows is kept.
    /// </para>
    ///
    /// <para>
    /// <b>Irreversible</b> (plan risk <b>R-5</b>) — see <c>Down()</c>. Idempotent by construction: it only acts on
    /// notes that still carry the prefix, so a second run finds nothing. That is required, not nice-to-have — a
    /// throw in Local mode calls <c>StopApplication()</c> (<b>R-7</b>).
    /// </para>
    /// </summary>
    public partial class MigrateAppointmentTypePrefix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    matched_and_set   int := 0;
                    stripped_id_won   int := 0;
                    unmatched_left    int := 0;
                BEGIN
                    -- The prefix only ever occupies the start of the FIRST line. `matched_procedure_*` is the
                    -- longest catalog name (for that appointment's own clinic) that the first line starts with.
                    CREATE TEMP TABLE _type_prefix_rows AS
                    SELECT
                        a."Id"                                            AS appointment_id,
                        a."ProcedureTypeId"                               AS existing_procedure_id,
                        -- everything after 'Type: ' on the first line
                        split_part(substring(a."Notes" from 7), E'\n', 1) AS first_line,
                        -- lines 2+, preserved verbatim
                        CASE
                            WHEN position(E'\n' in a."Notes") > 0
                            THEN substring(a."Notes" from position(E'\n' in a."Notes") + 1)
                            ELSE NULL
                        END                                               AS rest_of_note,
                        m."Id"                                            AS matched_procedure_id,
                        m."Name"                                          AS matched_procedure_name
                    FROM "Appointments" a
                    LEFT JOIN LATERAL (
                        SELECT pt."Id", pt."Name"
                        FROM "ProcedureTypes" pt
                        WHERE pt."ClinicId" = a."ClinicId"
                          AND split_part(substring(a."Notes" from 7), E'\n', 1) LIKE pt."Name" || '%'
                        ORDER BY length(pt."Name") DESC
                        LIMIT 1
                    ) m ON true
                    WHERE a."Notes" LIKE 'Type: %';

                    -- Class 3 counted FIRST, because it is the class that must not be touched at all.
                    SELECT count(*) INTO unmatched_left
                    FROM _type_prefix_rows WHERE matched_procedure_id IS NULL;

                    -- Class 1 — prefix with no id: adopt the matched act, keep any trailing free text.
                    WITH updated AS (
                        UPDATE "Appointments" a
                        SET "ProcedureTypeId" = r.matched_procedure_id,
                            "Notes"           = NULLIF(
                                btrim(
                                    btrim(substring(r.first_line from length(r.matched_procedure_name) + 1))
                                    || COALESCE(E'\n' || r.rest_of_note, ''),
                                    E' \n'
                                ), ''),
                            "UpdatedAt"       = now()
                        FROM _type_prefix_rows r
                        WHERE a."Id" = r.appointment_id
                          AND r.matched_procedure_id IS NOT NULL
                          AND r.existing_procedure_id IS NULL
                        RETURNING 1
                    )
                    SELECT count(*) INTO matched_and_set FROM updated;

                    -- Class 2 — prefix AND an id already set: the id wins, only the prefix text goes.
                    WITH updated AS (
                        UPDATE "Appointments" a
                        SET "Notes"     = NULLIF(
                                btrim(
                                    btrim(substring(r.first_line from length(r.matched_procedure_name) + 1))
                                    || COALESCE(E'\n' || r.rest_of_note, ''),
                                    E' \n'
                                ), ''),
                            "UpdatedAt" = now()
                        FROM _type_prefix_rows r
                        WHERE a."Id" = r.appointment_id
                          AND r.matched_procedure_id IS NOT NULL
                          AND r.existing_procedure_id IS NOT NULL
                        RETURNING 1
                    )
                    SELECT count(*) INTO stripped_id_won FROM updated;

                    DROP TABLE _type_prefix_rows;

                    RAISE NOTICE 'Migration du préfixe « Type: » — % rendez-vous rattachés à un acte du catalogue, % notes nettoyées (identifiant existant conservé), % laissés intacts (aucune correspondance au catalogue).',
                        matched_and_set, stripped_id_won, unmatched_left;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty.
            //
            // Irreversible by nature (plan risk R-5): the prefix is removed from free text a user wrote, and
            // afterwards a note that never carried a prefix is indistinguishable from one this migration cleaned.
            // Re-synthesising "Type: <name>" would also re-create the very note-vs-ProcedureTypeId divergence
            // this migration exists to remove.
            //
            // Rolling back means restoring the backup taken before the batch (see packaging/README.md) and
            // diffing the reports captured on either side. Shipping a Down() that throws would only surface that
            // at the worst possible moment.
        }
    }
}
