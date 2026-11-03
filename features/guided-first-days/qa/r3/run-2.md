# QA run 2 — Guided first days, release 3 (« Me guider »)

**Date:** 2026-11-03
**Cycle:** 2 of at most 3 (after F-1). No further source change in this cycle — four drives of the same `walk.mjs`
(`walk-run2.log` … `walk-run5.log`), each fixing only the probe:
- A4: watch for the « fait » toast while confirmations are answered (it came and went during them).
- B7: the account's creation moved back a day (fixture SQL, throwaway DB); the guide started **before** the due
  visit is created — created first, the question rightly takes the dashboard and its modal blocks the card.
- B6: poll for the hidden bubble (it hides on the next frame) instead of reading once.
- **D5 added** (never remove a row): the same patient creation with **no guide**, as a control for A3.

**Environment:** as run 1. **Widths looked at:** 320×640 · 390×844 · 1440×900 · 1536×730 — `Read`: A2-step3-1440,
C1-agenda-390, C3-submit-1536x730, C2-names-320
**Status:** GREEN for « Me guider » — the two remaining ❌ are one pre-existing defect, reproduced without any guide

## Scenarios (drive 5)

**34 ✅, 2 ❌, 1 ⏭**

| Row | Outcome |
|-----|---------|
| A1–A6 | ✅ all five guides end to end, each ending on its « fait » toast |
| B1–B9 | ✅ quit, cancel, leave the page, Escape, no patient, confirmation hides the bubble, no post-visit question during a guide (and it comes right after), « Suivant » keeps the dialog and the focus |
| C1–C3 | ✅ phone « + » lit with the bubble at the top; 320 sheet in view; 730 px tall clear of « Créer le rendez-vous » |
| D1–D3 | ✅ both list trees, booking with no guide, the card after the guides |
| D4 | ⏭ chat notice over dialogs — needs a second signed-in colleague; the selector change is additive |
| A3 « the patient page opens » + D5 | ❌ **pre-existing**, see Observations |

## Observations (off-plan)

- **Creating a patient from « Patients » does not open their page** (with or without a guide — D5). The page asks
  for `/patients/{id}`, and the dialog's back-gesture guard (`lib/hooks/use-dirty-guard.ts`) pops its own history
  entry on close in the same moment, which cancels the jump. A shared hook with many dialogs, so not touched here;
  captured in `follow-up/patient-create-does-not-open-page.md`. The guide does not depend on it (its last step ends
  on « saved and closed »).
- At 320 × 640 the docked bubble covers the sheet's title row (with its ✕) during the « names » step — there is no
  room elsewhere without covering the fields or « Créer le patient »; « Annuler » stays reachable.
