using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// The vendor console stops knowing what a cabinet earns: « Encaissé par le cabinet » is withdrawn from the
    /// screen, the wire, the counter pass and now the table. The stored figures go with it — keeping the column
    /// would leave a month-old turnover per practice in a table nothing reads, which is the data we decided not
    /// to hold. The vendor's OWN revenue lives in <c>SubscriptionPeriods</c> and is untouched.
    /// </summary>
    public partial class DropClinicActivityCollectedThisMonth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CollectedThisMonth",
                table: "ClinicActivitySnapshots");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CollectedThisMonth",
                table: "ClinicActivitySnapshots",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);
        }
    }
}
