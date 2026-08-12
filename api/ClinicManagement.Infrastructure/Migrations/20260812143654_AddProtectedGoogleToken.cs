using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// FR-3.4 — one nullable column beside the plaintext one, and <b>no backfill here on purpose</b>.
    ///
    /// <para>Encrypting needs the Data Protection key ring, which a migration cannot reach; raw SQL could only
    /// copy the cleartext across, which encrypts nothing while making every layer report success. The values are
    /// moved by <c>GoogleTokenProtectionBackfill</c> on the startup pass instead, and each converted row has its
    /// plaintext cleared — which is what lets <c>verify-schema</c>'s <c>google-token-protected</c> count what is
    /// left.</para>
    ///
    /// <para>⚠️ <b>The old column is deliberately NOT dropped.</b> Dropping it in the same change would destroy
    /// every clinic's calendar connection on any deployment where the backfill had not yet run — the
    /// destructive-before-backfill hazard, in its purest form. It goes in a later migration, once that check
    /// reads zero on the live deployment. <b>Rollback</b> is therefore free: the plaintext column is still
    /// present, so reverting is dropping the new one.</para>
    /// </summary>
    public partial class AddProtectedGoogleToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GoogleRefreshTokenProtected",
                table: "Clinics",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoogleRefreshTokenProtected",
                table: "Clinics");
        }
    }
}
