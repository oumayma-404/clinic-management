# QA run 2 — treatment-plan lifecycle remediation (Phase 6 step 5, cycle 1 re-run)

**Date:** 2026-09-15 · **Plan:** [`plan.md`](plan.md) · **Driver:** [`walk.mjs`](walk.mjs) · **Predecessor:** [`run-1.md`](run-1.md)
**Status:** `GREEN` — 73 checks, **0 red**, 2 not exercised with reasons

## Environment

Same as run 1, with two changes worth recording:

| | |
|---|---|
| web :3000 | **rebuilt** after the fix and restarted — the standalone server had to be stopped first, because it holds `.next/standalone` and `npm run build` fails `EBUSY` on `rmdir` otherwise |
| API :5000 | **not restarted**, deliberately. The fix is frontend-only, and a peer session landed `20260914215715_DropClinicActivityCollectedThisMonth` + an edit to `GetPlatformSummaryQuery.cs` at ~22:57. That migration is **unapplied** and unrelated to anything under test, so restarting would have applied a peer's migration behind their back (`shared-stack.md` § 3) |
| Widths | 320 · 390 · 820 · 1180 · 1440, plus 1536 × 730 |

## Result

| Tier | Result |
|---|---|
| A — happy path | all green, including the five new `A3x` checks the fix made assertable |
| B — alternate paths | all green; B4 not exercised (its devis takes the arrêt branch, which asks for no motif — B1 covers the cancel branch) |
| C — edge cases | all green, C3·targets now measured on a **real** coarse pointer |
| D — regression sweep | all green — no console error and no horizontal overflow on any swept screen |

### Not exercised

| id | Why |
|---|---|
| B4 | the devis holding money takes the **arrêt** branch, which requires no motif; the cancel branch's motif rule is B1, green |
| A4c | « Créances » pages and the plan was not on page 1; the reopened balance was verified on the wire instead (A4b, green) |

---

## Fixes applied (cycle 1)

| Finding | Verdict | Root cause | Files | Blast radius |
|---|---|---|---|---|
| **F1** (major) | **confirmed** | The server sorts a stop into **three** outcomes — cancel · stop · *refuse until refunded* — and the browser knew two. `stopWouldCancelPlan`'s money term pushes a deposit-carrying devis off the cancel branch onto a stop, and nothing then asked whether that stop can land | `plan-next-action.ts` (+`stopNeedsRefundFirst`), `plan-workspace.tsx` (title · description · body · footer) | `stopWouldCancelPlan` has 2 consumers; `plan-workspace.tsx` is the **only** surface offering the stop — greps in the table below |
| **F2** (minor) | **deliberate** | `RespreadSchedule` keeps every collected row, trimmed to what it actually took, and re-marks it `IsAutoRaised` — its own comments say why, at length: trimming preserves the payment evidence and stops a paid row being promoted to an « agreed » date wearing « En retard ». `Σ Amount == TotalPlanned` holds and `reconcile-money` is green | — | reported, not changed |
| **F3** (minor) | **already correct** | `PatientNameLink` carries `coarse:min-h-11` and measures **exactly 44 px** on a real coarse pointer. My probe's CDP `Emulation.setEmulatedMedia` never engaged — `matchMedia("(pointer: coarse)").matches` stayed `false` with and without `media: "screen"` — so every `coarse:` rule was off and all seven "violations" were fine | — | probe corrected in `walk.mjs` |

### F1 — blast radius

| # | Touching | What it is | Other consumers | Verdict |
|---|---|---|---|---|
| 1 | `stopNeedsRefundFirst` | **new** predicate | none yet | unaffected — additive |
| 2 | `stopWouldCancelPlan` | shared predicate | `canCancelPlan` (same file), `plan-workspace.tsx` | unaffected — **not modified**, only read alongside |
| 3 | the stop dialog | one surface | `rg "setStopOpen\|Arrêter le traitement" --glob '*.tsx'` → only `plan-workspace.tsx` (the two other hits are a `caisse` local variable of the same name and two comments) | must change — changed here |
| 4 | `canCancelPlan` | gates « Annuler le devis » | the ⋯ menu, `treatment-plans-table` | unaffected — its `amountPaid <= 0.0005` term is untouched |

