using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformNewsInApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "HiddenFromAppAtUtc",
                table: "PlatformNewsBroadcasts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlatformNewsReadMarkers",
                columns: table => new
                {
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReadUpToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformNewsReadMarkers", x => new { x.ClinicId, x.UserId });
                    table.ForeignKey(
                        name: "FK_PlatformNewsReadMarkers_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformNewsReadMarkers_UserId",
                table: "PlatformNewsReadMarkers",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformNewsReadMarkers");

            migrationBuilder.DropColumn(
                name: "HiddenFromAppAtUtc",
                table: "PlatformNewsBroadcasts");
        }
    }
}
