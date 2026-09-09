# treatment-plan-lifecycle — progress

**Session 1 — 2026-09-09.** Backend and frontend complete — the lifecycle fold, the visual restructure, and the
**full gate including the eye pass and the e2e suite**. Two defects of my own were found by that verification and
fixed; both are recorded below, because both are shapes this repository already has guards for.

## Decisions taken (not deferred back to the owner)

- **Option B, not Option A.** The teardown offered three depths of cut. B — `Stopped` as its own status, the
  verbs folded, the auto-raised échéance stripped of its fabricated date — delivers everything that was
  reacted to with **no migration and no change to any money read**. Option A (debt on a *delivered* basis)
  moves reported receivables, which is a management decision rather than a consequence of a UI pass. B is a
  strict step toward it. Recorded in `spec.md` § Out of Scope.
- **A kept-but-unfinished act stays billed in full.** A four-séance implant stopped after two is kept at
  1 500 DT. That is what the aggregate does; changing it is a money decision. It is now *said out loud* — the
  stop dialog names what is kept and at what price — and pinned by
  `TreatmentPlanStopAndReopenTests.A_kept_act_is_kept_at_its_full_price`.

## ⚠️ One dangerous error caught in this session's own spec, before any code

The first draft of § Échéancier said `Accept` should stop raising the container installment. **That would have
emptied the entire receivables ledger.** « Créances », « Solde patient », la caisse and the dashboard do *not*
read `TreatmentPlan.Outstanding` (`TotalPlanned − AmountPaid`); they sum `i.Amount - i.AmountPaid` **over the
installment rows** (`TreatmentPlanRepository.GetInstallmentOutstandingByPatientAsync:711-718`). Delete the row
and every accepted devis reports a zero receivable, with no error anywhere. The row stays; only its
*rendering* changes. The spec now carries the measurement so nobody removes it again.

## Shipped

### Domain
| File | Change |
|---|---|
| `Enums/TreatmentPlanStatus.cs` | `Stopped = 5`, appended. **No migration** — the column is already `integer` with no check constraint. |
| `Services/TreatmentPlanLifecycle.cs` | `IsLive` reads `LiveStatuses` instead of « not Cancelled and not Completed ». That phrasing read the new member as **live**. |
| `Services/PlanBillingRules.cs` | `Stopped` added to `DebtBearingPlanStatuses`; `CarriesDebt` is now an **exhaustive switch with no `_` arm**, so the compiler names the file on the next appended member. |
| `Entities/TreatmentPlan.cs` | `StopTreatment` writes `Stopped` (was `Complete(leaveUnrealisedActs: true)`) · `Reopen` accepts `Completed` **and** `Stopped` · `EnsurePayable` and `EnsureCorrectable` admit `Stopped` · new `StopWouldCancel`. |
| `Services/InstallmentLateness.cs` | An auto-raised row's own `dueDate` is **never consulted**. Late iff the work is finished and the balance unpaid. |

### Application
| File | Change |
|---|---|
| `Features/TreatmentPlans/TreatmentPlanBridgeRelease.cs` | **New.** The bridge-invoice detach, extracted from `CancelTreatmentPlanCommand` so the stop's cancel branch shares it rather than copying it. |
| `Commands/CancelTreatmentPlanCommand.cs` | Calls the shared helper. |
| `Commands/StopTreatmentPlanCommand.cs` | Optional `Reason`; branches on `plan.StopWouldCancel` to cancel (motif required) or stop. `SetExpectedVersion` on **both** paths. `ArgumentException` caught so a blank motif is not flattened. |

### Infrastructure
`TreatmentPlanRepository.GetRecallPlanFactsAsync` filters through `LiveStatuses` instead of a retyped
exclusion, which would have chased a treatment the patient had already abandoned.

### Tests — 4474 pass, 0 fail (18 new, 2 re-pointed)
- **`TreatmentPlanStatusCoverageTests`** (new, 7) — the derived guard. Enumerates the enum and fails when a
  member is classified by neither `LiveStatuses` nor `DebtBearingPlanStatuses`; also pins the SQL filter and
  the in-memory test against each other in both directions.
- **`TreatmentPlanStopAndReopenTests`** (new, 11) — the transition had **zero** domain coverage before this.
- `FollowedTreatmentLifecycleTests` — the intermediate status assertion re-pointed at `Stopped` (kept, not
  deleted: the transition it pins is the same one) and `Stopped` added to the `IsLive` Theory.
