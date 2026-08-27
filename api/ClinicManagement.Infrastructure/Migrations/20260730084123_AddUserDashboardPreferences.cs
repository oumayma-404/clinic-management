using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserDashboardPreferences : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// <b>The <c>xmin</c> column EF's differ generated here has been deleted by hand, deliberately.</b>
        /// <para>
        /// <c>ApplicationDbContext</c> maps every <c>Entity&lt;T&gt;.Version</c> onto PostgreSQL's <c>xmin</c>
        /// <i>system</i> column to get optimistic concurrency with no schema change. Because the property is part
        /// of the model, the differ believes a brand-new table needs a matching column and emits
        /// <c>xmin = table.Column&lt;uint&gt;(...)</c> inside <c>CreateTable</c> — which PostgreSQL rejects
        /// outright: <c>column name "xmin" conflicts with a system column name</c>. The migration would fail on
        /// apply, and on a Local install that means a service that will not start.
        /// </para>
        /// <para>
        /// This is the same trap the <c>AddConcurrencyToken</c> migration documents (there the differ emitted 38 ×
        /// <c>AddColumn&lt;uint&gt;("xmin")</c> and its <c>Up()</c> was left deliberately empty), reappearing in
        /// <c>CreateTable</c> form. Removing the line loses nothing: every PostgreSQL table already has
        /// <c>xmin</c>, so the concurrency token works the moment the table exists. The model snapshot keeps the
        /// mapping, which is what binds the two.
        /// </para>
        /// <para>
        /// ⚠️ Re-generating this migration will put the column back. Any future migration that creates a table
        /// needs the same edit.
        /// </para>
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserDashboardPreferences",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    HiddenKpisCsv = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDashboardPreferences", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserDashboardPreferences_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserDashboardPreferences");
        }
    }
}
