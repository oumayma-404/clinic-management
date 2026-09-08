# « Reste à payer » — what shipped, and why it is shaped this way

**Landed:** 2026-09-08 · one pass · gate green (4330 unit tests · `check:responsive` 55/55 · `tsc` · `build` ·
eye pass at 320/390/820/1180/1440 · `reconcile-money` clean before and after)

---

## The gap

« Solde dû 240,000 DT » was a plain `<span>` in the patient file's identity strip (`page.tsx:1296`) — not a
link, not a button, with nothing saying what it covered. Six of the seven fields the endpoint already sent were
fetched and never rendered.

The composition lived in two places the file could not put side by side:

| Debt source | Was it visible? | Could it be settled there? |
|---|---|---|
| Note d'honoraires | inside tab **7 of 7** | yes — tab → row → menu → modal |
| Devis échéance | a plan-level « Reste » in the strip | **no** — you had to leave for `/treatment-plans/{id}` |

⚠️ **`/creances` is retired and unrouted** (`web/lib/nav.ts:96`), so the page's own tombstone at `page.tsx:557`
was already admitting that the removal's stated fallback had « lost a leg ».

And the kicker: `/factures` is closed to a secretary, and its refusal reads *« Vous pouvez encaisser un paiement
depuis la fiche du patient »* (`app/factures/page.tsx:81`). The app already promised this feature existed. Every
action the band needs is `AnyClinicRole` — verified on all four routes — so it now keeps that promise.

## Where it sits — and it moved once, on review

It shipped first as a `border-y` band **above the tabs**, under `PatientPlansStrip`. That was wrong for two
reasons the owner named on sight: it put a money surface between the odontogramme and the whole record, and it
did not look like anything else in the app — a bare table paints `bg-card` with no border and inherits its
radius from a parent that had none, so it read as a borderless white slab beside lists that are all framed.

