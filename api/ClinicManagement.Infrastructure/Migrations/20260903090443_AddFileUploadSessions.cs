using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// One new table for an upload still arriving (<c>large-file-transfer</c> Part 2). Purely additive — nothing
    /// altered, narrowed or dropped — so there is no backfill and nothing for the destructive-before-backfill
    /// hazard to bite on.
    ///
    /// <para>⚠️ <b>EF's differ emitted an <c>xmin</c> column and it was removed by hand</b>, the same rejection
    /// that makes <c>AddConcurrencyToken</c>'s <c>Up()</c> deliberately empty and that
    /// <c>AddClinicSubscriptions</c> and <c>AddSuppliers</c> each had to undo: <c>Entity&lt;T&gt;.Version</c> maps
    /// onto PostgreSQL's <b>system</b> column, so writing it out as a real one fails with
    /// <c>column name "xmin" conflicts with a system column name</c>. Every row still gets its token from the
    /// system column.</para>
    ///
    /// <para>⚠️ <b>No foreign key to <c>Patients</c>, deliberately</b> — see the configuration. A patient deleted
    /// mid-upload should abandon the session, not cascade-delete a row the expiry sweep reclaims anyway, and the
    /// completion re-checks the patient against the caller's clinic before anything is stored.</para>
    /// </summary>
    public partial class AddFileUploadSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FileUploadSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    FolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DeclaredLength = table.Column<long>(type: "bigint", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UploadedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    StorageReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ChunkSize = table.Column<int>(type: "integer", nullable: false),
                    ReceivedParts = table.Column<int>(type: "integer", nullable: false),
                    ReceivedBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileUploadSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileUploadSessions_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileUploadSessions_ClinicId_PatientId",
                table: "FileUploadSessions",
                columns: new[] { "ClinicId", "PatientId" });

            migrationBuilder.CreateIndex(
                name: "IX_FileUploadSessions_ExpiresAtUtc",
                table: "FileUploadSessions",
                column: "ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileUploadSessions");
        }
    }
}
