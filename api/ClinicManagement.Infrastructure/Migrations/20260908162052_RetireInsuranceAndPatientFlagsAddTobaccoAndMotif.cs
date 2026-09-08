using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Retires the generic private-insurance block and the « signalement patient » table; adds « Motif de
    /// consultation » and « Tabac ».
    ///
    /// <para><b>Destructive, and deliberately so.</b> Four columns and one table go. Nothing reads the insurance
    /// block but the patient screens and two exports — no invoice, no CNAM bulletin, no PDF form (CNAM identity is
    /// <c>CnamInfo</c>, a different owned type, and is untouched). The flags table backed one filter chip and a
    /// badge, both removed with it.</para>
    ///
    /// <para>⚠️ <b>No backfill, and none is possible</b> — this drops data rather than reshaping it, so there is
    /// nothing for the destructive-before-backfill hazard to bite on. Take a dump first if the deployment wants
    /// the insurers kept; <c>Down()</c> restores the shape and not the rows.</para>
    ///
    /// <para>⚠️ Dropping <c>PatientFlags</c> also removes a <b>deletion blocker</b>: a patient carrying a flag
    /// could not be deleted, and now can (nothing else about the guard changed). That is the right reading — a
    /// signalement is a marker, not a record — but it is a behaviour change on a destructive path.</para>
    /// </summary>
    public partial class RetireInsuranceAndPatientFlagsAddTobaccoAndMotif : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PatientFlags");

            migrationBuilder.DropColumn(
                name: "InsuranceExpiryDate",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "InsuranceGroupNumber",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "InsurancePolicyNumber",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "InsuranceProvider",
                table: "Patients");

            migrationBuilder.AddColumn<string>(
                name: "ConsultationReason",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SmokingPerDay",
                table: "Patients",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SmokingStatus",
                table: "Patients",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SmokingUnit",
                table: "Patients",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConsultationReason",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "SmokingPerDay",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "SmokingStatus",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "SmokingUnit",
                table: "Patients");

            migrationBuilder.AddColumn<DateTime>(
                name: "InsuranceExpiryDate",
                table: "Patients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InsuranceGroupNumber",
                table: "Patients",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InsurancePolicyNumber",
                table: "Patients",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InsuranceProvider",
                table: "Patients",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PatientFlags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FlagType = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    // ⚠️ The scaffolder's `xmin` column was removed by hand — `Entity<T>.Version` maps onto
                    // PostgreSQL's *system* column, so `CREATE TABLE` refuses it with « column name "xmin"
                    // conflicts with a system column name ». Same fix as `AddClinicSubscriptions`,
                    // `AddSuppliers` and `AddTreatmentPlanItemSteps`; a row still takes its token from the
                    // system column. `Up()` needed none of it — it creates no table.
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientFlags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientFlags_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PatientFlags_PatientId",
                table: "PatientFlags",
                column: "PatientId");
        }
    }
}
