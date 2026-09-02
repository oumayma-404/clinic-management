using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// The price agreed for one act at one visit, so a price haggled on the telephone survives to the fiche de
    /// soins without anyone opening a devis.
    ///
    /// <para><b>Nullable with no default and no backfill</b>, deliberately: null means « nothing was negotiated,
    /// the catalogue tarif stands », which is precisely what every existing row means. Writing 0 — or today's
    /// <c>DefaultCost</c> — into them would turn every visit ever booked into a negotiation, freeze a snapshot of
    /// the current catalogue onto the past, and make a later tarif change invisible.</para>
    ///
    /// <para><c>numeric(18,3)</c> comes from <c>ConfigureConventions</c>' model-wide money precision, not from an
    /// annotation here — which is also why <c>verify-schema</c>'s « every decimal at (18,3) » term covers this
    /// column without being taught about it.</para>
    /// </summary>
    public partial class AddAppointmentProcedureAgreedCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AgreedCost",
                table: "AppointmentProcedures",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgreedCost",
                table: "AppointmentProcedures");
        }
    }
}
