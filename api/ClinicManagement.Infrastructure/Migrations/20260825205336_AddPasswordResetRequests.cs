using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// One new table for the self-service password-reset requests. Purely additive: no existing column is touched,
    /// no row is rewritten, and nothing else in the schema refers to it.
    ///
    /// <para>⚠️ <b>The scaffold emitted an <c>xmin</c> column inside <c>CreateTable</c> and it has been removed by
    /// hand.</b> <c>Entity&lt;TId&gt;.Version</c> is mapped onto PostgreSQL's <c>xmin</c> <i>system</i> column, so
    /// the differ believes every entity owes a real one — and <c>CREATE TABLE … (xmin xid)</c> is refused outright
    /// (« column name "xmin" conflicts with a system column name »), which would make this migration unappliable
    /// on an empty database. <c>AddClinicSignups</c> has the same shape and the same omission. The model snapshot
    /// keeps the mapping, which is correct: it describes a column PostgreSQL provides rather than one we create.</para>
    ///
    /// <para>⚠️ <b>No <c>ClinicId</c> and no foreign key to <c>Users</c></b>, both deliberate. The absent
    /// <c>ClinicId</c> is what puts this table outside the EF tenant query filter by construction — the two
    /// endpoints reading it are anonymous, so no scope is ever established and a filtered read would return zero
    /// rows with no error. The absent FK is explained in <c>PasswordResetRequestConfiguration</c>: a cascade would
    /// erase the record of a reset along with the account, and a restrict would make deactivating an account fail on
    /// a stale row. Nothing cascades these rows away; they are trimmed by the opportunistic purge on the request
    /// path.</para>
    /// </summary>
    public partial class AddPasswordResetRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PasswordResetRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EmailSendAttempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordResetRequests", x => x.Id);
                });

            // UNIQUE: « one row per account » is an invariant the database holds, not a race the handler hopes to
            // win. Two simultaneous requests for one account would otherwise leave two live tokens — and the
            // cooldown, which reads the single row, would then throttle neither of them.
            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetRequests_UserId",
                table: "PasswordResetRequests",
                column: "UserId",
                unique: true);

            // The completion lookup's only index, and unique for the same reason: two rows sharing one hash would
            // make « whose password does this link replace? » ambiguous.
            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetRequests_TokenHash",
                table: "PasswordResetRequests",
                column: "TokenHash",
                unique: true);

            // The opportunistic purge scans on these two together.
            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetRequests_ConsumedAtUtc_ExpiresAtUtc",
                table: "PasswordResetRequests",
                columns: new[] { "ConsumedAtUtc", "ExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PasswordResetRequests");
        }
    }
}
