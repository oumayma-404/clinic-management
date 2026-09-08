# The coupling matrix — what moves when one thing moves

> « Les fonctionnalités sont toutes liées : une partie bouge, toutes les parties bougent avec elle, parce qu'on
> automate beaucoup de choses. »

This is that statement written down, as a table, from the source. It is the **reason the scenario list asserts
across surfaces instead of on the screen that was touched**: every defect this repo has recorded was a surface
that did *not* move, with no error anywhere.

Read this before `scenarios.md`. Every scenario's *Expected* column is drawn from a row here.

---

## § 1 The eight writers

Only eight gestures in this product create or move money and clinical fact. Everything else is a read.

| # | Gesture | Where | Server writer |
|---|---|---|---|
| W1 | Save a booking | `create-appointment-dialog` | `CreateAppointmentCommand` (+ `materialisePlannedProtocols` → `StartTreatmentCommand`) |
| W2 | Edit a booking | `edit-appointment-dialog` | `UpdateAppointmentCommand` |
| W3 | Save a fiche de soins | `patient-record-modal` | `CreateDentalRecordCommand` / `UpdateDentalRecordCommand` |
| W4 | Bill a fiche | fiche save (auto) **or** « Facturer cette intervention » | `DentalRecordAutoBilling` → `BillDentalRecordCommand` |
| W5 | Collect on a treatment | fiche « Encaissé sur le traitement » | `CollectOnTreatmentCommand` |
| W6 | Accept / amend / stop / complete a devis | `plan-workspace` | `AcceptTreatmentPlanCommand`, `AmendTreatmentPlanCommand`, `StopTreatmentPlanCommand`, `CompleteTreatmentPlanCommand` |
| W7 | Record an échéance payment | `installment-payment-modal` | `RecordInstallmentPaymentCommand` |
| W8 | Continue a recorded act | `continue-session-dialog` | `ContinueRecordedActCommand` |

