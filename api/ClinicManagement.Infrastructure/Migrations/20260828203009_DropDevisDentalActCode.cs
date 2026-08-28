using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropDevisDentalActCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CodeActe",
                table: "TreatmentPlanItems");

            migrationBuilder.DropColumn(
                name: "DentalActCodeId",
                table: "TreatmentPlanItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CodeActe",
                table: "TreatmentPlanItems",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DentalActCodeId",
                table: "TreatmentPlanItems",
                type: "uuid",
                nullable: true);
        }
    }
}
