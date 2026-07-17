using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCnamBulletinFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: Clinics.MatriculeFiscal is intentionally NOT added here — the later
            // 20260717174602_AddInvoicesAndClinicBilling migration (facturation feature) owns that column.
            // Adding it here too would double-add and crash startup migration.

            // Doctor — CNAM provider code (Code professionnel de santé).
            migrationBuilder.AddColumn<string>(
                name: "CodeProfessionnelSante",
                table: "Doctors",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            // Patient — optional CNAM identity (owned CnamInfo value object), all nullable.
            migrationBuilder.AddColumn<string>(
                name: "CnamIdentifiantUnique",
                table: "Patients",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CnamRegime",
                table: "Patients",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CnamAssureFirstName",
                table: "Patients",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CnamAssureLastName",
                table: "Patients",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CnamAssureAddress",
                table: "Patients",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CnamAssurePostalCode",
                table: "Patients",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CnamMaladeLien",
                table: "Patients",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CnamMaladeLienRang",
                table: "Patients",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CodeProfessionnelSante", table: "Doctors");
            migrationBuilder.DropColumn(name: "CnamIdentifiantUnique", table: "Patients");
            migrationBuilder.DropColumn(name: "CnamRegime", table: "Patients");
            migrationBuilder.DropColumn(name: "CnamAssureFirstName", table: "Patients");
            migrationBuilder.DropColumn(name: "CnamAssureLastName", table: "Patients");
            migrationBuilder.DropColumn(name: "CnamAssureAddress", table: "Patients");
            migrationBuilder.DropColumn(name: "CnamAssurePostalCode", table: "Patients");
            migrationBuilder.DropColumn(name: "CnamMaladeLien", table: "Patients");
            migrationBuilder.DropColumn(name: "CnamMaladeLienRang", table: "Patients");
        }
    }
}
