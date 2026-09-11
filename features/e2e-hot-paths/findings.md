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

---

# The 2026-09-11 pass — coverage made measurable

**Why it ran:** the suite had not been run since 2026-09-08 and the tree was **~40 commits** newer. The
owner's report was « I keep finding bugs wherever I go, and the e2e tests keep skipping important details ».

Both halves of that turned out to be true, for one reason, and it is not the one the numbers first suggest.

## 8. The diagnosis: the tests were at the wrong LAYER

| | 2026-09-08 | 2026-09-11 |
|---|---|---|
| Scenarios in the catalogue | 268 | **335** (172 tier-0) |
| Tests in `e2e/` | 103 | **135** |
| Tests that open a **browser** | 21 | 21 → in progress |
| Tier-0 scenarios with **no test at all** | — | **47** (of the pre-existing 132) |
| Tier-0 rows covered **only on the wire** while their rule is client-side | — | **18** |

The decisive measurement is not the coverage gap. It is this: **every one of the five `fix(...)` commits
between the two passes was a defect no wire test could see.**

| Commit | What it was |
|---|---|
| `24f2883e` | « le total d'un acte modifié depuis le rendez-vous part enfin au serveur » — a price the dentist typed **never reached the server**. Money. Scenario `BOOK-48`, which had no test |
| `3df8627d` | editing a devis turned « solde dû » into an échéance « en retard » |
| `5b8d6e5a` | the continuation minted its devis on a **button press** instead of on save |
| `f0a6cf18` | « Encaissé sur le traitement » showed the wrong figure |
| `0e9f3207` | the booked duration was the whole act's, not the séance's — `CAT-08` |

A wire test posts a body **the test author wrote**. It proves the handler is right about that body, and nothing
about the body the product sends — and a wire test of `BOOK-48` would have passed on every day it was broken,
because the wire test sends the price itself.

**What was built for it:** `scenarios.md` § « Layer » (the rows a wire test cannot hold, grouped by *why*),
`.claude/rules/verification.md` § 4b, and `e2e/scripts/check-coverage.mjs` — a derived guard that parses the
catalogue and the specs and fails on an untested tier-0 row, a browser-layer row covered only on the wire, and
a test naming a row that does not exist. Nothing in it is maintained by hand, for the reason § 2 records about
`verify-schema`'s own hand-kept column list.

## 9. The catalogue was a record of things going FORWARD

HP-1…HP-13 held 268 scenarios and **not one of them undid anything**. There was no test for deleting a fiche,
cancelling a booking that had materialised a treatment, voiding a payment, unmarking a step, stopping a
treatment, or cancelling a note.

Four sections were derived and added — **67 rows, 40 of them tier-0**:

| § | What it generates | Rows |
|---|---|---|
| **HP-14 · Défaire** | the inverse of each of the eight writers in `coupling-matrix.md` § 1 | 36 |
| **HP-15 · Arrêter** | `TreatmentPlanStatus.Stopped` × every money read and every worklist | 11 |
| **HP-16 · Inachevé** | the visit that never happened, the quote nobody answered | 10 |
| **HP-17 · Refaire** | the second crown on the same tooth, the redone bridge, the re-billed séance | 7 |

The rule they apply: **everything `coupling-matrix.md` says a writer moved must move back — or the undo must
be refused with a remedy that exists.**

### Two open questions found by writing them down

Neither is a regression; both are states nobody had decided about, reached by ordinary gestures.

- **`DEL-03` — the « à traiter » a fiche clears is destroyed, and deleting the fiche cannot rebuild it.**
  `DentalRecordLinker.ClearDiagnosesForTreatedTeethAsync` (`DentalRecordLinker.cs:80`) **deletes** the
  diagnosis row when the fiche is saved. Deleting the fiche cascades its *treatment* states away — `ToothState`
  is child-of-record — but nothing restores the diagnosis. The tooth then reads **healthy**: the product has
  forgotten both that the work was done and that it was ever asked for. The choices are a soft delete restored
  on the record's delete, or refusing the delete; doing nothing is also a choice, but it should be one.