- `InstallmentLatenessTests` — `Today_And_Later_Are_Never_Late` split; the auto-raised half became
  `An_Auto_Raised_Row_Ignores_Its_Own_Date`, which states the new rule directly.

### Frontend
| File | Change |
|---|---|
| `plan-next-action.ts` | `isPlanLive` is a **positive list**; new `isPlanStopped`. |
| `treatment-plan-labels.ts` | `Stopped: "Arrêté"`, tone `negative`. The list's `?status=` filter picks it up automatically (it validates against `PLAN_STATUS_LABELS`). |
| `plan-workspace.tsx` | Menu **7 → 5**. « Terminer » removed, « Annuler » folded into the stop (branch derived from the plan, motif in the same dialog). `primaryAction` tests **Stopped before `canBill`** — the ordering that made « Reprendre » unreachable — and withholds « Facturer » while any act is unrealised. Dead code removed: `confirmAccept`, `confirmComplete`, `canCancel`, the `isBeforeToday` import. |
| `plan-workspace.tsx` (échéancier) | An auto-raised row renders « **Solde à régler** » with **no date** (new `InstallmentDueCell`), in the table, the card tree and the row's `aria-label`. |
| `plan-act-row.tsx` | The mute « Étapes » icon became the word « **Séances** ». A `title` needs a hover; the primary device has none. |
| `lib/api/treatment-plans.ts`, `types.ts` | `stopTreatment(id, version, reason?)`; the status comment names `Stopped`. |
| `scripts/check-responsive.mjs` | **N23 extended** to the negative shape (`plan.status !== "Cancelled" && … !== "Completed"`), anchored on a *plan* subject. |

⚠️ **The first version of that N23 pattern was unanchored and flagged two correct lines** —
`patients/[id]/page.tsx`'s « can this visit still be recorded? » and the agenda's « is this block draggable? »,
both about `AppointmentStatus`, which has its own `Cancelled`/`Completed`. A guard that fires on correct code
gets deleted. Proven green → red on one deliberate violation → green after revert.

### Frontend — the visual restructure (session 1, second half)

The workspace now matches the reviewed mockup (`claude.ai/code/artifact/d80f7145`).

| Change | Why |
|---|---|
| **Title is the TREATMENT**, not the devis number | It read `plan.number ?? plan.title`, so on every numbered devis the largest text on the page was a gapless accounting reference and the thing it is about was the small print. |
| **One identity line**: patient · dents · depuis le … · `2026-0028 · rév. 1` (mono, `ms-auto`) | The number keeps a home — a patient rings up holding a printout — set the way a reference is set on paper. |
| **Two blocks replace the bar + four figures**: `ProgressBlock` \| `NextSeanceBlock` | « Où en est-on ? » and « quoi faire ? », side by side at `lg:`. |
| `ProgressBlock` — one pip **per séance**, then « 3 séances sur 6 faites » | `plan-act-pips` is act-based and an act is only `Done` once every step is, so a six-visit implant showed one grey pip start to finish. Phrased as a count, never `3 / 6` (N31). Falls back to the bar past 14. |
| `NextSeanceBlock` — the next séance **named**, with « Planifier » | The page said « Prochaine séance : 14/08 » in a muted line while the control that books one was on a row below the fold, and the header's filled button offered « Facturer ». « Couronne / bridge » is identical on the préparation and on the scellement six weeks later. **« À planifier », never « en retard »**. |
| The act it hoists comes from **`schedulablePlanItems`**, never `plan.items[0]` | That is the gate `planIdByItem` is built from; N28's rule. Any *other* bookable act keeps its own button on its row. |
| **Money moved out of the header into « L'argent »**, after the acts | The page opened on dinars and the treatment came third. Three figures now sit with the payments they summarise. |
| « Échéancier » → « **L'argent** »; « Reste » → « **Reste à encaisser** » / « **Reste dû** » | The same distinction `InstallmentLateness` makes, said in the label instead of left to the reader. |
| « Parcours » → **`<details>` « Historique du devis »**, collapsed | A journal, not a chairside surface: nothing in it is actionable and every fact is stated where that fact lives. Nothing removed (§ 0). Native `<details>` + `group-open:` — a nested-bracket arbitrary variant would fail silently. |
| `treatedTeeth` derived from the **active** acts' `toothNumbers` | Not `treatedToothNumbers`, which is the whole séance's teeth and would name a tooth no act of this plan touches. |

⚠️ **The praticien is not on the identity line.** `TreatmentPlanDto` carries neither `doctorId` nor
`doctorName`, and adding a projected field is a backend change this pass did not make. The mockup showed it.

