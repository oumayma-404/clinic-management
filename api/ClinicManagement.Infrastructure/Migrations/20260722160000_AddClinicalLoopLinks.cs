using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicalLoopLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Diagnosis vs. Treatment discriminator on odontogram entries. Existing rows are all
            // treatment-derived → default 0 (Treatment).
            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "ToothStates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Optional link from an appointment to the treatment-plan step it schedules.
            migrationBuilder.AddColumn<Guid>(
                name: "TreatmentPlanItemId",
                table: "Appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TreatmentPlanItemId",
                table: "Appointments",
                column: "TreatmentPlanItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_TreatmentPlanItemId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "TreatmentPlanItemId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "ToothStates");
        }
    }
}
