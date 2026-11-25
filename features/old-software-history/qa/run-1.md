# Run 1 — PR #123 (`ec242a50`), 2026-11-25

**Environment:** isolated stack — API :5095 built from the worktree (Release, scratchpad), DB `clinic_pr123_qa`
(fresh), bucket `pr123-qa`, own key ring, console off; `next dev` :3095 from the worktree; headless Chrome
(playwright-core 1.65). Ordinary limit shrunk to **120/min** per address; boost `import@pr123.test` **600/min**.
A second short-lived API on :5094 for B3/B4. Peers live: clinic-management-01, -8a, -dd, anakin-bb, anakin-d5 —
none uses :5095/:3095 or this DB. Data: invented people in the old software's PDF form (`pr123-gen-data.mjs`).

**Status: RED (1 finding — minor, pre-existing on main)**

## Scenarios

| ID | Result | Evidence |
|---|---|---|
| A1 | ✅ | import: `{"permitPerMinute":600,"until":"2026-11-28T16:02:19+00:00"}` · colleague: nulls · anonymous: 401 |
| A2 | ✅ | log: « Rate-limit boost ON for import@pr123.test: 600 requests per 60s until 2026-11-28T16:02:19 » |
| A3-plan | ✅ (probe fixed) | the probe read « 120139,9 Ko » (value + size hint glued); `shots/run-1/C4-plan-1440x900.png` reads 40 · 0 · 120 (139,9 Ko) · 0 |
| A3 | ✅ | « Import terminé. » in 207 s · 428 API calls · peak 131 in 60 s (over the ordinary 120) · 0 × 429 · never paused |
| A4 | ✅ | colleague in the same browser, 1 call/s through the whole import: 203 calls, all 200 |
| A4-wire | ✅ (probe fixed) | cabinet's minute full (colleague 429 before and after): import account 150/150 accepted. First drive fired 150 at once → 2 × 500 « 53300 too many clients » from the shared local Postgres — environment, re-driven 10 at a time |
| A5 | ✅ | 40 patients · 120 files · 200 history lines; Amira Trabelsi `CreatedAt` 2015-03-07 11:00 UTC = « Dossier créé le » at noon Tunis |
| A6 | ✅ | « Relancer l'import »: 56 s, counts unchanged 40 / 120 / 200 |
| B1 | ✅ | colleague (no boost), 390 px: « Pause de 15 s : le serveur demande de ralentir. L'import reprend tout seul. » (`shots/run-1/B1-pause-390.png`), 2 × 429 absorbed, « Import terminé. » in 88 s, 15 patients · 45 files |
| B2 | ✅ | forged token (import e-mail, bad signature) ×150: 120 through (401), 30 × 429 — address bucket |
| B2-shared | ✅ | right after, the colleague's call is 429: the forged calls spent the cabinet's minute |
| B3 | ✅ | `Until` 8 days away: « Rate-limit boost NOT applied: … is more than 7 days away », rate-boost null |
| B4 | ✅ | `Until` = start + 90 s: rate-boost 600 before, null after, same process |
| C1-plan | ✅ | « L'espace du cabinet ne suffit pas pour tous ces fichiers … » on the plan |
| C1 | ✅ | quota = used + 1,3 Mo (SQL on the throwaway DB): footer « L'espace de stockage du cabinet est plein … les fichiers restants attendent … » (`shots/run-1/C1-full.png`), 5 cards « en attente d'espace », 6 patients, 30 history lines, 5/18 files |
| C2 | ✅ | quota back to default, « Relancer l'import »: 18/18 files, 6 patients, no file twice |
| C3 | ✅ | Fatma and Ines Ben Ali, one phone +21698765432: both created |
| C4 | ✅ | plan at 320×640 · 390×844 · 820×1180 · 1180×820 · 1440×900 · 1536×730: no sideways scroll, « Importer » on screen (`shots/run-1/C4-plan-*.png`) |
| D1 | ✅ | colleague burst 150: 120 × 200, 30 × 429 « Trop de tentatives. Veuillez réessayer dans 5 minutes. » |
| D2 | ✅ | `/health` 200 while the cabinet is at its limit |
| D3 | ✅ | Fatma Ben Ali, « Ancien logiciel » tab: Diagnostic, Consultations (Détartrage, Obturation), Ordonnance (AMOXICILLINE), Radio (Panoramique) (`shots/run-1/D3-history.png`); folder holds PDF + 2 radios |

## Findings

| # | Severity | What | Where |
|---|---|---|---|
| 1 | minor | A lone « 0 » is printed above « Dossiers de radios et photos » on every plan with enough space — `{summary.quotaShort && …}` renders the number 0. Seen at 1440 and 390 px (`shots/run-1/C4-plan-1440x900.png`, `shots/run-1/B1-pause-390.png`). **Pre-existing**: on main since `a2a373bb` (#122), so live on prod | `web/components/legacy-import/import-screen.tsx:178` |

## Observations (off-plan)

- **The higher limit does not make the import faster today.** The server answers each import call in 10–60 ms
  (log: upload 57 ms, create 38 ms, lists ~10 ms), yet the page makes about 2 calls a second, one patient at a
  time: ~5 s per patient **with or without** the boost (A3 40 patients in 207 s; B1 15 in 88 s incl. a 15 s pause).
  It peaked at 131 calls/min — under prod's ordinary 600/min — so on prod the import alone would not reach the
  limit. What the boost buys is the cabinet's shared minute staying free for the other screens. Running several
  patients at once is what would make the import faster (and make the boost matter).
- A 429 from the API says « réessayer dans 5 minutes » while the API window is 1 minute: `OnRejected` falls back to
  the sign-in window (`RateLimiting.cs:291`) because a sliding-window lease carries no Retry-After. Pre-existing.
