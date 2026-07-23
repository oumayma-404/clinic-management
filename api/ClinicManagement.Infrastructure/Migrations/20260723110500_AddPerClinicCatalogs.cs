using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPerClinicCatalogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Per-clinic conversion (feature cloud-security-and-tenant-isolation, #5): the catalogs move from
            // ONE global set to a per-clinic copy. Clear the existing global seed rows first — they carry no
            // ClinicId and would violate the new NOT NULL column + FK — then the per-clinic seeder re-seeds the
            // SAME default set into every clinic (on creation, and via the startup backfill for existing
            // clinics). This is the approved "reset to the clean default" behaviour. DentalActCodeId on invoice
            // lines / treatment-plan items is a soft Guid reference (not an FK), so these deletes are safe.
            migrationBuilder.Sql("DELETE FROM \"MedicationActiveIngredients\";");
            migrationBuilder.Sql("DELETE FROM \"Medications\";");
            migrationBuilder.Sql("DELETE FROM \"CnamNomenclatureEntries\";");
            migrationBuilder.Sql("DELETE FROM \"CnamLetterValues\";");
            migrationBuilder.Sql("DELETE FROM \"DentalActCodes\";");

            migrationBuilder.DropIndex(
                name: "IX_Medications_BrandName",
                table: "Medications");

            migrationBuilder.DropIndex(
                name: "IX_DentalActCodes_CodeActe",
                table: "DentalActCodes");

            migrationBuilder.DropIndex(
                name: "IX_CnamNomenclatureEntries_CodeActe",
                table: "CnamNomenclatureEntries");

            migrationBuilder.DropIndex(
                name: "IX_CnamLetterValues_LettreCle",
                table: "CnamLetterValues");

            migrationBuilder.AddColumn<Guid>(
                name: "ClinicId",
                table: "Medications",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ClinicId",
                table: "DentalActCodes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ClinicId",
                table: "CnamNomenclatureEntries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ClinicId",
                table: "CnamLetterValues",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Medications_ClinicId_BrandName",
                table: "Medications",
                columns: new[] { "ClinicId", "BrandName" });

            migrationBuilder.CreateIndex(
                name: "IX_DentalActCodes_ClinicId_CodeActe",
                table: "DentalActCodes",
                columns: new[] { "ClinicId", "CodeActe" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CnamNomenclatureEntries_ClinicId_CodeActe",
                table: "CnamNomenclatureEntries",
                columns: new[] { "ClinicId", "CodeActe" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CnamLetterValues_ClinicId_LettreCle",
                table: "CnamLetterValues",
                columns: new[] { "ClinicId", "LettreCle" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CnamLetterValues_Clinics_ClinicId",
                table: "CnamLetterValues",
                column: "ClinicId",
                principalTable: "Clinics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CnamNomenclatureEntries_Clinics_ClinicId",
                table: "CnamNomenclatureEntries",
                column: "ClinicId",
                principalTable: "Clinics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DentalActCodes_Clinics_ClinicId",
                table: "DentalActCodes",
                column: "ClinicId",
                principalTable: "Clinics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Medications_Clinics_ClinicId",
                table: "Medications",
                column: "ClinicId",
                principalTable: "Clinics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CnamLetterValues_Clinics_ClinicId",
                table: "CnamLetterValues");

            migrationBuilder.DropForeignKey(
                name: "FK_CnamNomenclatureEntries_Clinics_ClinicId",
                table: "CnamNomenclatureEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_DentalActCodes_Clinics_ClinicId",
                table: "DentalActCodes");

            migrationBuilder.DropForeignKey(
                name: "FK_Medications_Clinics_ClinicId",
                table: "Medications");

            migrationBuilder.DropIndex(
                name: "IX_Medications_ClinicId_BrandName",
                table: "Medications");

            migrationBuilder.DropIndex(
                name: "IX_DentalActCodes_ClinicId_CodeActe",
                table: "DentalActCodes");

            migrationBuilder.DropIndex(
                name: "IX_CnamNomenclatureEntries_ClinicId_CodeActe",
                table: "CnamNomenclatureEntries");

            migrationBuilder.DropIndex(
                name: "IX_CnamLetterValues_ClinicId_LettreCle",
                table: "CnamLetterValues");

            migrationBuilder.DropColumn(
                name: "ClinicId",
                table: "Medications");

            migrationBuilder.DropColumn(
                name: "ClinicId",
                table: "DentalActCodes");

            migrationBuilder.DropColumn(
                name: "ClinicId",
                table: "CnamNomenclatureEntries");

            migrationBuilder.DropColumn(
                name: "ClinicId",
                table: "CnamLetterValues");

            migrationBuilder.CreateIndex(
                name: "IX_Medications_BrandName",
                table: "Medications",
                column: "BrandName");

            migrationBuilder.CreateIndex(
                name: "IX_DentalActCodes_CodeActe",
                table: "DentalActCodes",
                column: "CodeActe",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CnamNomenclatureEntries_CodeActe",
                table: "CnamNomenclatureEntries",
                column: "CodeActe",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CnamLetterValues_LettreCle",
                table: "CnamLetterValues",
                column: "LettreCle",
                unique: true);
        }
    }
}
