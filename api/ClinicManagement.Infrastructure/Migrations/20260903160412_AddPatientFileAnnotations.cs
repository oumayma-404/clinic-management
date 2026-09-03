using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientFileAnnotations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PatientFileAnnotations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientFileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    X = table.Column<double>(type: "double precision", nullable: false),
                    Y = table.Column<double>(type: "double precision", nullable: false),
                    Z = table.Column<double>(type: "double precision", nullable: false),
                    NormalX = table.Column<double>(type: "double precision", nullable: false),
                    NormalY = table.Column<double>(type: "double precision", nullable: false),
                    NormalZ = table.Column<double>(type: "double precision", nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                    // ⚠️ The scaffolder emitted `xmin = table.Column<uint>(type: "xid", rowVersion: true)` here
                    // and it is REMOVED deliberately. `Entity<TId>.Version` maps onto PostgreSQL's own system
                    // column, which every table already has implicitly — declaring it makes the CREATE TABLE
                    // fail outright ("column name \"xmin\" conflicts with a system column name"). This is the
                    // same scaffolding trap that made three earlier migrations ship a deliberately empty Up().
                    // The mapping still works: the column exists because Postgres provides it, not because this
                    // migration created it.
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientFileAnnotations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientFileAnnotations_PatientFiles_PatientFileId",
                        column: x => x.PatientFileId,
                        principalTable: "PatientFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PatientFileAnnotations_ClinicId",
                table: "PatientFileAnnotations",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientFileAnnotations_PatientFileId",
                table: "PatientFileAnnotations",
                column: "PatientFileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PatientFileAnnotations");
        }
    }
}
