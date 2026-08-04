using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Gives <c>ProcedureType</c> a real <c>Category</c> column, and moves into it the values that had been living
    /// in <c>Description</c>.
    /// <para>
    /// <b>This is a correction, not a new field.</b> <c>ProcedureTypeCatalogSeed</c> assigned every starter act a
    /// clinical discipline from the day it was written, and — there being no column for it — passed that discipline
    /// into the constructor's <c>description</c> parameter. So every seeded clinic has nineteen acts carrying
    /// « Endodontie », « Prothèse fixe » and friends in a field its own admin form labels « Description
    /// (optionnel) », and the act picker grouped on it while documenting, in a comment, that it was not allowed to
    /// trust what it read.
    /// </para>
    /// <para>
    /// ⚠️ The backfill therefore <b>clears the Description it copies from</b>. Leaving it would print the same word
    /// twice on every seeded row, and worse, would leave the false value in place: « Endodontie » is not a
    /// description of « Traitement de canal », so keeping it there would preserve the original mistake behind a
    /// column that had just been added to fix it. Only descriptions that exactly match a canonical discipline are
    /// touched — a clinic that typed real prose into that box keeps it, and its act simply arrives unfiled.
    /// </para>
    /// <para>
    /// Hand-written rather than scaffolded: <c>dotnet ef</c> cannot load a freshly-built assembly on the
    /// development machine (Smart App Control, <c>0x800711C7</c>). The model snapshot and the paired Designer were
    /// updated by hand to match, and the shape is verified against PostgreSQL's own catalog by
    /// <c>dotnet run -- verify-schema</c>, which matches indexes on table + ordered columns rather than on name.
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class AddProcedureTypeCategory : Migration
    {
        /// <summary>
        /// The twelve disciplines as an SQL list. Spelled out here rather than parameterised because a migration
        /// is a historical record: it must keep migrating the rows it was written for even after
        /// <c>ProcedureTypeCategories.Canonical</c> gains a thirteenth entry.
        /// </summary>
        private const string CanonicalCategoryList = @"
            'Consultation',
            'Radiologie',
            'Soins conservateurs',
            'Endodontie',
            'Parodontologie',
            'Chirurgie/Extraction',
            'Prothèse fixe',
            'Prothèse amovible',
            'Implantologie',
            'Orthodontie',
            'Esthétique',
            'Pédodontie'";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "ProcedureTypes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // The move. `TRIM` because the seed's values went through `description?.Trim()` but a clinic that
            // retyped one by hand may have left a trailing space, and an untrimmed comparison would silently
            // decline to migrate exactly those rows.
            migrationBuilder.Sql($@"
                UPDATE ""ProcedureTypes""
                SET ""Category"" = TRIM(""Description""),
                    ""Description"" = NULL
                WHERE ""Category"" IS NULL
                  AND TRIM(""Description"") IN ({CanonicalCategoryList});");

            migrationBuilder.CreateIndex(
                name: "IX_ProcedureTypes_ClinicId_Category_Name",
                table: "ProcedureTypes",
                columns: new[] { "ClinicId", "Category", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProcedureTypes_ClinicId_Category_Name",
                table: "ProcedureTypes");

            // Put the discipline back where Up() found it, but only for a row whose Description is still empty —
            // a description written *after* the migration ran is the clinic's own text and must not be
            // overwritten by a rollback. Categories the clinic invented are dropped with the column, which is
            // the honest outcome: there was nowhere to keep them before this migration existed.
            migrationBuilder.Sql($@"
                UPDATE ""ProcedureTypes""
                SET ""Description"" = ""Category""
                WHERE ""Description"" IS NULL
                  AND ""Category"" IN ({CanonicalCategoryList});");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "ProcedureTypes");
        }
    }
}
