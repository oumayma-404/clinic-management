using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRelayHandback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "HandbackAppliedAtUtc",
                table: "ClinicRelays",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "HandbackAppliedId",
                table: "ClinicRelays",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnStuckSinceUtc",
                table: "ClinicRelays",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnedAtUtc",
                table: "ClinicRelays",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnedCutSinceUtc",
                table: "ClinicRelays",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RelayReviewItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelayId = table.Column<Guid>(type: "uuid", nullable: false),
                    CutSinceUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Table = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EntityKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CloudVersion = table.Column<string>(type: "text", nullable: true),
                    CabinetVersion = table.Column<string>(type: "text", nullable: true),
                    CloudEntityKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CloudChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CloudChangedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedByUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelayReviewItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RelayReviewItems_ClinicRelays_RelayId",
                        column: x => x.RelayId,
                        principalTable: "ClinicRelays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RelayReviewItems_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RelayReviewItems_ClinicId_ReviewedAtUtc",
                table: "RelayReviewItems",
                columns: new[] { "ClinicId", "ReviewedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RelayReviewItems_RelayId_CutSinceUtc",
                table: "RelayReviewItems",
                columns: new[] { "RelayId", "CutSinceUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RelayReviewItems");

            migrationBuilder.DropColumn(
                name: "HandbackAppliedAtUtc",
                table: "ClinicRelays");

            migrationBuilder.DropColumn(
                name: "HandbackAppliedId",
                table: "ClinicRelays");

            migrationBuilder.DropColumn(
                name: "ReturnStuckSinceUtc",
                table: "ClinicRelays");

            migrationBuilder.DropColumn(
                name: "ReturnedAtUtc",
                table: "ClinicRelays");

            migrationBuilder.DropColumn(
                name: "ReturnedCutSinceUtc",
                table: "ClinicRelays");
        }
    }
}
