using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTreatmentPlanItemSteps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The catalogue act's proposed steps, as a JSON array. Every existing act gets « aucune étape
            // proposée », which is the ordinary case: an act done in one séance has none. `[]` rather than the
            // scaffolded `""` so the column is self-describing JSON — the converter reads both, treating blank
            // as an empty list, so nothing depends on which one is stored.
            migrationBuilder.AddColumn<string>(
                name: "DefaultSteps",
                table: "ProcedureTypes",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<Guid>(
                name: "TreatmentPlanItemStepId",
                table: "AppointmentProcedures",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TreatmentPlanItemSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TreatmentPlanItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SequenceNumber = table.Column<int>(type: "integer", nullable: false),
                    DoneDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LinkedDentalRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    EstimatedDurationMinutes = table.Column<int>(type: "integer", nullable: true)
                    // ⚠️ An `xmin` column was scaffolded here and REMOVED by hand. Entity<TId>.Version is mapped
                    // onto PostgreSQL's own `xmin` system column, which every table already has and which
                    // CREATE TABLE refuses to declare ("column name xmin conflicts with a system column name").
                    // The differ cannot know that, so it emits one for every new entity — see the root
                    // CLAUDE.md's first trap, and the three migrations that ship an empty Up() for this reason.
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreatmentPlanItemSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TreatmentPlanItemSteps_TreatmentPlanItems_TreatmentPlanItem~",
                        column: x => x.TreatmentPlanItemId,
                        principalTable: "TreatmentPlanItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentProcedures_TreatmentPlanItemStepId",
                table: "AppointmentProcedures",
                column: "TreatmentPlanItemStepId",
                filter: "\"TreatmentPlanItemStepId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlanItemSteps_LinkedDentalRecordId",
                table: "TreatmentPlanItemSteps",
                column: "LinkedDentalRecordId",
                filter: "\"LinkedDentalRecordId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlanItemSteps_TreatmentPlanItemId_SequenceNumber",
                table: "TreatmentPlanItemSteps",
                columns: new[] { "TreatmentPlanItemId", "SequenceNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TreatmentPlanItemSteps");

            migrationBuilder.DropIndex(
                name: "IX_AppointmentProcedures_TreatmentPlanItemStepId",
                table: "AppointmentProcedures");

            migrationBuilder.DropColumn(
                name: "DefaultSteps",
                table: "ProcedureTypes");

            migrationBuilder.DropColumn(
                name: "TreatmentPlanItemStepId",
                table: "AppointmentProcedures");
        }
    }
}
