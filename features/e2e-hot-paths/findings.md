# The hot-path pass — findings

**Runs:** 2026-09-08, two passes — the survey, then a tier-0 batch after the fix
**Suite:** `e2e/` · 9 spec files · scenario ids match [`scenarios.md`](scenarios.md)

| | Pass 1 (survey) | Pass 2 (after the fix + new batch) |
|---|---|---|
| Tests | 65 | 103 |
| Green | 64 | see § 6 |
| Red | **1 — a real tier-0 defect** | 0 product defects |

Verified on two databases throughout: the hand-edited developer database, and a **freshly bootstrapped** one
(`e2e/bootstrap.mjs` → `provision-clinic` → TOTP enrolment → seeded catalogue). Everything below reproduced on
both, which is what rules out dev-data artefacts.

---

## 1. FIXED — issuing a Draft note made a continuation's money readable nowhere · T0

### What it was

`ContinueRecordedActCommand` decided how to price a continuation by asking *« does this note represent the plan
right now? »*:

```csharp
var noteRepresentsThePlan = billingInvoice != null && PlanBillingRules.RepresentsItsPlan(billingInvoice.Status);
var noteKeepsTheFirstAct  = noteRepresentsThePlan && remainingCost > 0m;
```

`RepresentsItsPlan(Draft)` is **false**, so a fiche on a *Draft* note took the other branch: the first act kept
its full fee **and the note was attached to the plan**.

Harmless while the note stayed a Draft — a Draft claims nothing, so the plan carried all 40 and the balance was
right. But **a status read once does not stay read.** Issue that note and `RepresentsItsPlan` flips to `true`;
`BilledPlanIds` then drops the plan **whole** from « Solde patient », « Créances », la caisse and the dashboard —
while the plan is holding 10 DT the note does not bill.

```
fiche 30 DT, Draft note over it        → balance  0
continue the act, +10 DT of new work   → balance 40
ISSUE that note (30 DT, gets a number) → balance 30   ← the 10 readable NOWHERE
                                         plan still reports 40
```

Exactly the state `AmendTreatmentPlanCommand` refuses in as many words — « acts added afterwards would be
invisible in every balance » — reached by a door nobody closed. Four ordinary gestures get there, and
`invoice-form-modal.tsx:354` sends `dentalRecordId` on create while `POST /invoices` always makes a Draft.

### The fix

Stop reading the status:

```csharp
var noteKeepsTheFirstAct = billingInvoice != null && remainingCost > 0m;
```

The right question is not *« does this note represent the plan today? »* but **« does this note bill everything
the plan holds? »** — and that has the same answer at every moment of the note's life: `remainingCost == 0`.

| note | new money | attach? | first act | why |
|---|---|---|---|---|
| none | any | — | its fee | the plan owns the work |
| Draft **or** live | **none** | **yes** | its fee | the note bills the whole plan, so attaching is a *true* claim — and the de-duplication it buys is the point. This is why the fix is not « never attach » |
| Draft **or** live | **yes** | **no** | **0** | the documents are made disjoint immediately and stay disjoint through the issue |

⚠️ **The « but that loses the 30 » objection is answered, not accepted.** With a Draft the 30 is not lost, it is
*not claimed yet* — which is what a Draft **is**, and is exactly what the same fiche under the same Draft note
reads with no continuation at all. Issuing it brings the 30 in, and the devis' 10 is still beside it because
nothing was ever attached.

### A second, smaller defect the change exposed

The devis a continuation mints prints « la 1re séance est facturée sur … » in its own notes, interpolating
`billingInvoice.Number` — which is **null** until `Issue`. On the Draft path that printed « sur la note
d'honoraires  (30,000 DT) », a hole in the middle of a sentence on a document handed to a patient. Now routed
through `DentalRecordBillingRefusals.Document()`, which already said « un brouillon de note d'honoraires » for
exactly this case; the helper was made `public` rather than the phrase written twice.

### What proves it

| | |
|---|---|
| `ContinueRecordedActMoneyTests.A_Draft_Note_Is_Treated_Exactly_Like_A_Live_One` | replaces the test that **encoded the defect** — its doc comment argued the mistaken case |
| `…Issuing_A_Draft_Note_Afterwards_Cannot_Hide_The_Remaining_Work` | the unit-level regression, asserted through `BilledPlanIds` because that set is what `GetPatientBillingSummaryQuery` subtracts |
| `…An_Unpriced_Continuation_On_A_DRAFT_Note_Still_Attaches_It` | the other half, so « never attach » cannot creep in |
| `e2e/specs/continuation.spec.ts` · `CONT-05b` | end to end: balance **40** after the issue, on both databases |
| whole unit suite | **4307 passed, 0 failed** |

