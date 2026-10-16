using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWebPushSubscriptionKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WebPushAuth",
                table: "DeviceRegistrations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebPushP256dh",
                table: "DeviceRegistrations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WebPushAuth",
                table: "DeviceRegistrations");

            migrationBuilder.DropColumn(
                name: "WebPushP256dh",
                table: "DeviceRegistrations");
        }
    }
}
