using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// One nullable <c>text</c> column on <c>ClinicSignups</c>, so a public signup can carry the onboarding
    /// wizard's « Horaires » step across verification.
    ///
    /// <para><b>Why the pending row has to hold it.</b> Signup is now the wizard itself — the visitor answers all
    /// three steps in one sitting and the emailed link only confirms — so anything the wizard collects and this
    /// row does not persist is silently discarded between the form and the clinic. Every other wizard field was
    /// already stored (<c>Phone</c>, <c>Address</c>, <c>City</c>, <c>DoctorInfoJson</c>) and
    /// <c>LocalClinicRequest</c> has always accepted <c>WorkingHoursJson</c>; this column is the only missing
    /// link between the two.</para>
    ///
    /// <para>Purely additive and nullable: every existing pending row keeps verifying, and a clinic that skipped
    /// the step gets no working-hours restriction — which is exactly what <c>WorkingHoursResolver</c> already
    /// treats « none » as. No backfill, so nothing for <c>verify-schema</c> to count; the column itself is diffed
    /// against PostgreSQL's catalog for free.</para>
    ///
    /// <para><b>Hand-written</b> for the same reason as <c>AddClinicSignups</c>, <c>AddChequeDetailsToPayments</c>
    /// and <c>AddProcedureTypeCategory</c>: <c>dotnet ef</c> cannot scaffold here while the API is running (it
    /// holds <c>ClinicManagement.API/bin</c>) and Smart App Control refuses freshly-built design-time assemblies.
    /// One nullable column is small enough to verify by eye.</para>
    /// </summary>
    public partial class AddClinicSignupWorkingHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WorkingHoursJson",
                table: "ClinicSignups",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WorkingHoursJson",
                table: "ClinicSignups");
        }
    }
}
