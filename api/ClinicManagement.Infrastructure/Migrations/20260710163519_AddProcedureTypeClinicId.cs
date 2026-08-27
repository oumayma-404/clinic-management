using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProcedureTypeClinicId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProcedureTypes_Name",
                table: "ProcedureTypes");

            migrationBuilder.AddColumn<Guid>(
                name: "ClinicId",
                table: "ProcedureTypes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Backfill existing (previously global) procedure types to the earliest clinic by CreatedAt.
            // Lossy-but-documented default for a multi-clinic install that shared procedure types; a
            // single-clinic Local install is unaffected. Guarded so it never sets NULL when no clinic exists.
            migrationBuilder.Sql(@"
                UPDATE ""ProcedureTypes""
                SET ""ClinicId"" = (SELECT ""Id"" FROM ""Clinics"" ORDER BY ""CreatedAt"" ASC LIMIT 1)
                WHERE ""ClinicId"" = '00000000-0000-0000-0000-000000000000'
                  AND EXISTS (SELECT 1 FROM ""Clinics"");");

            migrationBuilder.CreateIndex(
                name: "IX_ProcedureTypes_ClinicId_Name",
                table: "ProcedureTypes",
                columns: new[] { "ClinicId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProcedureTypes_ClinicId_Name",
                table: "ProcedureTypes");

            migrationBuilder.DropColumn(
                name: "ClinicId",
                table: "ProcedureTypes");

            // Recreate the Name index as NON-unique on rollback. The forward migration's composite
            // UNIQUE(ClinicId, Name) deliberately allows two clinics to reuse the same name; restoring the
            // old global UNIQUE(Name) would throw a duplicate-key error if any such cross-clinic duplicate
            // exists, leaving Down() half-applied. A non-unique index restores the lookup without that risk;
            // an operator rolling back to code that requires global name-uniqueness must dedupe manually.
            migrationBuilder.CreateIndex(
                name: "IX_ProcedureTypes_Name",
                table: "ProcedureTypes",
                column: "Name");
        }
    }
}
