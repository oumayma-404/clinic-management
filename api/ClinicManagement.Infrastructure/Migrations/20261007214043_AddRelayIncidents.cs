using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRelayIncidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RelayIncidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelayId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EmailedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                    // No `xmin` column: Entity<T>.Version maps onto PostgreSQL's system column, which CREATE TABLE refuses.
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelayIncidents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RelayIncidents_ClinicRelays_RelayId",
                        column: x => x.RelayId,
                        principalTable: "ClinicRelays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RelayIncidents_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RelayIncidents_ClinicId_StartedAtUtc",
                table: "RelayIncidents",
                columns: new[] { "ClinicId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RelayIncidents_RelayId_Kind",
                table: "RelayIncidents",
                columns: new[] { "RelayId", "Kind" },
                unique: true,
                filter: "\"EndedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RelayIncidents");
        }
    }
}
