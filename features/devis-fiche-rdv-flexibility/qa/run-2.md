# QA run 2 — wave 2 — 2026-09-23 — GREEN

Peers live: anakin-28, clinic-management-d6/a6/a8/bf, anakin-d6 (all idle). API restarted by clinic-management-31; web = bf's `next dev`.

| Pass | Result |
|---|---|
| 1 | F2 ✅ · F9 ❌ no-change save bumped the revision · F1 ✅ · F3 ❌ stale save 200 (no 409) · F10 cascade · C4 probe bug (button is « Confirmer la séance ») · E4 ✅ |
| 2 (fixes) | `stepSignature([])` → null; the form saves with the version it was filled from (`hydratedVersionRef`), not the page's live one |
| 3 | all 13 rows ✅ · E3 not exercised (no reception credentials; policy pinned by `TreatmentPlansControllerAuthorizationTests`) |

Gates after the last edit: unit tests 4784 passed / 0 failed · `tsc` clean · `check:responsive` 72/72 · `npm run build` ok.
