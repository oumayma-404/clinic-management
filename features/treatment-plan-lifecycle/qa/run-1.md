# QA run 1 — treatment-plan lifecycle remediation (Phase 6 step 5)

**Date:** 2026-09-15 · **Plan:** [`plan.md`](plan.md) · **Driver:** [`walk.mjs`](walk.mjs) (re-runnable)
**Status:** `RED (3 findings)` — 0 blocker · 1 major · 2 minor

## Environment

| | |
|---|---|
| API | :5000, rebuilt from this working tree, `AddTreatmentPlanDiscountAndWriteOff` applied. ⚠️ The API that was running was **5 h stale** — it predated every Phase 1–5 backend edit. Stopped by PID (owner session `qa-plan-acts` was gone), rebuilt, restarted, lease recorded as `phase6`. |
| web | :3000, `.next/standalone/server.js` from the build that passed the gate — not `next dev`, not `next start`. |
| Account | the e2e QA admin, clinic `f64a8a75…` |
| Test data | minted by the walk: 17 plans on 17 throwaway `QA-PHASE6 <run>-*` patients. **Nothing in the shared 1366-patient dataset was written.** |
| Live peers | `clinic-management-71` (shell), 6 idle. None held api/web/browser. |
| Widths looked at | **320 · 390 · 820 · 1180 · 1440**, plus **1536 × 730** (the owner's real laptop) |

⚠️ **One environment fact worth putting in `e2e/README.md`:** serving `web` on **:3100**, which that README
recommends to dodge the contested :3000, makes **every** browser row fail with « Connexion au serveur
impossible ». `FrontendUrl` is `http://localhost:3000` and `Cors:AllowedOrigins` is empty, so the API refuses
the origin. It presents as a total product collapse and is a config fact.

## Result

**65 checks green · 3 red · 4 not exercised** out of the 43 planned rows (a row can carry several checks).

| Tier | Covered | Notes |
|---|---|---|
| A — happy path | 18/18 | A5 red (see F1) |
| B — alternate paths | 10/10 | B4 not exercised — its devis takes the arrêt branch, which asks for no motif; B1 covers the cancel branch |
| C — edge cases | 6/6 | C3 red (see F3) |
| D — regression sweep | 9/9 | all green, no console error on any swept screen |

### Not exercised, with the reason

| id | Why |
|---|---|
| B4 | the devis holding money takes the **arrêt** branch, which requires no motif — the cancel branch's motif rule is covered by B1 |
| A4c | « Créances » pages, and the plan was not on page 1; the balance was verified on the wire instead |
| A14 (server) | re-verified per run; the write-off itself is green |
| C3·targets | one control is reported — see F3 |

---

## Findings

### F1 · **major** — « Arrêter le traitement » is offered and fully described on a devis the server then refuses

**Repro**
1. A numbered devis, **nothing delivered**, carrying a payment (200,000 DT on a 720,000 DT devis).
2. Workspace → ⋯ → « Arrêter le traitement » — the entry is offered.
3. The dialog lists the acts under **MIS DE CÔTÉ**, states « Ce qui a déjà été fait est conservé, et le
   traitement passe à « Arrêté » », and shows « L'échéancier est ramené au total conservé (0,000 DT) ».
4. Press « Arrêter le traitement ».

**Expected** — either the plan reaches `Stopped`, or the action is not offered / the dialog says up front that
an avoir is required first.

**Actual** — the request is refused, the plan **stays `InProgress`**, and the toast reads:

> Aucun acte de ce devis n'a été réalisé, mais 200.000 DT y ont déjà été encaissés. Remboursez-les par un avoir
> avant de clôturer ce devis.

**Second manifestation (A5).** On the same shape with 150,000 DT collected, the arrêt dialog — the **only**
door the ⋯ menu offers, since `canCancelPlan` withholds « Annuler le devis » wherever the stop branch can
reach — never mentions the money at all and never says « irréversible ». So the one sentence that would have
explained the refusal is absent from the one dialog the dentist reads.

**Why this is the interesting one.** The server side is deliberate and documented in as many words
(`api/ClinicManagement.Domain/Entities/TreatmentPlan.cs:983-1000`): « sending the dentist there would name a
remedy the product would then refuse, which is the defect shape this audit found four times ». The frontend is
one door over from the case that note is about — `StopWouldCancel`'s money term correctly routes this devis to
the *stop* branch, and nothing then checks that the stop can actually land.

**Evidence** — `shots/A3x-dialog.png`, `shots/A3x.png`, `shots/A5.png`; walk rows `A3x`, `A5`.
**Suspected owner** — `web/components/treatment-plans/plan-next-action.ts` (`canStopPlan`) and the stop dialog
in `web/components/treatment-plans/plan-workspace.tsx`. The server rule stays as it is.

---

### F2 · **minor** — a stop followed by a « Reprendre le traitement » leaves **two** « Solde à régler » rows

**Repro**
1. Numbered devis, 400,000 DT, one act carried out, 100,000 DT collected.
2. « Arrêter le traitement » — lands on `Stopped`, the échéancier is re-spread to the kept total.
3. « Reprendre le traitement » — the parked act is restored and `totalPlanned` returns to 400,000 DT. ✅

**Actual** — the échéancier now holds **two auto-raised rows**, both rendering as « Solde à régler » with no
date:

```
f8e89d99   amount 100   paid 100   isAutoRaised true
dcff859e   amount 300   paid   0   isAutoRaised true
```

**Expected** — one container row of 400,000 DT with 100,000 DT against it. The arithmetic is right either way
(total 400, paid 100, outstanding 300) and `reconcile-money`'s `plan-schedule-balances` is green, so **no money
is wrong** — but « L'argent » shows the patient two identical undated cards for one debt.

**Evidence** — `shots/eye/stoppable-argent2-320.png`; wire read in the report above.
**Suspected owner** — the reopen path raising a fresh container row rather than folding back into the one
`Accept` raised (`ReopenTreatmentPlanCommand` / `TreatmentPlan.Reopen`).

---

### F3 · **minor** — the patient link in the workspace identity line is 20 px on a coarse pointer

Measured at 320 px under a real `pointer: coarse` emulation (CDP `Emulation.setEmulatedMedia`), on the plan
workspace: `QA-PHASE6 <run>-cancelled` — the patient name, an `<a>` — paints 20 px tall and carries neither
`touch-target` nor a `coarse:` minimum. Every other control on the screen is clean: the 32 px header buttons
all carry `touch-target`, whose `::after` is the real 44 px hit area.

AC-16 says every control clears 44 px on a coarse pointer. A link inside a heading line is the arguable case,
which is why this is minor rather than major.

**Evidence** — walk row `C3·targets`; `shots/C3-cancelled-320.png`.
**Suspected owner** — the identity line in `web/components/treatment-plans/plan-workspace.tsx`.

---

## What went green, and is worth naming

- **The whole lifecycle, end to end:** stop → `Stopped` → « Reprendre le traitement » as the filled primary →
  reopen → parked acts restored and `totalPlanned` back to its pre-stop figure (A3 · A3b · A4 · A4b · B10).
- **The ⋯ menu** carries all six expected entries and **no** « Terminer le traitement » (AC-11).
- **S1–S4 all land through the product:** duplicate → `Draft`, no number, all acts carried (A11) · a remise
  drops the devis total by exactly the remise (A12) · « Régler le devis » takes « Reste » to 0 (A13) ·
  « Passer la créance en perte » → `WrittenOff`, confirm disabled until a motif is typed (A14).
- **The 409 offers « Recharger »** (C1) — the poisoned-dialog trap is closed on this form.
- **An auto-raised row reads « Solde à régler », with no date and no « En retard »** (B5, AC-8/AC-9), and names
  « Modifier l'échéancier » as the route to a real schedule (B5c, AC-13).
- **The cancel branch requires a motif**, its field carries the `aria-describedby` hint, and nothing-delivered
  on a numbered devis lands on `Cancelled` (B1 · B1b · B1c, AC-6 · m12).
- **A `WrittenOff` and a `Cancelled` devis are both absent from « Créances »** (D2x · D2y).
- **The regression sweep is clean:** la caisse, Créances, the patient page, the dashboard,
  `/traitements-en-cours` and the odontogramme all render with **no console error and no horizontal
  overflow**, at every width.
- **No horizontal overflow anywhere**, at 320 / 390 / 820 / 1180 / 1440 and 1536 × 730.

## Observations (off-plan)

- The séance progress bar fills its **first** segment on a plan with 0 séances done — green = done, orange =
  next — so a cancelled 0-of-1 plan draws a fully orange bar. Consistent with its own vocabulary; noted only
  because it reads as « complete » at a glance.

## Probe bugs fixed during the run (not product, and not counted)

Six, each traced to source before a line of product code was looked at — worth recording because three are
this repo's own documented traps:

| Symptom | Actual cause |
|---|---|
| every page replaced by the error boundary, `TypeError: s.find is not a function` | my `pending-reviews` stub returned `{value:[]}`; the client expects a **bare array**. One wrong stub shape read as a total product collapse |
| 7 controls « under 44 px » on a coarse pointer | `touch-target` grows the **hit area** through `::after` and paints nothing — `getBoundingClientRect()` can never see it |
| the ⋯ menu « not clickable » on half the plans | the post-visit review modal was swallowing clicks, and its own filled « Ajouter le dossier médical » was polluting every « what is the primary action? » reading |
| five tier-D rows died at once | `page.screenshot` timed out at 30 s on a page holding a running animation; `animations: "disabled"` fixes it, and a failed screenshot must never end a row |
| « no Remise control on an act row » | the button's `aria-label` (« Accorder une remise sur … ») overrides its visible word « Remise » |
| the list row offered « Modifier le brouillon » | `?search=` is discarded — `useUrlFilters` writes and never reads — so `.first()` was a different plan entirely |

Two plan rows were wrong rather than the code: « Facturer le devis » on a `Completed` plan and « Modifier le
brouillon » on a stepless followed draft are both documented-deliberate (`canBillPlan`, `canUseDraftEditor`).

## Status

`RED (3 findings)` — 1 major, 2 minor. Next: `/fix-qa-findings` with this report.
