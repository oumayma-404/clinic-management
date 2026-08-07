using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Gives the seven clinical children of <c>Patients</c> a denormalised <c>ClinicId</c>, so each can carry a
    /// global query filter instead of relying on the per-handler check as its only layer.
    ///
    /// <para>
    /// ⚠️ <b>The backfill below is not optional and must stay between the columns and the indexes.</b> EF scaffolds
    /// <c>AddColumn</c> with <c>defaultValue: Guid.Empty</c>, and the filter added in the same change compares
    /// <c>ClinicId</c> to the scoped clinic — so a deployment that applied the columns without the backfill would
    /// leave every existing fiche, document, file, folder, antécédent and tooth state matching no clinic at all.
    /// The symptom is not an error: it is a clinic opening a patient of ten years' standing and finding an empty
    /// record. This is exactly the class of defect <c>verify-schema</c> exists for, which is why it gained
    /// <c>clinical-child-clinic-matches-patient</c> in the same change — one figure that catches both a backfill
    /// which covered nothing (rows left at <c>Guid.Empty</c>) and a write path that names the wrong clinic.
    /// </para>
    ///
    /// <para>
    /// Every one of the seven is a direct child of <c>Patients</c> by <c>PatientId</c>, so one shape of statement
    /// serves all seven and the clinic can only ever be the patient's own. Six cascade with their patient and
    /// <c>MedicalDocuments</c> restricts, so no row can outlive the patient it takes its clinic from — an orphan
    /// would keep <c>Guid.Empty</c> and be caught by the verifier rather than silently filtered away.
    /// </para>
    /// </summary>
    public partial class AddClinicIdToClinicalChildren : Migration
    {
        /// <summary>The seven tables, each a direct child of <c>Patients</c> by <c>PatientId</c>.</summary>
        private static readonly string[] ClinicalChildTables =
        {
            "ToothStates",
            "PatientMedicalHistories",
            "PatientFolders",
            "PatientFiles",
            "PatientFamilyHistories",
            "MedicalDocuments",
            "DentalRecords",
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClinicId",
                table: "ToothStates",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ClinicId",
                table: "PatientMedicalHistories",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ClinicId",
                table: "PatientFolders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ClinicId",
                table: "PatientFiles",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ClinicId",
                table: "PatientFamilyHistories",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ClinicId",
                table: "MedicalDocuments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ClinicId",
                table: "DentalRecords",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // ---- Backfill, BEFORE the indexes and before anything can read through the new filter ----
            // Read the class note above before touching this. One statement per table, identical in shape: the
            // clinic of a clinical child is its patient's, and there is no second candidate.
            foreach (var table in ClinicalChildTables)
            {
                migrationBuilder.Sql($"""
                    UPDATE "{table}" AS child
                    SET "ClinicId" = patient."ClinicId"
                    FROM "Patients" AS patient
                    WHERE patient."Id" = child."PatientId";
                    """);
            }

            migrationBuilder.CreateIndex(
                name: "IX_ToothStates_ClinicId",
                table: "ToothStates",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientMedicalHistories_ClinicId",
                table: "PatientMedicalHistories",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientFolders_ClinicId",
                table: "PatientFolders",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientFiles_ClinicId",
                table: "PatientFiles",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientFamilyHistories_ClinicId",
                table: "PatientFamilyHistories",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalDocuments_ClinicId",
                table: "MedicalDocuments",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_DentalRecords_ClinicId",
                table: "DentalRecords",
                column: "ClinicId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ToothStates_ClinicId",
                table: "ToothStates");

            migrationBuilder.DropIndex(
                name: "IX_PatientMedicalHistories_ClinicId",
                table: "PatientMedicalHistories");

            migrationBuilder.DropIndex(
                name: "IX_PatientFolders_ClinicId",
                table: "PatientFolders");

            migrationBuilder.DropIndex(
                name: "IX_PatientFiles_ClinicId",
                table: "PatientFiles");

            migrationBuilder.DropIndex(
                name: "IX_PatientFamilyHistories_ClinicId",
                table: "PatientFamilyHistories");

            migrationBuilder.DropIndex(
                name: "IX_MedicalDocuments_ClinicId",
                table: "MedicalDocuments");

            migrationBuilder.DropIndex(
                name: "IX_DentalRecords_ClinicId",
                table: "DentalRecords");

            migrationBuilder.DropColumn(
                name: "ClinicId",
                table: "ToothStates");

            migrationBuilder.DropColumn(
                name: "ClinicId",
                table: "PatientMedicalHistories");

            migrationBuilder.DropColumn(
                name: "ClinicId",
                table: "PatientFolders");

            migrationBuilder.DropColumn(
                name: "ClinicId",
                table: "PatientFiles");

            migrationBuilder.DropColumn(
                name: "ClinicId",
                table: "PatientFamilyHistories");

            migrationBuilder.DropColumn(
                name: "ClinicId",
                table: "MedicalDocuments");

            migrationBuilder.DropColumn(
                name: "ClinicId",
                table: "DentalRecords");
        }
    }
}
