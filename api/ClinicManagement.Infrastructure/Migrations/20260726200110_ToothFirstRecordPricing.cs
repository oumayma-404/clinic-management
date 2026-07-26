using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ToothFirstRecordPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Per-tooth pricing provenance on a dental act: the unit price its total was built from, and
            // whether that total is unit × treated teeth. Existing rows default to a flat fee (UnitCost null,
            // IsPerTooth false), which is exactly how they were entered.
            migrationBuilder.AddColumn<decimal>(
                name: "UnitCost",
                table: "DentalRecordActs",
                type: "decimal(18,3)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPerTooth",
                table: "DentalRecordActs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Widen the dental-record totals to millimes (3 decimals) so the derived Cost can equal the sum
            // of its acts (DentalRecordActs.Cost is already decimal(18,3)); a 2-decimal column rounded the
            // third decimal away. ProcedureTypes.DefaultCost seeds an act's unit price, so it matches too.
            migrationBuilder.AlterColumn<decimal>(
                name: "Cost",
                table: "DentalRecords",
                type: "decimal(18,3)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "AmountPaid",
                table: "DentalRecords",
                type: "decimal(18,3)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "DefaultCost",
                table: "ProcedureTypes",
                type: "decimal(18,3)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnitCost",
                table: "DentalRecordActs");

            migrationBuilder.DropColumn(
                name: "IsPerTooth",
                table: "DentalRecordActs");

            migrationBuilder.AlterColumn<decimal>(
                name: "Cost",
                table: "DentalRecords",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,3)");

            migrationBuilder.AlterColumn<decimal>(
                name: "AmountPaid",
                table: "DentalRecords",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,3)");

            migrationBuilder.AlterColumn<decimal>(
                name: "DefaultCost",
                table: "ProcedureTypes",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,3)",
                oldNullable: true);
        }
    }
}
