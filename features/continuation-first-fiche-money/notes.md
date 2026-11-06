# Continuation's first séance — its money stays on its note

## What happened (prod, 06/11/2026, Dr Khaireddine Hamdane)

- 30/10: traitement de canal on 46, fiche 300 DT, note 2026-0141, 100 DT paid.
- 06/11: « Suite d'une séance précédente » with a « Prix du reste » → devis 2026-0016. The 30/10 act sits on it at
  0 « sur la note 2026-0141 », and the note is deliberately **not** attached (`ContinueRecordedActCommand`,
  `noteKeepsTheFirstAct`).
- Reopening the 30/10 fiche, the screen and the server both treated the act as « payé sur le traitement » and forced
  it to 0 (API log: « imposing 0 in place of the 300 sent »).
- « Corriger la note » with Payé 0 then cancelled 2026-0141, voided the 100 DT and raised **no** note — while the
  dialog announced « Nouvelle note ». The 300 DT séance was billed nowhere.
- He then deleted both fiches and re-entered the 100 on the devis.

## What shipped

1. **The act keeps its fee on its own fiche.** `PlanCarriedActPricing` no longer imposes 0 when the devis line's
   `BilledOnInvoiceId` is a note that bills the fiche being saved (header or line). The old exemption covered only
   the *attached* shape (`billedOnANoteRepresentingThePlan`). The modal's `ownNoteRepresentsThePlan` mirrors it
   (`carriedOnNoteNumber` + `recordCarriesPlanItem`).
2. **A correction that raises no replacement is a cancellation**, so it answers to `NoteCarriedActGuard` like
   cancel and delete. Refused with `invoice_carries_a_treatment_plan_act` and its own sentence
   (`CorrectionRefusal`). With money paid the replacement is raised and every read resolves it through the fiche,
   so that path stays open.
3. **The « Corriger la note » dialog says « Aucune nouvelle note »** when nothing is paid (`raisesReplacement`),
   instead of promising one.

## Blast radius

| # | Touching | Other consumers | Verdict |
|---|---|---|---|
| 1 | `PlanCarriedActPricing.ImposeAsync` (+2 optional params) | `CreateDentalRecordCommand` | unaffected — passes neither; a new fiche has no note |
| 2 | `UpdateDentalRecordCommand` exemption + `RetireForCorrectionAsync` | — | changed here |
| 3 | `patient-record-modal` `ownNoteRepresentsThePlan` | `markBilledOnPlan` effect only | changed here |
| 4 | `NoteCarriedActGuard.EnsureNotCarriedAsync` (+optional sentence) | `CancelInvoiceCommand`, `DeleteInvoiceCommand` | unaffected — default sentence unchanged |
| 5 | `CorrectionPreview.raisesReplacement` (optional) | `invoices-table`, `caisse-row-actions` | unaffected — omitted = old behaviour |
| 6 | `/factures` « Corriger » (`CorrectInvoiceCommand`) | — | unaffected — always issues a replacement draft |

## Tests

`ContinuationFirstSeanceResaveTests` (7). The three handler cases fail on the old code (checked by removing both
server fixes).

## Not done

- No browser pass. The `.tsx` change is one predicate and two text branches.
- Hamdane's data is untouched: both fiches stay deleted. The 30/10 one is in the 06/11 03:00 recovery point.
