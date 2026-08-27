using System;
using ClinicManagement.Infrastructure.Services;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// The vendor-purchased WhatsApp reminder forfait (<c>vendor-whatsapp-messaging-quota</c> Part 1): the append-only
    /// allocation ledger, one counting row per cabinet per Tunisian month, and the FR-3 rollout backfill. Schema and
    /// data in <b>one</b> migration, so both land under <c>MigrationLock</c>'s advisory lock together.
    ///
    /// <para><b>Purely additive.</b> Two new tables and one backfill — no existing column is altered, narrowed or
    /// dropped, so the « destructive statement before the backfill » hazard has nothing to bite on here. The backfill is
    /// nonetheless placed <b>after every DDL statement</b>, which is the order a future edit should inherit.</para>
    ///
    /// <para>⚠️ <b>The scaffolder's <c>xmin</c> columns were removed by hand, and this is not cosmetic.</b> EF maps
    /// <c>Entity&lt;T&gt;.Version</c> onto PostgreSQL's <c>xmin</c> <i>system</i> column, so the differ emits it as a
    /// real column in every <c>CreateTable</c> — and PostgreSQL refuses:
    /// <c>column name "xmin" conflicts with a system column name</c>. It is the same rejection that makes
    /// <c>AddConcurrencyToken</c>'s <c>Up()</c> deliberately empty, and the same edit
    /// <c>AddClinicSubscriptions</c> needed. Every row here gets its concurrency token from the system column, with no
    /// column of its own.</para>
    ///
    /// <para>⚠️ <b><c>Amount</c> carries <c>numeric(18,3)</c> from the model-wide convention</b>, not from an annotation
    /// on the property — that is what keeps <c>verify-schema</c>'s decimal diff quiet on both sides.</para>
    ///
    /// <para><b>The rollout (FR-3).</b> Every cabinet that already exists gets one <c>Standing</c> entry at the
    /// provisional default and the current Tunisian month's counting row, so no practice is left holding
    /// <c>MessagingAllowanceMissing</c> — which reads to them as our own bookkeeping fault and to us as a support
    /// call.</para>
    ///
    /// <para>⚠️ <b>The entry insert is gated on « this cabinet has no STANDING entry »</b> (R-13) — not on « no rollout
    /// entry ». Gating on the narrower predicate is the mistake <c>AddClinicSubscriptions</c> explicitly avoided: a
    /// cabinet provisioned <i>after</i> this migration already has its own standing entry, and a second one written on
    /// top would silently reset a figure the vendor had deliberately changed.</para>
    ///
    /// <para>⚠️ <b>The counting row's figure comes from the cabinet's own ledger, never from the constant.</b> For a
    /// cabinet the statement above just wrote an entry for, the two are the same number — but for one that already had
    /// a standing entry they are not, and writing the default would make
    /// <c>monthly-allowance-matches-ledger</c> red for exactly those cabinets. The <c>LATERAL</c> subquery below
    /// reproduces <c>MessagingAllowanceLedger.StandingInForce</c>'s ordering, and its <c>LIMIT 1</c> also drops a
    /// cabinet with no standing entry at all — which is correct: no ledger means no row, so AC-4.3's « forfait
    /// introuvable » stays reachable rather than being papered over with a zero.</para>
    /// </summary>
    public partial class AddClinicMessagingAllowances : Migration
    {
        /// <summary>
        /// <c>MessagingAllowanceKind.Standing</c>'s ordinal. Spelled out because the value is what lands in the
        /// column and the SQL below is matched on it in three places; named once so they cannot disagree.
        /// </summary>
        private const int StandingKind = 1;

        /// <summary>
        /// The Tunisian month, as SQL. Tunisia is UTC+1 all year, so this is <c>ClinicClock.CurrentMonthKey()</c>
        /// expressed in the database — and it must be, because a UTC month files the rollout of a migration run at
        /// 00:30 on the 1st into the month that has just closed.
        /// </summary>
        private const string RolloutMonthSql =
            "to_char(((NOW() AT TIME ZONE 'UTC') + INTERVAL '1 hour'), 'YYYY-MM')";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClinicMessagingMonths",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    MonthKey = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    AllowanceMessages = table.Column<int>(type: "integer", nullable: false),
                    ConsumedMessages = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicMessagingMonths", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicMessagingMonths_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MessagingAllowanceEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Messages = table.Column<int>(type: "integer", nullable: false),
                    EffectiveMonth = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    Method = table.Column<int>(type: "integer", nullable: true),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsCancelled = table.Column<bool>(type: "boolean", nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessagingAllowanceEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessagingAllowanceEntries_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicMessagingMonths_ClinicId_MonthKey",
                table: "ClinicMessagingMonths",
                columns: new[] { "ClinicId", "MonthKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MessagingAllowanceEntries_ClinicId_EffectiveMonth",
                table: "MessagingAllowanceEntries",
                columns: new[] { "ClinicId", "EffectiveMonth" });

            migrationBuilder.CreateIndex(
                name: "IX_MessagingAllowanceEntries_ClinicId_RecordedAtUtc",
                table: "MessagingAllowanceEntries",
                columns: new[] { "ClinicId", "RecordedAtUtc" });

            // ---- The FR-3 rollout — after every DDL statement, and idempotent ---------------------------------
            //
            // The ledger entry is written FIRST so that a cabinet is never left holding a counting row with no ledger
            // behind it, which is precisely the drift `monthly-allowance-matches-ledger` reports.
            //
            // ⚠️ The figure is `MessagingAllowancePolicy.DefaultMonthlyAllowance`, referenced rather than retyped: the
            // constant is public for exactly this reason, so the number a rolled-out cabinet gets and the number a
            // newly-provisioned one gets cannot drift. R-12 records it as a PROVISIONAL commercial decision — nothing
            // in the code depends on its value, and the vendor adjusts any cabinet with `messaging-grant`.
            migrationBuilder.Sql($"""
                INSERT INTO "MessagingAllowanceEntries" (
                    "Id", "ClinicId", "Kind", "Messages", "EffectiveMonth", "Amount", "Method", "Reference",
                    "Note", "RecordedAtUtc", "RecordedBy", "IsCancelled", "CancelledAtUtc", "CancelledBy",
                    "CancelReason", "CreatedAt")
                SELECT
                    gen_random_uuid(), c."Id", {StandingKind},
                    {MessagingAllowancePolicy.DefaultMonthlyAllowance},
                    {RolloutMonthSql},
                    NULL, NULL, NULL,
                    'Forfait de rappels WhatsApp attribué lors de la mise en place des forfaits.',
                    NOW(),
                    'job|migration:AddClinicMessagingAllowances',
                    false, NULL, NULL, NULL, NOW()
                FROM "Clinics" c
                WHERE NOT EXISTS (
                    SELECT 1 FROM "MessagingAllowanceEntries" e
                    WHERE e."ClinicId" = c."Id" AND e."Kind" = {StandingKind});
                """);

            // The counting row, with its figure read back out of the ledger — see the ⚠️ on the class. `ConsumedMessages`
            // starts at 0, which is a true statement: nothing has been sent under a forfait that did not exist.
            migrationBuilder.Sql($"""
                INSERT INTO "ClinicMessagingMonths" (
                    "Id", "ClinicId", "MonthKey", "AllowanceMessages", "ConsumedMessages", "CreatedAt", "UpdatedAt")
                SELECT
                    gen_random_uuid(), c."Id", {RolloutMonthSql}, f."Messages", 0, NOW(), NULL
                FROM "Clinics" c
                CROSS JOIN LATERAL (
                    SELECT e."Messages"
                    FROM "MessagingAllowanceEntries" e
                    WHERE e."ClinicId" = c."Id"
                      AND e."Kind" = {StandingKind}
                      AND NOT e."IsCancelled"
                      AND e."EffectiveMonth" <= {RolloutMonthSql}
                    ORDER BY e."EffectiveMonth" DESC, e."RecordedAtUtc" DESC, e."Id" DESC
                    LIMIT 1
                ) f
                WHERE NOT EXISTS (
                    SELECT 1 FROM "ClinicMessagingMonths" m
                    WHERE m."ClinicId" = c."Id" AND m."MonthKey" = {RolloutMonthSql});
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The rollout data goes with the tables, which is harmless: re-running Up() regenerates it from the
            // clinic list. Nothing outside these two tables was touched, so there is nothing else to reverse.
            migrationBuilder.DropTable(
                name: "ClinicMessagingMonths");

            migrationBuilder.DropTable(
                name: "MessagingAllowanceEntries");
        }
    }
}
