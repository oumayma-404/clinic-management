using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// One nullable column on the console's access ledger: which <c>MessagingAllowanceEntry</c> a vendor write produced
    /// or acted on (<c>vendor-whatsapp-messaging-quota</c> Part 3, AC-6.8).
    ///
    /// <para><b>Purely additive</b> — nothing altered, narrowed or dropped, and no backfill, so the
    /// destructive-before-backfill hazard has nothing to bite on. Every existing row is legitimately null: none of them
    /// recorded a forfait de rappels, because until now none could.</para>
    ///
    /// <para>⚠️ <b>Its own column rather than a reuse of <c>SubscriptionPeriodId</c>, and that is the decision this
    /// migration exists for.</b> Both name « the thing the vendor was paid for » and sharing one would have been zero
    /// migrations — which is exactly why it is refused: the journal would then assert that a forfait de rappels extended
    /// the cabinet's right to record work, and a replay keyed on <c>IdempotencyKey</c> would hand the console back the
    /// wrong kind of id. It is the argument <c>PlatformReadShape</c> already makes about not overloading
    /// <c>Note</c>/<c>Reference</c>: a semantic overload is not a free pass.</para>
    ///
    /// <para>⚠️ <b>No index and no foreign key.</b> Nothing looks a row up by the entry it names — both id columns are
    /// read only as part of a row already found by its idempotency key or by the page's own ordering — and the whole type
    /// deliberately holds no FK, so that a ledger row outlives the cabinet it names.</para>
    ///
    /// <para>⚠️ <b>Hand-written, not scaffolded</b>, for the reason four migrations before it were: <c>dotnet ef</c>
    /// cannot load a freshly-built assembly on the dev machine (Smart App Control, <c>0x800711C7</c>). The delta is one
    /// nullable <c>uuid</c>, small enough to verify by eye, and its shape is checked against PostgreSQL's own catalog by
    /// <c>verify-schema</c> — which diffs columns by table and name, never by which tool wrote them.</para>
    /// </summary>
    /// <inheritdoc />
    public partial class AddMessagingAllowanceAccessLedgerLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MessagingAllowanceEntryId",
                table: "PlatformAccessEntries",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MessagingAllowanceEntryId",
                table: "PlatformAccessEntries");
        }
    }
}
