using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicRelay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClinicChangeCursors",
                columns: table => new
                {
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastSeq = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicChangeCursors", x => x.ClinicId);
                    table.ForeignKey(
                        name: "FK_ClinicChangeCursors_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClinicChanges",
                columns: table => new
                {
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Seq = table.Column<long>(type: "bigint", nullable: false),
                    Table = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EntityKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Op = table.Column<int>(type: "integer", nullable: false),
                    Origin = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicChanges", x => new { x.ClinicId, x.Seq });
                    table.ForeignKey(
                        name: "FK_ClinicChanges_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClinicRelays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PairingCodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PairingCodeExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SecretHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PublicKey = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CertificateFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LanAddresses = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    Build = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PairedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SeededAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SeedPercent = table.Column<int>(type: "integer", nullable: false),
                    AppliedSeq = table.Column<long>(type: "bigint", nullable: false),
                    HighWaterAtLastAck = table.Column<long>(type: "bigint", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastReadyAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FilesTotal = table.Column<int>(type: "integer", nullable: false),
                    FilesCopied = table.Column<int>(type: "integer", nullable: false),
                    DiskFreeBytes = table.Column<long>(type: "bigint", nullable: true),
                    ClockSkewSeconds = table.Column<int>(type: "integer", nullable: true),
                    MismatchSinceUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MismatchTables = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsUpdating = table.Column<bool>(type: "boolean", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RetiredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetiredReason = table.Column<int>(type: "integer", nullable: true),
                    RetiredByUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                    // ⚠️ The scaffolded `xmin` column is removed: Version maps onto the system column, which
                    // CREATE TABLE refuses (« conflicts with a system column name »).
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicRelays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicRelays_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicChanges_ClinicId_Table_EntityKey",
                table: "ClinicChanges",
                columns: new[] { "ClinicId", "Table", "EntityKey" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicRelays_ClinicId",
                table: "ClinicRelays",
                column: "ClinicId",
                unique: true,
                filter: "\"Status\" <> 3");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicRelays_PairingCodeHash",
                table: "ClinicRelays",
                column: "PairingCodeHash",
                unique: true,
                filter: "\"PairingCodeHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicRelays_SecretHash",
                table: "ClinicRelays",
                column: "SecretHash",
                unique: true,
                filter: "\"SecretHash\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicChangeCursors");

            migrationBuilder.DropTable(
                name: "ClinicChanges");

            migrationBuilder.DropTable(
                name: "ClinicRelays");
        }
    }
}
