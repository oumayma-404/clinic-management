using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// The cabinet entitlement and its ledger (<c>clinic-subscription</c> Part A), plus the three nullable columns
    /// Parts E and G write to. Schema and data in <b>one</b> migration, so both land under
    /// <c>MigrationLock</c>'s advisory lock together.
    ///
    /// <para><b>Purely additive.</b> Two new tables, three nullable columns, and one backfill — no existing column
    /// is altered, narrowed or dropped, so the « destructive statement before the backfill » hazard has nothing to
    /// bite on here. The backfill is nonetheless placed <b>after</b> every DDL statement, which is the order a
    /// future edit should inherit.</para>
    ///
    /// <para>⚠️ <b>The scaffolder's <c>xmin</c> columns were removed by hand, and this is not cosmetic.</b> EF maps
    /// <c>Entity&lt;T&gt;.Version</c> onto PostgreSQL's <c>xmin</c> <i>system</i> column, so the differ emits it as
    /// a real column in every <c>CreateTable</c> — and PostgreSQL refuses:
    /// <c>column name "xmin" conflicts with a system column name</c>. It is the same rejection that makes
    /// <c>AddConcurrencyToken</c>'s <c>Up()</c> deliberately empty. Every row here gets its concurrency token from
    /// the system column, with no column of its own.</para>
    ///
    /// <para>⚠️ <b><c>Amount</c> carries <c>numeric(18,3)</c> from the model-wide convention</b>, not from an
    /// annotation on the property — that is what keeps <c>verify-schema</c>'s decimal diff quiet on both sides.</para>
    ///
    /// <para><b>Grandfathering (AC-6.1, AC-6.2, AC-6.3).</b> Every cabinet that already exists gets an
    /// entitlement with <b>no end date</b> and one <c>Grandfathered</c> ledger entry stating why, so no existing
    /// practice sees a banner, a warning or a refusal as a result of this deployment. <c>EndsOn</c> is left
    /// <c>NULL</c> rather than computed, which is exactly what folding that one open-ended entry yields — so
    /// <c>verify-schema</c>'s <c>subscription-end-date-matches-ledger</c> reads 0 immediately after this runs.</para>
    ///
    /// <para>⚠️ <b>Both inserts are gated on « this cabinet has no entitlement row »</b>, which makes <c>Up()</c>
    /// re-runnable and, more importantly, makes it <b>safe</b> to re-run on a populated database: a cabinet created
    /// after this migration already has its own <c>Trial</c> entry, so gating on « no <c>Grandfathered</c> row »
    /// instead would hand a paying cabinet's trial an open-ended entry and it would never expire again.</para>
    /// </summary>
    public partial class AddClinicSubscriptions : Migration
    {
        /// <summary>
        /// <c>SubscriptionPeriodKind.Grandfathered</c>'s ordinal. Spelled out because a migration cannot reference
        /// the enum, and named here rather than inline so the backfill and its <c>verify-schema</c> counterpart
        /// cannot disagree about which kind was written.
        /// </summary>
        private const int GrandfatheredKind = 3;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SubscriptionThresholdDays",
                table: "StaffNotifications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BlockedReason",
                table: "PushDeliveries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BlockedReason",
                table: "Notifications",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClinicSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Plan = table.Column<int>(type: "integer", nullable: true),
                    EndsOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsSuspended = table.Column<bool>(type: "boolean", nullable: false),
                    SuspensionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SuspendedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SuspendedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicSubscriptions_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    DurationMonths = table.Column<int>(type: "integer", nullable: true),
                    DurationDays = table.Column<int>(type: "integer", nullable: true),
                    ExplicitEndsOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    Method = table.Column<int>(type: "integer", nullable: true),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedOnClinicDay = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsCancelled = table.Column<bool>(type: "boolean", nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionPeriods_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicSubscriptions_ClinicId",
                table: "ClinicSubscriptions",
                column: "ClinicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPeriods_ClinicId_RecordedAtUtc",
                table: "SubscriptionPeriods",
                columns: new[] { "ClinicId", "RecordedAtUtc" });

            // ---- Grandfathering (AC-6.1–6.3) — after every DDL statement, and idempotent ----------------
            //
            // The ledger entry is written FIRST so that a cabinet is never left holding an entitlement whose
            // ledger is empty: both statements share the same « no entitlement row » predicate, so inserting the
            // entry first means the predicate still selects the cabinet on the second statement.
            //
            // `RecordedOnClinicDay` is midnight of the CLINIC's day (Tunisia, UTC+1, no DST), stored so that
            // reading it back yields that date at 00:00 — the same value ClinicClock.ClinicToday() produces. It is
            // cosmetic for an open-ended entry (it anchors nothing, since such an entry advances no cursor) and is
            // computed correctly anyway, because « depuis le … » on the history screen reads it.
            migrationBuilder.Sql($"""
                INSERT INTO "SubscriptionPeriods" (
                    "Id", "ClinicId", "Kind", "DurationMonths", "DurationDays", "ExplicitEndsOn", "Amount",
                    "Method", "Reference", "Note", "RecordedAtUtc", "RecordedOnClinicDay", "RecordedBy",
                    "IsCancelled", "CancelledAtUtc", "CancelledBy", "CancelReason", "CreatedAt")
                SELECT
                    gen_random_uuid(), c."Id", {GrandfatheredKind}, NULL, NULL, NULL, NULL,
                    NULL, NULL,
                    'Cabinet déjà en service avant la mise en place des abonnements : accès conservé sans échéance.',
                    NOW(),
                    ((((NOW() AT TIME ZONE 'UTC') + INTERVAL '1 hour')::date)::timestamp) AT TIME ZONE 'UTC',
                    'job|migration:AddClinicSubscriptions',
                    false, NULL, NULL, NULL, NOW()
                FROM "Clinics" c
                WHERE NOT EXISTS (
                    SELECT 1 FROM "ClinicSubscriptions" s WHERE s."ClinicId" = c."Id");
                """);

            // EndsOn stays NULL — « sans échéance ». Deliberately not computed: NULL is exactly what folding this
            // cabinet's one open-ended entry returns, which is what makes `subscription-end-date-matches-ledger`
            // read 0 the moment this migration finishes.
            migrationBuilder.Sql("""
                INSERT INTO "ClinicSubscriptions" (
                    "Id", "ClinicId", "Plan", "EndsOn", "IsSuspended", "SuspensionReason", "SuspendedAtUtc",
                    "SuspendedBy", "CreatedAt", "UpdatedAt")
                SELECT
                    gen_random_uuid(), c."Id", NULL, NULL, false, NULL, NULL, NULL, NOW(), NULL
                FROM "Clinics" c
                WHERE NOT EXISTS (
                    SELECT 1 FROM "ClinicSubscriptions" s WHERE s."ClinicId" = c."Id");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The grandfathering data goes with the tables, which is harmless: re-running Up() regenerates it
            // from the clinic list.
            migrationBuilder.DropTable(
                name: "ClinicSubscriptions");

            migrationBuilder.DropTable(
                name: "SubscriptionPeriods");

            migrationBuilder.DropColumn(
                name: "SubscriptionThresholdDays",
                table: "StaffNotifications");

            migrationBuilder.DropColumn(
                name: "BlockedReason",
                table: "PushDeliveries");

            migrationBuilder.DropColumn(
                name: "BlockedReason",
                table: "Notifications");
        }
    }
}
