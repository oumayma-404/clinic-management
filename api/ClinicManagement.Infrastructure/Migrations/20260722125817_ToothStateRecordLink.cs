using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ToothStateRecordLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The odontogram is now record-driven (every ToothState is produced by a dental record). Any
            // pre-existing rows came from the retired manual editor and have no source record/treatment date,
            // so clear them before adding the non-null TreatmentDate + record link.
            migrationBuilder.Sql("DELETE FROM \"ToothStates\";");

            migrationBuilder.DropIndex(
                name: "IX_ToothStates_PatientId_ToothNumber",
                table: "ToothStates");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "ToothStates");

            migrationBuilder.AddColumn<Guid>(
                name: "DentalRecordId",
                table: "ToothStates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TreatmentDate",
                table: "ToothStates",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "IX_ToothStates_DentalRecordId",
                table: "ToothStates",
                column: "DentalRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_ToothStates_PatientId_ToothNumber",
                table: "ToothStates",
                columns: new[] { "PatientId", "ToothNumber" });

            migrationBuilder.AddForeignKey(
                name: "FK_ToothStates_DentalRecords_DentalRecordId",
                table: "ToothStates",
                column: "DentalRecordId",
                principalTable: "DentalRecords",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ToothStates_DentalRecords_DentalRecordId",
                table: "ToothStates");

            migrationBuilder.DropIndex(
                name: "IX_ToothStates_DentalRecordId",
                table: "ToothStates");

            migrationBuilder.DropIndex(
                name: "IX_ToothStates_PatientId_ToothNumber",
                table: "ToothStates");

            migrationBuilder.DropColumn(
                name: "DentalRecordId",
                table: "ToothStates");

            migrationBuilder.DropColumn(
                name: "TreatmentDate",
                table: "ToothStates");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "ToothStates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ToothStates_PatientId_ToothNumber",
                table: "ToothStates",
                columns: new[] { "PatientId", "ToothNumber" },
                unique: true);
        }
    }
}
