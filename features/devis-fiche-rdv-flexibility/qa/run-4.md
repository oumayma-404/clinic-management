# Run report — wave 4 (H · I · J · C4b)

**Status: GREEN — 26/26 on the wave-4 plan.**

| | |
|---|---|
| Plan | [`plan.md`](plan.md) § wave 4 |
| Driver | `walk-4.mjs` (fixtures by `arrange-4.mjs`, re-run fresh before every drive) |
| Cycles | 4 drives of the wave-4 plan + 2 of a6's walk; two product fixes (C4b options, C4b notice placement), whole plan re-run after the last |
| Environment | API pid 27888 (mine, Release, `Development`, migration `RetireDoctorsInsteadOfDeleting` applied) · web bf's `next dev` :3000 · peers idle except a6 (demo finished before the walk) |

## Results

| ID | Result |
|----|--------|
| H1 · H1b · J4 · H10 · H6 · H7 · H8 · H9 · H11 | ✅ |
| H2 · H3 · H4 · H5 | ✅ (wire — server rules) |
| C4b | ✅ after fix (drive 2 was red) |
| I1b · I1 · I4 · I3 | ✅ |
| J3a · J3b · J3c · J5a · J5b | ✅ |
| R1 · C320 · C730 | ✅ |
| R2 (a6's walk, updated by a6) | ✅ 23/23 — with its catalogue click scoped to the result row (`.last()`, scratch copy): `.first()` hit card 1's « Détartrage » header on a reopened fiche |

## Finding fixed during the run

| ID | Defect | Fix |
|----|--------|-----|
| C4b | A reopened fiche's second devis act still showed « 0,000 DT »: the modal's plan options hold open acts only (+ the lead), so the done second act could not be resolved | `app/patients/[id]/page.tsx` adds every act of `editingRecord.treatmentPlanItemIds` to the options |
| C4b | Found by the R2 regression: with several carried cards, the lead's « étape 2 sur 2 · Pose » and « L'acte entier est chiffré 300 DT » went to the FIRST carried card — a détartrage | `planActKey` picks the lead act's card (`billedPlanItem.procedureTypeId`), else the first carried |

## Probe bugs (not product)

| Symptom | Cause |
|---|---|
| H1b, J3 text « not found » | the walk's space normaliser had lost its U+00A0 / U+202F characters, so any « … » never matched |
| H10 visit « Terminé » before the walk | fixture: a fiche saved with no visit is tied to that day's only visit (`DentalRecordVisitLink`) and closes it — fixtures moved to other days |
| J3a patient not on screen | « À clôturer » holds 455 rows; checked on the patient's own « Travail non facturé » (same helper) |
| R1 textarea timeout | the notes field is folded behind « Notes et options » |
| C4b count 0 | act cards render collapsed; read the card line (no « DT ») instead |

## Checks outside the browser

- Unit suite 4842 ✅ (unfiltered, Release) · `tsc` ✅ · `check:responsive` 72/72 ✅ · production build ✅
- verify-schema before/after: same drift list (all pre-existing), nothing new from the migration
- reconcile-money HEAD vs tree: identical

Evidence: `shots/w4-H6-confirm.png` · `w4-C4b-reopened.png` · `w4-C320-history.png` · `w4-C320-procedure-types.png` · `w4-C320-edit-locked.png` · `w4-C730-edit.png`
