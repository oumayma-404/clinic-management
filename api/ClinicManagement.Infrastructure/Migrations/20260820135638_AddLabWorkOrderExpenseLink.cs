using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLabWorkOrderExpenseLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ExpenseId",
                table: "LabWorkOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LabWorkOrders_ExpenseId",
                table: "LabWorkOrders",
                column: "ExpenseId");

            migrationBuilder.AddForeignKey(
                name: "FK_LabWorkOrders_Expenses_ExpenseId",
                table: "LabWorkOrders",
                column: "ExpenseId",
                principalTable: "Expenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LabWorkOrders_Expenses_ExpenseId",
                table: "LabWorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_LabWorkOrders_ExpenseId",
                table: "LabWorkOrders");

            migrationBuilder.DropColumn(
                name: "ExpenseId",
                table: "LabWorkOrders");
        }
    }
}
