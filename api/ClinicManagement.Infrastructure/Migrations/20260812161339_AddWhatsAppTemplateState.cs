using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Where Meta's review of a cabinet's reminder template stands (<c>vendor-whatsapp-messaging-quota</c> Part 4,
    /// FR-7a/FR-7b): four nullable columns on <c>ClinicReminderSettings</c> plus the index the webhook resolves a
    /// WhatsApp Business Account by.
    ///
    /// <para><b>Purely additive.</b> Nothing is altered, narrowed or dropped and there is no backfill, so the
    /// destructive-before-backfill hazard has nothing to bite on.</para>
    ///
    /// <para>⚠️ <b>All four are nullable with no default, and that is the decision rather than the absence of
    /// one.</b> Null means « we do not track a template for this cabinet », which is true of every row that exists
    /// today — including the cabinets sending perfectly well on the install's own pre-approved template. A scaffolded
    /// <c>defaultValue: 0</c> on the status would be <c>NotSubmitted</c>, which <c>OutboxMessagingGate</c>'s § 33a
    /// term reads as « not usable » — so every WhatsApp reminder on the deployment would be held the moment this
    /// applied. Same class of bug as the backup-schedule zeros, and here it is silent rather than merely wrong.</para>
    ///
    /// <para>⚠️ The index is <b>filtered on <c>IS NOT NULL</c></b>: it serves exactly one equality read (the
    /// webhook's), and most rows never connect through Embedded Signup at all.</para>
    ///
    /// <para>⚠️ Third of the batch the plan's Migration 1 bundled into one, continuing the split by part (Part 1's
    /// two tables, Part 2's warning columns, Part 3's access-ledger link). Part 5 runs <c>verify-schema</c> across
    /// the whole batch and diffs it.</para>
    /// </summary>
    /// <inheritdoc />
    public partial class AddWhatsAppTemplateState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WhatsAppTemplateCategory",
                table: "ClinicReminderSettings",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppTemplateId",
                table: "ClinicReminderSettings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WhatsAppTemplateStatus",
                table: "ClinicReminderSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WhatsAppTemplateStatusCheckedAtUtc",
                table: "ClinicReminderSettings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicReminderSettings_WhatsAppBusinessAccountId",
                table: "ClinicReminderSettings",
                column: "WhatsAppBusinessAccountId",
                filter: "\"WhatsAppBusinessAccountId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClinicReminderSettings_WhatsAppBusinessAccountId",
                table: "ClinicReminderSettings");

            migrationBuilder.DropColumn(
                name: "WhatsAppTemplateCategory",
                table: "ClinicReminderSettings");

            migrationBuilder.DropColumn(
                name: "WhatsAppTemplateId",
                table: "ClinicReminderSettings");

            migrationBuilder.DropColumn(
                name: "WhatsAppTemplateStatus",
                table: "ClinicReminderSettings");

            migrationBuilder.DropColumn(
                name: "WhatsAppTemplateStatusCheckedAtUtc",
                table: "ClinicReminderSettings");
        }
    }
}
