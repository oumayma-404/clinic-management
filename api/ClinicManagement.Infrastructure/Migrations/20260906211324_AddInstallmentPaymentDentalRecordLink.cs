using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInstallmentPaymentDentalRecordLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DentalRecordId",
                table: "InstallmentPayments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstallmentPayments_DentalRecordId",
                table: "InstallmentPayments",
                column: "DentalRecordId",
                filter: "\"DentalRecordId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InstallmentPayments_DentalRecordId",
                table: "InstallmentPayments");

            migrationBuilder.DropColumn(
                name: "DentalRecordId",
                table: "InstallmentPayments");
        }
    }
}