---

## 2. A model-only column halted every fiche save — and the unit suite could not see it

Mid-session, an in-flight change added two properties with **no migration**:

```
DentalRecordActs.ImplantPilierToothNumbers   declared, EF-mapped, absent from the database
ToothStates.BridgeGroupId                    declared, EF-mapped, absent from the database
```

Every `POST /patients/{id}/dental-records` answered
`42703: column "ImplantPilierToothNumbers" of relation "DentalRecordActs" does not exist`, flattened by the
handler's catch-all into « Erreur lors de l'enregistrement de la fiche de soins. Veuillez réessayer. »

**Two things worth keeping:**

- **4307 unit tests were green while every fiche save was broken.** That is the gap `api/CLAUDE.md` names —
  nothing in `UnitTests` touches a database, so a migration is the one change unit tests structurally cannot
  verify — and it is the strongest argument for the CI `e2e` job. The suite found it in one run.
- ⚠️ **`verify-schema` caught `ToothStates(BridgeGroupId)` and not
  `DentalRecordActs(ImplantPilierToothNumbers)`** — and the one it missed is the one breaking saves. Its column
  list is hand-maintained, so it drifts from the model it checks. **Still open**: the check for the second
  column was not added here, because `SchemaVerificationService.cs` was being edited in parallel and editing a
  file someone is working in is how in-flight work gets clobbered.

Resolved by `20260908122028_AddBridgeIdentityAndImplantPilier`.

---

## 3. Documentation was stale — `act-card` withholds the price, it does not lock it

`CLAUDE.md` said `act-card` renders its price `readOnly`. It does not, and has not for some time: when
`billedOnPlan`, `act-card.tsx:278` **replaces the input** with « Aucun honoraire sur cette séance. Cet acte est
chiffré une fois, sur le traitement. »

The code is **better** than the note, for the reason its own comment gives — a collapsed card reading
« 0,000 DT » « is the third of the séance's zeros and it says nothing ». Corrected in `CLAUDE.md`; `FICHE-20/22`
now asserts the code and says so inline.

---

## 4. Pre-existing, not from this work — 7 broken audit chains

`verify-schema` on the dev database:

| Check | State |
|---|---|
| `audit-chain-intact` | **DRIFT** — 7 broken chains, « cette entrée a été modifiée après son écriture » |
| `clinic-signup-has-no-orphans` | DRIFT — 3 signups that can no longer become anything |
| `messaging-month-covers-every-clinic` | DRIFT — 2 of 7 cabinets have no 2026-09 counting row (the 06:00 pass) |
| `key-ring-protection` | DRIFT — Development-only, documented |

**Not caused by this work**: the named breaks are on *other* clinics, at entries n° 200–202 — old, low sequence
numbers — and nothing here wrote to or deleted from an audit table. `audit-declared-gaps` separately reports 5
interruptions the product logged itself (failed journal writes, or **restorations**), the likeliest cause and one
this repo has recorded experience of. Flagged because nobody had run the verb recently.
`reconcile-money`: **no drift**, before and after every mutating run.

---

## 5. Correct behaviour that surprised the scenario list

Six things the list expected to be wrong and which are right. Recorded because each cost time and would cost it
again.

| Expected | Actually | Why the product is right |
|---|---|---|
| A full avoir lets a billed fiche's acts be edited | still refused, with a **different** code | `Check` asks `IsSpent` **first**, deliberately: « an invoice that can take no money at all is reported before what changed on the fiche ». Correcting goes through `correctionReason`, not an avoir |
| `Scheduled → Confirmed` is an ordinary status change | refused | `Confirmed` is legacy-only; no transition leads to it. « Withdrawing a confirmation » has no clinical meaning — the slot is booked either way |
| the last séance charts the teeth of earlier ones | only if the fiche **sends** them | the server charts what the last fiche carries and has **no fallback**. The guard is the client prefill (`TreatedToothNumbers`) — which is why `FICHE-33` is a browser test |
| an appointment carries a `TreatmentPlanId` | **no such field** | not on the entity, the DTO, or the table. One-treatment-per-booking is derived from `TreatmentPlanItemId`. CLAUDE.md's phrasing is conceptual |
| a pontic mark applies to any act | inert unless the act's condition is a **bridge unit** | `BridgeCharting.IsUnit` — that is what keeps every row written before the split, and every bridge a dentist does not detail, charting the way it always did |
| the séance editor is missing in the edit dialog | **present**, with the frise and « Tout faire en une seule séance » | the reported field defect is fixed; `BOOK-31/32/33` now hold it in all three situations |

