using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Three nullable columns on the console's access ledger, so a second-factor reset can be recorded: the clinic
    /// account it was performed on, that account's address at the time, and the motif.
    ///
    /// <para>⚠️ <b>The motif is on this row because it has nowhere else to be.</b> A suspension writes its reason
    /// onto the entitlement and a cancellation onto the entry it strikes through, so the ledger stays a list of
    /// « who did what to which cabinet » for those. <c>User.DisableTotp</c> keeps no trace at all, so without these
    /// columns « qui a désarmé le compte de qui, et pourquoi ? » would have no answer anywhere in the product.</para>
    ///
    /// <para>Purely additive — nothing altered, narrowed or dropped, so no backfill and nothing for the
    /// destructive-before-backfill hazard to bite on. No <c>defaultValue</c> on any of the three: NULL is the
    /// correct reading for every existing row and for every future row of the other eight actions, which act on a
    /// cabinet rather than on a person. No index either — the journal is read by cabinet and by console account,
    /// both of which already have one, and « every reset ever performed » is a report nobody has asked for.</para>
    ///
    /// <para>⚠️ EF emitted no <c>xmin</c>: the trap in the <c>CreateTable</c> migrations does not apply to an
    /// <c>AddColumn</c>.</para>
    /// </summary>
    public partial class AddPlatformAccessTargetAndReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "PlatformAccessEntries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetEmail",
                table: "PlatformAccessEntries",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetUserId",
                table: "PlatformAccessEntries",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Reason",
                table: "PlatformAccessEntries");

            migrationBuilder.DropColumn(
                name: "TargetEmail",
                table: "PlatformAccessEntries");

            migrationBuilder.DropColumn(
                name: "TargetUserId",
                table: "PlatformAccessEntries");
        }
    }
}
