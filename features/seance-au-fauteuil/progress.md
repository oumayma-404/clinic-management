# Séance au fauteuil — progress

Worktree `.claude/worktrees/seance-au-fauteuil`, branch `feature/seance-au-fauteuil` from `origin/main` a3249a87.
Committed locally only — no push, no PR (the owner's call). Design: `blueprint.md`; what shipped: `notes.md`.

## Status: DONE — both waves, all gates green, browser QA GREEN

| Gate | Result |
|---|---|
| Backend unit suite (unfiltered, Release) | 7271 passed · 0 failed · 6 skipped (baseline 7222) |
| `npx tsc --noEmit` | clean |
| `npm run check:responsive` | 90/90 (two new derived checks: N57 `fiche-door-has-one-composer`, N58 `pdf-print-has-one-way`) |
| `npm run build` | exit 0 |
| `/test-in-browser` run 1 | GREEN — 24/24 (`qa/run-1.md`), isolated stack :5098/:3098 on a DB copy |
| New e2e specs (`seance-au-fauteuil*.spec.ts`) | 11/11 against the same stack |
| `e2e/scripts/check-coverage.mjs` | the 11 new tier-0 rows covered (2 in a browser); no new failure (its 64 untested tier-0 rows pre-exist on `main`) |

## Wave 1 — the fiche during the séance (done)
- `Features/Patients/FicheVisitCompletion.cs`: « Terminer la séance » or the slot's end completes the visit; both fiche
  commands ask it. `AppointmentProgressJob` completes a fiche-bearing visit at its end.
- Fiche modal: in-place « Enregistrer » (`SaveMode` stay/close/finish), « Terminer la séance », « Enregistré à … »,
  « Payé » empty + « Tout payé », « Imprimer l'ordonnance / la demande d'examens », the save toast's « Imprimer ».
- `printPdfBlob` is the one print path (preview dialog, invoice PDF dialog, fiche) — N58.
- Every « open the fiche » door goes through `lib/visits/fiche-door.ts` — N57.

## Wave 2 — the desk takes the money (done)
- `GET /appointments/to-collect` (`GetSeancesToCollectQuery`, hidden under Mode discret): Unbilled · NoteBalance ·
  DraftNote · Treatment (a devis séance with money due now).
- Agenda « À encaisser · N » pill + list (`to-collect-button.tsx`), the wallet mark on blocks, the RDV window's row,
  one resolver (`use-seance-collection.tsx`), one shared read (`use-seances-to-collect.ts`).
- Cheque fields in « Facturer cette intervention ».
- Hardening: (a) `CollectedOnNote` → « Payé » reopens on the note's figure; (b) « Corriger la note » carries the desk's
  payments (`NoteCorrectionCarry`), refuses a deposited cheque it would drop or a note carrying a treatment;
  (c) `RecordInstallmentPaymentCommand.DentalRecordId`; (d) `seance_has_note` + « À clôturer » counts issued notes
  only; (e) `correction_requires_doctor`.

## Deviations from the blueprint
- `to-collect-sheet.tsx` became `to-collect-button.tsx` (a `Dialog` with `mobile="sheet"`, the app's own sheet form).
- « One note per séance » refuses only a note naming **no fiche**: a visit may carry several fiches, each billing its own
  acts (`VisitClosureReader` sums them), so another fiche's note is not in the way.
- The correction carry keeps the note-level « Corriger »'s rule (rows in order, clamped) with « Payé » as the ceiling,
  and refuses only a deposited cheque it would **not** carry in full — carried in full, its bank mark moves with it.
- A correction from the fiche refuses a note attached to a devis or holding another séance's money (new code
  `dental_record_note_carries_treatment`): the replacement would be attached to neither.
- `DentalRecordPostVisitCompletionTests.cs.deferred` stays parked; `FicheVisitCompletionTests` covers the rule.

## Open
- Merge order with PR #116 (cabinet de groupe), which edits `appointment-calendar.tsx` and `edit-appointment-dialog.tsx`.
- `features/seance-au-fauteuil/blueprint.md` also exists, untracked, in the main checkout — identical to this one.
