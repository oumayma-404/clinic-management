using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicArchiveGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClinicArchiveGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SecretHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                    // ⚠️ EF's differ emitted an `xmin` column here and it was removed by hand, the same fix
                    // AddClinicSubscriptions and AddSuppliers needed: Entity<T>.Version maps onto PostgreSQL's
                    // SYSTEM column, so creating a real one fails with « column name "xmin" conflicts with a
                    // system column name ». Every row still gets its token from the system column.
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicArchiveGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicArchiveGrants_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicArchiveGrants_ClinicId_CreatedAtUtc",
                table: "ClinicArchiveGrants",
                columns: new[] { "ClinicId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicArchiveGrants_SecretHash",
                table: "ClinicArchiveGrants",
                column: "SecretHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicArchiveGrants");
        }
    }
}
