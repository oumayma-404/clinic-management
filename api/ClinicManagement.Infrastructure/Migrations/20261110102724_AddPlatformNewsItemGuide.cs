using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformNewsItemGuide : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            /*
             * The guide under a news row (console-news-broadcast, 2026-11-10): an optional paragraph and up to five
             * numbered steps, the latter as a JSON string array in one text column (DentalRecord.Notes' shape).
             *
             * Purely additive, and deliberately no backfill: a NULL description and "" for the steps — which the
             * converter reads back as an empty list — mean « no guide », which is exactly what every row sent before
             * today carries, so the e-mail and the app render those rows unchanged.
             *
             * Checked for the scaffolded `xmin` column the 38 `Entity<TId>.Version` mappings can provoke: none — an
             * AddColumn creates no table, and the model snapshot was current when this was generated.
             */
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "PlatformNewsItems",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Steps",
                table: "PlatformNewsItems",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "PlatformNewsItems");

            migrationBuilder.DropColumn(
                name: "Steps",
                table: "PlatformNewsItems");
        }
    }
}