- **`DEL-08` — a billed fiche deletes, and its note d'honoraires stands.** `DeleteDentalRecordCommand` drops
  only the invoice line's `DentalRecordId` provenance, on the stated rule that « deleting a clinical record
  must never alter a fiscal document » (`DeleteDentalRecordCommand.cs:163`). Defensible alone; the *pair* is
  not. The note keeps its number, its amount, its caisse movement and its place in « Créances », for a séance
  that now exists in no history — and `FEDIT-16` says that state must not be reachable.

`e2e/specs/delete-fiche.spec.ts` measures both, and each failure message states what was measured and what the
alternatives are.

## 10. `CONT-16` is stale — `isUnfinished` shipped

`scenarios.md` said « **Spec only.** there is no `IsUnfinished` anywhere in `api/` or `web/`, no migration ».
As of 2026-09-11 there is: `DentalRecordAct.IsUnfinished`, `GetUnfinishedActsQuery`, `ContinuableActDto`, and
migration `20260911161617_AddDentalRecordActIsUnfinished`. « Acte non terminé — il faudra une autre séance.
L'acte passe dans « Suites à planifier » ; aucun montant n'est modifié » renders on the act card today.
`features/unfinished-act-continuation/` still has **no `notes.md`**, so the nine acceptance criteria remain
un-catalogued; that is the next section to derive, not a defect.

## 11. What the pass actually found — and why « 14 failures » was 13 measurements of the harness

Two full runs, then a third after the fixes. **Every product-level failure was triaged before a line of source
was touched** (§ 2), and the ratio is the headline: of the **14** failures in the second run, **13 were the
environment or the probe** and one was a stale locator. The suite's *own* machinery produced more false defect
reports than the product produced real ones — again.

### 11a. The environment, twice, and neither said so

| What happened | What it looked like |
|---|---|
| **`npm run start` with `output: 'standalone'`** — `next start` prints a warning, **binds anyway**, reports « Ready in 630 ms », serves ~35 tests and then **exits silently** | ten product defects: seven `XCUT-09` scrollbar failures, `PLAN-04`, `EDIT-06`, `EDIT-07`, all `net::ERR_CONNECTION_REFUSED`. Nothing in the server log says it died |
| **A `next dev` respawned by an editor task and stole :3000** minutes after being killed | the *first* run's five money failures were an `AppLoader` overlay reading « Jest worker encountered 2 child process exceptions » — the documented dev-server degradation, on a server nobody thought was running. One later run **hung its first browser test for 2.1 hours** and then failed the remaining **93 tests in ~40 ms each** |

Fixed in `ci.yml` (the standalone runner **plus** a « web is still up » re-check after the install steps, because
a readiness poll only proves the server answered **once**) and written up in `e2e/README.md`. ⚠️ **CI started
`web` the same way**, so the hot-path gate's browser half could die mid-run and report product defects — the
one thing a gate must never do.

### 11b. The probes, and the trap that has now cost four endpoints

| Probe | Reported | Actually |
|---|---|---|
| `getByText(/SÉANCE 1 SUR 2/i)` | the fiche does not say which séance it is | the sentence moved onto the **act card** in `06a47cb0` and became « Cette séance : étape 1 sur 2 · &lt;nom&gt; » — `N31`, because a bare rank is read as progress |
| `receivables("?pageSize=200")` | **a money gap** — « the patient must appear in « Créances » » | the clinic has **285** receivable rows; the 20 DT fixture patient sat past the page |
| `caisseLedger(..., pageSize=500)` | « an expense must appear as its own movement kind » | the endpoint **silently caps `pageSize` at 200**; `totalCount` was 239 and the expense was on page 2. The caisse *summary* had already counted it, so two reads appeared to disagree |

⚠️ **The second and third are the same mistake on a fourth and fifth endpoint**, and the appendix already named
three: `fromDay`/`toDay` on la caisse, `startDate`/`endDate` on the appointments, `searchTerm` on the patients
— now **`search`** on the receivables (a *different word for the same concept*, one endpoint over) and a
**silently capped page size** on the extrait. The rule generalises past parameter names:

> **An over-large `pageSize` is adjusted, not refused — exactly like an unknown parameter is ignored, not
> refused. In both cases the response looks complete.** So a test may never conclude anything from *absence*
> in a list it did not prove it read whole.

