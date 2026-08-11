using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// <c>platform-console</c> Part 4. Three columns and one index, all additive — nothing is altered, narrowed or
    /// dropped, so the destructive-before-backfill hazard has nothing to bite on. The backfill still sits below every
    /// DDL statement so a later edit inherits that order.
    ///
    /// <para><b><c>ClinicSubscriptions.LatestCoverKind</c></b> is the clock-free denormalisation the console's
    /// « en essai » filter reads, so that AC-2.3 can be a SQL predicate rather than a fold of N cabinets' ledgers
    /// before a page is cut (AC-2.4a, EC-11). ⚠️ It is <b>nullable</b> and stays null for a cabinet whose every entry
    /// has been cancelled: that is a real state, not a missing value, and defaulting it to <c>Paid</c> would put an
    /// unentitled cabinet in the portfolio's paid bucket — the class of scaffolded-default bug that has cost this
    /// repo a feature before.</para>
    ///
    /// <para><b><c>PlatformAccessEntries.IdempotencyKey</c></b> carries a <b>partial unique</b> index, and that index
    /// — not a handler reading first — is what makes « a double-click produces one entry » (AC-4.6) true: two
    /// simultaneous submissions both read « rien encore enregistré ». Filtered on non-null because every read row
    /// legitimately has none.</para>
    ///
    /// <para><b><c>PlatformAccessEntries.SubscriptionPeriodId</c></b> names the ledger entry a console write
    /// produced. Deliberately <b>no</b> foreign key, like every other column on this table: the ledger outlives the
    /// cabinet whose rows it names, and a cascade would erase the record of a payment taken for a practice that has
    /// since closed.</para>
    /// </summary>
    public partial class AddPlatformConsoleWrites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "PlatformAccessEntries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubscriptionPeriodId",
                table: "PlatformAccessEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LatestCoverKind",
                table: "ClinicSubscriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAccessEntries_IdempotencyKey",
                table: "PlatformAccessEntries",
                column: "IdempotencyKey",
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            // ── Backfill, below every DDL statement ──────────────────────────────────────────────────────────
            //
            // Every existing cabinet gets the kind its ledger already folds to, in the fold's own order
            // (RecordedAtUtc then Id) — the same order `SubscriptionLedger` applies, so the column and the fold
            // agree from the first run of `verify-schema`'s `subscription-cover-kind-matches-ledger` rather than
            // going red on every pre-existing cabinet.
            //
            // ⚠️ It reproduces the fold's « last NON-CANCELLED entry » rule and nothing else about the fold. The
            // dates are untouched: `EndsOn` is already correct on every row and re-deriving it here would be the
            // second arithmetic the whole feature exists to avoid.
            //
            // Idempotent by construction (a plain assignment from the ledger), so a half-applied re-run converges.
            migrationBuilder.Sql("""
                UPDATE "ClinicSubscriptions" cs
                SET "LatestCoverKind" = (
                    SELECT sp."Kind"
                    FROM "SubscriptionPeriods" sp
                    WHERE sp."ClinicId" = cs."ClinicId"
                      AND sp."IsCancelled" = FALSE
                    ORDER BY sp."RecordedAtUtc" DESC, sp."Id" DESC
                    LIMIT 1
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlatformAccessEntries_IdempotencyKey",
                table: "PlatformAccessEntries");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "PlatformAccessEntries");

            migrationBuilder.DropColumn(
                name: "SubscriptionPeriodId",
                table: "PlatformAccessEntries");

            migrationBuilder.DropColumn(
                name: "LatestCoverKind",
                table: "ClinicSubscriptions");
        }
    }
}
