using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// One new table for the pending clinic self-signups (<c>clinic-self-signup</c>). Purely additive: no
    /// existing column is touched, no row is rewritten, and nothing else in the schema refers to it.
    ///
    /// <para>⚠️ <b>No foreign key and no <c>ClinicId</c></b>, which is the table's whole point — a signup exists
    /// because its clinic does not yet. That also means nothing cascades these rows away; they are trimmed by the
    /// opportunistic purge on the signup path, and <c>verify-schema</c>'s <c>clinic-signup-has-no-orphans</c>
    /// counter is what makes a deployment that stopped trimming visible.</para>
    ///
    /// <para><b>Hand-written</b>, like <c>AddChequeDetailsToPayments</c> and <c>AddProcedureTypeCategory</c>
    /// before it: <c>dotnet ef</c> cannot scaffold on this machine (the running API holds
    /// <c>ClinicManagement.API/bin</c>, and Smart App Control refuses freshly-built design-time assemblies). One
    /// table with three indexes and no relationships is small enough to verify by eye, and the shape is checked
    /// against PostgreSQL's own catalog by <c>verify-schema</c>, which matches indexes on table + ordered columns
    /// rather than by name.</para>
    /// </summary>
    public partial class AddClinicSignups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClinicSignups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DoctorInfoJson = table.Column<string>(type: "text", nullable: true),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EmailSendAttempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicSignups", x => x.Id);
                });

            // UNIQUE: « one pending row per address » is an invariant the database holds, not a race the handler
            // hopes to win. Two simultaneous signups for one address would otherwise leave two live tokens.
            migrationBuilder.CreateIndex(
                name: "IX_ClinicSignups_Email",
                table: "ClinicSignups",
                column: "Email",
                unique: true);

            // The verification lookup's only index, and unique for the same reason: two rows sharing one hash
            // would make « which clinic does this link create? » ambiguous.
            migrationBuilder.CreateIndex(
                name: "IX_ClinicSignups_TokenHash",
                table: "ClinicSignups",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicSignups_ConsumedAtUtc_ExpiresAtUtc",
                table: "ClinicSignups",
                columns: new[] { "ConsumedAtUtc", "ExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicSignups");
        }
    }
}
