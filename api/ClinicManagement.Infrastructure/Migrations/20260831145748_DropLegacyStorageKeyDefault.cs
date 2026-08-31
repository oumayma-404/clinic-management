using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Removes the legacy <c>DEFAULT ''</c> from <c>PatientFiles.StorageKey</c>, so that half of
    /// <c>CK_PatientFiles_ResidencyForm</c> can actually bite (<c>clinic-file-vault</c>).
    ///
    /// <para>⚠️ <b>Hand-written, and the scaffold was empty on purpose.</b> The default exists only in the
    /// database — it was added when the column was still <c>IsRequired()</c> — and the EF model never declared
    /// one, so the differ has nothing to compare and emits nothing. Adding a <c>HasDefaultValue</c> to the
    /// configuration just to delete it again would leave the model asserting a default the product does not want.
    /// This is the same shape as the three migrations in this folder whose <c>Up()</c> is deliberately empty: the
    /// model snapshot and the SQL answer to different things.</para>
    ///
    /// <para>⚠️ <b>What it fixes, measured.</b> The check reads
    /// <c>(Residency = 1 AND StorageKey IS NOT NULL) OR (Residency = 2 AND StorageKey IS NULL)</c>. With the
    /// default in place an insert that <i>omitted</i> the column stored <c>''</c> rather than NULL, so
    /// <c>'' IS NOT NULL</c> passed and a hosted row with no key was accepted — the constraint refused the
    /// vault-shaped violation and waved the hosted-shaped one through. Nothing in the application could produce
    /// it (EF always writes the column: a real key from the constructor, an explicit <c>null</c> from
    /// <c>RegisterInVault</c>), so this closes the door on hand-written SQL and on whatever writes this table
    /// next.</para>
    /// </summary>
    public partial class DropLegacyStorageKeyDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""PatientFiles"" ALTER COLUMN ""StorageKey"" DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""PatientFiles"" ALTER COLUMN ""StorageKey"" SET DEFAULT '';");
        }
    }
}
