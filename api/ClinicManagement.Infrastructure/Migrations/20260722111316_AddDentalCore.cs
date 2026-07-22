using System;
using Microsoft.EntityFrameworkCore.Migrations;
using ClinicManagement.Infrastructure.Persistence;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDentalCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DentalActCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeActe = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DesignationFr = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    LettreCle = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Coefficient = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DefaultFee = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    RequiresAccordPrealable = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsProvisional = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DentalActCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ToothStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToothNumber = table.Column<int>(type: "integer", nullable: false),
                    Condition = table.Column<int>(type: "integer", nullable: false),
                    Surfaces = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToothStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ToothStates_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TreatmentPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AcceptedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TotalPlanned = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreatmentPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Installments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TreatmentPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    AmountPaid = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    LastMethod = table.Column<int>(type: "integer", nullable: true),
                    LastPaidOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Installments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Installments_TreatmentPlans_TreatmentPlanId",
                        column: x => x.TreatmentPlanId,
                        principalTable: "TreatmentPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TreatmentPlanItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TreatmentPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    DentalActCodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CodeActe = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DesignationFr = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ToothNumbers = table.Column<string>(type: "text", nullable: false),
                    PlannedCost = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DoneDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LinkedDentalRecordId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreatmentPlanItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TreatmentPlanItems_TreatmentPlans_TreatmentPlanId",
                        column: x => x.TreatmentPlanId,
                        principalTable: "TreatmentPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DentalActCodes_CodeActe",
                table: "DentalActCodes",
                column: "CodeActe",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Installments_TreatmentPlanId",
                table: "Installments",
                column: "TreatmentPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_ToothStates_PatientId_ToothNumber",
                table: "ToothStates",
                columns: new[] { "PatientId", "ToothNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlanItems_TreatmentPlanId",
                table: "TreatmentPlanItems",
                column: "TreatmentPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlans_ClinicId_Number",
                table: "TreatmentPlans",
                columns: new[] { "ClinicId", "Number" },
                unique: true,
                filter: "\"Number\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlans_ClinicId_PatientId",
                table: "TreatmentPlans",
                columns: new[] { "ClinicId", "PatientId" });

            // Seed the provisional dental act catalog (chapitre DCH) from the shared DentalActCatalogSeed
            // single-source-of-truth. Every row is IsProvisional = true ("à vérifier") with no Coefficient
            // (the cotation lives in the NGAP arrêté) until an admin confirms/completes it. Deterministic ids.
            var seededAt = DentalActCatalogSeed.SeededAtUtc;

            foreach (var a in DentalActCatalogSeed.Acts)
            {
                migrationBuilder.InsertData(
                    table: "DentalActCodes",
                    columns: new[] { "Id", "CodeActe", "DesignationFr", "LettreCle", "Coefficient", "Category", "DefaultFee", "RequiresAccordPrealable", "IsActive", "IsProvisional", "CreatedAt", "UpdatedAt" },
                    values: new object[] { a.Id, a.CodeActe, a.DesignationFr, DentalActCatalogSeed.LettreCle, null, a.Category, null, a.RequiresAccordPrealable, true, true, seededAt, null });
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DentalActCodes");

            migrationBuilder.DropTable(
                name: "Installments");

            migrationBuilder.DropTable(
                name: "ToothStates");

            migrationBuilder.DropTable(
                name: "TreatmentPlanItems");

            migrationBuilder.DropTable(
                name: "TreatmentPlans");
        }
    }
}
