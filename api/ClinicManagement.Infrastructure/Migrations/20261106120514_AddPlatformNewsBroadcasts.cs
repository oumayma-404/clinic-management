using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformNewsBroadcasts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformNewsBroadcasts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SentByAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    SentByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformNewsBroadcasts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformNewsItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Emoji = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    BroadcastId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformNewsItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformNewsItems_PlatformNewsBroadcasts_BroadcastId",
                        column: x => x.BroadcastId,
                        principalTable: "PlatformNewsBroadcasts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlatformNewsRecipients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcastId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AttemptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformNewsRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformNewsRecipients_PlatformNewsBroadcasts_BroadcastId",
                        column: x => x.BroadcastId,
                        principalTable: "PlatformNewsBroadcasts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformNewsBroadcasts_CreatedAtUtc",
                table: "PlatformNewsBroadcasts",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformNewsBroadcasts_RequestId",
                table: "PlatformNewsBroadcasts",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformNewsItems_BroadcastId",
                table: "PlatformNewsItems",
                column: "BroadcastId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformNewsRecipients_BroadcastId_Status",
                table: "PlatformNewsRecipients",
                columns: new[] { "BroadcastId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformNewsItems");

            migrationBuilder.DropTable(
                name: "PlatformNewsRecipients");

            migrationBuilder.DropTable(
                name: "PlatformNewsBroadcasts");
        }
    }
}
