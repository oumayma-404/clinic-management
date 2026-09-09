using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMedicalDocumentDentalRecordId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DentalRecordId",
                table: "MedicalDocuments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MedicalDocuments_DentalRecordId",
                table: "MedicalDocuments",
                column: "DentalRecordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MedicalDocuments_DentalRecordId",
                table: "MedicalDocuments");

            migrationBuilder.DropColumn(
                name: "DentalRecordId",
                table: "MedicalDocuments");
        }
    }
}
