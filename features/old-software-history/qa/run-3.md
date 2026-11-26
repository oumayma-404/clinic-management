# Run 3 — the unattended import (PR #125), on main @ `8d42f366`, 2026-11-26

**Cycle:** 1 of at most 3 (first run of `plan-unattended.md`; runs 1–2 were PR #123)
**Plan:** `qa/plan.md` (regression) + `qa/plan-unattended.md` incl. « Run 3 additions » (G1–G5, E8)
**Driver:** `walk.mjs` → `walk-unattended.mjs` → `walk-b1-large.mjs` → `api-checks.mjs`
**Environment:** isolated — API :5095 built from main (Release, scratchpad), DB `clinic_pr123_qa` (fresh), bucket
`pr125-qa`, own key ring, console off, ordinary limit 120/min, boost `import@pr123.test` 600/min, **access tokens
3 min**; `next dev` :3095 from the worktree (detached at `8d42f366`); headless Chrome (playwright-core 1.56.1).
Fake data only (`gen-data.mjs`, batches A–H). Nothing on prod, nothing on the shared DB.
**Peer sessions live during the run:** clinic-management-d2 (shell), clinic-management-41 (idle), anakin-69 (idle) —
none on :5095/:3095 or this DB.
**Widths looked at:** 320×640 · 390×844 · 820×1180 · 1180×820 · 1440×900 · 1536×730 (C4); 390×844 (B1-large)
**Status:** RED (2 findings: 1 blocker, 1 minor) — plus 3 probe fixes and 1 row not exercised

## Scenarios

| ID | Outcome | Evidence |
|---|---|---|
| A3-plan | ✅ | 40 à créer · 120 fichiers |
| C4 ×6 widths | ✅ | no sideways scroll, « Importer » on screen, no stray « 0 » |
| A3 | ✅ | « Import terminé. » in **88 s** (run-2: 195 s) · 294 calls · peak 217 / 60 s · 0 × 429 |
| A3-rate / A4 / A5 / A5-date / C3 | ✅ | colleague 88 calls all 200 · 40 · 120 · 200 · CreatedAt 2015-03-07 noon Tunis · mother + daughter both created |
| A6 | ✅ | relaunch in 0 s, counts unchanged |
| B1 | ⏭ | not exercised — batch B (15) ends in 27 s with fewer calls than the 120 limit; re-driven as B1-large |
| B1-large | ⏭ | 60 patients · 180 files · 0 failed · 286 s — but **no 429 ever came**: the page paces itself under the limit, so the « Pause de N s » path was not reached |
| C1-plan / C1 / C2 | ✅ | space warning · storage full stops files only · room again → 18/18, nothing twice |
| D3 / D3-files | ✅ | « Ancien logiciel » tab lines · folder holds PDF + 2 radios |
| E1-plan / E2-warn | ✅ | 40 · 120 · the card warns before the run |
| E1 | ✅ | « Import terminé. » in 156 s despite **46 lost answers + 15 lost calls**; footer showed « Connexion perdue … reprend tout seul » and « N sur 40 » |
| E1-rows / E1-sql | ✅ | 0 failed rows · 40 · 120 · 200 history · nothing twice |
| E2 | ❌ F-2 | data right (birth 2023-06-01, « Dossier créé le … 26/05/2022 » in the notes) but the card never says « Créé sans un champ refusé » |
| E3 | ✅ (probe fixed) | 156 s **with 61 faults** vs run-2's 195 s with none — the probe's « < 120 s » was stricter than the plan's « well under 195 s »; the no-fault figure is A3: 88 s vs 195 s |
| E4 | ✅ | relaunch after a full run: 0 s, 0 patient calls |
| E5 | ✅ (probe fixed) | footer « 1 ligne incomplète », 3 patients, 8/9 files; the probe counted 2 rows because the card's error list is a nested `<li>` |
| G1-plan | ✅ | 300 à créer |
| G1 | ❌ F-1 | the cut was due after the 100th create; after **15 min only 16 patients** had been created (no probe activity in the page during that wait) |
| G2 | ✅ | whole folder again on a new page: 284 à créer · 16 déjà dans le cabinet |
| G3 / G3-rows / G3-sql / G3-heal | ❌ F-1 | the second page re-checked the 16 (23:24–23:26), then **sent nothing at all** from 23:26:27; renderer at **4.5 GB and ~2.8 cores**; stopped by hand at 23:29 |
| G4 / G5 | ⏭ | not exercised — G3 never ran long; token renewals during G1 all 200 (23:09, 23:11, 23:17, 23:21) |
| E8 | ✅ | 408 « La connexion est trop lente … Réessayez. », WRN « Request refused before reaching a handler (408) », no new ERR |
| B2 / B2-shared / A4-wire / D1 / D2 | ✅ | forged token 120 × 401 + 30 × 429 · colleague then 429 · import 150/150 · burst 118 + 30 × 429 · /health 200 |

## Findings

### F-1 · blocker · a folder of 300 dossiers makes the page crawl, then freeze

- **Repro:** import account → « Ancien logiciel » → pick batch G (300 PDFs + 600 radios) → Analyser → Importer.
- **Expected:** the 6-lane pace seen on 40 dossiers (A3: 40 in 88 s); a page that keeps sending.
- **Actual:** ~1 patient a minute (16 in 15 min, API log: 0–3 « POST api/Patients » per minute); on a second page
  with the same 300-card plan, no call at all after 2 min, not even the 10 s connectivity ping; the tab's renderer
  at 4.5 GB and ~2.8 CPU cores. 40-card plans (A, E) and a 60-card plan (H) run normally.
- **Evidence:** `qa-api.log` 23:08–23:29 per-minute counts; `Get-Process` on the renderer (4 474 → 4 561 MB,
  +14 CPU-s in 5 s); `walk-run3.log`.
- **Why it matters:** TDental's real folder is ~1 000 dossiers, and prod's run stopped twice today mid-patient with
  no server error (14:22, 16:38).
- **Suspected owner:** client-side cost growing with the plan size — `web/components/legacy-import/import-screen.tsx`
  (`results` state, 300 `PatientRow`s) and/or `web/lib/legacy-import/run.ts`. Not diagnosed yet.
- **Regression?** Unknown — the 300-dossier case was never tested before.

### F-2 · minor · « Créé sans un champ refusé » is lost when that create's answer is lost

- **Repro:** E2 under E1's faults: the first create is refused for the birth date; the bare create **arrives**
  (API 201 at 23:04:46) but its answer is lost; the retry finds the patient (GET 23:04:48) and returns `created: false`.
- **Expected:** the card says « Créé sans un champ refusé … » and counts the patient as created.
- **Actual:** no such line on the card; the patient counts as « found ».
- **Evidence:** `qa-api.log` 23:04:37 400 → 23:04:43 400 → 23:04:46 201 → 23:04:48 GET; SQL notes hold the kept line.
- **Suspected owner:** `web/lib/legacy-import/run.ts:460-476` — `simplified` and `created` live in one attempt only.
- **Data:** correct either way.

## Probe fixes (not product)

| Row | Cause | Fix |
|---|---|---|
| E3 | threshold 120 s, plan says « well under 195 s », and the run carried 61 injected faults | judge against the plan |
| E5 | `li` filter matched the card AND its nested error `<li>` | count cards only |
| B1 | batch B is too small to cross 120 / 60 s with the new page | B1-large (60); still no 429 — the page stays under the limit — needs an arranged 429 |

## Observations (off-plan)

- The page's own pace for an unboosted account stayed under the 120 / 60 s limit on 60 dossiers (286 s, 0 × 429).
- 429 sentence still says « dans 5 minutes » for a 1-minute window (pre-existing, `RateLimiting.cs`).
