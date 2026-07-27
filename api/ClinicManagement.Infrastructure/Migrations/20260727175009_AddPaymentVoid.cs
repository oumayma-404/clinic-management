using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentVoid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsVoided",
                table: "Payments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceInstallmentPaymentId",
                table: "Payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "Payments",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAt",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidedByName",
                table: "Payments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidedByUserId",
                table: "Payments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_InvoiceId_PaidOn",
                table: "Payments",
                columns: new[] { "InvoiceId", "PaidOn" },
                filter: "NOT \"IsVoided\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_InvoiceId_PaidOn",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "IsVoided",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "SourceInstallmentPaymentId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "VoidedByName",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                table: "Payments");
        }
    }
}
