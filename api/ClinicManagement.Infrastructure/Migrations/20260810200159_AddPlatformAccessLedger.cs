using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// The vendor console's own access ledger (<c>platform-console</c> Part 3, FR-5, AC-7.3).
    ///
    /// <para><b>Purely additive.</b> One new table and two indexes; no existing column is touched, no row is
    /// rewritten and there is no backfill — so neither hazard a differ's output usually carries applies here: there
    /// is no destructive statement to have been emitted above the copy it would destroy, and no scaffolded default
    /// whose meaning to the reading code has to be argued about, since the table starts empty and its rows are
    /// only ever inserted.</para>
    ///
    /// <para>⚠️ <b>No foreign key to <c>Clinics</c> and none to <c>PlatformAccounts</c> — the one thing about this
    /// table that looks like an omission and is not.</b> Its two siblings from Part 2
    /// (<c>ClinicActivityDays</c>, <c>ClinicActivitySnapshots</c>) cascade from a cabinet on purpose: those are
    /// measurements <i>of</i> a cabinet, meaningless once it is closed. This records what the <b>vendor</b> did, and
    /// « who opened the file of the practice that has since been deleted? » is the row an audit of this console
    /// would be looking for first — a cascade would remove exactly it. <c>AccountEmail</c> and <c>ClinicName</c>
    /// are denormalised for the same reason, so such a row still names both parties.</para>
    ///
    /// <para>⚠️ Both indexes are <c>(dimension, OccurredAt)</c> rather than on the dimension alone: the journal's
    /// only order is newest-first and its two filters are « ce compte » and « ce cabinet », so a bare equality
    /// index would leave PostgreSQL sorting the matched rows on every page.</para>
    /// </summary>
    public partial class AddPlatformAccessLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformAccessEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformAccessEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAccessEntries_ClinicId_OccurredAt",
                table: "PlatformAccessEntries",
                columns: new[] { "ClinicId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAccessEntries_PlatformAccountId_OccurredAt",
                table: "PlatformAccessEntries",
                columns: new[] { "PlatformAccountId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformAccessEntries");
        }
    }
}