Plus one **non-human** writer that participates in concurrency: `AppointmentProgressJob`, which writes an
appointment **every minute** (« En cours » at the slot's start, « Séance passée » at its end).

⚠️ **The patient file's « Reste à payer » band is a ninth DOOR, not a ninth writer** — count it as one and the
list stops being the closed set it exists to be. Its « Encaisser » mounts the same `PaymentModal` /
`InstallmentPaymentModal` as W7 and `/factures`, so every payment taken from a patient's file is
`RecordPaymentCommand` or `RecordInstallmentPaymentCommand`; its « Facturer » is W4's own dialog. Cash still
lives in exactly two ledgers. What the band adds is a **read** — `PatientBillingSummaryDto.Lines`, the
decomposition of « Solde patient », projected by `PatientDebtLines` from the two collections that read already
had in hand. See [`patient-outstanding-breakdown`](../patient-outstanding-breakdown/notes.md).

---

## § 2 W3 — saving a fiche de soins: the widest blast radius in the product

One `Enregistrer` on a fiche touches **eleven** surfaces. This is the row that matters most.

| Surface | What must change | Rule that decides it | Silent-failure shape |
|---|---|---|---|
| The fiche itself | acts, teeth, total, payé | `SetActs` **replaces** the whole list | omitting `procedures` restores every act to its **tarif** |
| Odontogramme | tooth states for **finished** acts only | `ToothChartingRules.ChartableActs` | step 1 of 2 charts « Implant » weeks early |
| Open diagnoses | « à traiter » cleared on charted teeth only | `DentalRecordLinker.ClearDiagnosesForTreatedTeethAsync` | work both finished *and* never asked for |
| Devis step | the step is marked done **before** charting | `DentalRecordLinker.PlanActLink.ItemIsComplete` | only the aggregate knows if it was the last step |
| Plan status | `Planned → InProgress`, auto-`Completed` on last step | `TreatmentPlan.Complete(leaveUnrealisedActs: false)` | — |
| Plan act price | the carried act is forced to **0** | `PlanCarriedActPricing.ImposeAsync` (server-side) | overtyping bills the act twice, on two unlinked documents |
| Note d'honoraires | raised, or **topped up** — never a second note | `BillDentalRecordCommand` idempotence | a re-save raising note #2 for one séance |
| Caisse | one movement, dated on `InterventionDate`, method = the fiche's | `DentalRecordAutoBilling` | cash booked to *today* for a fiche entered two days late |
| Chèques à encaisser | a cheque fiche appears there, not under « espèces » | `record.PaymentMethod` threading | a cheque counted as notes in the drawer |
| Créances / Solde patient | outstanding = note's, **or** plan's — never both | `PlanBillingRules.BilledPlanIds` | a bridged plan's money readable **nowhere** |
| « Reste à payer » (patient file) | a row appears, disappears or re-prices | `PatientDebtLines` over that same read | the band's rows stop summing to the figure above them |
| Historique des actes | row reads « Acte / étape n / N » with its own money | `DentalRecordPlanLinkRow` | three fiches of one implant read as three implants |
| « À clôturer » | the visit leaves the worklist | `VisitClosure` | — |
| Dashboard | « Traitements en cours », « Devis en attente » | `TreatmentsInProgressReader`, `CountUnansweredDraftsAsync` | a Draft counted as an unanswered quote |
| SignalR | every open client refreshes | `RealtimeBroadcastBehavior` | — |

### The two money fields on a fiche are NOT interchangeable

| Field | Goes to | Never |
|---|---|---|
| « Payé » | a **note d'honoraires** for this séance | folded with the next |
| « Encaissé sur le traitement » (`amountCollectedOnPlan`) | the **devis** échéancier | folded into « Payé » |

And `seanceIsWhollyOnTreatment` (**all** named acts carried) is what withholds « Total » and « Payé »
altogether — structural, never `grandTotal === 0`, which is also true of a détartrage nobody has priced.

---

## § 3 W1/W2 — a booking

| Surface | What must change | Rule |
|---|---|---|
| Agenda grid | the slot, day/week/month | — |
| Appointment acts | rows, each with its own `AgreedCost` | `AppointmentProcedureSelection` |
| A **treatment** | created as un-numbered `Draft` **at save**, one per booking | `materialisePlannedProtocols`, `resolveAttachedPlanId` |
| Plan step link | the booking takes a step, not the act | `attachPlanAct` → `schedulablePlanItems` |
| Duration | the sum of the acts, unless hand-touched | `durationTouched` |
| Negotiated price | `AgreedCost` survives to the fiche's prefill | must be threaded explicitly — the fiche re-prices from the **catalogue** |
| Google Calendar | pushed (asymmetric, per-clinic) | — |
| Rappels | the reminder queue | — |
| « Traitements en cours » | a new followed treatment appears **the same day** | `item.Status == Planned` **with steps** counts |

Three advisory refusals that are **questions, not dead ends**, and each re-enters `performCreate` from the top:
slot taken · out of hours · past time. Plus a fourth on the patient: « Ce patient existe déjà ».
⚠️ `createdPatientIdRef` and `createdPlansRef` are what stop each confirmation duplicating the patient / the
treatment.

---

## § 4 W4–W7 — the money ledgers

```
                    ┌──────────────────────┐
   fiche « Payé » ──▶│ Note d'honoraires    │──▶ Caisse (movement, dated InterventionDate)
                    │  (numbered, gapless) │──▶ Chèques à encaisser (if method = cheque)
                    └──────────────────────┘──▶ Créances / Solde patient (outstanding)
                                │
                          avoir │ (the ONLY correction once issued)
                                ▼
                          Credit note

   fiche « Encaissé  ┌──────────────────────┐
   sur le traitement»│ Devis échéancier     │──▶ Caisse (installment payment movement)
        ────────────▶│  (Accepted+)         │──▶ Créances (unless bridged)
                    └──────────────────────┘
                                │ bridge (IssueDevis / BillDentalRecord with TreatmentPlanId)
                                ▼
                    Note d'honoraires — and the plan is then dropped WHOLE
                    from every money read (`BilledPlanIds`)
```

**The all-or-nothing bridge is the single most dangerous edge in the product.** The moment a real
(non-Draft, non-Cancelled) note names a plan, `BilledPlanIds` drops that plan from « Solde patient »,
« Créances », la caisse **and** the dashboard. So any money added to a bridged plan afterwards is owed by the
patient and readable **nowhere** — measured: a 30 DT coiffage billed on a note, continued at 10 DT, left the
balance reading **0**.

`ContinueRecordedActCommand` therefore has two modes, gated on `PlanBillingRules.RepresentsItsPlan` and **never**
on « is there a note »:

| Note state | New money? | What happens |
|---|---|---|
| none | any | plan owns the act's fee, bills once when done |
| **live** note | none | note **attached** to plan (stops the double count) |
| **live** note | yes | note **NOT attached**; billed act priced **0** on the devis; two disjoint documents |
| **Draft** note | any | represents nothing → plan keeps its own balance → attach is wrong |

---

## § 5 Guards that must refuse — and the remedy each names

A refusal whose remedy is unreachable is worse than no refusal. Each of these was that, once.

| Code / sentence | Fires when | Remedy the message must name | Correctable? |
|---|---|---|---|
| `dental_record_payment_exceeds_cost` | « Payé » > acts total, unbilled fiche | correct the amount, or add the missing act | no |
| `dental_record_acts_changed_after_billing` | acts (i.e. `Cost`) moved after the note issued | avoir, then re-bill | **yes** → « Corriger » |
| `dental_record_payment_lowered` | « Payé » lowered below what the note collected | avoir | **yes** → « Corriger » |
| `dental_record_invoice_not_live` | the note is cancelled or fully credited | re-bill from « Facturer cette intervention » | no |
| `dental_record_payment_banked` | séance redated, a cheque already banked | — | no |
| `EnsureWorkIsNotBilledAsync` | detaching a fiche a live note bills | `Snapshot.Remedy` — **the one that exists**: avoir for the whole remaining amount if money was collected, else cancel | — |
| « Un brouillon se supprime, il ne s'annule pas. » | `Cancel` on a `Draft` | delete it, from `/treatment-plans` | — |
| « Le plan de traitement est requis pour lier l'acte. » | `planIdByItem` missed the item | — (this is the `plan.items[0]` bug) | — |
| 409 « modifié par quelqu'un d'autre » | `xmin` moved | « Recharger » — via `useConflict`, never a bare `setError` | — |

⚠️ **`IsSpent` vs `RepresentsItsPlan`**: a credit note is a separate aggregate and leaves the invoice `Paid`.
A guard asking `RepresentsItsPlan` alone **survives the avoir it just demanded** — which is how « Détacher la
fiche » told a dentist twice to do the thing they had just done, while `CanCancel` refused the other remedy.

---

## § 6 « Draft » means two opposite things

The single highest-cost ambiguity in the codebase. Ask the named rule, never a hand-written status test.

| Question | Ask | `Draft` answer |
|---|---|---|
| Is this treatment still being carried out? | `TreatmentPlanLifecycle.IsLive` / `isPlanLive` | **yes** — clinically live |
| Does it carry patient debt? | `PlanBillingRules.CarriesDebt` | **no** — financially inert |

A followed treatment (« Suivre ce traitement ») is an un-numbered `Draft`: **clinically live, financially
inert**, and `AdvanceAfterWorkRecorded` keeps it a Draft however many séances it records. Its corollary:
**an un-numbered plan must never wear a debt-bearing status** — `OpenStatusFromWork` keys on `Number is null`
for exactly that, after « Arrêter » → « Reprendre » turned a followed treatment into an `Accepted` devis with a
null number and a live créance for a total nobody had quoted.

---

## § 7 Tri-state, replace-whole, and the four-argument copy

Three shapes that lose data with no error.

1. **An update DTO is tri-state.** absent = unchanged · `[]` / null = **clear**. `{ status }` alone on an
   appointment drops every act of the séance.
2. **`SetActs` / `SetSteps` / `SetProcedures` replace the whole list.** A saved `procedures` list that omits
   prices restores every act to its tarif.
3. **`TreatmentPlanItemStepInput`'s fourth argument is `MinDaysAfterPrevious` and defaults to null.** A
   three-argument copy compiles, reads correctly, and **erases the interval from every step of the act**.
   Renaming one step of an implant wiped its osseointegration wait; the symptom was `RecallWorklistRules`
   reporting a healthy implant as abandoned.

And `SelectedAct.plannedProtocol` is itself tri-state: `undefined` = undecided · `null` = one séance ·
a list = the confirmed séances. `[]` is never stored — emptying the list **is** choosing one séance.

---

## § 8 Dates, and why every money read is off by a day when they are wrong

| Wrong | Right | Symptom |
|---|---|---|
| `DateTime.UtcNow` / `.Today` | `ClinicClock` (Tunisia = UTC+1) | — |
| `EndOfLocalDayUtc` in a money read | `LastTickOfLocalDayUtc` | a midnight payment lands in **two** adjacent periods |
| `new Date().toISOString().slice(0,10)` | `todayLocalIso()` | for the first hour of every Tunisian day, pre-fills **yesterday** — and on the 1st, last month |
| `DateTime.Now` for a fiche's payment | `record.InterventionDate` | a fiche entered late books cash to the wrong day's caisse |

---

## § 9 What the odontogramme may and may not assert

| Rule | Consequence |
|---|---|
| An end state is charted when the act is **`Done`** | never from step 1 of N |
| One act charts **one** state across all its teeth | — |
| **Except a bridge** — the only exception in the product | `DentalRecordAct.PonticToothNumbers` + `BridgeCharting.ConditionFor`; an act with **no** pontique marked charts exactly as before |
| Never infer pilier/pontique from position | a pier abutment is crowned mid-span; 12·11·21 sorts to 11·12·21 |
| « Which teeth is this treatment on? » reads the **devis line** first | `teethUnderTreatment`; the union was measured wrong — a quote on 13/43 whose fiche named 13,27,36,37,43 ringed three healthy teeth |
| A fiche's chart **seed** prefers the teeth actually worked on | `openPlanItems` — the **opposite** priority, deliberately |
