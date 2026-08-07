using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPerClinicTtnIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TtnApiSecretEncrypted",
                table: "Clinics",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TtnCertificateKey",
                table: "Clinics",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TtnCertificatePasswordEncrypted",
                table: "Clinics",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TtnUsername",
                table: "Clinics",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TtnApiSecretEncrypted",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "TtnCertificateKey",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "TtnCertificatePasswordEncrypted",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "TtnUsername",
                table: "Clinics");
        }
    }
}
