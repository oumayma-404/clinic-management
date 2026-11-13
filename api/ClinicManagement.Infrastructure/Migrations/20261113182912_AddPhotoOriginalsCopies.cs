using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotoOriginalsCopies : Migration
    {
        /// <summary>
        /// features/camera-photo-import, phase 2: one row per clinic PC that reported copying the camera originals it
        /// keeps to the archive disk. Purely additive — a new table, nothing altered, no backfill (a PC with no row has
        /// simply never reported, which is what the 7-day check measures from).
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PhotoOriginalsCopies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    HeldBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastCompleteCopyAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastReportAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastReportedFiles = table.Column<int>(type: "integer", nullable: false),
                    LastReportedBytes = table.Column<long>(type: "bigint", nullable: false)
                    // ⚠️ The scaffolded `xmin` column was removed: Entity<T>.Version maps onto PostgreSQL's SYSTEM column,
                    // which CREATE TABLE refuses (« column name "xmin" conflicts with a system column name »).
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoOriginalsCopies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoOriginalsCopies_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoOriginalsCopies_ClinicId_HeldBy",
                table: "PhotoOriginalsCopies",
                columns: new[] { "ClinicId", "HeldBy" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PhotoOriginalsCopies");
        }
    }
}
