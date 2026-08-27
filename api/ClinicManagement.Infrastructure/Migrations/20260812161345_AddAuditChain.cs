using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditChain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ChainKey",
                table: "AuditEntries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "EntryHash",
                table: "AuditEntries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeclaredGap",
                table: "AuditEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PreviousHash",
                table: "AuditEntries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Sequence",
                table: "AuditEntries",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_ChainKey_Sequence",
                table: "AuditEntries",
                columns: new[] { "ChainKey", "Sequence" },
                unique: true,
                filter: "\"Sequence\" > 0");

            // ── The backfill, BELOW every DDL statement (the AddPlatformConsoleWrites precedent). Nothing above
            // is destructive, so there is no data to lose here — the order is kept so a later edit inherits it.
            //
            // ⚠️ `ChainKey`'s scaffolded default is Guid.Empty, which is CORRECT for the deployment-wide chain and
            // wrong for every clinic row. A new column's default is a behavioural decision, and this one would
            // have put a practice's entire history on the unattributed chain — where a walk would still read
            // « intact », because a chain of the wrong rows is a perfectly valid chain.
            migrationBuilder.Sql("""
                UPDATE "AuditEntries"
                SET "ChainKey" = "ClinicId"
                WHERE "ClinicId" IS NOT NULL
                  AND "ChainKey" = '00000000-0000-0000-0000-000000000000';
                """);

            // Existing rows are numbered but NOT hashed: no key exists in SQL, and inventing hashes for history
            // written before the chain would be fabricating exactly the evidence the chain is for. They are
            // therefore the ledger's *pre-chain* history — `AuditChain.Walk` counts them and reports them, and
            // what it refuses is an unhashed row appearing AFTER a hashed one, which is what erasing a hash to
            // hide an edit looks like.
            //
            // Offsetting by each chain's current maximum makes this re-runnable: a second pass numbers only the
            // rows still at 0, and continues from where the first stopped rather than colliding with it.
            migrationBuilder.Sql("""
                WITH base AS (
                    SELECT "ChainKey", COALESCE(MAX("Sequence"), 0) AS max_seq
                    FROM "AuditEntries"
                    GROUP BY "ChainKey"
                ),
                ordered AS (
                    SELECT a."Id",
                           b.max_seq + row_number() OVER (
                               PARTITION BY a."ChainKey" ORDER BY a."OccurredAt", a."Id") AS rn
                    FROM "AuditEntries" a
                    JOIN base b ON b."ChainKey" = a."ChainKey"
                    WHERE a."Sequence" = 0
                )
                UPDATE "AuditEntries" a
                SET "Sequence" = o.rn
                FROM ordered o
                WHERE a."Id" = o."Id";
                """);

            // The declared boundary at each chain's start — one row per chain, not one per historical entry.
            // It is what says « verifiable history begins here » rather than leaving a reader to infer it from a
            // count, and it keeps `audit-declared-gaps` reporting a handful of deliberate markers instead of a
            // deployment's entire past.
            migrationBuilder.Sql("""
                UPDATE "AuditEntries"
                SET "IsDeclaredGap" = true
                WHERE "EntryHash" IS NULL
                  AND "Sequence" = 1
                  AND "IsDeclaredGap" = false;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_ChainKey_Sequence",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "ChainKey",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "EntryHash",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "IsDeclaredGap",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "PreviousHash",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "AuditEntries");
        }
    }
}
