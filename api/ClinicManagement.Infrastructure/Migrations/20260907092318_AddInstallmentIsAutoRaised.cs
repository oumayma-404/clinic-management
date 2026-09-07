using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Tells apart an échéance somebody agreed from the lump-sum row the system raises so a payment has
    /// somewhere to live — see <c>Installment.IsAutoRaised</c> and <c>InstallmentLateness</c>.
    /// </summary>
    public partial class AddInstallmentIsAutoRaised : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAutoRaised",
                table: "Installments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            /*
             * ⚠️ **The backfill is the half that matters.** Without it every devis already in a clinic's database
             * keeps reading « En retard » — 25 of 27 unpaid échéances on the dev database — and the column would
             * only start meaning something for plans accepted after the deploy.
             *
             * The discriminator is `"DueDate" = "AcceptedDate"` **to the instant**, not to the day, and that is
             * exact rather than a heuristic: `TreatmentPlan.Accept` writes `AcceptedDate.Value` verbatim into the
             * row it raises, so the two timestamps are byte-identical, while every échéance a human entered comes
             * from a `YYYY-MM-DD` field and lands at midnight. Measured on the dev database — 30 rows, three
             * clean groups, no overlap:
             *
             *   rows on plan | DueDate = AcceptedDate | midnight | full total | count
             *   -------------+------------------------+----------+------------+------
             *              1 | true                   | false    | true       |   17   <- Accept's lump sum
             *              1 | false                  | true     | true       |    9   <- a date somebody typed
             *              2 | false                  | true     | false      |    4   <- a real échéancier
             *
             * ⚠️ Deliberately conservative, and in the safe direction: a row this misses stays « agreed » and can
             * still go red, which is at worst the behaviour of the day before. Claiming a typed date was
             * auto-raised is the error that cannot be seen — it silences an échéance a patient really did miss,
             * which is the only thing an échéancier is for.
             *
             * A re-spread row (`RespreadSchedule`) is also auto-raised going forward but is NOT backfilled: its
             * date comes from the clinic clock at the time of the re-spread and matches nothing here. There is no
             * evidence in the data to identify one, and inventing a rule for it would break the paragraph above.
             */
            migrationBuilder.Sql("""
                UPDATE "Installments" i
                SET "IsAutoRaised" = TRUE
                FROM "TreatmentPlans" p
                WHERE p."Id" = i."TreatmentPlanId"
                  AND p."AcceptedDate" IS NOT NULL
                  AND i."DueDate" = p."AcceptedDate";
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAutoRaised",
                table: "Installments");
        }
    }
}