**Guard added:** `check:responsive` **N40** now fails any surface that reads `stopWouldCancelPlan` without also
reading `stopNeedsRefundFirst`, and requires the owner to export all five predicates. Comments are masked on
both sides — a first cut tested the raw source and my own explanatory prose (« see `stopNeedsRefundFirst` »)
satisfied it, so the guard passed over a file with the call deleted. **Proven red on a deliberate violation,
green after revert.**

**What the fix does, in words.** On a numbered devis carrying money with no act realised, the ⋯ menu still
offers « Arrêter le traitement » — a named refusal, never a withheld control, on M26's rule — and the dialog
now reads:

> **Remboursez d'abord 200,000 DT**
> Aucun acte de ce devis n'a été réalisé, mais **200,000 DT y ont déjà été encaissés**. Il n'y a donc rien à
> mettre de côté et rien à conserver — et cet argent ne peut pas être effacé d'une journée de caisse déjà
> close. Remboursez-le par un **avoir**, puis revenez arrêter le traitement.

with « Retour » as the only control. The server rule is unchanged: it was deliberate and documented
(`TreatmentPlan.cs` — « sending the dentist there would name a remedy the product would then refuse, which is
the defect shape this audit found four times »). This was that shape, one door over.

**Gates after the last edit:** `check:responsive` **70/70** ✅ · `tsc --noEmit` ✅ · `npm run build` ✅ ·
`dotnet test -c Release` **4710 / 0 / 6** ✅

---

## Probe bugs fixed between the two runs (not product, not counted)

Eleven in total. Three are worth carrying into the repo's own notes, because each produced a confident false
defect report and each is a trap this codebase can hit again:

| Symptom | Actual cause |
|---|---|
| every row after C3 landed on `/login` with a 401 — **six** "product defects" | the touch context reused the walk's `storageState`. **Refresh rotation IS the theft detection**, so the second context signed the first out. `signIn({ name })` mints a separate family now |
| 7 controls "under 44 px" on a coarse pointer | **CDP `Emulation.setEmulatedMedia` does not deliver `(pointer: coarse)`** — `matches` stayed `false`. `hasTouch: true` on the **context** is what does, and a context's touch flag cannot be changed after creation, hence a second context for that one measurement |
| every page replaced by the error boundary, `TypeError: s.find is not a function` | my `pending-reviews` stub returned `{value: []}`; the client expects a **bare array**. One wrong stub shape reads as a total product collapse |
| five tier-D rows died at once | `page.screenshot` timed out at 30 s on a page holding a running animation. `animations: "disabled"` + a capped timeout, and a failed screenshot must never end a row |
| the ⋯ menu "unclickable" on half the plans | the post-visit review modal swallowing clicks, and its own filled « Ajouter le dossier médical » polluting every "what is the primary action?" reading |
| the list row offered « Modifier le brouillon » | `?search=` is discarded — `useUrlFilters` writes and never reads — so `.first()` was a different plan |
| "no Remise control on an act row" | the button's `aria-label` (« Accorder une remise sur … ») overrides its visible word « Remise » |

Three **plan rows** were wrong rather than the code, and were corrected rather than "fixed":
« Facturer le devis » on a `Completed` plan (`canBillPlan` — a plan auto-completes on its last step, so gating
on active withdrew the button exactly when the treatment became billable), « Modifier le brouillon » on a
stepless followed draft (`canUseDraftEditor`), and « irréversible » demanded on the **arrêt** branch, where
nothing irreversible happens — that word is C2's requirement for the **cancel** branch.

## Observations (off-plan, not fixed)

- The séance progress bar fills its first segment at 0 done (green = done, orange = next), so a cancelled
  0-of-1 plan draws a fully orange bar. Consistent with its own vocabulary; noted because it reads as
  « complete » at a glance.
- F2 above is a reading question, not an arithmetic one: two undated « Solde à régler » cards for one debt
  after a stop→reopen, one of them a settled receipt. The label keys purely on `isAutoRaised`. Left alone —
  the wording has one owner (`installmentDueLabel`/`Sentence`/`Title`) and changing it is the owner's call.

## Status

`GREEN`. Cycle 1 closed — no cycle 2 needed.
