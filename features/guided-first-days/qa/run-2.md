# QA run 2 — Guided first days, release 1 (parts A + B)

**Date:** 2026-11-03
**Cycle:** 2 of at most 3
**Plan:** features/guided-first-days/qa/plan.md (34 scenarios: A18 B8 C5 D8) — the whole plan, unchanged
**Driver:** features/guided-first-days/qa/walk.mjs (log: `walk-run2.log`)
**Environment:** same isolated stack as run 1 — api :5097 (unchanged since run 1; the fixes are web-only) · web
:3097 `next dev` (hot-reloaded the two edited files; `npm run build` writes `.next/` beside dev's own `.next/dev`,
and `/login` answered 200 after it) · database `clinic_gfd_qa` · a fresh signed-up cabinet for this run
**Peer sessions live during the run:** as run 1 (clinic-management-29 busy on its own ports)
**Widths looked at:** 320 · 390 · 820 · 1180 · 1440 · 1536×730
**Status:** GREEN

## Scenarios

Every row of run 1 re-driven: **65 checks ✅, 0 ❌**, 2 ⏭ with the same reasons (B8 — the stack runs with an
Android link; D3 — `e2e/bootstrap.mjs` not run here). The two run-1 findings, re-looked at in the screenshots:

| Finding | Row | Now |
|---------|-----|-----|
| F-1 | C5 | « Ou, depuis le téléphone : télécharger » / « l'application » — wraps between words; shots/C5-phone-dialog-320.png |
| F-2 | C3 | « Masquer » top-right at 320, the title wraps to two lines instead; shots/C3-card-320.png, C3-card-1440.png |

## Fixes applied (cycle 1)

| Finding | Verdict | Root cause | Files | Blast radius |
|---------|---------|------------|-------|--------------|
| F-1 | confirmed | `break-all` on a link whose text is words (it suits a raw URL, not « télécharger l'application ») | `components/dashboard/phone-app-dialog.tsx` | one element, one consumer; the host `<span>` keeps `break-all` (it is an address) |
| F-2 | confirmed | the header row was `flex-wrap` with a title block that never shrank, so the BUTTON wrapped | `components/dashboard/getting-started-card.tsx` | one component, mounted once (`GettingStartedBlock`) |

**Guards added:** none — both are layout choices inside a new component, held by the C3/C5 eye pass rows.
**Gates after the last edit:** check:responsive 81/81 ✅ · tsc ✅ · build ✅ (no backend change → dotnet suite not re-run;
last run 6091 ✅ on `dba3bb8d`)
**Re-run:** this report — GREEN (65 ✅, 0 ❌, 2 ⏭ with reasons)

## Observations (off-plan, carried from run 1, not fixed)

- At 1536×730 the enrolment screen's « Confirmer » sits below the fold once a notice is shown; Enter submits and
  the page scrolls. Pre-existing screen height.
