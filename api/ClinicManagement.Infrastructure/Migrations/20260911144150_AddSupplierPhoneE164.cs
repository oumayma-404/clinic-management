using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// A supplier's dialable number, stored for the same reason the patient's is (<c>PhoneNumber.E164</c>): the
    /// country is known only while the form is open. EC-1 is untouched — an unreadable number is still stored and
    /// still merely loses the WhatsApp action; what this repairs is the form's promise that choosing the country
    /// changes that. ⚠️ Not backfilled; `Supplier.PhoneE164` re-derives for existing rows.
    /// </summary>
    public partial class AddSupplierPhoneE164 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhoneNumberE164",
                table: "Suppliers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PhoneNumberE164",
                table: "Suppliers");
        }
    }
}
