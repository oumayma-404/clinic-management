using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarImportReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PatientId",
                table: "StaffNotifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CalendarImportPendingReviewSince",
                table: "Patients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "GoogleCalendarHoldsOnlyAppointments",
                table: "Clinics",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Patients_ClinicId_CalendarImportPendingReviewSince",
                table: "Patients",
                columns: new[] { "ClinicId", "CalendarImportPendingReviewSince" },
                filter: "\"CalendarImportPendingReviewSince\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Patients_ClinicId_CalendarImportPendingReviewSince",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "PatientId",
                table: "StaffNotifications");

            migrationBuilder.DropColumn(
                name: "CalendarImportPendingReviewSince",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "GoogleCalendarHoldsOnlyAppointments",
                table: "Clinics");
        }
    }
}