## Gate — all run

| Check | Result |
|---|---|
| `dotnet test` (unfiltered, Release) | ✅ **4474 passed, 0 failed** |
| `npx tsc --noEmit` | ✅ clean, whole tree |
| `npm run check:responsive` | ✅ **60/60** |
| `npm run build` | ✅ compiled, 38 static pages generated |
| **eye pass**, 320 / 390 / 820 / 1180 / 1440 | ✅ **ALL CLEAR** — screenshots read, not just queried |
| **`e2e/` hot-path suite** | **119 passed · 2 failed · 1 skipped** (16.6 min) — both failures triaged as not ours |

⚠️ **The API had to be rebuilt and restarted first.** It was serving `bin/Debug` while every backend change had
only ever been built Release to a scratch `BaseOutputPath`, so a browser check before that would have tested a
new frontend against a server with no `Stopped = 5` in it (`verification.md` § 6). ⚠️ And `nohup … &` from a
tool call does **not** survive here — the process died leaving a stale listener on :5000, and the symptom was a
BFF **502** (« Impossible de joindre le serveur du cabinet »), which reads like a product fault and is not one.

### The two e2e failures, triaged (§ 2 — a failing check is a claim about the probe until excluded)

- **BOOK-35** « a confirmation on a SPLIT act creates ONE treatment, not two » — **passes on re-run in
  isolation**. Order/state-dependent inside a `@mutating` suite sharing one clinic. Not ours.
- **FICHE-20/22** — **pre-existing**. It asserts `/SÉANCE\s*1\s*SUR\s*2/i`; the fiche renders
  `Cette séance : étape 1 sur 2 · …`, and `" : étape "` sits between the two words, so the regex cannot match.
  Both the spec and `patient-record-modal.tsx` are pristine in this tree — the assertion went stale when commit
  `34876fb3` reworded that line.

### ⚠️ Two defects the verification found in THIS feature's own code

1. **`NextSeanceBlock` claimed « TRAVAIL TERMINÉ » on a treatment at one séance of six.** The mechanical eye
   pass reported `ALL CLEAR`; **reading the screenshot** is what caught it. The block matched only
   `to-schedule` and let every other état fall through to the "all done" arm, so a `to-record` act — the visit
   happened, the fiche is not written — printed « Tous les actes sont réalisés » beside « 1 séance sur 6 faite »
   and « 0 acte sur 1 réalisé ». Three statements, two contradicting the third. It branches on the act's own
   état now (**Séance réservée · Séance à enregistrer · À planifier**), and « terminé » is reachable only with
   no act left.
2. **A bare rank, caught by `check:responsive`'s own N31.** « Empreinte secondaire — séance 2 sur 6 » means the
   séance still to come in `to-schedule` and the one that already happened in `to-record`: identical words,
   opposite facts, which is exactly the defect that guard exists for and which this product has now shipped
   three times. The rank carries a literal « à faire » / « à planifier » beside the figure, and on `to-record`
   it is **dropped entirely** — neither word is true of a séance that took place but has no fiche.
   ⚠️ The word must be a **literal on the same line as the figure**: interpolating it satisfied a reader and
   not the guard, and the guard was right — a variable leaves the counter looking bare in the source too.

### One layout defect the eye pass found

At 320 px the identity line wrapped after « Emna Belhadj **·** », leaving a dangling middot. The separator was
its own flex item and so free to end a line; it is bound to the item it precedes now — the same class of defect
`quoteFr` exists for with guillemets.

## Remaining

1. **The eye pass** at 320 / 390 / 820 / 1180 / 1440 plus a landscape phone — the load-bearing half of the
   frontend gate (`frontend-web.md` § 14) and the one step not yet run. Needs the app up. Watch in particular:
   the identity line's `ms-auto` number at 320 px, the two header blocks stacking below `lg:`, and
   `NextSeanceBlock`'s `basis-40` beside its button.
2. **AC-12's interval hint** — « dès le 3 juin » in the État cell. Blocked on one **served** field:
   `TreatmentPlanItemStepDto.earliestOn`, fed by the existing `TreatmentPlanItemStep.DueFrom`. A TODO at the
   render site says why it must not be derived in the browser.
3. **The praticien on the identity line** — needs `doctorName` projected onto `TreatmentPlanDto`.
4. **Docs** — `web/components/CLAUDE.md`'s `plan-workspace` / `plan-act-row` rows, and a `notes.md`.
5. `dotnet run -- verify-schema` before/after, to prove the appended enum needed no migration.
