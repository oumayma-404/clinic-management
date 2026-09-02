using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// The clinic logo's stored content type — the follow-up `patient-file-uploads` dropped from its own scope
    /// because the working tree then carried an uncommitted model snapshot, which is how two migrations come to
    /// duplicate each other's operations. The tree is clean now, so it lands here.
    ///
    /// <para>⚠️ <b>Purely additive: one nullable column, no backfill, no default.</b> Nullable rather than
    /// defaulted to <c>image/png</c> deliberately — a logo uploaded before this column existed has an *unknown*
    /// type, which is a different fact from a PNG, and <see cref="Clinic.LogoContentType"/>'s reader falls back to
    /// PNG explicitly and says so. A default would assert the same guess in the database, where nothing could
    /// later tell it from a real answer.</para>
    ///
    /// <para>Scaffolded clean: no <c>AddColumn&lt;uint&gt;("xmin")</c> (an <c>AddColumn</c> creates no table) and
    /// nothing dropped, so the destructive-before-backfill hazard has nothing to bite on.</para>
    /// </summary>
    public partial class AddClinicLogoContentType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LogoContentType",
                table: "Clinics",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LogoContentType",
                table: "Clinics");
        }
    }
}
