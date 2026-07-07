using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStockItemClinicId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClinicId",
                table: "StockItems",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_StockItems_ClinicId",
                table: "StockItems",
                column: "ClinicId");

            migrationBuilder.AddForeignKey(
                name: "FK_StockItems_Clinics_ClinicId",
                table: "StockItems",
                column: "ClinicId",
                principalTable: "Clinics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockItems_Clinics_ClinicId",
                table: "StockItems");

            migrationBuilder.DropIndex(
                name: "IX_StockItems_ClinicId",
                table: "StockItems");

            migrationBuilder.DropColumn(
                name: "ClinicId",
                table: "StockItems");
        }
    }
}
