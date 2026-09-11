using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// « Acte non terminé » — the dentist's own statement that an act needs another séance.
    /// See <c>DentalRecordAct.IsUnfinished</c>.
    /// <para>
    /// ⚠️ <b>Nothing is backfilled, and that is the decision rather than an omission.</b> The flag is not
    /// derivable — a fiche records what was <i>carried out</i> and says nothing about what remains — so any
    /// backfill would be a guess written into the clinical record, and a wrong guess reads as a dentist's own
    /// observation for ever. <c>false</c> for every existing row means « nobody said this act was unfinished »,
    /// which is exactly true and claims nothing.
    /// </para>
    /// <para>
    /// ⚠️ The consequence is worth knowing: « Suites à planifier » is <b>empty on the day this ships</b> and
    /// fills as séances are charted. That is correct and must not be repaired by inference.
    /// </para>
    /// <para>
    /// Checked for the scaffolded <c>AddColumn&lt;uint&gt;("xmin")</c> trap — the differ emitted none here, and
    /// there is no backfill for a <c>DropColumn</c> to be ordered against.
    /// </para>
    /// </summary>
    public partial class AddDentalRecordActIsUnfinished : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsUnfinished",
                table: "DentalRecordActs",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsUnfinished",
                table: "DentalRecordActs");
        }
    }
}