It now sits **under l'historique des actes, inside the « Actes dentaires » tab**, as one
`rounded-md border` surface holding both trees (`invoices-table`'s shape). That reads as the second half of the
tab it is in: the table above says what was done and what each fiche took, this says what is still owed on it
and settles it.

⚠️ **The consequence is that « Solde dû » in the identity strip had to become a real control** — a section
inside a tab cannot be seen from the top of the page, so that figure is now the only route to it. It switches
the tab *and* scrolls (`PATIENT_OUTSTANDING_SECTION_ID`, exported so the two ends cannot drift), and the scroll
waits a frame: Radix mounts a `TabsContent` only when it becomes active, so scrolling in the same tick finds no
element at all.

| Width | First version | Now |
|---|---|---|
| 320 | 964 px | **569 px** |
| 390 | 856 px | **481 px** |
| 1440 | 312 px | **280 px** |

## The three decisions that shaped it

### 1. Served, never derived — and one row per DOCUMENT, not per échéance

`GetPatientBillingSummaryQueryHandler` already loaded the exact two collections it summed and then **discarded
the rows**. So `PatientDebtLines.Project` is a pure projector over those same two variables: no second query, no
second status filter, no second copy of `PlanBillingRules`. **The parts equal the whole by construction**, and
`TotalOutstanding` is now *derived from* the lines rather than computed beside them.

⚠️ **A client-side breakdown was rejected, not overlooked.** `/factures:121` states the rule for its own figure:
« Served, never derived here: only the server can apply PlanBillingRules BilledPlanIds ». The one client-side
attempt at this rule is on record in `displayedOutstanding`'s docstring — 4 of 4 bridged plans reported as wholly
unpaid, two of them fully settled, one patient shown « Solde dû 31,000 DT » beside « Reste 120,000 DT » on the
same page.

⚠️ **Per-échéance rows would not have added up.** `MoneyReconciliationService.cs:132`: « Solde patient » reads
`TotalPlanned − Σ AmountPaid` while « Créances » reads `Σ(Amount − AmountPaid)`, and `AddItems`/`RemoveItem` can
pull the two apart (`reconcile-money`'s `plan-schedule-balances` exists for it). A plan row therefore carries
`plan.Outstanding` — the header's own formula. Échéances are **how you pay**, not what you owe, which is why
`PayableInstallmentId` and `PayableRoom` are separate fields rather than the row's amount.

### 2. No new money writer — the band is a launcher

Cash lives in exactly two ledgers (`caisse-extrait/notes.md:35`: « not to teach a fourth read about a fourth
source »). « Encaisser » mounts the **same** `PaymentModal` and `InstallmentPaymentModal` that `/factures` and
the devis workspace use, so a payment recorded from the patient file goes through `RecordPaymentCommand` /
`RecordInstallmentPaymentCommand` like any other and reaches la caisse, « Créances », the dashboard and its own
reçu without this page knowing they exist. It also inherits `useDirtyGuard`, `parseAmountInput`,
`todayLocalIso()`, the shared cheque payload builder and the receipt-in-toast.

⚠️ **A generic « Régler le solde » box was rejected by precedent**: `follow-up/ux-hot-path-audit.md:117` killed
cash-with-no-invoice — « the capability is correctly on the invoice ».

⚠️ **The document is re-read on open**, never taken from the band's snapshot: both dialogs bound themselves on
the live « reste », so a colleague settling the note in the meantime would otherwise prefill a figure the server
now refuses — a refusal for a number this page had just printed. The devis path also **re-picks** the oldest
payable échéance from the fresh aggregate rather than trusting `payableInstallmentId`.

### 3. « Travail non facturé » reuses `VisitClosureRules` rather than adding a rule

A fiche saved with « Payé » = 0 raises **no note** (`DentalRecordAutoBilling.cs:58`), so delivered work can sit
with nothing in any money read while the « Actes dentaires » tab shows an amber « Reste » for it — the page
contradicting itself.

⚠️ **But that state already has an owner.** `VisitClosureStep.Billing` (« Combien a-t-il payé ? ») is exactly
it, and `/a-cloturer` shows it unbounded by default. What was missing is that it was not on the *patient's*
page. So the second group is `GET /appointments/to-close?patientId=…&days=` — one new **filter parameter**
beside the `doctorId` that was already there — and it inherits for free the four terms a hand-rolled rule would
have nagged about:

| Legitimately not a gap | The term that already knows |
|---|---|
| contrôle gratuit | `FicheCost == 0` (`VisitClosure.cs:19` says `0` is meaningful) |
| séance carried by a devis | `CoveredByPlan` |
| « rien à facturer » (mandatory motif) | `NothingToBill` |
| « retirée » | `Disregarded` |

⚠️ It is **outside the total** and takes « Facturer », not « Encaisser ». Folding it in would break the five
money reads `MoneyReadConsistencyTests` holds equal. Its honest boundary: the rule is keyed on the **visit**, so
a fiche recorded with no appointment is out of scope — stated rather than silently mis-reported.

## Two defects fixed on the way

**`isPlanBilled` was wider than the server's rule.** `plan-next-action.ts:48` was
`plan.linkedInvoiceId != null`, but `TreatmentPlanMappingExtensions.cs:40` deliberately keeps
`planIsBilled = hasInvoice && RepresentsItsPlan(invoice.Status)` separate, and the server sets `LinkedInvoiceId`
for Draft and Cancelled notes too. Pinned by `Cancelling_The_Bridge_Invoice_Returns_The_Plan_To_The_Balance`: a
cancelled bridge returns the plan's balance. So a devis of 1 000 with 400 collected and a cancelled bridge note
showed « Reste sur la note 1 000,000 DT » in the strip while the header said « Solde dû 600,000 DT » — two
figures about one devis, on one screen, naming the wrong document. It reads `linkedInvoiceStatus` now.

**`coarse:py-3` was inert on my own buttons.** `Button size="sm"` is `h-8`, a **fixed** height, so padding
cannot grow it — the buttons stayed 32 px on a finger, under the § 2 floor, and no check could see it. The
repo's own pattern is `coarse:h-11` (`appointment-acts-picker.tsx:1218`). Found by the device pass, which is the
only thing that could have.

## The card tree's cap

⚠️ It caps at **2 rows** with « Afficher les N autres documents », and it is a **row-count slice, not a
`max-h`**: `patient-undocumented-visits`' own docstring says a px cap is honest *there* because every row is one
line, and these are not (title · covers · two or three money fields · a full-width action), so no constant
describes N of them. Nothing factual is deferred — the heading states the whole total and the full document
count. The table tree shows every row and needs no expander, so the control is `CARDS_ONLY_LG`-gated rather
than behind a JS width test: rotating a tablet changes the presentation, never the state.

The `lg:` hinge (not `md:`) is what keeps « Actions » reachable on an iPad portrait — this table is nested a
`Card`-inside-a-`TabsContent` deep, where the box measures ~451 px at 820 px, and `Button` is
`whitespace-nowrap shrink-0` so that column cannot give width back.

## The guard

**N29 `debt-rule-has-no-typescript-copy`** — derived from the rule, not from a file list: it fails on a local
`carriesDebt`/`billedPlanIds`/`debtBearing`, or on a hand-written disjunction over the debt-bearing statuses,
anywhere outside `plan-next-action.ts` (which reads the server's per-plan figures rather than re-deriving). Two
tripwires: zero candidates scanned, and the owner file gone. **Proven red** on a throwaway probe before being
trusted — it caught both shapes.

And the invariant that keeps the decomposition honest for good lives inside
`MoneyReadConsistencyTests.SoldePatientAsync`, not in a case of its own: **every** fixture in that file —
bridged, Draft-bridged, cancelled-bridge, unbridged, Draft-with-a-schedule — now asserts
`Σ lines.outstanding == totalOutstanding`, both per-track figures, and that no settled row is ever listed. A
fixture added later is covered without anyone remembering to.

## Verified

- **Wire**: `1800 = 230 (invoices) + 1570 (plans)`, and the four lines sum to `1800`. Notes carry `isOverdue:
  false` and no échéance target; devis carry theirs. An auto-raised échéance on unfinished work is **not** late
  (`InstallmentLateness` doing its job); a future échéance is not late either.
- **Both payment paths, end to end, without recording money**: the note row opened « Facture 2026-0008 — reste
  dû 100,000 DT » and the devis row « Échéance du 4 sept. 2026 — reste dû 1 500,000 DT », each prefilled in
  French format with today's date.
- `reconcile-money`: no drift, before and after.

## Deliberately not done

- **No per-act payment.** `InvoiceLine` has no payment column and `DentalRecordAct.Id` is regenerated on every
  save, so nothing can key money to an act. A partial payment cannot say which act it settled — the band names
  the work (`covers`) without claiming which part of it is unpaid.
- **No `Features/Billing/Commands`** — it would emit an undeclared realtime key and fail
  `RealtimeResourceResolverTests`. The read is a Query, which emits none.
- **No `[AllowsWithoutSubscription]`** — recording money is correctly 402-gated on a lapsed hosted cabinet.
- The identity strip keeps « Solde dû » (a header should carry the one figure) and the Factures tab keeps every
  document ever raised. The band is the third thing: what is owed **now**, de-duplicated across both tracks,
  with the payment beside each row.
