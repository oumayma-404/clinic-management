using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// The vendor console's two counter tables (<c>platform-console</c> Part 2, FR-3).
    ///
    /// <para><b>Purely additive.</b> Two new tables, three indexes, two cascading foreign keys onto
    /// <c>Clinics</c>; no existing column is touched, no row is rewritten and there is no backfill — so the
    /// ordering hazard that makes a differ's output dangerous (a destructive statement emitted above the copy it
    /// would destroy) does not arise here, and neither does the « what does this scaffolded default mean to the
    /// code that reads it? » question, since both tables start empty.</para>
    ///
    /// <para>⚠️ <b>An unmeasured cabinet is a missing row, and that is the intended state on day one.</b> The
    /// portfolio LEFT JOINs the snapshot and renders a missing one as « jamais mesuré » rather than as zeros
    /// (EC-15), so no seeding is needed or wanted: writing zero rows for every clinic here would assert that the
    /// pass had already run and found nothing.</para>
    ///
    /// <para>⚠️ <c>ClinicActivityDays.Day</c> is a PostgreSQL <c>date</c> (<see cref="DateOnly"/>), not a
    /// timestamp: it is a Tunisian calendar day, and the context's global UTC value converter — which applies to
    /// every <c>DateTime</c> — would shift it across the very boundary the figure is defined on.</para>
    /// </summary>
    public partial class AddClinicActivityCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClinicActivityDays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Writes = table.Column<int>(type: "integer", nullable: false),
                    Appointments = table.Column<int>(type: "integer", nullable: false),
                    PatientsCreated = table.Column<int>(type: "integer", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicActivityDays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicActivityDays_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClinicActivitySnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Writes7d = table.Column<int>(type: "integer", nullable: false),
                    Writes30d = table.Column<int>(type: "integer", nullable: false),
                    Appointments30d = table.Column<int>(type: "integer", nullable: false),
                    ActiveDays30d = table.Column<int>(type: "integer", nullable: false),
                    LastWriteAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Patients = table.Column<int>(type: "integer", nullable: false),
                    Users = table.Column<int>(type: "integer", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CollectedThisMonth = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicActivitySnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicActivitySnapshots_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicActivityDays_ClinicId_Day",
                table: "ClinicActivityDays",
                columns: new[] { "ClinicId", "Day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicActivitySnapshots_ClinicId",
                table: "ClinicActivitySnapshots",
                column: "ClinicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicActivitySnapshots_Writes30d",
                table: "ClinicActivitySnapshots",
                column: "Writes30d");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicActivityDays");

            migrationBuilder.DropTable(
                name: "ClinicActivitySnapshots");
        }
    }
}
