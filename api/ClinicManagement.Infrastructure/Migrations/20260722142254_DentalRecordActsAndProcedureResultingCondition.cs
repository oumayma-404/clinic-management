using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DentalRecordActsAndProcedureResultingCondition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ResultingCondition",
                table: "ProcedureTypes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DentalRecordActs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DentalRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcedureTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcedureName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Cost = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    ToothNumbers = table.Column<string>(type: "text", nullable: false),
                    ResultingCondition = table.Column<int>(type: "integer", nullable: true),
                    Surfaces = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DentalRecordActs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DentalRecordActs_DentalRecords_DentalRecordId",
                        column: x => x.DentalRecordId,
                        principalTable: "DentalRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DentalRecordActs_DentalRecordId",
                table: "DentalRecordActs",
                column: "DentalRecordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DentalRecordActs");

            migrationBuilder.DropColumn(
                name: "ResultingCondition",
                table: "ProcedureTypes");
        }
    }
}
