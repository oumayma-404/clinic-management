using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Gives every starter act that needs one its <b>protocole de séances</b>, in the clinics already seeded.
    /// <para>
    /// <b>This is a correction, not a new field.</b> <c>ProcedureType.DefaultSteps</c> shipped with an editor,
    /// a column and a DTO, and only three of the thirty-three starter acts carried a protocol — the other
    /// eleven that need one were left blank, so a dentist adding « Implant dentaire » to a devis got an act
    /// with no séances and had to type « Pose de l'implant », « Désenfouissement » and four more by hand, on
    /// every devis. The seed is fixed for clinics created from now on; this moves the rows already on disk.
    /// </para>
    /// <para>
    /// ⚠️ <b>It only fills a protocol that is EMPTY.</b> A practice that authored its own sequence for an act
    /// chose it deliberately, and a migration that "corrects" a deliberate choice is a data loss the practice
    /// cannot see happening — the guard is the same idea as
    /// <c>SplitCollidingProcedureCategoryColours</c>' « only touch a row still wearing what the seed gave it »,
    /// and here it is stricter: no protocol at all, or nothing happens. Matched on <c>Name</c>, so an act the
    /// clinic added itself is never touched either.
    /// </para>
    /// <para>
    /// ⚠️ <b>The nineteen acts with no protocol are deliberate and this migration must never grow to cover
    /// them.</b> A consultation, a radiographie, une extraction, un détartrage, un scellement de sillons, une
    /// couronne pédodontique préformée and — against the textbook reflex — <b>un traitement de canal</b> are
    /// single-séance acts (Cochrane 2022, 47 studies: no difference in success between one and two visits).
    /// Giving them a protocol would put a second appointment in front of every dentist who books one.
    /// </para>
    /// <para>
    /// The JSON is spelled out rather than read from the seed, for the reason the colour migration gives: a
    /// migration is a historical record and must keep migrating the rows it was written for after the seed's
    /// protocols move again. It is written with the same <c>\uXXXX</c> escaping <c>System.Text.Json</c>
    /// produces, so a row written here is byte-identical to one written by the seed.
    /// </para>
    /// <para>
    /// No model change — the column already exists and keeps its type, so <c>Up</c> is data only and the paired
    /// Designer holds the previous migration's model verbatim. Hand-written for the reason
    /// <c>AddProcedureTypeCategory</c> gives: <c>dotnet ef</c> cannot load a freshly-built assembly on this
    /// development machine (Smart App Control, <c>0x800711C7</c>). Verify with
    /// <c>dotnet run -- verify-schema</c>, which compares against PostgreSQL's own catalog.
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class SeedProcedureStepProtocols : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[{""Label"":""Pr\u00E9paration + empreinte"",""DurationMinutes"":90},{""Label"":""Essai de l''armature"",""DurationMinutes"":30},{""Label"":""Essayage + scellement d\u00E9finitif"",""DurationMinutes"":45}]'
                WHERE ""Name"" = 'Couronne / bridge (par élément)'
                  AND (""DefaultSteps"" IS NULL OR ""DefaultSteps"" IN ('', '[]'));");
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[{""Label"":""Pr\u00E9paration canalaire + empreinte"",""DurationMinutes"":60},{""Label"":""Essayage + scellement du faux moignon"",""DurationMinutes"":45}]'
                WHERE ""Name"" = 'Inlay-core (reconstitution corono-radiculaire)'
                  AND (""DefaultSteps"" IS NULL OR ""DefaultSteps"" IN ('', '[]'));");
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[{""Label"":""Bilan esth\u00E9tique + empreintes"",""DurationMinutes"":60},{""Label"":""Validation du mock-up"",""DurationMinutes"":45},{""Label"":""Pr\u00E9paration + provisoires"",""DurationMinutes"":150},{""Label"":""Collage d\u00E9finitif"",""DurationMinutes"":150}]'
                WHERE ""Name"" = 'Facette'
                  AND (""DefaultSteps"" IS NULL OR ""DefaultSteps"" IN ('', '[]'));");
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[{""Label"":""Empreinte primaire"",""DurationMinutes"":30},{""Label"":""Empreinte secondaire"",""DurationMinutes"":45},{""Label"":""Rapports intermaxillaires"",""DurationMinutes"":45},{""Label"":""Essai des dents en cire"",""DurationMinutes"":30},{""Label"":""Mise en bouche"",""DurationMinutes"":45},{""Label"":""Contr\u00F4le et retouches"",""DurationMinutes"":30}]'
                WHERE ""Name"" = 'Prothèse amovible (partielle / complète)'
                  AND (""DefaultSteps"" IS NULL OR ""DefaultSteps"" IN ('', '[]'));");
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[{""Label"":""Empreinte de rebasage"",""DurationMinutes"":30},{""Label"":""Remise de la proth\u00E8se rebas\u00E9e"",""DurationMinutes"":30}]'
                WHERE ""Name"" = 'Réparation / rebasage de prothèse'
                  AND (""DefaultSteps"" IS NULL OR ""DefaultSteps"" IN ('', '[]'));");
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[{""Label"":""Empreintes + enregistrement occlusal"",""DurationMinutes"":45},{""Label"":""Pose et r\u00E9glage occlusal"",""DurationMinutes"":45},{""Label"":""Contr\u00F4le et r\u00E9glage"",""DurationMinutes"":30}]'
                WHERE ""Name"" = 'Gouttière occlusale (bruxisme)'
                  AND (""DefaultSteps"" IS NULL OR ""DefaultSteps"" IN ('', '[]'));");
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[{""Label"":""Bilan pr\u00E9-implantaire"",""DurationMinutes"":45},{""Label"":""Pose de l''implant"",""DurationMinutes"":90},{""Label"":""Contr\u00F4le post-op\u00E9ratoire"",""DurationMinutes"":20},{""Label"":""D\u00E9senfouissement"",""DurationMinutes"":30},{""Label"":""Empreinte implantaire"",""DurationMinutes"":30},{""Label"":""Pose de la couronne"",""DurationMinutes"":30}]'
                WHERE ""Name"" = 'Implant dentaire'
                  AND (""DefaultSteps"" IS NULL OR ""DefaultSteps"" IN ('', '[]'));");
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[{""Label"":""Greffe osseuse"",""DurationMinutes"":90},{""Label"":""Contr\u00F4le post-op\u00E9ratoire"",""DurationMinutes"":20}]'
                WHERE ""Name"" = 'Greffe osseuse / comblement'
                  AND (""DefaultSteps"" IS NULL OR ""DefaultSteps"" IN ('', '[]'));");
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[{""Label"":""R\u00E9\u00E9valuation parodontale"",""DurationMinutes"":30},{""Label"":""Gingivectomie"",""DurationMinutes"":60},{""Label"":""D\u00E9pose du pansement"",""DurationMinutes"":20}]'
                WHERE ""Name"" = 'Gingivectomie'
                  AND (""DefaultSteps"" IS NULL OR ""DefaultSteps"" IN ('', '[]'));");
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[{""Label"":""Fr\u00E9nectomie"",""DurationMinutes"":30},{""Label"":""Contr\u00F4le post-op\u00E9ratoire"",""DurationMinutes"":15}]'
                WHERE ""Name"" = 'Frénectomie'
                  AND (""DefaultSteps"" IS NULL OR ""DefaultSteps"" IN ('', '[]'));");
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[{""Label"":""Incision et drainage"",""DurationMinutes"":30},{""Label"":""Contr\u00F4le et retrait du drain"",""DurationMinutes"":15}]'
                WHERE ""Name"" = 'Incision d''abcès et drainage'
                  AND (""DefaultSteps"" IS NULL OR ""DefaultSteps"" IN ('', '[]'));");
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[{""Label"":""Surfa\u00E7age 1er quadrant"",""DurationMinutes"":60},{""Label"":""Surfa\u00E7age 2e quadrant"",""DurationMinutes"":60},{""Label"":""Surfa\u00E7age 3e quadrant"",""DurationMinutes"":60},{""Label"":""Surfa\u00E7age 4e quadrant"",""DurationMinutes"":60},{""Label"":""R\u00E9\u00E9valuation parodontale"",""DurationMinutes"":30}]'
                WHERE ""Name"" = 'Traitement parodontal (surfaçage / curetage)'
                  AND (""DefaultSteps"" IS NULL OR ""DefaultSteps"" IN ('', '[]'));");
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[{""Label"":""D\u00E9pose et d\u00E9sinfection"",""DurationMinutes"":105},{""Label"":""R\u00E9obturation canalaire"",""DurationMinutes"":50}]'
                WHERE ""Name"" = 'Retraitement endodontique'
                  AND (""DefaultSteps"" IS NULL OR ""DefaultSteps"" IN ('', '[]'));");
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[{""Label"":""Empreinte mainteneur d''espace"",""DurationMinutes"":40},{""Label"":""Scellement du mainteneur"",""DurationMinutes"":25}]'
                WHERE ""Name"" = 'Mainteneur d''espace fixe'
                  AND (""DefaultSteps"" IS NULL OR ""DefaultSteps"" IN ('', '[]'));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Symmetrical and guarded the same way: a protocol edited *after* this migration ran is the
            // practice's own and a rollback must not reach into it. Only a row still holding exactly what was
            // written here goes back to « aucune étape », which is the state the column was in.
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[]'
                WHERE ""Name"" = 'Couronne / bridge (par élément)'
                  AND ""DefaultSteps"" = '[{""Label"":""Pr\u00E9paration + empreinte"",""DurationMinutes"":90},{""Label"":""Essai de l''armature"",""DurationMinutes"":30},{""Label"":""Essayage + scellement d\u00E9finitif"",""DurationMinutes"":45}]';");
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[]'
                WHERE ""Name"" = 'Inlay-core (reconstitution corono-radiculaire)'
                  AND ""DefaultSteps"" = '[{""Label"":""Pr\u00E9paration canalaire + empreinte"",""DurationMinutes"":60},{""Label"":""Essayage + scellement du faux moignon"",""DurationMinutes"":45}]';");
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[]'
                WHERE ""Name"" = 'Facette'
                  AND ""DefaultSteps"" = '[{""Label"":""Bilan esth\u00E9tique + empreintes"",""DurationMinutes"":60},{""Label"":""Validation du mock-up"",""DurationMinutes"":45},{""Label"":""Pr\u00E9paration + provisoires"",""DurationMinutes"":150},{""Label"":""Collage d\u00E9finitif"",""DurationMinutes"":150}]';");
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[]'
                WHERE ""Name"" = 'Prothèse amovible (partielle / complète)'
                  AND ""DefaultSteps"" = '[{""Label"":""Empreinte primaire"",""DurationMinutes"":30},{""Label"":""Empreinte secondaire"",""DurationMinutes"":45},{""Label"":""Rapports intermaxillaires"",""DurationMinutes"":45},{""Label"":""Essai des dents en cire"",""DurationMinutes"":30},{""Label"":""Mise en bouche"",""DurationMinutes"":45},{""Label"":""Contr\u00F4le et retouches"",""DurationMinutes"":30}]';");
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[]'
                WHERE ""Name"" = 'Réparation / rebasage de prothèse'
                  AND ""DefaultSteps"" = '[{""Label"":""Empreinte de rebasage"",""DurationMinutes"":30},{""Label"":""Remise de la proth\u00E8se rebas\u00E9e"",""DurationMinutes"":30}]';");
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[]'
                WHERE ""Name"" = 'Gouttière occlusale (bruxisme)'
                  AND ""DefaultSteps"" = '[{""Label"":""Empreintes + enregistrement occlusal"",""DurationMinutes"":45},{""Label"":""Pose et r\u00E9glage occlusal"",""DurationMinutes"":45},{""Label"":""Contr\u00F4le et r\u00E9glage"",""DurationMinutes"":30}]';");
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[]'
                WHERE ""Name"" = 'Implant dentaire'
                  AND ""DefaultSteps"" = '[{""Label"":""Bilan pr\u00E9-implantaire"",""DurationMinutes"":45},{""Label"":""Pose de l''implant"",""DurationMinutes"":90},{""Label"":""Contr\u00F4le post-op\u00E9ratoire"",""DurationMinutes"":20},{""Label"":""D\u00E9senfouissement"",""DurationMinutes"":30},{""Label"":""Empreinte implantaire"",""DurationMinutes"":30},{""Label"":""Pose de la couronne"",""DurationMinutes"":30}]';");
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[]'
                WHERE ""Name"" = 'Greffe osseuse / comblement'
                  AND ""DefaultSteps"" = '[{""Label"":""Greffe osseuse"",""DurationMinutes"":90},{""Label"":""Contr\u00F4le post-op\u00E9ratoire"",""DurationMinutes"":20}]';");
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[]'
                WHERE ""Name"" = 'Gingivectomie'
                  AND ""DefaultSteps"" = '[{""Label"":""R\u00E9\u00E9valuation parodontale"",""DurationMinutes"":30},{""Label"":""Gingivectomie"",""DurationMinutes"":60},{""Label"":""D\u00E9pose du pansement"",""DurationMinutes"":20}]';");
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[]'
                WHERE ""Name"" = 'Frénectomie'
                  AND ""DefaultSteps"" = '[{""Label"":""Fr\u00E9nectomie"",""DurationMinutes"":30},{""Label"":""Contr\u00F4le post-op\u00E9ratoire"",""DurationMinutes"":15}]';");
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[]'
                WHERE ""Name"" = 'Incision d''abcès et drainage'
                  AND ""DefaultSteps"" = '[{""Label"":""Incision et drainage"",""DurationMinutes"":30},{""Label"":""Contr\u00F4le et retrait du drain"",""DurationMinutes"":15}]';");
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[]'
                WHERE ""Name"" = 'Traitement parodontal (surfaçage / curetage)'
                  AND ""DefaultSteps"" = '[{""Label"":""Surfa\u00E7age 1er quadrant"",""DurationMinutes"":60},{""Label"":""Surfa\u00E7age 2e quadrant"",""DurationMinutes"":60},{""Label"":""Surfa\u00E7age 3e quadrant"",""DurationMinutes"":60},{""Label"":""Surfa\u00E7age 4e quadrant"",""DurationMinutes"":60},{""Label"":""R\u00E9\u00E9valuation parodontale"",""DurationMinutes"":30}]';");
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[]'
                WHERE ""Name"" = 'Retraitement endodontique'
                  AND ""DefaultSteps"" = '[{""Label"":""D\u00E9pose et d\u00E9sinfection"",""DurationMinutes"":105},{""Label"":""R\u00E9obturation canalaire"",""DurationMinutes"":50}]';");
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""DefaultSteps"" = '[]'
                WHERE ""Name"" = 'Mainteneur d''espace fixe'
                  AND ""DefaultSteps"" = '[{""Label"":""Empreinte mainteneur d''espace"",""DurationMinutes"":40},{""Label"":""Scellement du mainteneur"",""DurationMinutes"":25}]';");
        }
    }
}
