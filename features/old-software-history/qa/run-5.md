# Run 5 — the radios/photos pass (`feature/legacy-import-radios`), 2026-11-27

**Cycle:** 1 of at most 3 (first run of `plan-radios.md`; the regression plan re-run in full)
**Plan:** `qa/plan-radios.md` (R0–R6) + `qa/plan.md` + `qa/plan-unattended.md`
**Driver:** `walk-radios.mjs` → `walk.mjs` → `walk-unattended.mjs` → `walk-b1-large.mjs` → `api-checks.mjs`
**Environment:** isolated stack — API :5095 (Release build of main, unchanged: this change is web-only), DB
`clinic_pr123_qa` fresh for each radios drive, `next dev` :3095 on the branch, headless Chrome. Fake data only.
**Peer sessions live:** clinic-management-d2, clinic-management-41, anakin-69 — none on these ports or this DB.
**Widths looked at:** C4's six (320×640 · 390×844 · 820×1180 · 1180×820 · 1440×900 · 1536×730); radios at 1440×900
**Status:** GREEN after probe fixes — no product defect found

## Scenarios

| ID | Outcome | Evidence |
|---|---|---|
| R0 | ✅ | 415 patients through the API |
| R1-plan | ✅ (probe fixed) | **2.9 s** for 417 folders against 415 patients · 5 121 elements · 403 filed on their own, **each on its own patient** (name + birth date checked on every card) · 13 to confirm = 10 homonym folders + the misspelt folder + « Amel Rouissi » / « Kamel Rouissi radio » · 1 not imported |
| R2 | ✅ | « Importer » enabled with 13 undecided · « 13 dossiers à confirmer : ils attendront votre choix, le reste peut être importé. » |
| R3 | ✅ | « Import en cours : N sur 403 patients » · « Import terminé. 13 dossiers attendent votre choix : choisissez, puis relancez. » in 300 s · 806 files · none on the waiting · nothing twice |
| R5 | ✅ | a click on a candidate shows its destination in 192 ms with 417 cards on the page |
| R4 | ✅ | relaunch asked only the 2 confirmed (2 folder reads); the patient whose second folder was confirmed: 4 files; the homonym: 2; 810 in all; « 11 dossiers attendent … » |
| R6 | ✅ | 810 files, each on the patient its folder was made for |
| walk.mjs (A3…D3, C4 ×6) | ✅ | A3 « Import terminé. » in 55 s; B1 5 × 429 absorbed |
| walk-unattended.mjs E1–E4, G0–G5, E8 | ✅ | E1 54 s despite 60 faults; G0 11 991 elements; G3 300 · 900 · nothing twice; G4 8 renewals, 0 × 401 |
| E5 | ✅ (probe fixed) | product right (the vanished radio named, 3 patients, 8/9 files); the walk read the footer one repaint before « Import terminé » |
| B1-large | ✅ | 7 × 429 absorbed, 60 · 180 · 0 failed |
| B2 · B2-shared | ✅ (re-driven) | first drive: all 150 forged calls 401, none 429 — the burst straddled the limiter's 60 s window; re-driven: 120 × 401 + 30 × 429, colleague then refused. API unchanged since run 4 (green there) |
| A4-wire · D1 · D2 | ✅ | unchanged |

## Probe fixes (cycle 1)

| Row | Cause | Fix |
|---|---|---|
| R1–R4 | the plan expected the « Amel Rouissi » / « Kamel Rouissi » pair to be filed on its own; the matcher asks — by design, see below | R1 now holds invariants (homonyms and the misspelt folder ask; only one-letter neighbours may also ask; nothing filed is misfiled); R2–R4 count on what R1 measured |
| E5 (and every walk's `run()`) | the footer was read before the button check, so a quick run could return the previous line | read the footer again after the run is seen finished |
| B2 | timing of the 60 s window | re-driven, green |

## Decision recorded

« An exact name that only one patient carries wins over a one-letter neighbour » was tried in the simulation and
**rejected**: on names one letter apart with one typo (Amel/Kamel, Amine/Amina, Sami/Samia…) it misfiles 17
folders in 600 against 6 today. Those folders keep asking; a misfiled radio costs more than a click.

## Observations (off-plan, not fixed)

- On the radios-only pass the boosted import account met one 429 in run 5's first drive (the arrangement's 415
  API calls had spent the same account's minute). Absorbed by the pause; no effect on prod's single-run case.
