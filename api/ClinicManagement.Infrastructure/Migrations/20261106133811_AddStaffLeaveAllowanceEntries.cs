using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffLeaveAllowanceEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StaffLeaveAllowanceEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Days = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Accrual = table.Column<int>(type: "integer", nullable: true),
                    CarryOver = table.Column<bool>(type: "boolean", nullable: false),
                    OnDay = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    // No xmin here: Entity<T>.Version maps onto PostgreSQL's system column, which CREATE TABLE refuses.
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffLeaveAllowanceEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffLeaveAllowanceEntries_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffLeaveAllowanceEntries_ClinicId_UserId_OnDay",
                table: "StaffLeaveAllowanceEntries",
                columns: new[] { "ClinicId", "UserId", "OnDay" });

            // The old « droit annuel » becomes the rule it always was — N days on 1 January, lost on 31 December — from
            // 1 January of the cabinet's current year (UTC+1, ClinicClock), so no balance moves. Before the column goes.
            migrationBuilder.Sql("""
                INSERT INTO "StaffLeaveAllowanceEntries"
                    ("Id", "ClinicId", "UserId", "Kind", "Days", "Accrual", "CarryOver", "OnDay", "Reason",
                     "CreatedByUserId", "CreatedByName", "CreatedAt")
                SELECT gen_random_uuid(), u."ClinicId", u."Id", 1, u."AnnualLeaveAllowanceDays", 1, false,
                       make_date(extract(year from (now() at time zone 'UTC' + interval '1 hour'))::int, 1, 1),
                       NULL, 'migration', 'Droit annuel (repris)', now() at time zone 'UTC'
                FROM "Users" u
                WHERE u."AnnualLeaveAllowanceDays" IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "AnnualLeaveAllowanceDays",
                table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AnnualLeaveAllowanceDays",
                table: "Users",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            // Back to one yearly figure: the latest live « par an » rule, the only shape the old column can say.
            migrationBuilder.Sql("""
                UPDATE "Users" u SET "AnnualLeaveAllowanceDays" = r."Days"
                FROM (SELECT DISTINCT ON ("UserId") "UserId", "Days"
                      FROM "StaffLeaveAllowanceEntries"
                      WHERE "Kind" = 1 AND "Accrual" = 1 AND "CancelledAt" IS NULL
                      ORDER BY "UserId", "OnDay" DESC, "CreatedAt" DESC) r
                WHERE u."Id" = r."UserId";
                """);

            migrationBuilder.DropTable(
                name: "StaffLeaveAllowanceEntries");
        }
    }
}
