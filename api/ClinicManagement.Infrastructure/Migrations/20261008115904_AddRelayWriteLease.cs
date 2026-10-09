using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRelayWriteLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ConfirmedAckArmed",
                table: "ClinicRelays",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "ConfirmedAckSeq",
                table: "ClinicRelays",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "LastAckSeq",
                table: "ClinicRelays",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "PendingArmedAckSeq",
                table: "ClinicRelays",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfirmedAckArmed",
                table: "ClinicRelays");

            migrationBuilder.DropColumn(
                name: "ConfirmedAckSeq",
                table: "ClinicRelays");

            migrationBuilder.DropColumn(
                name: "LastAckSeq",
                table: "ClinicRelays");

            migrationBuilder.DropColumn(
                name: "PendingArmedAckSeq",
                table: "ClinicRelays");
        }
    }
}
