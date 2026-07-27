using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInstallmentPaymentLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InstallmentPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstallmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    PaidOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsVoided = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    VoidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VoidReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    VoidedByUserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    VoidedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstallmentPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstallmentPayments_Installments_InstallmentId",
                        column: x => x.InstallmentId,
                        principalTable: "Installments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Installments_DueDate",
                table: "Installments",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_InstallmentPayments_InstallmentId",
                table: "InstallmentPayments",
                column: "InstallmentId");

            migrationBuilder.CreateIndex(
                name: "IX_InstallmentPayments_PaidOn",
                table: "InstallmentPayments",
                column: "PaidOn",
                filter: "NOT \"IsVoided\"");

            // ---- Backfill: one ledger row per already-collected installment -------------------------------
            //
            // Deliberately reproduces today's figures EXACTLY (spec AC-24): the cumulative AmountPaid, dated
            // LastPaidOn — precisely what every cash read attributes today. It does NOT retro-fix the
            // wrong-month attribution, because the information to do so was never stored: an échéance paid
            // twice kept only the last date. The ledger fixes attribution from its first day FORWARD.
            //
            // WHERE NOT EXISTS makes it idempotent. That is not optional: in Local mode migrations run
            // fire-and-forget AFTER Kestrel is serving, and a throw there calls StopApplication() — a
            // non-idempotent backfill that re-ran would take the whole app down.
            //
            // COALESCE on the date: AmountPaid > 0 with a NULL LastPaidOn is unreachable through the domain
            // but possible in the data, and PaidOn is NOT NULL — a bare insert would abort the entire
            // migration. Falling back to the plan's acceptance (then creation) date keeps the money visible;
            // `reconcile-money` reports how many rows took that path, because it silently assigns them a month.
            migrationBuilder.Sql("""
                INSERT INTO "InstallmentPayments"
                    ("Id", "InstallmentId", "Amount", "Method", "PaidOn", "CreatedAt", "IsVoided")
                SELECT
                    gen_random_uuid(),
                    i."Id",
                    i."AmountPaid",
                    COALESCE(i."LastMethod", 0),
                    COALESCE(i."LastPaidOn", tp."AcceptedDate", tp."CreatedAt"),
                    now(),
                    false
                FROM "Installments" i
                JOIN "TreatmentPlans" tp ON tp."Id" = i."TreatmentPlanId"
                WHERE i."AmountPaid" > 0
                  AND NOT EXISTS (
                      SELECT 1 FROM "InstallmentPayments" ip WHERE ip."InstallmentId" = i."Id"
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InstallmentPayments");

            migrationBuilder.DropIndex(
                name: "IX_Installments_DueDate",
                table: "Installments");
        }
    }
}
