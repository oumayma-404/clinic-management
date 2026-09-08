using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDentalRecordActPonticTeeth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            /*
             * Which teeth of a bridge act are pontiques rather than piliers, as a JSON int array — the same
             * shape as `ToothNumbers` beside it.
             *
             * ⚠️ **Purely additive, and there is deliberately no backfill.** `""` deserialises to an empty
             * list through the value converter, and an empty list means « this act is not a bridge, or nobody
             * said which tooth is which » — which is exactly true of every row written before today. Guessing
             * one from the span's geometry is precisely what `BridgeCharting` refuses to do: a pier abutment
             * sits in the middle of a span and a cantilever hangs past its end, so an inferred backfill would
             * write a confident wrong shape onto historical clinical records.
             *
             * ⚠️ Checked for the scaffolded `xmin` column this solution's 38 `Entity<TId>.Version` mappings
             * provoke — none was emitted here, because the model snapshot was current when this was generated.
             */
            migrationBuilder.AddColumn<string>(
                name: "PonticToothNumbers",
                table: "DentalRecordActs",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PonticToothNumbers",
                table: "DentalRecordActs");
        }
    }
}