---

## 6. Coverage

| Path | Spec | Tests |
|---|---|---|
| HP-5/6 fiche money · charting | `fiche-money.spec.ts` | 10 |
| HP-5c **le bridge** | `bridge-charting.spec.ts` | 9 |
| HP-6/10 billing guards · avoir · collect-on-plan | `billing-guards.spec.ts` | 9 |
| HP-8 la suite d'un acte | `continuation.spec.ts` | 8 |
| HP-7/9 lifecycle · clôture | `plan-lifecycle.spec.ts` | 12 |
| HP-4 sous-étapes (×4 editors) | `steps.spec.ts` | 6 |
| HP-2/3 acts · tri-state DTO | `appointment.spec.ts` | 6 |
| HP-10 day boundary · devis→note bridge | `money-boundaries.spec.ts` | 9 |
| HP-7/11/4 plan reads · detach remedy | `plan-reads.spec.ts` | 5 |
| HP-2 booking, in a browser | `booking-browser.spec.ts` | 6 |
| HP-3 concurrency · 409 + « Recharger » | `concurrency.spec.ts` | 3 |
| the client-only half | `browser-hot-paths.spec.ts` | 15 |
| read-only, for the deploy gate | `smoke-readonly.spec.ts` | 5 |

**103 tests covering ~130 of the 268 scenarios** (125 of which are tier-0).

### Not covered, each a deliberate call

- **`isUnfinished` (« Acte non terminé »)** — `features/unfinished-act-continuation/spec.md` is APPROVED and
  **not implemented**: no column, no migration, no code. Its nine acceptance criteria are not testable and the
  suite asserts none of them (`CONT-16`).
- **`EDIT-05`'s job half** — `AppointmentProgressJob` fires on the minute at an appointment's own slot
  boundary, which is neither promptly nor deterministically reachable; forcing it would assert the scheduler,
  not the rule. The observable half is tested and the test **reports itself as not exercised** when the job has
  not run.
- **The device contract at five widths** — `check:responsive` + an eye pass own it. The one device fact this
  suite holds is `XCUT-09`, because a third scrollbar is invisible to every other check.
- **Google Calendar, SMS/WhatsApp, file upload and the viewers** — real outbound integrations; a hot-path gate
  that needs a Google token is a gate that gets disabled.
- **The remaining tier-1/2 scenarios** in `scenarios.md`. The list is ordered so the next batch is a reading
  exercise, not a rediscovery.

---

## 7. What the passes learned about their own measurement

**Seventeen probe traps across the two passes** — nine in the survey, eight in the batch — each of which
produced a convincing false defect report against correct product code. They are catalogued in
[`scenarios.md`](scenarios.md) § Appendix and recorded at their call sites in `e2e/`.

The two that generalise beyond this repo:

- **A wait for a loading string is not a wait for the app.** `AppLoader` rotates ten French messages and keeps
  « Chargement… » only as an `sr-only` span; a `getByText(/Chargement/i)` wait resolved instantly and three
  tests then drove a spinner. Wait for a **positive** signal — the app shell's navigation landmark.
- **On the login page most assertions pass vacuously.** It is short, does not scroll, and raises no console
  error, so it satisfies almost any check. Seven `XCUT-09` checks once reported green from there. Every
  navigation now goes through `gotoApp()`, which re-authenticates and **fails loudly** rather than measuring
  the login page.

And two environment lessons that invalidated whole runs: a `next dev` server up 11½ hours had degraded to
`Jest worker encountered 2 child process exceptions` on every route (hence CI runs a **production** build), and
the suite killed its own session every run until the refresh cookie had exactly one holder — the BFF rotates it
per `/bff/auth/token` and `PreviousCredentialHash` **detects** a replay rather than forgiving one.
