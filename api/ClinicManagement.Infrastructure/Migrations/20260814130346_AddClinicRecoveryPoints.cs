using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicRecoveryPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastArchiveDownloadedAtUtc",
                table: "Clinics",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClinicRecoveryPoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    Contents = table.Column<int>(type: "integer", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    TableCount = table.Column<int>(type: "integer", nullable: true),
                    RowCount = table.Column<int>(type: "integer", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true)
                    // ⚠️ EF's differ emitted `xmin = table.Column<uint>(type: "xid", rowVersion: true, …)` here and
                    // it is REMOVED BY HAND. `Entity<T>.Version` is mapped onto PostgreSQL's *system* column, so the
                    // scaffolder writes it out as a real one and the migration fails with
                    // `column name "xmin" conflicts with a system column name` — the same rejection that makes
                    // AddConcurrencyToken's Up() deliberately empty. The row still gets its token from the system
                    // column; nothing is lost by removing the line.
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicRecoveryPoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicRecoveryPoints_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicRecoveryPoints_ClinicId_StartedAt",
                table: "ClinicRecoveryPoints",
                columns: new[] { "ClinicId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicRecoveryPoints");

            migrationBuilder.DropColumn(
                name: "LastArchiveDownloadedAtUtc",
                table: "Clinics");
        }
    }
}
