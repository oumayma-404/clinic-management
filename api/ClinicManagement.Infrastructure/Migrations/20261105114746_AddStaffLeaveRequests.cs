using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffLeaveRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AnnualLeaveAllowanceDays",
                table: "Users",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LeaveRequestsEnabled",
                table: "Clinics",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "StaffLeaveRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    HalfDay = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestedDays = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CountedDays = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    DecidedByUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledByUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    EnteredByAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    // No xmin here: Entity<T>.Version maps onto PostgreSQL's system column, which CREATE TABLE refuses.
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffLeaveRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffLeaveRequests_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffLeaveRequests_ClinicId_Status",
                table: "StaffLeaveRequests",
                columns: new[] { "ClinicId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffLeaveRequests_ClinicId_UserId_StartDate",
                table: "StaffLeaveRequests",
                columns: new[] { "ClinicId", "UserId", "StartDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffLeaveRequests");

            migrationBuilder.DropColumn(
                name: "AnnualLeaveAllowanceDays",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LeaveRequestsEnabled",
                table: "Clinics");
        }
    }
}
