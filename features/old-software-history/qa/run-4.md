# Run 4 — re-run after the F-1 / F-2 fixes (PR #127), 2026-11-26 → 27

**Cycle:** 2 of at most 3
**Plan:** `qa/plan.md` + `qa/plan-unattended.md` (incl. run-3 additions and G0)
**Driver:** `walk.mjs` → `walk-unattended.mjs` → `walk-b1-large.mjs` → `api-checks.mjs` (same files as run 3, plus the probe fixes)
**Environment:** same isolated stack as run 3 on a **fresh** `clinic_pr123_qa` (dropped, re-provisioned), API unchanged
(web-only fix), `next dev` :3095 restarted on the fix. ⚠️ **This PC slept from 00:00:15 to 08:48** (System log
events 42 / 1) in the middle of G1.
**Peer sessions live:** clinic-management-d2, clinic-management-41, anakin-69 — none on these ports or this DB.
**Widths looked at:** 320×640 · 390×844 · 820×1180 · 1180×820 · 1440×900 · 1536×730 (C4); 390×844 (B1, B1-large)
**Status:** GREEN — every row ✅; G1's threshold missed for an environment reason (below), its purpose met

## Scenarios

| ID | Outcome | Evidence |
|---|---|---|
| A3-plan · C4 ×6 | ✅ | 40 · 120; no sideways scroll, « Importer » on screen, no stray « 0 » |
| A3 | ✅ | « Import terminé. » in **36 s** (run 3: 88 s, run 2: 195 s) · peak 310 / 60 s · 0 × 429 |
| A3-rate · A4 · A5 · A5-date · C3 · A6 | ✅ | colleague all 200 · 40 / 120 / 200 · CreatedAt noon Tunis · mother + daughter · relaunch 0 s |
| B1 | ✅ (probe fixed) | arranged burst → « Pause de 15 s … reprend tout seul », 3 × 429 absorbed, ended in 148 s, 15 · 45 |
| C1-plan · C1 · C2 · D3 · D3-files | ✅ | storage full stops files only; room again → 18/18; « Ancien logiciel » tab + folder |
| E1-plan · E2-warn | ✅ | 40 · 120 · card warns |
| E1 · E1-pause · E1-count · E1-rows · E1-sql | ✅ | « Import terminé. » in **56 s** despite 45 lost answers + 15 lost calls; nothing twice |
| E2 | ✅ (F-2 fixed) | card says « Créé sans un champ refusé », notes hold « Dossier créé le », birth 2023-06-01 |
| E3 | ✅ | 56 s with 60 faults (run 2: 195 s with none) |
| E4 · E5 | ✅ | relaunch 0 s / 0 patient calls · the vanished radio named on its row, 3 · 8/9 |
| G1-plan | ✅ | 300 à créer |
| **G0** | ✅ (F-1 fixed) | **11 991 elements** for the 300-dossier plan (run 3: ~390 000) |
| G1 | ✅ (environment) | the page was cut after **24** creates, not 100: its 15-min wait ran out during the PC's 8 h 48 sleep. The state the row exists for was produced — 1 patient without files, 3 without history |
| G2 | ✅ | whole folder again: 276 à créer · 24 déjà dans le cabinet |
| G3 · G3-rows · G3-sql | ✅ | « Import terminé. » in 767 s · 0 failed · **300 patients · 900 files · every one with PDF and history · nothing twice** |
| G3-heal | ✅ | the patients cut without files / history are complete |
| G4 | ✅ | 767 s ≈ 4 three-minute tokens: 8 renewals, 0 × 401, never signed out |
| G5 | ✅ | peak 252 calls / 60 s (prod limit 600) · 0 × 429 · 2.8 s per new patient incl. the re-check |
| E8 | ✅ | 408 « La connexion est trop lente … », WRN, no new ERR |
| B1-large | ✅ (probe fixed) | « Pause de 15 s … », 7 × 429 absorbed, 60 · 180 · 0 failed in 322 s |
| B2 · B2-shared · A4-wire · D1 · D2 | ✅ | unchanged from run 3 |

## Fixes applied (cycle 1)

| Finding | Verdict | Root cause | Files | Blast radius |
|---|---|---|---|---|
| F-1 blocker | confirmed | every « Dossiers de radios et photos » card mounted a Radix `Select` of every patient of the lot (Radix renders items while closed): 300 × 300 ≈ 390 000 nodes; the run's 500 ms repaint re-rendered all of them, unmemoised, with a new `onChoose` closure each time | `folder-choice-card.tsx` (list mounted on open, `memo`), `import-screen.tsx` (stable `chooseFolder`) | one consumer (`import-screen.tsx`); no test or walk drives that Select |
| F-2 minor | confirmed | `simplified` / `created` lived inside one `persist` attempt; a lost answer to the bare create made the next attempt « find » the patient | `run.ts` | the import page only |
| E3 / E5 / B1 | probe | stricter-than-plan threshold · nested `<li>` counted twice · the page now stays under the limit, so a 429 must be arranged | `walk-unattended.mjs`, `walk.mjs`, `walk-b1-large.mjs` | — |

**Guard added:** G0 — the plan's element count for 300 dossiers must stay under 60 000.
**Gates after the last edit:** check:responsive 91 ✅ · tsc ✅ · build ✅ (web-only; backend suite 7 357 ✅ on the API change in #125).
**Shipped:** PR #127, merged and deployed (run #97, 156f16a1) before this run finished; prod integrity GREEN.

## Observations (off-plan, not fixed)

- A « Créer quand même » row whose create answer is lost creates the patient twice (`run.ts` — that path skips the
  find on its first try and allows a duplicate on the retry). Code reading, not reproduced.
- The 429 sentence says « dans 5 minutes » for a 1-minute window (pre-existing, `RateLimiting.cs`).
