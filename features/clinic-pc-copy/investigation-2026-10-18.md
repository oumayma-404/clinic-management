# « Mode hors ligne » — investigation of 2026-10-18

Reported by the owner, on her own PC de secours (DESKTOP-U8E2P9P) with Wi-Fi off: the search shows one patient,
opening a patient opens another one, « Remplir la fiche » does not open (and the page goes to the dashboard), a
past RDV is missing from « À clôturer », « Reste à payer » is missing, « other weird behaviours ».

## What each symptom was

| Symptom | Cause | How it was established |
|---|---|---|
| Search / opening a patient / « Remplir la fiche » land on one patient | `CarriedDraftResume` re-read the page load's memoised carried draft on **every** remount of the app shell (= every navigation), handed it back to the shell and reloaded its page — for the draft's whole 2 h. Triggered by ONE full page load while a carried form is open (the switch itself, F5, the shell reloading the PC) — the fiche carries the moment it opens | Reproduced on `d30df41c` in an isolated cloud + PC (`e2e/relay-roundtrip/run.sh`, `RT_STOP_AFTER=paired`), the PC's own web build behind its front door, the shell's slot emulated in Chrome: 3/3 rail clicks and 2/2 search results landed on the stuck patient |
| « … and goes to the dashboard » | Only the Windows app navigates to `pc.Origin + "/"`: when one « still holding? » check fails, or a PC page reports a failed load, it leaves the PC and comes back on « / ». Already `next-fixes.md` #21 / #22 | Code only; needs the real shell |
| « Reste à payer » missing | **Not a bug** — « Mode discret » was on from 2026-10-12 13:49 to 2026-10-15 12:17 and hides that tab by design | PC audit (`IsMoneyHidden: non → oui`, `oui → non`) + the PC log: « À clôturer » opened at 11:52, 11:57, 12:17 without `GetResteAPayerQuery`; present from 13:38 |
| Past RDV missing from « À clôturer » | Not reproduced: listed by the API and by the page during a cut. A séance appears once its **end** (start + duration) has passed | The four RDVs created on 15/10 are `AwaitingClosure`, with a patient and no fiche, in the PC copy |
| « Other weird behaviours » on her laptop | The 16/10 updates failed: `StopClinicServices` sent the web's STOP while the API — which depends on it — was still stopping, and the SCM refused it (`1051`, measured again during the repair); nssm never heard of it, the copy hit `node\node.exe` « Access is denied », Inno rolled back — which deletes new files and keeps overwritten ones. Result: the API on the 16/10 build, the web serving the 15/10 build from memory (`"b":"Dx5vh1h…"` vs `BUILD_ID z_JFWC5p…`), and 41 pages on disk pointing at a deleted chunk — a restart would serve 500 everywhere | `api\logs\relay-update-*.log`, Application event log (API stop at 16:25:11, no web stop), `scan-chunks` over `web\.next` |

| …and it had been wrong since 12/10 | Next keeps the pages it serves in `web\.next\server\route-cache`, keyed by the **route**, not the build, and an install only overwrites files — so all 24 main pages (login, dashboard, patients, À clôturer, agenda, caisse…) were still the **12/10** build's HTML and JavaScript after the 15/10 update and the 16/10 repair: the cabinet's 15/10 tests ran the 12/10 front-end against the 15/10 API. The hosted web never meets this (each deploy is a fresh container) | 146 files in `route-cache`, all carrying `"b":"Dx5vh1h…"`, whose `static/` folder is dated 12/10 11:42; after clearing it `/login` served `z_JFWC5p…` |

## What was done

| # | Fix | Where |
|---|---|---|
| 1 | The carried-draft resume decides **once** per page load, and only for a draft no form has claimed | `lib/forms/carried-draft.ts` `resumeTarget`, `components/relay/carried-draft-resume.tsx` |
| 2 | API → web → DB, **each stopped before the next**; leftovers ended by path; refuse before copying if one still runs | `clinic-setup.iss` `StopTiers` (install, uninstall) |
| 3 | The page cache is emptied on every install | `clinic-setup.iss` `[InstallDelete]` |
| 4 | Her laptop's PC de secours: the `d30df41c` installer re-run (SHA-256 checked against the VPS) after a real stop, then the stale page cache removed | done 2026-10-18 18:01 / 18:06, with her two UAC confirmations |

## Verification

- `e2e/specs/carried-draft-browser.spec.ts` (CARRY-01, CARRY-02) on the isolated rig: **both fail on `d30df41c`**, both
  pass with fix 1. The earlier walks pass too: list → patient, search → patient (also once « stuck »), « Remplir la
  fiche », « Reste à payer » within ~0.5 s of each load, a fiche + reload then three rail clicks all honoured.
- Fix 2's script, extracted from the `.iss` and run against stub processes: nothing running · a lingering web host and
  API (ended, nssm first) · a stuck database (never ended, the step fails) · a database not asked about (ignored).
  `lint-iss` and ISCC pass. The 1051 refusal itself was reproduced on her PC during the repair.
- `check:responsive` 81/81 · `tsc` · `next build` (relay settings). `check-coverage`: unchanged from `main` (its 64
  untested tier-0 rows predate this), CARRY-01/02 covered by a browser test.
- Not reproduced: « goes to the dashboard » (needs the real Windows app — `next-fixes.md` #21/#22) and « a past RDV
  missing from À clôturer ».

## Blast radius (written before the first edit)

| # | Touching | What it is | Other consumers | Verdict |
|---|---|---|---|---|
| 1 | `CarriedDraftResume` | the once-per-load redirect to a carried form's page | `app-shell.tsx` only (one mount per page) | must change — itself |
| 2 | `lib/forms/carried-draft.ts` — new `resumeTarget()` | page-load state beside `taken` / `pending` | `use-carried-draft.ts` (`takeCarriedOnce`, `claimCarried`) | unaffected — additive export, `pending` unchanged |
| 3 | the five carried forms (new RDV, RDV, patient, fiche, plan) | claim a draft through `useCarriedDraft` | — | must re-test — the designed resume (a switch reopens the form, once) |
| 4 | `StopClinicServices` → `StopTiers` (`clinic-setup.iss`) | stops API, web, database before the copy | `PrepareToInstall` (server role **and** relay role), `StopRelayAppServices` + `CurUninstallStepChanged` (uninstall) | must change — all three go through `StopTiers`; re-test: compile + the extracted script against stub processes (4 cases) |
| 5 | `DeinitializeSetup` | puts the services back after a failed install | every server / relay install | unaffected — still starts DB, web, API; a refusal now happens before any copy |
| 6 | `[InstallDelete]` `web\.next\server\route-cache` | a runtime cache, regenerated on demand | server role and relay role (the same `.iss`) | unaffected — only a cache; re-test: her PC served the current build once it was cleared |
