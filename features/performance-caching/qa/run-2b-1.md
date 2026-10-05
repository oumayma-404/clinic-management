# Run 2b-1 — the shared reference cache

**Date:** 2026-10-05 · **Branch:** `feature/performance-caching` (worktree, uncommitted 2b) · **Plan:** `plan-2b.md` · **Driver:** `walk-2b.mjs` (driven twice — the second drive is the one reported)

**Environment:** branch API `:5099`, worktree `next dev` `:3099`, own headless Chrome (`playwright-core`).
Baseline read-only on the peer's `:3000` → `:5000`. **Live peers:** `quick-fix-notes` (shared API/web/MCP
browser — none restarted), `clinic-management-0b`.

## Scenarios

| ID | Result | Evidence |
|---|---|---|
| C-0 | ℹ | old code: loading `/appointments` → **8** × `user-status`; two « Nouveau » dialog opens → **2** × `procedure-types` |
| C-1 | ✅ | new code: loading `/appointments` → **1** × `user-status` |
| C-2 | ✅ | the picker listed 44 acts on each of two opens; **1** × `procedure-types` over both — `shots/C-2.png` |
| C-3 | ✅ | sidebar « Patients » → « Rendez-vous »: **0** × `user-status`, **0** × `unread-count` |
| C-4 | ✅ | an act created over the API (admin) appeared in the secretary's open picker within 10 s, no reload; gone after the delete — `shots/C-4.png` |
| C-5 | ✅ | a file uploaded over the API appeared on the secretary's patient « Fichiers » tab within 10 s, no reload (it did not before the 2.4 fix), and left it after the delete |
| C-6 | ✅ | signed out and in as the secretary in the same tab: `user-status` read again for her, no « Factures » in the rail |
| C-7 | ✅ | patient page and devis workspace: no load-failure notice |
| C-8 | ⏭ | « Mode discret » not toggled — clinic-wide on a shared database with a live peer, and showing it again spends a TOTP code; its state machine is unchanged, only its fetch moved |
| C-9 | ⏭ | a new cabinet right after `/setup` not exercised — needs a fresh account; `hasClinic: false` has `staleTime: 0` by construction |

**Widths:** none — no layout changed.
**Mutations:** per drive, one act (`QA-2b acte …`) and one file (`qa-2b-….png`), each created and deleted by the
walk; SQL confirms none remain.

## Findings

None.

## Probe fixes made during the run (not product changes)

| What looked broken | Real cause |
|---|---|
| every row timed out on « Nouveau rendez-vous » | that `aria-label` is the phone's round « + »; at 1440 px the button reads « Nouveau » |
| C-5 answered 405 | the upload route is `…/files/upload`, not `…/files` |
| C-0 baseline « unavailable » | it threw on the dialog step before printing the load count; the two halves are now recorded separately |

## Observations (off-plan)

- `C-5.png` shows the top of the patient page; the asserted file row is further down the « Fichiers » tab.

**Status: GREEN** (C-8, C-9 not exercised)
