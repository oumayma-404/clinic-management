using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StaffChatReadMarkers",
                columns: table => new
                {
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ConversationKey = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    LastReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffChatReadMarkers", x => new { x.ClinicId, x.UserId, x.ConversationKey });
                    table.ForeignKey(
                        name: "FK_StaffChatReadMarkers_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StaffMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationKey = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    SenderUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    QuickMessageKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    QuickMessageLabel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    IsUrgent = table.Column<bool>(type: "boolean", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClaimedByUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    // No `xmin` column: Version maps onto PostgreSQL's system column, which CREATE TABLE refuses.
                    ClaimedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffMessages_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffMessages_ClinicId_AppointmentId",
                table: "StaffMessages",
                columns: new[] { "ClinicId", "AppointmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffMessages_ClinicId_ConversationKey_SentAt",
                table: "StaffMessages",
                columns: new[] { "ClinicId", "ConversationKey", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffMessages_ClinicId_SenderUserId_SentAt",
                table: "StaffMessages",
                columns: new[] { "ClinicId", "SenderUserId", "SentAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffChatReadMarkers");

            migrationBuilder.DropTable(
                name: "StaffMessages");
        }
    }
}
