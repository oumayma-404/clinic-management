using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDoctorCachetAndOrdre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CachetContentType",
                table: "Doctors",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CachetStorageKey",
                table: "Doctors",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrdreNumberCnomdt",
                table: "Doctors",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CachetContentType",
                table: "Doctors");

            migrationBuilder.DropColumn(
                name: "CachetStorageKey",
                table: "Doctors");

            migrationBuilder.DropColumn(
                name: "OrdreNumberCnomdt",
                table: "Doctors");
        }
    }
}
