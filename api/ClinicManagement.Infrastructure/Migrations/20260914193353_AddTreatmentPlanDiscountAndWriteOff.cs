using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// S2's remise and S4's write-off record — three additive columns, all defaulted.
    ///
    /// <para>
    /// ⚠️ <b>No backfill, and none is possible or wanted.</b> Every existing act gets
    /// <c>DiscountAmount = 0</c>, so its <c>NetCost</c> equals its <c>PlannedCost</c> and
    /// <c>TreatmentPlan.TotalPlanned</c> — which now sums the net — is unchanged for every devis in every
    /// database. No échéancier moves, no balance moves, no note d'honoraires moves. The write-off pair is null
    /// / 0 on every plan, which is exactly what « has not been written off » means.
    /// </para>
    /// <para>
    /// ⚠️ Checked for the scaffolded <c>xmin</c> column before it was kept (<c>Entity&lt;TId&gt;.Version</c> maps
    /// onto PostgreSQL's system column, and the differ emits <c>AddColumn&lt;uint&gt;("xmin")</c> for all 38
    /// entities when the snapshot is stale). This one emitted none.
    /// </para>
    /// </summary>
    public partial class AddTreatmentPlanDiscountAndWriteOff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "WriteOffAmount",
                table: "TreatmentPlans",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "WriteOffReason",
                table: "TreatmentPlans",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "TreatmentPlanItems",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WriteOffAmount",
                table: "TreatmentPlans");

            migrationBuilder.DropColumn(
                name: "WriteOffReason",
                table: "TreatmentPlans");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "TreatmentPlanItems");
        }
    }
}
