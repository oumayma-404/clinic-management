using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Records <b>which note d'honoraires already collects</b> a devis act the plan deliberately holds at 0 —
    /// see <c>TreatmentPlanItem.BilledOnInvoiceId</c>.
    ///
    /// <para>The 0 itself has been written since the continuation feature shipped; nothing recorded whose it was.
    /// So every surface read after the booking dialog reported the devis' money as the treatment's money
    /// (« Total convenu 10,000 · Encaissé 0,000 » on a treatment already 50 paid and 40 owed), and cancelling or
    /// deleting the note dropped the fee out of every balance with no error anywhere.</para>
    /// </summary>
    public partial class AddTreatmentPlanItemBilledOnInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            /*
             * ⚠️ No `xmin` in this migration, and it is checked rather than assumed: `Entity<TId>.Version` maps
             * onto PostgreSQL's system column, so a stale model snapshot makes the differ emit
             * `AddColumn<uint>("xmin")` for all 38 entities. It emitted none — these are `AddColumn`s on an
             * existing table, which creates nothing for the trap to bite on.
             *
             * ⚠️ `BilledOnInvoiceAmount` defaults to 0 and that is the right reading for every existing row: a
             * line nothing bills elsewhere is worth 0 *of somebody else's money*, and its own fee is in
             * `PlannedCost` beside it. `BilledOnInvoiceId` takes no default — NULL is the honest value and a
             * sentinel Guid would read as a real note.
             *
             * ⚠️ The index is what the cancel/delete guards ask through (`GetPlansBilledOnInvoiceAsync`), which
             * is the reverse direction: one invoice id, « does any devis hold an act billed on it? ».
             */
            migrationBuilder.AddColumn<decimal>(
                name: "BilledOnInvoiceAmount",
                table: "TreatmentPlanItems",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "BilledOnInvoiceId",
                table: "TreatmentPlanItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlanItems_BilledOnInvoiceId",
                table: "TreatmentPlanItems",
                column: "BilledOnInvoiceId");

            /*
             * ─── The backfill, and it sits BELOW every DDL statement above so a later edit inherits that order
             *     (the `AddSuppliers` scar: EF put a `DropColumn` first, above the backfill that read it).
             *
             * ⚠️ **This repo's standing rule is « never backfill an inference », and this is deliberately not
             * one.** The rule it would break — `ToothState.BridgeGroupId`'s « inferring a group from adjacency
             * would freeze today's wrong answer into data » — is about a *geometric guess* with no fact behind
             * it. Here the fact exists and is recoverable: a continuation's billed path leaves a signature no
             * other write path in the product produces, and the alternative is that every devis already created
             * this way stays wrong for ever, which is exactly the state that produced this migration.
             *
             * The signature, and every term is load-bearing:
             *
             *   · `PlannedCost = 0` — a plan-carried act keeps its REAL fee on the devis (the 0 lives on the
             *     fiche, imposed by `PlanCarriedActPricing`), so a 0-cost devis line is already rare.
             *   · `SequenceNumber = 0` — `ContinueRecordedActCommand` always writes the already-billed act first
             *     and the priced remainder second.
             *   · the plan has ≥ 2 acts — a continuation reaches the billed path ONLY when there is new money
             *     (`remainingCost > 0`), and that always adds a second line. With no new money the note is
             *     *attached* instead and needs no marker at all. This term is what excludes a lone
             *     « contrôle gratuit » line priced at 0 on an ordinary devis.
             *   · exactly ONE fiche behind the act (its own link or its steps'), and exactly ONE note billing
             *     that fiche — where either is ambiguous nothing is written, because a marker naming the wrong
             *     note is worse than no marker.
             *   · that note bridges no plan (`"TreatmentPlanId" IS NULL`) — a bridged note represents its devis
             *     and is the *opposite* arrangement.
             *   · `"BilledOnInvoiceId" IS NULL` — so `Up()` is safe to re-run, and a row the application has
             *     since written is never overwritten.
             *
             * ⚠️ **The amount degrades honestly rather than guessing.** The act's own fee is recoverable only
             * when its fiche recorded exactly one act; on a multi-act séance nothing says which share of the
             * note was this act, so it stays 0 — which the read then renders as « facturé sur la note n° X »
             * with no figure, and reports `BillsOtherWork` (true, since the note's TTC exceeds 0). Both are
             * correct statements about a séance that billed more than this treatment.
             *
             * ⚠️ **No status filter on the note**, deliberately: the enum's ordinals are not checked by any
             * compiler in SQL, and the read path already drops a cancelled note — such a row simply renders as
             * it does today.
             */
            migrationBuilder.Sql("""
                WITH linked AS (
                    -- The fiche(s) behind an act: its steps' links, and its own. Both halves, because a
                    -- stepped act takes its own link only when its LAST step lands — and a continuation's
                    -- first act is precisely one whose step 1 of 2 has landed.
                    SELECT it."Id" AS item_id, s."LinkedDentalRecordId" AS record_id
                      FROM "TreatmentPlanItems" it
                      JOIN "TreatmentPlanItemSteps" s ON s."TreatmentPlanItemId" = it."Id"
                     WHERE s."LinkedDentalRecordId" IS NOT NULL
                    UNION
                    SELECT it."Id", it."LinkedDentalRecordId"
                      FROM "TreatmentPlanItems" it
                     WHERE it."LinkedDentalRecordId" IS NOT NULL
                ),
                one_record AS (
                    -- Exactly one, or nothing is written: a marker naming the wrong note is worse than none.
                    SELECT item_id, (array_agg(record_id))[1] AS record_id
                      FROM linked
                     GROUP BY item_id
                    HAVING COUNT(*) = 1
                ),
                fiche_note AS (
                    SELECT l."DentalRecordId"              AS record_id,
                           (array_agg(DISTINCT i."Id"))[1] AS invoice_id,
                           COUNT(DISTINCT i."Id")          AS invoice_count,
                           bool_or(i."TreatmentPlanId" IS NOT NULL) AS any_bridged
                      FROM "InvoiceLines" l
                      JOIN "Invoices" i ON i."Id" = l."InvoiceId"
                     WHERE l."DentalRecordId" IS NOT NULL
                     GROUP BY l."DentalRecordId"
                ),
                single_act AS (
                    -- The act's own fee, recoverable only when its séance recorded exactly one act. On a
                    -- multi-act fiche nothing says which share of the note was this act, so it stays 0 and
                    -- the read renders the note without a figure (and reports « facture aussi d'autres actes »,
                    -- which is true).
                    SELECT "DentalRecordId" AS record_id,
                           CASE WHEN COUNT(*) = 1 THEN MIN("Cost") ELSE 0 END AS cost
                      FROM "DentalRecordActs"
                     GROUP BY "DentalRecordId"
                ),
                plan_size AS (
                    SELECT "TreatmentPlanId", COUNT(*) AS n
                      FROM "TreatmentPlanItems"
                     GROUP BY "TreatmentPlanId"
                )
                UPDATE "TreatmentPlanItems" AS tgt
                   SET "BilledOnInvoiceId"     = fn.invoice_id,
                       "BilledOnInvoiceAmount" = COALESCE(sa.cost, 0)
                  FROM one_record orec
                  JOIN fiche_note fn ON fn.record_id = orec.record_id
                  LEFT JOIN single_act sa ON sa.record_id = orec.record_id
                 WHERE tgt."Id" = orec.item_id
                   AND tgt."BilledOnInvoiceId" IS NULL
                   AND tgt."PlannedCost" = 0
                   AND tgt."SequenceNumber" = 0
                   AND fn.invoice_count = 1
                   AND fn.any_bridged = false
                   AND (SELECT ps.n FROM plan_size ps
                         WHERE ps."TreatmentPlanId" = tgt."TreatmentPlanId") >= 2;
                """);

            /*
             * ─── And the continuation line's own catalogue link, for the plans already created without it.
             *
             * `ContinueRecordedActCommand` used to write the remaining-work line with a null `ProcedureTypeId`,
             * so booking it produced a link-only appointment row and the fiche opened from that booking had
             * nothing to prefill — the dentist was asked « Choisissez l'acte réalisé… » about a séance the app
             * had arranged. The command carries the link now; this is the same repair for the devis already on
             * file, which would otherwise stay unanswerable for the life of each treatment.
             *
             * ⚠️ **This is not the inference the migration's own rule warns about.** The source is the carried
             * act's OWN column on the same plan — a recorded fact, copied — not a guess from shape or position.
             * Only a line with no link of its own is touched, and only on a plan that has a carried act, so an
             * ordinary multi-act devis (where two lines are genuinely different acts) is never reached.
             */
            migrationBuilder.Sql("""
                UPDATE "TreatmentPlanItems" AS tgt
                   SET "ProcedureTypeId" = parent."ProcedureTypeId"
                  FROM "TreatmentPlanItems" AS parent
                 WHERE parent."TreatmentPlanId" = tgt."TreatmentPlanId"
                   AND parent."BilledOnInvoiceId" IS NOT NULL
                   AND parent."ProcedureTypeId" IS NOT NULL
                   AND tgt."BilledOnInvoiceId" IS NULL
                   AND tgt."ProcedureTypeId" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TreatmentPlanItems_BilledOnInvoiceId",
                table: "TreatmentPlanItems");

            migrationBuilder.DropColumn(
                name: "BilledOnInvoiceAmount",
                table: "TreatmentPlanItems");

            migrationBuilder.DropColumn(
                name: "BilledOnInvoiceId",
                table: "TreatmentPlanItems");
        }
    }
}
