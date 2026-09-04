using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Gives a devis step its <b>interval</b> — the calendar days it must wait after the previous one.
    /// <para>
    /// A step carried chair time and nothing about the calendar, so every interval the catalogue's own research
    /// had established was discarded at the model boundary: « les séances sont espacées d'une semaine environ »,
    /// « la réévaluation est à 8 semaines minimum », « dépose du pansement ou des sutures à 7–10 jours », three
    /// to six months of ostéointégration. With nothing to compare against, « Traitements en cours » alarmed on a
    /// flat fortnight — so an implant progressing exactly to its own protocol read as abandoned for ten of its
    /// twelve weeks, and there was no « pas encore due » state at all.
    /// </para>
    /// <para>
    /// One nullable column. <b>Null is the state every existing row keeps</b>, and it means « the interval is
    /// clinically free », not zero — a devis already signed must not acquire a claim about its own rhythm that
    /// nobody quoted. The catalogue side needs no column at all: <c>ProcedureType.DefaultSteps</c> is JSON, so a
    /// pre-existing row deserialises with the new property null.
    /// </para>
    /// <para>
    /// ⚠️ No backfill from the catalogue, deliberately. A live devis owns its steps — that is the rule that stops
    /// an improved protocol moving under a quote the patient signed — and reaching into those rows to stamp
    /// today's intervals onto them would be exactly the drift the ownership rule exists to prevent.
    /// </para>
    /// <para>
    /// ⚠️ Hand-written, and the scaffolder must not be trusted here: <c>Entity&lt;TId&gt;.Version</c> maps onto
    /// PostgreSQL's <c>xmin</c> system column, so the differ emits an <c>AddColumn&lt;uint&gt;("xmin")</c> for
    /// all 38 entities that PostgreSQL refuses. The paired Designer holds the previous migration's model with
    /// this one property added.
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class AddTreatmentPlanItemStepInterval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MinDaysAfterPrevious",
                table: "TreatmentPlanItemSteps",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MinDaysAfterPrevious",
                table: "TreatmentPlanItemSteps");
        }
    }
}
