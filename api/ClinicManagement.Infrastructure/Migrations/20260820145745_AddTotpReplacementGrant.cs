using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// One nullable column: until when an account may replace its own second factor without a code from it —
    /// the short window a redeemed recovery code earns, so a cabinet with a single administrator can re-secure
    /// itself after losing the phone.
    ///
    /// <para>Purely additive: nothing is altered, narrowed or dropped, so there is no backfill and nothing for
    /// the destructive-before-backfill hazard to bite on.</para>
    ///
    /// <para>⚠️ <b>Deliberately no <c>defaultValue</c>.</b> NULL is the correct reading for every existing row —
    /// « no window is open » — and a scaffolded default of any instant would either be in the past (harmless but
    /// meaningless) or hand every account on the deployment a standing right to replace its factor. Nullable with
    /// no default is the only version of this column that says what it means.</para>
    ///
    /// <para>⚠️ EF emitted no <c>xmin</c> here because nothing is created — the trap in the two
    /// <c>CreateTable</c> migrations before it does not apply to an <c>AddColumn</c>.</para>
    /// </summary>
    public partial class AddTotpReplacementGrant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TotpReplacementAllowedUntil",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotpReplacementAllowedUntil",
                table: "Users");
        }
    }
}
