using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Data backfill (no schema change): existing clinics were seeded before ProcedureType.ResultingCondition
    /// existed, so their procedures had no resulting odontogram state — meaning dental-record acts saved with
    /// no état and produced no odontogram entries. This retro-fixes that in three idempotent steps. Harmless
    /// on a fresh DB (tables are empty when migrations run; the per-clinic seed sets the states directly).
    ///
    /// ToothCondition enum ints: Obturation=2, Couronne=3, TraitementDeCanal=4, Implant=6, ExtraitAbsent=7.
    /// </summary>
    public partial class BackfillDentalResultingConditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Backfill each existing procedure's resulting state from its category (mirrors the seed's
            //    CategoryResultingConditions). Only touches rows that still have no state set.
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes"" SET ""ResultingCondition"" = 2
                    WHERE ""ResultingCondition"" IS NULL AND ""Description"" = 'Soins conservateurs';
                UPDATE ""ProcedureTypes"" SET ""ResultingCondition"" = 4
                    WHERE ""ResultingCondition"" IS NULL AND ""Description"" = 'Endodontie';
                UPDATE ""ProcedureTypes"" SET ""ResultingCondition"" = 7
                    WHERE ""ResultingCondition"" IS NULL AND ""Description"" = 'Chirurgie/Extraction';
                UPDATE ""ProcedureTypes"" SET ""ResultingCondition"" = 3
                    WHERE ""ResultingCondition"" IS NULL AND ""Description"" = 'Prothèse fixe';
                UPDATE ""ProcedureTypes"" SET ""ResultingCondition"" = 6
                    WHERE ""ResultingCondition"" IS NULL AND ""Description"" = 'Implantologie';
            ");

            // 2) Backfill existing record acts' resulting state from their linked procedure, but only where the
            //    act had none (never overwrite an état the doctor set manually).
            migrationBuilder.Sql(@"
                UPDATE ""DentalRecordActs"" a
                SET ""ResultingCondition"" = pt.""ResultingCondition""
                FROM ""ProcedureTypes"" pt
                WHERE a.""ProcedureTypeId"" = pt.""Id""
                  AND a.""ResultingCondition"" IS NULL
                  AND pt.""ResultingCondition"" IS NOT NULL;
            ");

            // 3) Generate the missing odontogram entries: one ToothState per act × tooth for acts that now have
            //    a real état (not null / not Sain), skipping any that already exist (idempotent re-run safe).
            migrationBuilder.Sql(@"
                INSERT INTO ""ToothStates""
                    (""Id"", ""PatientId"", ""ToothNumber"", ""Condition"", ""Surfaces"", ""Note"", ""DentalRecordId"", ""TreatmentDate"", ""CreatedAt"")
                SELECT gen_random_uuid(), dr.""PatientId"", t.tooth, a.""ResultingCondition"", a.""Surfaces"", a.""Note"",
                       a.""DentalRecordId"", dr.""InterventionDate"", now()
                FROM ""DentalRecordActs"" a
                JOIN ""DentalRecords"" dr ON dr.""Id"" = a.""DentalRecordId""
                CROSS JOIN LATERAL (
                    SELECT (jsonb_array_elements_text(a.""ToothNumbers""::jsonb))::int AS tooth
                ) t
                WHERE a.""ResultingCondition"" IS NOT NULL
                  AND a.""ResultingCondition"" <> 0
                  AND NOT EXISTS (
                      SELECT 1 FROM ""ToothStates"" ts
                      WHERE ts.""DentalRecordId"" = a.""DentalRecordId""
                        AND ts.""ToothNumber"" = t.tooth
                        AND ts.""Condition"" = a.""ResultingCondition""
                  );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data backfill — not reversible (cannot distinguish backfilled rows from user-entered ones).
        }
    }
}
