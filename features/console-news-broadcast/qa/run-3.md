# QA run 3 — Nouveautés, after the review fixes

**Date:** 2026-11-06
**Cycle:** re-run after `/apply-review-fixes` (the 4 Major + 16 Minor review findings)
**Plan:** plan.md + 7 rows added for the fixes (R2, R3, R4, R9, R11, R12, R14; R16 ⏭)
**Driver:** qa/walk.mjs + qa/api-rows.mjs + qa/prep-account.mjs — same files, rows added, none removed
**Environment:** rebuilt from scratch — new DB `clinic_news_qa`, API from the worktree (:5094 / console listener :5494),
console production build (:3094), own key ring, Mailpit `@news-qa.test`. Nothing shared written.
**Widths looked at:** 320 · 390 (coarse pointer) · 820 · 1180 · 1440 · 1536×730
**Status:** GREEN (9 API ✅ · 31 browser ✅ · 4 ⏭ with reasons · 0 ❌)

## New rows for the review fixes

| ID | Review finding | Outcome | Evidence |
|----|----------------|---------|----------|
| R2 | F2 row buttons steal taps | ✅ | coarse pointer: 44/44/44 px buttons, gaps 8/16 px, `::after` no wider than its button |
| R3 | F3 card clips the result | ✅ | Outcome `white-space: normal`, the `dd` does not overflow |
| R4 | F4 failed refresh wipes the page | ✅ | history read forced to 503 during a real send: « Actualisation impossible : … » inline, heading « Nouveautés » and the form still there |
| R9 | F9 draft lost on reload | ✅ | subject + row restored after `page.reload()` |
| R11 | F11 focus after closing the confirm | ✅ | focus back on « Envoyer à 2 cabinets » |
| R12 | F12 focus after delete | ✅ | deleting row 2 of 3 focuses « Nouveauté 2 » (the next row) |
| R14 | F14 preview link navigates | ✅ | anchors in the preview have `pointer-events: none` |
| R16 | F16 reason when no audience | ⏭ | needs a seed with no eligible admin; read in code |

Every run-2 row stayed green (A1–A7, B1–B5, C1–C6, D2–D5); A4 now waits for the confirm's fresh re-read of the audience.

## Probe bugs fixed during the run
| Row | Symptom | Real cause |
|-----|---------|------------|
| R12 | expected « Nouveauté 1 » | the saved draft had restored a third row from an earlier walk step; expectation now counts rows |
| R12 | count 0 | the shell heredoc ate `\d` in the regex (known trap); fixed with an exact edit |
