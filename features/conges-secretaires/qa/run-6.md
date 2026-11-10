# QA run 6 — Congés · « Droit à congé » (rule, starting balance, relevé)

**Date:** 2026-11-06 (14:50 Tunis)
**Why:** `droit-conges-blueprint.md` — « Droit annuel » became a rule + starting balance + days ± + relevé. Whole plan
re-run with A4/A16/A19 re-stated and seven rows added (K1–K7; named K so they do not collide with the existing G1).
**Environment:** the demo stack — API :5096 (worktree, migration `20261106133811_AddStaffLeaveAllowanceEntries`
applied to `clinic_pointage_demo` only) · web :3096 `next dev` · Chrome 9335 / 9336 (the owner's windows on 9333 / 9334
untouched). The owner's 2 congé requests set aside in `qa_keep_leaves` before the walk's reset.
**Status:** RED — 111 ✅ · 9 ❌ · 1 ⏭ (3 probe, 1 product, the rest following from it)

## Findings

### F1 — major · « Modifier la règle »: switching « par an » → « par mois » empties « À partir du », the rule is not saved

- **Repro:** her file → « Modifier la règle » → « par mois » → 2 → « Enregistrer la règle ».
- **Observed:** « À partir du » blank (`shots/K1-rule-dialog.png`); no toast; SQL: no monthly rule.
- **Cause:** the frequency swaps the Select's option list in the same render that sets the new date; Radix answers
  with `onValueChange("")`, which the dialog stored. Next to it, a latent one: the window re-seeded its fields on every
  refetch of the file (`file.balance.rule` in the effect's dependencies), so a background refresh would erase typing.
- **Owner:** `web/components/staff-leave/staff-leave-account-card.tsx` — `RuleDialog`.

## Probe bugs (fixed in walk.mjs, no source change)

| Row | Why it was red |
|---|---|
| A10, C5 | the new relevé's lines are `li` too — `card()` and the `main li` count now skip a line reading « solde N j » |
| A22 | the walk assumed today is 5 November; it reads the day now |
| K5 (first drive) | timed out opening the file right after C5's switch off → on; passed alone, and in the re-run |
| K1, K6 (next credit) | followed from F1 |

## Fixes applied (cycle 1)

| Finding | Verdict | Root cause | Files | Blast radius |
|---------|---------|------------|-------|--------------|
| F1 | confirmed | Radix `onValueChange("")` on a list swap stored as the date; fields re-seeded on every refetch | `staff-leave-account-card.tsx` (Select keyed by frequency, empty value ignored, seeded on open only) | the rule dialog only — the other three dialogs seed on `[open, today]`, which never changes while open |

**Gates after the last edit:** `tsc` clean · `check:responsive` 86/86 · unit suite unchanged (no backend edit).
K1 re-driven alone: « 1er … » shown, « Règle enregistrée », the monthly rule appended (SQL).
**Re-run:** run-7.md (RED on the walk alone: C5 pressed « on » before the switch was read and a « désactivées » toast satisfied `/activées/`, so every K row met congés off; A10's relevé filter used ``, which textContent defeats. Both probe fixes are in walk.mjs; the whole plan runs again as run 8).
