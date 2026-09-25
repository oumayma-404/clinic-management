# Blast radius — gap1 QA fixes, fiche + patient page + lists (items 1–9)

Display only. No request, payload, reducer action, tri-state rule or id changes.

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `factures/cheque-fields.tsx` `ChequeFields` markup (heading + paragraph gone, `@container` grid) | shared sub-form | fiche footer, `payment-modal`, `installment-payment-modal`, `settle-plan-modal` (4, not 3) | must re-test all 4 — ids / `chequePaymentFields` untouched; grid hinges on the box (448 px dialogs get 2+1, fiche gets 1 row) |
| 2 | fiche footer: cheque + money band in one capped wrapper (`max-md:` and `max-height:560px` → `max-h-[38dvh]`), cheque moved BELOW the figures, « Payé (séance) » floor `min-w-[12rem]` | layout | none outside the modal; ids `#paid` `#paid-method` `#session-total` `#collected-on-plan` kept (e2e FICHE-20/22/24) | unaffected — wrapper has no `flex-1` (N27 `dialog-owns-one-scroller` reads flex-1 + overflow) |
| 3 | labels « Payé (séance) » / « Payé (traitement) » / « Reste à payer (séance) » when both fields render | display | e2e reads ids, not labels (grep) | unaffected — gate = `collectsOnTreatment && !withholdSeanceMoneyFields`, the existing `(traitement)` gate |
| 4 | focused-act line under the chart: no amount for `billedOnPlan` | display | e2e FICHE-23/24 count « Inclus dans le traitement » (strict `toHaveCount(1)` / `toBeVisible`) | must NOT add the tag word here — « nothing » chosen so the count stays 1 |
| 5 | `font-mono` → `tabular-nums` (acts count, teeth summary, tooth chips, odontogram pontique chips) | display | e2e `/2\s*actes/` text match | unaffected |
| 6 | patient tab « Dossiers médicaux » → « Fiches de soins »; helper line removed; « Ajouter un acte dentaire » → « Nouvelle fiche de soins » (×2) | display | e2e: none by name (grep); `RETIRED_PATIENT_TABS` keys on value | unaffected |
| 7 | `patient-plans-strip` fold « Détail des séances » (count dropped) | display | none | unaffected |
| 8 | `patient-outstanding-strip` card field « Échéance » / « Émise le » → « Depuis » | display | N39 (échéance date owner) — `line.since`, not `dueDate` | unaffected |
| 9 | `RECORDED_ACT_LABEL` / `RECORDED_ACT_LEGEND` (in `odontogram-recorded-acts.ts`, NOT in my list) « Acte réalisé » → « Acte fait » | shared constant | `odontogram.tsx` legend, `tooth-symbols.tsx` symbol legend; N35 reads declarations, not text | must change — one constant, so both legends move together (a local override would split them) |
| 10 | odontogram: « Traitement en cours ou prévu », « glissez » hint gone, « Créer un devis » one label | display | aria-label keeps the full phrase; drag gesture unchanged | unaffected |
| 11 | `/treatment-plans` header subtitle removed | display | `PageHeader.subtitle` optional | unaffected |
| 12 | worklist table: one `<tbody>` per group, heading a `<th scope="rowgroup">` band | markup | card tree unchanged in shape; e2e: no group assertions (grep) | must re-test the table at `lg:`+ |
| 13 | post-visit prompt: title « Séance terminée », body composed from the appointment (prefetched), `handleAddRecord` reuses it | display + one read | e2e `dismissPostVisitPrompt` finds « Plus tard » (unchanged); toast path uses the same text | must re-test dialog + toast |

## Capabilities moved (none removed)

| Capability | Before | After |
|---|---|---|
| Cheque identity (n°, banque, encaissable le) | block above the figures, with heading + caption | same three labelled fields, one row, below the figures |
| Drag-select discoverability | permanent « Glissez… » caption | « Plusieurs dents » button stays (keyboard/AT route and the readout) |
| Group headings on the worklist | a `<tr>` styled like column heads | a full-width tinted band per `<tbody>` |
