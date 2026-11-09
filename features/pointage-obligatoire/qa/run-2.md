# QA run 2 — Pointage obligatoire (part 3)

**Date:** 2026-11-06 (09:29 Tunis)
**Cycle:** 2 — re-run after `/fix-qa-findings` cycle 1 (F1)
**Plan:** `features/pointage-obligatoire/qa/plan.md` — all 34 scenarios, same `walk.mjs` (no row removed; A9 gained the
arrival toast check, A10's confirmation check moved from a toast to the dialog)
**Environment:** isolated — API :5096 (unchanged backend, mine) · web :3096 `next dev` (restarted after the fix, mine)
· DB `clinic_pointage_qa` · Chrome 9333 / 9334 · peers as in run 1, none on these ports or this DB
**Status:** RED (1 finding — introduced by the cycle-1 fix, caught by the eye pass)

## Scenario results

102 ✅ · 0 ❌ · 0 ⏭ — every row of run 1 green, including the two rows the fix reaches:

| ID | Outcome | Evidence |
|----|---------|----------|
| A9 | ✅ | the lock's pad punch now toasts « Arrivée enregistrée à HH:mm. » and the lock closes |
| A10 | ✅ (script) | « Départ enregistré · 09:33 » readable in the dialog before sign-out — `shots/A10-departure-confirmed.png` |

## Findings

### F2 · minor · the confirmation sits under a header that is no longer true
- **Repro:** as A10 — Ines on the borne device, present → « Se déconnecter » → draw the schéma.
- **Expected:** once the departure is recorded, the dialog says so, and only so.
- **Actual:** the body reads « Départ enregistré · 09:33 » while the header still asks « Pointer votre départ ? » and
  states « Vous êtes présente. » — the punch's `team` broadcast refreshed her status under the open dialog
  (`presentSince` → null) and the description fell back to a sentence that is now false.
- **Owner:** `web/components/team/sign-out-prompt.tsx` — the header ignores `departedAt`, and the « Vous êtes
  présente. » fallback can only ever render once she is no longer present.
- **Evidence:** `shots/A10-departure-confirmed.png` (1536×730).

## Fixes applied (cycle 1)

| Finding | Verdict | Root cause | Files | Blast radius |
|---------|---------|------------|-------|--------------|
| F1 | confirmed | `logout()` navigates (`window.location.href`) in the same tick as `toast.success`, so the toast dies with the page | `sign-out-prompt.tsx` (confirmation in the dialog, sign-out after 2 s; closing it still signs out) · `clock-in-gate.tsx` (the lock's punch toasts « Arrivée enregistrée à HH:mm. », the one other punch with no word) | 2 consumers (`dashboard-header`, root layout), both re-run |

**Gates after the last edit:** check:responsive 86/86 · tsc (source) clean · build green — after clearing a half-written
`.next/dev/types/validator.ts` left by my own `next dev` (environment, not source).
