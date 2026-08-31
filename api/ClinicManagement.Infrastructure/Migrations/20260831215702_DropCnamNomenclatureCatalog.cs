using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Retires the invented CNAM act catalogue (feature <c>single-act-catalogue</c>): <c>DentalActCodes</c>, whose
    /// 100 <c>DCH</c> codes are the ones the CNAM actually publishes, is the only act catalogue now.
    ///
    /// <para>Also drops the <c>VD</c> valeur de la lettre clé. « Visite dentaire » appears in neither the NGAP
    /// arrêté du 1er juin 2006 art. 4 nor any CNAM tariff table, so it was a key nothing could ever value.</para>
    ///
    /// <para>⚠️ <b>The two consultation acts (Cd/Cds) are deliberately NOT inserted here.</b>
    /// <c>ClinicCatalogSeeder</c> tops them up by code on every startup, for every clinic, which reaches an
    /// existing cabinet as well as a new one and keeps the ids deterministic — a migration would have to invent
    /// them per clinic in SQL. Do not add an <c>InsertData</c> loop for them: it would double-insert on a fresh
    /// database.</para>
    /// </summary>
    public partial class DropCnamNomenclatureCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CnamNomenclatureEntries");

            migrationBuilder.Sql("DELETE FROM \"CnamLetterValues\" WHERE upper(\"LettreCle\") = 'VD';");
        }

        /// <summary>
        /// Recreates the table empty, and does <b>not</b> restore the <c>VD</c> valeurs: the rows carried
        /// per-clinic ids this migration did not record, and the lettre clé has no basis in either source.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CnamNomenclatureEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeActe = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Coefficient = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DesignationFr = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsProvisional = table.Column<bool>(type: "boolean", nullable: false),
                    LettreCle = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CnamNomenclatureEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CnamNomenclatureEntries_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CnamNomenclatureEntries_ClinicId_CodeActe",
                table: "CnamNomenclatureEntries",
                columns: new[] { "ClinicId", "CodeActe" },
                unique: true);
        }
    }
}
