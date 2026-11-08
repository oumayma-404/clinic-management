# Continuation's first séance — its money stays on its note

## What happened (prod, 06/11/2026, Dr Khaireddine Hamdane)

- 30/10: traitement de canal on 46, fiche 300 DT, note 2026-0141, 100 DT paid.
- 06/11: « Suite d'une séance précédente » with a « Prix du reste » → devis 2026-0016. The 30/10 act sits on it at
  0 « sur la note 2026-0141 », and the note is deliberately **not** attached (`ContinueRecordedActCommand`,
  `noteKeepsTheFirstAct`).
- He wanted to say « le patient n'a pas payé ces 100 » — Payé 100 → 0, same devis, same acts.
- Reopening the 30/10 fiche, the screen and the server both treated the act as « payé sur le traitement » and forced
  it to 0 (API log: « imposing 0 in place of the 300 sent »).
- « Corriger la note » with Payé 0 then cancelled 2026-0141, voided the 100 and raised **no** note, while the dialog
  announced « Nouvelle note ». The 300 DT séance was billed nowhere.
- He then deleted both fiches and re-entered 100 on the devis.

## What shipped

1. **The act keeps its fee on its own fiche.** `PlanCarriedActPricing` no longer imposes 0 when the devis line's
   `BilledOnInvoiceId` is a note that bills the fiche being saved (header or line). The old exemption covered only
   the *attached* shape (`billedOnANoteRepresentingThePlan`). The modal's `ownNoteRepresentsThePlan` mirrors it
   (`carriedOnNoteNumber` + `recordCarriesPlanItem`).
2. **A correction always raises its replacement note — unpaid when Payé is 0.** `DentalRecordAutoBilling` used to
   skip billing at 0 even when superseding, which turned « le patient n'a pas payé » into « this séance is billed
   nowhere », on every fiche, not only a continuation. Now: old note cancelled (its payments « jamais reçus »), new
   note for the same acts, nothing collected. The devis is untouched; its reads find the new note through the
   fiche. An ordinary save at 0 still bills nothing.
3. **« Corriger la note » shows what each note bills and what was paid** (`previousCollected` / `nextCollected`).
   It compared a collected figure with a total (« il reste 200 » on a new note owing 300). One builder for its two
   doors in the fiche modal.
4. **The devis page gets « Corriger » on the séance-1 note row** → opens that fiche (`?editRecord=`), where the
   fiche and its note move together.
5. `/caisse` « Corriger le montant » opens a draft, so its dialog no longer previews « nouvelle note X » (`nextTotal: null`).
6. **The line under « Payé » points at « Corriger la note »** (QA F-1). It said « un montant payé ne se diminue pas ici
   — établissez un avoir » above the button that does exactly that; the server's `PaymentLowered` sentence said the same
   and now names « Corriger la note » too (an avoir only when money really went back).
7. **The devis text names no note** (QA F-2): « La 1re séance du … est facturée sur sa propre note d'honoraires ». A
   number stored there went stale on the first correction; the live number is on the act row. These notes never reach
   the PDF (`DevisPdfData` has none), whatever the old comment said.
8. **« avec d'autres actes » is counted in lines** (QA F-3), never inferred from money: after a correction the share is
   0 (« not attributable »), and a re-priced act or a timbre fiscal made `TTC − share > 0` lie. `GetMoneyByIdsAsync`
   carries `LineCount`; the creation-time twin in `ContinueRecordedActCommand` uses `Lines.Count`.

⚠️ An earlier version of this branch **refused** the Payé-0 correction while a devis held the act. That blocked a
legitimate fix of a typo; the replacement note is what makes it safe.

## Blast radius

| # | Touching | Other consumers | Verdict |
|---|---|---|---|
| 1 | `PlanCarriedActPricing.ImposeAsync` (+2 optional params) | `CreateDentalRecordCommand` | unaffected — passes neither; a new fiche has no note |
| 2 | `DentalRecordAutoBilling.BillIfPaidAsync` at 0 | `CreateDentalRecordCommand` (never supersedes) | unaffected — only the correction path changes |
| 3 | `patient-record-modal` `ownNoteRepresentsThePlan` | `markBilledOnPlan` effect only | changed here |
| 4 | `CorrectionPreview` (+2 optional fields) | `invoices-table` (draft, `null`), `caisse-row-actions` | unaffected without the fields — same figures as before |
| 5 | `plan-workspace` carried-note row | — | additive link |
| 6 | `GetMoneyByIdsAsync` tuple (+`LineCount`) | `TreatmentPlanWorkflowProjection` (only caller) + `CarriedNoteMoneyTests` | changed here |
| 7 | `DentalRecordBillingRefusals.PaymentLowered` sentence | `DentalRecordBillingGuard` (fiche turns its code into the dialog) | re-tested — QA A3 |
| 8 | stored devis notes sentence | devis page header, devis form | re-tested — QA A5/F2; old plans keep their text |

## Tests

- `ContinuationFirstSeanceResaveTests` (8) — the re-save and correction cases fail on the old code.
- `CarriedNoteMoneyTests` + 4 (a corrected note, re-priced, timbre fiscal, a real second line).
- Browser: `qa/plan.md` + `qa/walk.mjs` on an isolated copy — run 1 RED (3 findings, all already on main),
  run 2 GREEN, 59/59. To re-run: copy a main-lineage dev database, start API/web from this tree on spare ports,
  `arrange.mjs` for fresh fixtures, then `walk.mjs`.

## Not done

- Hamdane's data is untouched: both fiches stay deleted. The 30/10 one is in the 06/11 03:00 recovery point.
- His devis 2026-0016 on prod keeps its old text naming the cancelled note 2026-0141 (stored before this fix).
