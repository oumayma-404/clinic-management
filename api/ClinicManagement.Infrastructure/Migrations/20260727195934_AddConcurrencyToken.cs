using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Optimistic concurrency for all 38 entities — <b>snapshot-only, on purpose</b>.
    ///
    /// <para>
    /// <c>Entity&lt;T&gt;.Version</c> is mapped onto PostgreSQL's <c>xmin</c>, a <b>system column that already
    /// exists on every table</b>. There is nothing to create. The generated body — 38 × <c>AddColumn&lt;uint&gt;
    /// ("xmin", type: "xid")</c>, and 38 × <c>DropColumn</c> going back — was not merely redundant: PostgreSQL
    /// rejects it outright with <i>«&#160;column name "xmin" conflicts with a system column name&#160;»</i>, so
    /// shipping it as generated would have failed every deployment on the first migrate.
    /// </para>
    /// <para>
    /// The file is kept rather than deleted because the <b>model snapshot</b> is the point: without a committed
    /// migration recording the 38 mappings, the next unrelated <c>migrations add</c> silently absorbs all of
    /// them into itself — and that one <i>would</i> run.
    /// </para>
    /// </summary>
    public partial class AddConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty — see the class summary. xmin is a system column; nothing to add.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty — nothing was created, so there is nothing to drop.
        }
    }
}
