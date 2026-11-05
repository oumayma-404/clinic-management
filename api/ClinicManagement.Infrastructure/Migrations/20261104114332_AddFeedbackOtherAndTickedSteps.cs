using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedbackOtherAndTickedSteps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TickedStepsCsv",
                table: "UserDashboardPreferences",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HardOther",
                table: "ProductFeedback",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LikedOther",
                table: "ProductFeedback",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TickedStepsCsv",
                table: "UserDashboardPreferences");

            migrationBuilder.DropColumn(
                name: "HardOther",
                table: "ProductFeedback");

            migrationBuilder.DropColumn(
                name: "LikedOther",
                table: "ProductFeedback");
        }
    }
}
