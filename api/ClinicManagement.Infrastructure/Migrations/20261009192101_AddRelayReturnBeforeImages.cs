using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRelayReturnBeforeImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RelayReturnBeforeImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnId = table.Column<Guid>(type: "uuid", nullable: false),
                    Table = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EntityKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    RowJson = table.Column<string>(type: "text", nullable: false),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false),
                    SavedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelayReturnBeforeImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RelayReturnBeforeImages_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RelayReturnBeforeImages_ClinicId_Table_EntityKey",
                table: "RelayReturnBeforeImages",
                columns: new[] { "ClinicId", "Table", "EntityKey" });

            migrationBuilder.CreateIndex(
                name: "IX_RelayReturnBeforeImages_ReturnId",
                table: "RelayReturnBeforeImages",
                column: "ReturnId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RelayReturnBeforeImages");
        }
    }
}
