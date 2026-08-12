using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSecondFactorAndSessionFamilies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProtectedTotpSecret",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TotpEnrolledAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SessionFamilies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CurrentCredentialHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PreviousCredentialHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DeviceLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastRotatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                    // ⚠️ EF scaffolded `xmin = table.Column<uint>(type: "xid", …)` here and it was removed by
                    // hand. `Entity<T>.Version` is mapped onto PostgreSQL's *system* column, so the differ
                    // writes it out as a real one and the migration fails with
                    // `column name "xmin" conflicts with a system column name`. Every row still gets its token
                    // from the system column. Same rejection that makes AddConcurrencyToken's Up() empty.
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionFamilies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionFamilies_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRecoveryCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                    // ⚠️ The same scaffolded `xmin` column, removed by hand — see the note in the CreateTable above.
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRecoveryCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRecoveryCodes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionFamilies_CurrentCredentialHash",
                table: "SessionFamilies",
                column: "CurrentCredentialHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionFamilies_PreviousCredentialHash",
                table: "SessionFamilies",
                column: "PreviousCredentialHash");

            migrationBuilder.CreateIndex(
                name: "IX_SessionFamilies_UserId_ExpiresAtUtc",
                table: "SessionFamilies",
                columns: new[] { "UserId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserRecoveryCodes_UserId_IsUsed",
                table: "UserRecoveryCodes",
                columns: new[] { "UserId", "IsUsed" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SessionFamilies");

            migrationBuilder.DropTable(
                name: "UserRecoveryCodes");

            migrationBuilder.DropColumn(
                name: "ProtectedTotpSecret",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TotpEnrolledAt",
                table: "Users");
        }
    }
}
