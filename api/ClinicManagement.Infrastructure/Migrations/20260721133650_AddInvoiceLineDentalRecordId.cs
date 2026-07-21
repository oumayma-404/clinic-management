using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceLineDentalRecordId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DentalRecordId",
                table: "InvoiceLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_DentalRecordId",
                table: "InvoiceLines",
                column: "DentalRecordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InvoiceLines_DentalRecordId",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "DentalRecordId",
                table: "InvoiceLines");
        }
    }
}
