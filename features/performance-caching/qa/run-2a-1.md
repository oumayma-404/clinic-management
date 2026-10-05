# Run 2a-1 — one shared live connection per tab

**Date:** 2026-10-05 · **Branch:** `feature/performance-caching` (worktree, uncommitted 2a) · **Plan:** `plan-2a.md` · **Driver:** `walk-2a.mjs`

**Environment:** branch API `:5099`, worktree `next dev` `:3099`, own headless Chrome (`playwright-core`). Baseline
read-only on the peer's `:3000` → `:5000`, own context and sign-in. **Live peers:** `quick-fix-notes` (owns the
shared API/web/MCP browser — none restarted), `clinic-management-0b`.

## Scenarios

| ID | Result | Evidence |
|---|---|---|
| RT-0 | ℹ | old code: loading `/appointments` created **4** hub sockets; loading a patient page created **7** |
| RT-1 | ✅ | new code: loading `/appointments` created **1** |
| RT-2 | ✅ | sidebar « Patients » → « Liste d'attente »: **0** new sockets |
| RT-3 | ✅ | loading the patient page created **1** — `shots/RT-3.png` |
| RT-4 | ✅ | secretary's `/waiting-list` showed an entry created over the API (doctor's token) within 10 s, no reload, and dropped it within 10 s of the delete — `shots/RT-4.png` |
| RT-5 | ✅ | after sign-out, `/login` attempted **no** hub socket over 11 s (the old hook retried every 5 s with no session) |
| RT-6 | ⏭ | reconnect catch-up not exercised — needs the API taken down mid-walk; the `onreconnected` handler moved verbatim into the provider |

**Widths:** none — nothing visible changed.
**Mutations:** two waiting-list entries (`QA-2a …`), each created and deleted by the walk; SQL confirms none remain.

## Findings

None.

## Probe fixes made during the run (not product changes)

| What looked broken | Real cause |
|---|---|
| « 2 / 3 sockets open » after one page load | CDP reports no `webSocketClosed` for a socket torn down by a **full navigation**, so an « open » set accumulates across `page.goto` calls — the count is now sockets **created per load** |

## Observations (off-plan)

- The post-visit « Séance terminée » prompt opened over the secretary's waiting list during RT-4. It is the existing
  pending-review popup, not this change; the row behind it was present.

**Status: GREEN** (RT-6 not exercised)
