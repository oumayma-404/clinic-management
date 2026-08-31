using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientFileResidency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "StorageKey",
                table: "PatientFiles",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "PatientFiles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviewStorageKey",
                table: "PatientFiles",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Residency",
                table: "PatientFiles",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PatientFiles_ResidencyForm",
                table: "PatientFiles",
                sql: "(\"Residency\" = 1 AND \"StorageKey\" IS NOT NULL) OR (\"Residency\" = 2 AND \"StorageKey\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PatientFiles_ResidencyForm",
                table: "PatientFiles");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "PatientFiles");

            migrationBuilder.DropColumn(
                name: "PreviewStorageKey",
                table: "PatientFiles");

            migrationBuilder.DropColumn(
                name: "Residency",
                table: "PatientFiles");

            migrationBuilder.AlterColumn<string>(
                name: "StorageKey",
                table: "PatientFiles",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);
        }
    }
}
