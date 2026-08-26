using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Gives « Esthétique » and « Pédodontie » a colour of their own in every clinic already seeded.
    /// <para>
    /// <b>This is a correction, not a restyling.</b> <c>ProcedureTypeCatalogSeed</c> paints a starter act by its
    /// clinical discipline, and two of its twelve entries were written onto a neighbour's hex: « Esthétique » on
    /// « Orthodontie »'s <c>#FB7185</c>, « Pédodontie » on « Parodontologie »'s <c>#6BAA75</c>. Twelve disciplines
    /// therefore rendered as ten colours, and since the colour is the only thing separating two blocks in the
    /// agenda at a glance, a facette and a séance orthodontique were indistinguishable — as were a détartrage and
    /// a soin d'enfant. The seed is fixed for clinics created from now on; this moves the rows already on disk.
    /// </para>
    /// <para>
    /// ⚠️ The update is guarded on <b>the old colour as well as the category</b>, so it only touches a row still
    /// wearing what the seed gave it. A clinic that recoloured its own facettes chose that hex deliberately, and a
    /// migration that "corrects" a deliberate choice is a data loss the clinic cannot even see happening. For the
    /// same reason nothing here touches an act the clinic added itself: it never carried the collision.
    /// </para>
    /// <para>
    /// No model change — <c>ProcedureTypes.ColorHex</c> already exists and keeps its type, so <c>Up</c> is data
    /// only and the paired Designer holds the previous migration's model verbatim. Hand-written for the reason
    /// <c>AddProcedureTypeCategory</c> gives: <c>dotnet ef</c> cannot load a freshly-built assembly on this
    /// development machine (Smart App Control, <c>0x800711C7</c>). Verify with
    /// <c>dotnet run -- verify-schema</c>, which compares against PostgreSQL's own catalog.
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class SplitCollidingProcedureCategoryColours : Migration
    {
        // The four hexes, spelled out rather than read from the seed, because a migration is a historical record:
        // it must keep migrating the rows it was written for even after the seed's palette moves again.
        private const string OrthodontieRose = "#FB7185";
        private const string EsthetiqueRose = "#F79AA6";
        private const string ParodontologieVert = "#6BAA75";
        private const string PedodontieVert = "#93C79C";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                UPDATE ""ProcedureTypes""
                SET ""ColorHex"" = '{EsthetiqueRose}'
                WHERE ""Category"" = 'Esthétique'
                  AND upper(""ColorHex"") = '{OrthodontieRose}';");

            migrationBuilder.Sql($@"
                UPDATE ""ProcedureTypes""
                SET ""ColorHex"" = '{PedodontieVert}'
                WHERE ""Category"" = 'Pédodontie'
                  AND upper(""ColorHex"") = '{ParodontologieVert}';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Symmetrical, and guarded the same way: a row recoloured *after* this migration ran is the clinic's
            // own choice and a rollback must not reach into it. Restoring the collision is the honest inverse —
            // it is the state the schema was in.
            migrationBuilder.Sql($@"
                UPDATE ""ProcedureTypes""
                SET ""ColorHex"" = '{OrthodontieRose}'
                WHERE ""Category"" = 'Esthétique'
                  AND upper(""ColorHex"") = '{EsthetiqueRose}';");

            migrationBuilder.Sql($@"
                UPDATE ""ProcedureTypes""
                SET ""ColorHex"" = '{ParodontologieVert}'
                WHERE ""Category"" = 'Pédodontie'
                  AND upper(""ColorHex"") = '{PedodontieVert}';");
        }
    }
}
