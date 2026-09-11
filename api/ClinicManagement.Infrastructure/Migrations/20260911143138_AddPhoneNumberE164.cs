using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// The dialable normalisation of a patient's two phone numbers, stored because it cannot be re-derived: a
    /// national number needs its country, and the country is known only at the moment of writing (the form's
    /// country selector). See <c>PhoneNumber.E164</c>.
    ///
    /// <para>⚠️ <b>Nothing is backfilled, deliberately.</b> Normalising the existing rows needs libphonenumber's
    /// metadata, which SQL does not have — and a guess would be worse than a null here, since every legacy row
    /// was *entered* under the Tunisian-only rule and already re-derives correctly against the default region.
    /// `PhoneNumber.E164` falls back for exactly that reason, so no stored number changes meaning.</para>
    ///
    /// <para>⚠️ Both columns are `character varying(20)`, matching the raw columns beside them. E.164 caps at 15
    /// digits plus a `+`, so 20 is room to spare.</para>
    /// </summary>
    public partial class AddPhoneNumberE164 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmergencyContactPhoneE164",
                table: "Patients",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumberE164",
                table: "Patients",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmergencyContactPhoneE164",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "PhoneNumberE164",
                table: "Patients");
        }
    }
}
