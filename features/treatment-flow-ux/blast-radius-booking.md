# Blast radius — booking (R1–R4, P1/P2 in the booking files)

Written before the first edit. Verdicts: `unaffected` · `must change` · `must re-test`.

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `PlanStepSuggestionNotice` → `ContinueTreatmentList` (same file) | the R1/R3 list | create + edit dialogs only | must change — both dialogs in this batch |
| 2 | `ContinueSessionDialog` (component) | 2nd window for « la suite » | create, edit, `unfinished-acts-list` | must change — all three stop mounting it; file keeps `ContinuationChoice` + a `useContinuableActs` loader |
| 3 | `PendingContinuation` (+ `previous`, `remainingInput`) | row state on `SelectedAct` | picker, `materialiseTreatments` (reads 4 fields), both dialogs | unaffected for the materialiser — additive; must re-test BOOK-49/50 |
| 4 | `continuationToSelectedAct` | builds the pending row | create (preset + press), edit (press), picker (inline edits) | must change — one builder for all four call sites |
| 5 | `protocolError` | save refusal | both `validateForm` | must change — adds « Prix du reste invalide »; same call sites |
| 6 | `AppointmentProtocolEditor` | séance editor | picker only | must change — controlled open row, no hint, no foot button |
| 7 | `suggestedPlanSteps` (shared, not edited) | 3-cap suggestion read | both dialogs | unaffected — re-called on the remaining plans for the fold, file untouched |
| 8 | `resolvePlannedProtocols` / `materialiseTreatments` / `createdPlansRef` / `schedulablePlanItems` | split + materialise | both dialogs, N26, N28 | unaffected — not edited; must re-test BOOK-31/32/33/35/37 |
| 9 | `commitTotal` / `saveActTotal` | devis-act price save on blur | picker, both dialogs | unaffected — same handler, label only |
| 10 | `toggleStep` / `groupActs` | step selection | picker, recap names (`actLabelsOf`) | unaffected — same handler, now fired from the strip |
| 11 | `AppointmentRecap` / `AppointmentRecapSection` | récapitulatif | both dialogs | must change — duplicated rows dropped (Actes count, rail Statut/Facturation) |
| 12 | edit footer (`showCancelDialog`, `showDeleteDialog`) | cancel / delete | edit dialog | must change — into « ⋯ »; same AlertDialogs |
| 13 | `AppointmentQuickActions` confirms | agenda « ⋯ » | agenda | must re-test — body text only |
| 14 | e2e `booking-browser.spec.ts`, `browser-hot-paths.spec.ts` | assertions on changed strings | BOOK-31/32/33/35/37/49/50 | must change — selectors follow the new labels |
| 15 | check-responsive N16/N17/N19/N21/N26/N28/N31/icon-button | derived guards | picker + dialogs | must re-test — run after the last edit |

## Capabilities moved (nothing removed)

| Capability | Before | After |
|---|---|---|
| Book the next séance of a treatment | suggestion card button | « Planifier : X · N min » on its card |
| Treatments beyond the 3rd | only « Actes du devis » search group | « N autres ▾ » fold + the group (kept) |
| Hide the reminder | ✕ | ✕ (same) |
| Continue an unfinished act | link → 2nd dialog → « Ajouter au rendez-vous » | « Continuer » on its card (ticked) or under « Suite d'une séance passée… » (unticked) |
| Name the continuation séance · price the rest | fields in the 2nd dialog | inline on the pending card (« Nom de la séance », « Prix du reste ») |
| Change which past séance is continued | reopen the 2nd dialog | « Continuer » on another card (replaces, never appends) |
| « Planifier la suite » (/a-cloturer) | 2nd dialog then booking | booking dialog opens with the row already added |
| Pick which step(s) this RDV is | chips behind « modifier » | tap a séance on the strip (done = disabled, booked elsewhere = « prévue le ») |
| Edit a protocol (rename · min · days · up · down · delete · add · reset) | editor always open in the card | same editor behind « Séances » (tap a séance on the strip opens its row) |
| One séance / split | « Tout faire en une seule séance » / « Répartir en N séances » | « Tout en 1 séance » / « Répartir en N séances » (outline buttons, gap 1) |
| Price a devis act (save on blur) | « Total convenu » | « Prix du traitement » (same field) |
| Cancel / delete a RDV | two footer buttons | « ⋯ » → « Annuler le rendez-vous » / « Supprimer » (same confirms) |
