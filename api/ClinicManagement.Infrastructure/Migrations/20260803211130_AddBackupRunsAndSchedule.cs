using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// L4 — the backup ledger (<c>BackupRuns</c>) and the four per-clinic schedule columns.
    ///
    /// <para>⚠️ <b>Two deliberate edits to what the scaffolder produced</b>, both load-bearing:</para>
    ///
    /// <para>(a) EF's differ emitted <c>xmin = table.Column&lt;uint&gt;(type: "xid", rowVersion: true)</c> inside
    /// <c>CreateTable</c>, because <c>Entity&lt;TId&gt;.Version</c> is mapped onto PostgreSQL's <c>xmin</c> system
    /// column for solution-wide optimistic concurrency. PostgreSQL <b>rejects</b> that
    /// (<c>column name "xmin" conflicts with a system column name</c>) — the same trap that forced the
    /// <c>AddConcurrencyToken</c> migration to ship with a deliberately empty <c>Up()</c>. The column is removed
    /// here; the concurrency token still works, because <c>xmin</c> exists on every table by virtue of being a
    /// system column.</para>
    ///
    /// <para>(b) The four <c>Clinics</c> columns were scaffolded with <c>defaultValue: false / 0</c>, which would
    /// have left every existing clinic with the backup <b>disabled</b>, an hour of 0, a retention of 0 and a
    /// staleness threshold of 0 — a feature that ships switched off for everyone who already has the product, and
    /// a retention of 0 is the one value the pruner's floor exists to survive. They are backfilled to the
    /// entity's own <c>Clinic.DefaultBackup*</c> constants, so the column and the constructor cannot disagree.</para>
    /// </summary>
    public partial class AddBackupRunsAndSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backup ON by default: the point of L4a is that protection must not depend on somebody remembering
            // to switch it on, and an upgrade is exactly when nobody is looking at the settings screen.
            migrationBuilder.AddColumn<bool>(
                name: "BackupEnabled",
                table: "Clinics",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "BackupHourLocal",
                table: "Clinics",
                type: "integer",
                nullable: false,
                defaultValue: ClinicManagement.Domain.Entities.Clinic.DefaultBackupHourLocal);

            migrationBuilder.AddColumn<int>(
                name: "BackupRetentionCount",
                table: "Clinics",
                type: "integer",
                nullable: false,
                defaultValue: ClinicManagement.Domain.Entities.Clinic.DefaultBackupRetentionCount);

            migrationBuilder.AddColumn<int>(
                name: "BackupStaleAfterHours",
                table: "Clinics",
                type: "integer",
                nullable: false,
                defaultValue: ClinicManagement.Domain.Entities.Clinic.DefaultBackupStaleAfterHours);

            // `AddColumn` with a default does backfill existing rows, so this is belt-and-braces for a database
            // where an earlier partial run left the columns present-but-zero. Idempotent, one row per clinic, and
            // `verify-schema` asserts the count it leaves behind (zero clinics with a non-positive retention).
            migrationBuilder.Sql($@"
                UPDATE ""Clinics""
                SET ""BackupHourLocal"" = {ClinicManagement.Domain.Entities.Clinic.DefaultBackupHourLocal},
                    ""BackupRetentionCount"" = {ClinicManagement.Domain.Entities.Clinic.DefaultBackupRetentionCount},
                    ""BackupStaleAfterHours"" = {ClinicManagement.Domain.Entities.Clinic.DefaultBackupStaleAfterHours}
                WHERE ""BackupRetentionCount"" <= 0 OR ""BackupStaleAfterHours"" <= 0;");

            migrationBuilder.CreateTable(
                name: "BackupRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    DestinationPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    VerifiedObjectCount = table.Column<int>(type: "integer", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    Trigger = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                    // NO xmin column here — see the class remarks. PostgreSQL refuses it, and every table
                    // already has one as a system column.
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackupRuns_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackupRuns_ClinicId_StartedAt",
                table: "BackupRuns",
                columns: new[] { "ClinicId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupRuns");

            migrationBuilder.DropColumn(
                name: "BackupEnabled",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "BackupHourLocal",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "BackupRetentionCount",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "BackupStaleAfterHours",
                table: "Clinics");
        }
    }
}
