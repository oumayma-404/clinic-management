using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// The <c>(clinic, month, threshold)</c> dedupe key for the WhatsApp-forfait warnings
    /// (<c>vendor-whatsapp-messaging-quota</c> Part 2, FR-6): two nullable columns on <c>StaffNotifications</c>.
    ///
    /// <para><b>Purely additive.</b> Nothing is altered, narrowed or dropped and there is no backfill, so the
    /// destructive-before-backfill hazard has nothing to bite on here.</para>
    ///
    /// <para>⚠️ <b>Both columns are nullable with no default, and that is the decision rather than the absence of
    /// one.</b> Null means « this row is not a forfait warning », which is true of every row that already exists and of
    /// every row the other ten categories will ever write. A scaffolded <c>defaultValue: 0</c> on the percentage would
    /// have made every appointment reminder in the table look like a 0 % forfait warning to
    /// <c>GetMessagingWarningAsync</c> — the class of bug the backup-schedule zeros cost this repo once already.</para>
    ///
    /// <para>⚠️ <b>The plan's Migration 1 bundled these two columns with Part 1's two tables and Part 4's four
    /// template columns.</b> Part 1 shipped the tables alone, so the batch is split by part — which the plan's own
    /// « before and after the migration <i>batch</i> » wording already allows for. Part 5 runs
    /// <c>verify-schema</c> across the whole batch and diffs it.</para>
    /// </summary>
    /// <inheritdoc />
    public partial class AddMessagingAllowanceWarningColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MessagingAllowanceMonth",
                table: "StaffNotifications",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MessagingThresholdPercent",
                table: "StaffNotifications",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MessagingAllowanceMonth",
                table: "StaffNotifications");

            migrationBuilder.DropColumn(
                name: "MessagingThresholdPercent",
                table: "StaffNotifications");
        }
    }
}
