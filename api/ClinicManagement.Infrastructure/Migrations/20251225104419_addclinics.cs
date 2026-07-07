using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class addclinics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClinicId",
                table: "Patients",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ClinicId",
                table: "Appointments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "DoctorId",
                table: "Appointments",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Clinics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clinics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Patients_ClinicId",
                table: "Patients",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ClinicId",
                table: "Appointments",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_Clinics_Code",
                table: "Clinics",
                column: "Code",
                unique: true,
                filter: "\"Code\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Users_ClinicId",
                table: "Users",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_ClinicId_Role",
                table: "Users",
                columns: new[] { "ClinicId", "Role" });

            // Delete orphaned data with invalid ClinicId before adding foreign key
            // Delete in order: dependent records first, then appointments, then patients
            migrationBuilder.Sql(@"
                -- Delete MedicalDocuments for orphaned patients
                DELETE FROM ""MedicalDocuments"" 
                WHERE ""PatientId"" IN (
                    SELECT ""Id"" FROM ""Patients"" 
                    WHERE ""ClinicId"" = '00000000-0000-0000-0000-000000000000' 
                       OR ""ClinicId"" NOT IN (SELECT ""Id"" FROM ""Clinics"")
                );
            ");

            migrationBuilder.Sql(@"
                -- Delete other dependent records for orphaned patients
                DELETE FROM ""PatientFiles"" 
                WHERE ""PatientId"" IN (
                    SELECT ""Id"" FROM ""Patients"" 
                    WHERE ""ClinicId"" = '00000000-0000-0000-0000-000000000000' 
                       OR ""ClinicId"" NOT IN (SELECT ""Id"" FROM ""Clinics"")
                );
            ");

            migrationBuilder.Sql(@"
                DELETE FROM ""PatientFlags"" 
                WHERE ""PatientId"" IN (
                    SELECT ""Id"" FROM ""Patients"" 
                    WHERE ""ClinicId"" = '00000000-0000-0000-0000-000000000000' 
                       OR ""ClinicId"" NOT IN (SELECT ""Id"" FROM ""Clinics"")
                );
            ");

            migrationBuilder.Sql(@"
                DELETE FROM ""PatientMedicalHistories"" 
                WHERE ""PatientId"" IN (
                    SELECT ""Id"" FROM ""Patients"" 
                    WHERE ""ClinicId"" = '00000000-0000-0000-0000-000000000000' 
                       OR ""ClinicId"" NOT IN (SELECT ""Id"" FROM ""Clinics"")
                );
            ");

            migrationBuilder.Sql(@"
                DELETE FROM ""PatientFamilyHistories"" 
                WHERE ""PatientId"" IN (
                    SELECT ""Id"" FROM ""Patients"" 
                    WHERE ""ClinicId"" = '00000000-0000-0000-0000-000000000000' 
                       OR ""ClinicId"" NOT IN (SELECT ""Id"" FROM ""Clinics"")
                );
            ");

            migrationBuilder.Sql(@"
                DELETE FROM ""DentalRecords"" 
                WHERE ""PatientId"" IN (
                    SELECT ""Id"" FROM ""Patients"" 
                    WHERE ""ClinicId"" = '00000000-0000-0000-0000-000000000000' 
                       OR ""ClinicId"" NOT IN (SELECT ""Id"" FROM ""Clinics"")
                );
            ");

            migrationBuilder.Sql(@"
                -- Delete orphaned appointments
                DELETE FROM ""Appointments"" 
                WHERE ""ClinicId"" = '00000000-0000-0000-0000-000000000000' 
                   OR ""ClinicId"" NOT IN (SELECT ""Id"" FROM ""Clinics"");
            ");

            migrationBuilder.Sql(@"
                -- Delete orphaned patients (after all dependent records are deleted)
                DELETE FROM ""Patients"" 
                WHERE ""ClinicId"" = '00000000-0000-0000-0000-000000000000' 
                   OR ""ClinicId"" NOT IN (SELECT ""Id"" FROM ""Clinics"");
            ");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Clinics_ClinicId",
                table: "Appointments",
                column: "ClinicId",
                principalTable: "Clinics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Patients_Clinics_ClinicId",
                table: "Patients",
                column: "ClinicId",
                principalTable: "Clinics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Clinics_ClinicId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_Patients_Clinics_ClinicId",
                table: "Patients");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Clinics");

            migrationBuilder.DropIndex(
                name: "IX_Patients_ClinicId",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_ClinicId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ClinicId",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "ClinicId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "DoctorId",
                table: "Appointments");
        }
    }
}