Both are now structurally impossible rather than corrected: **`ClinicApi.receivableFor`** asks the filtered read
and throws if the filter was dropped, and **`ClinicApi.caisseLedger`** walks every page and throws if what it
collected does not equal `totalCount`. ⚠️ `receivableFor` was wired into **all five** specs that were paging for
a fixture patient (`continuation` ×2, `document-integrity`, `money-boundaries`, `plan-lifecycle`) — not just the
one that went red. Three of those were passing on `pageSize=300` against 285 rows, i.e. **15 rows from
becoming false failures**, and `plan-lifecycle`'s asserted an *absence*, which would have started passing for
the wrong reason.

### 11c. The one that was the product — and the one where the product was right

**`DEL-03` — CONFIRMED, and left as a decision, not a fix.** The « à traiter » a fiche clears is **deleted** at
save time and deleting the fiche cannot rebuild it, so the tooth reads **healthy**: the product has forgotten
both that the work was done and that it was ever asked for. Measured end to end. The test is `test.fixme` with
the three ways out named in it — soft-delete and restore, refuse the delete, or accept it and say so — because
this is a clinical-data decision and not one to take inside a testing pass.

**`DEL-08` — the finding was wrong, and the code won.** The catalogue said a billed fiche's delete must be
« refused, or the note dealt with first — never an orphaned note », and a first pass **implemented that**:
`DentalRecordBillingGuard.EnsureWorkIsNotBilledAsync` wired into the delete, refusing while a live note bills
the séance. It worked, it was idiomatic, its refusal named the note and a reachable remedy — and it was
**reverted**, because `DeleteDentalRecordCommand` already states the opposite decision at the call site
(« deleting a clinical record must never alter a fiscal document ») and `document-integrity.spec.ts`'s own
`FEDIT-16` argues it: forcing an **avoir** — a fiscal document — to correct a *clinical* mistake is the heavier
outcome, not the lighter one; the money really was received; and the note keeps its own line text, so nothing
is left claiming money nobody owes. ⚠️ **The catalogue row was an inference and the source comment was a
decision** — which is this file's own rule (« where a row disagrees with the code, the code won »), applied
against the row *this pass had just written*. `DEL-08/09` now asserts what actually matters on a hot path: that
after the delete, « Solde patient », « Créances », la caisse and the note itself still agree.

## 12. Where the pass left the suite

| | 2026-09-08 | 2026-09-11 |
|---|---|---|
| Scenarios in the catalogue | 268 | **340** (174 tier-0) |
| Tests | 103 | **135** |
| Tier-0 rows with a test | — | **110 / 174 (63 %)** |
| Rows declared **browser-layer** (§ Layer) | — | **118** |
| Last full run | 64 / 65 | **132 passed · 1 failed · 2 skipped** |

The one failure is `EDIT-07`, `net::ERR_CONNECTION_REFUSED` — the `next start` death of § 11a, on a developer
machine whose editor keeps reclaiming :3000. It is the reason CI now uses the standalone runner and re-checks
that the server is still up. The two skips are `DEL-03` (§ 11c) and the read-only smoke, which a mutating run
skips by design.

**New this pass:** `e2e/specs/money-reads.spec.ts` (`MONEY-20/21/22/25` and `MONEY-14/15` — the « no money
gaps » batch, five tier-0 rows that had no test at all) and `e2e/specs/delete-fiche.spec.ts` (`DEL-01/02`,
`DEL-03`, `DEL-04/05`, `DEL-08/09`, `DEL-12` — the first tests in this suite's life that undo anything).

**What is still open, in priority order:**

1. **`DEL-03`** — the decision, above.
2. **64 tier-0 rows with no test**, `node e2e/scripts/check-coverage.mjs` lists them by name. The 40 newest are
   HP-14–17's undo/stop/incomplete/redo axes, which is where the product has never been exercised at all.
3. **18 tier-0 rows covered only on the wire** while § Layer says their rule is client-side — the same class as
   the five `fix(...)` commits of § 8, and the reason `check-coverage` exists.
4. **`features/unfinished-act-continuation/notes.md`** does not exist, so the nine acceptance criteria of a
   feature that shipped on 2026-09-11 are not catalogued (§ 10). CONT-17…21 are what could be derived from the
   code; the rest needs the author.
