# Run 2 — PR #123 (`ec242a50` + the F-1 fix), 2026-11-25

**Environment:** same isolated stack as run 1, on a **fresh** `clinic_pr123_qa` (dropped, recreated, cabinet
re-provisioned — the walk creates its own patients). API restarted on the same build (the fix is web-only);
`next dev` :3095 hot-reloaded the fix. Same peers live, none on these ports or this DB.

**Status: GREEN — 24/24 rows, 0 ⏭**

| ID | Result | Evidence |
|---|---|---|
| A1 | ✅ | import `{"permitPerMinute":600,…}` · colleague nulls · anonymous 401 |
| A2 | ✅ | « Rate-limit boost ON for import@pr123.test: 600 requests per 60s until 2026-11-28T16:25:34 » |
| A3-plan | ✅ | 40 à créer · 120 fichiers (probe now reads the value's own text node) |
| A3 | ✅ | « Import terminé. » in 195 s · 428 calls · peak 141 / 60 s (ordinary limit 120) · 0 × 429 · never paused |
| A4 | ✅ | colleague, same browser, 192 calls during the import, all 200 |
| A4-wire | ✅ | cabinet's minute full (colleague 429): import account 150/150 accepted (10 at a time) |
| A5 | ✅ | 40 patients · 120 files · 200 history lines · Amira Trabelsi `CreatedAt` 2015-03-07 11:00 UTC |
| A6 | ✅ | re-run 53 s, counts unchanged |
| B1 | ✅ | colleague at 390 px: « Pause de 15 s … reprend tout seul. », 3 × 429 absorbed, « Import terminé. » in 88 s, 15 · 45 |
| B2 / B2-shared | ✅ | forged token: 120 × 401 + 30 × 429; colleague then 429 — the forged calls spent the address bucket |
| B3 | ✅ | `Until` 8 days away → « Rate-limit boost NOT applied … more than 7 days away », rate-boost null |
| B4 | ✅ | `Until` start + 90 s → 600 before, null after, same process |
| C1-plan / C1 | ✅ | space warning on the plan; storage full → footer names it, 5 cards « en attente d'espace », 6 patients, 30 history, 5/18 files |
| C2 | ✅ | quota back → re-run: 18/18 files, 6 patients, no file twice |
| C3 | ✅ | Fatma and Ines Ben Ali on one phone: both created |
| C4 (6 widths) | ✅ | 320×640 · 390×844 · 820×1180 · 1180×820 · 1440×900 · 1536×730: no sideways scroll, « Importer » on screen, **no stray « 0 »** (`shots/run-2/`) |
| D1 | ✅ | colleague burst: 120 × 200, 30 × 429 « Trop de tentatives … » |
| D2 | ✅ | `/health` 200 at the limit |
| D3 | ✅ | « Ancien logiciel » tab: Détartrage, AMOXICILLINE, Panoramique, Carie; folder holds PDF + 2 radios |

## Fixes applied (cycle 1)

| Finding | Verdict | Root cause | Files | Blast radius |
|---|---|---|---|---|
| F-1 stray « 0 » | confirmed (pre-existing since `a2a373bb`) | `{summary.quotaShort && …}` — a number on the left of `&&` renders when it is 0 | `web/components/legacy-import/import-screen.tsx:178` → `quotaShort > 0 &&` | 1 site; the other `&&` guards in `components/legacy-import/` are strings or booleans |
| A3-plan | probe | `innerText` glued the size hint onto the figure | `qa/walk.mjs` | — |
| A4-wire 2 × 500 | environment | 150 simultaneous calls exhausted the shared local Postgres (« 53300 too many clients ») while a second API was up | `qa/api-checks.mjs` (10 at a time) | — |

**Guards added:** the walk's C4 rows now fail on a bare text « 0 » inside a layout `<div>` (no mechanical guard in
`check:responsive`: telling a number from a string on the left of `&&` needs types).
**Gates after the last edit:** check:responsive 91 ✅ · tsc ✅ · build ✅ (web-only fix; backend suite 7 352 ✅ unchanged since `ec242a50`).

## Observations (carried from run 1, not fixed — off-plan)

- Speed is set on the browser side, not by the limit: ~5 s per patient with or without the boost; peak 141
  calls/min, under prod's ordinary 600/min.
- An API 429 says « réessayer dans 5 minutes » while the API window is 1 minute (`RateLimiting.cs:291`, pre-existing).
