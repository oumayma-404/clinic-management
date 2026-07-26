# 🦷 Adoption QA Report — Clinic Management, as a Tunisian dentist would live with it

> Manual-QA functional audit from the point of view of a Tunisian dentist evaluating the app for
> daily use. Every workflow was traced through to the database and back (frontend → API client →
> controller → MediatR handler → repository/EF → DB → DTO → render). Findings are verified against
> **source code**, not the CLAUDE.md docs. Evidence is cited as `file:line`.
> Generated 2026-07-24 on branch `feature/windows-desktop-app`.

## Bottom line

This is a genuinely capable, well-built product. The plumbing is unusually clean — **zero dead menu
links, zero orphan screens, zero buttons that hit a 404** across 27 pages and 30 API modules. The
invoice math, the CNAM BS1 bulletin, the odontogram, and the reminder/recall/waiting-list machinery
all close their loops. **But** there are a handful of broken loops that would bite on day one — the
worst of them right in the two areas Tunisian dentistry lives or dies on: **the scheduler** and the
**devis → facture → CNAM money trail**.

---

## Scorecard

| Area | Verdict | Headline |
|---|---|---|
| Agenda & appointments | ⚠️ Partial | No hard double-booking block; recurring series skip reminders/sync |
| Patient records & files | ⚠️ Partial | Dead "record visit" deep-link; emergency contact uncollectable |
| Odontogram & dental records | ✅ Strong | Full FDI chart + bidirectional plan link; visit-prompt mismatch |
| Treatment plans / devis | ❌ Weakest | Quotes counted as debt; no →invoice bridge; "Terminé" unreachable |
| Billing / invoices / payments | ✅ Strong | Math correct; voiding a *paid* note silently loses the cash |
| CNAM / e-invoicing / ordonnance | ✅ Strong | BS1 + El Fatoora work; ordonnance drops the DCI |
| Reminders / recall / waiting list / notifs | ✅ Strongest | All six loops close cleanly |
| Lab / caisse / stock / dashboard | ✅ Good | KPIs correct; stock has no consume/restock action |
| Auth / tenancy / users | ✅ Solid (Local) | Secure login & isolation; onboarding hours discarded |
| Navigation & FE↔API wiring | ✅ Excellent | No dead nav, no orphan routes, no 404s |

---

## A day in the clinic — where the loops break

**8:00 — Open the app.** Today's agenda loads correctly (`use-appointments.ts:22-29` sends true UTC
instants). ✅ But the dashboard's "Rendez-vous du jour" *count* card uses bare wall-clock times with
no `Z` (`use-dashboard-stats.ts:23-24`), so at UTC+1 the card and the list below it can disagree by
an appointment near midnight.

**8:30 — Book a walk-in into a slot that's already taken.** The app lets you. **There is no
server-side conflict check at all** — both `CreateAppointmentCommand` and `UpdateAppointmentCommand`
were read end to end; neither queries for a time clash. The only guard is an amber hint that never
disables Save (`create-appointment-dialog.tsx:857-859`), and it fetches the whole day *without
doctor scoping* (`use-appointment-overlap.ts:44-48`), so in a 2-chair clinic it warns about a
colleague's patient. **Two patients, one slot, no stop.** 🔴

**9:00 — Set up a weekly ortho series.** It creates the rows fine — but `CreateRecurringSeriesCommand`
never calls the reminder scheduler, the Google push, or the notification generator that normal
bookings call (`CreateAppointmentCommand.cs:186-212`). **Recurring patients silently get no SMS
reminders and show "non synchronisé" forever.** 🔴

**11:00 — Chart a new patient's mouth.** The odontogram is genuinely good: 32-tooth FDI chart,
click-to-diagnose, per-clinic procedure catalog, and "Créer un plan depuis l'odontogramme" really
does seed a devis (`odontogram.tsx:114-130` → `treatment-plan-form-modal.tsx:141-160`). ✅

**11:30 — Build the devis (plan de traitement).** Line totals and the échéancier math are correct.
But then the money story falls apart (see the ❌ block below): the quote just drafted — that the
patient hasn't even accepted — **immediately shows up as money owed** in "Solde patient" and inflates
the CNAM estimate.

**14:00 — Patient finished; the bell says "record the visit."** Clicking the post-visit
notification lands on the patient page… and **nothing opens.** Verified: `dashboard-header.tsx:106`
navigates to `/patients/{id}?addRecord=1&appointmentId=…`, but the patient page reads **no** query
params (no `useSearchParams` anywhere in `patients/[id]/page.tsx`). The one-click "record the
finished visit" is dead. 🔴 And even recording the *dental* work manually doesn't clear the prompt —
only creating a *medical document* does (`CreateDentalRecordCommand` has no appointment link), so the
popup keeps nagging.

**16:00 — Invoice and take payment.** This works well — subtotal/TVA/timbre/TTC are all correct
through one authority (`InvoiceCalculator.cs:26-35`), overpayment is refused, invoice numbers are
gapless. ✅ But to invoice the plan work you must **re-type every line** — there's no devis→facture
bridge — and voiding a *paid* invoice silently drops its cash from the day's caisse with no
avoir/credit-note (`InvoiceRepository.cs:92`).

**19:00 — Close the caisse.** Recettes − dépenses is correct (`GetCaisseSummaryQuery.cs:61-72`). ✅
Stock, dashboard KPIs, recall list — all fine.

---

## Broken loops, ranked by impact

### 🔴 Critical — hit in week one

1. **No double-booking prevention.** Single-appointment booking never blocks a time collision; only
   a non-blocking amber hint exists, and it ignores *which* dentist (over-warns in multi-chair).
   — `CreateAppointmentCommand.cs`, `use-appointment-overlap.ts:44-54`

2. **Unaccepted quotes are counted as patient debt.** `GetPatientBillingSummaryQuery.cs:72-77` sums
   every plan that isn't *Cancelled* — including **Draft** quotes and installment-less plans — into
   "Solde dû total" *and* the CNAM estimate. Patients would be shown a balance for treatment they
   never agreed to. — `TreatmentPlan.cs:40`

3. **Recurring-series appointments get no reminders, no Google sync, no notifications.**
   — `CreateRecurringSeriesCommand.cs` (missing the three dispatchers `CreateAppointmentCommand.cs:186-212` has)

4. **The "record the finished visit" bell deep-link is dead** (verified). Lands on the patient page;
   the add-record modal never opens because the page ignores `?addRecord=1`.
   — `dashboard-header.tsx:106` vs `patients/[id]/page.tsx`

### 🟠 Major — real friction / money integrity

5. **"Terminé" (Completed) is unreachable for a treatment plan.** `TreatmentPlan.Complete()` has no
   caller anywhere — a fully-treated, fully-paid plan is stuck "InProgress" forever, and the FE even
   has a dead `Completed` branch. — `TreatmentPlan.cs:174-183`, `treatment-plans-table.tsx:276`

6. **A plan with no échéancier can never record a payment** — `RecordInstallmentPayment` requires an
   `installmentId`, so `Outstanding` stays at the full total permanently.
   — `RecordInstallmentPaymentCommand.cs:64`

7. **No devis→invoice bridge**, and using both circuits double-counts the same acts.
   — Invoices feature has zero `TreatmentPlan` references

8. **Voiding a *paid* invoice silently erases its collected cash** from recettes/caisse with no
   avoir/refund trail. The UI even allows cancelling a paid note (`invoices-table.tsx:299`).
   — `InvoiceRepository.cs:92`

9. **Google Calendar controls are shown to every user but the endpoints are AdminOnly** → a
   secretary clicking connect/push gets a 403 → generic "Échec" toast.
   — `GoogleCalendarController.cs:62,177,204` vs `appointments/page.tsx:205-229`

10. **Post-visit prompt only closes via a medical *document*, not by charting the *dental record***
    (the natural clinical action). — `CreateDentalRecordCommand.cs` has no appointment link;
    `CreateMedicalDocumentCommand.cs:274-307` is the only closer

### 🟡 Minor / latent — annoyances & data hygiene

11. **Emergency contact** is displayed everywhere but **no form ever collects it** — permanently
    blank. — `edit-patient-dialog.tsx` (no field), `GetPatientQuery.cs:75-76` (maps it)
12. **Ordonnance drops the DCI** — the medication picker captures the active ingredient but the
    printed PDF/Word/preview omit it. — `PdfGenerationService.cs:652-665`
13. **`/documents/honoraires` editor is a reachable dead-end** — PDF generation now rejects that type
    and it shows **€ not DT**. (Primary path avoids it via the invoice launcher.)
    — `PdfGenerationService.cs:46-49`, `document-editor-content.tsx:315`
14. **Stock has no consume/restock action** — `AddStock`/`RemoveStock` are dead code (zero call
    sites); quantity only changes by absolute overwrite, no movement audit. Also: an item created
    *already low* never notifies, and the **stock page doesn't live-refresh** (docs claim it does).
    — `StockItem.cs:50-73`
15. **No delete-patient path** exists (entity/repo support it; no endpoint/UI).
    — `PatientsController.cs`
16. **Onboarding wizard silently discards working hours** (Step 3 collects them; the setup call never
    sends them). Recoverable in Settings. — `setup-wizard.tsx:121-129` vs `CreateClinicCommand.cs:12-30`
17. **"Governorate" on the patient form is free text**, not the Tunisian dropdown the docs advertise.
    — `edit-patient-dialog.tsx:671-679`
18. **"Total encaissé" (factures) attributes by IssueDate; caisse uses PaidOn** → the two "collected"
    figures can disagree. — `GetInvoiceRevenueQuery.cs:50-55`
19. **Odontogram diagnose can't set surfaces** despite full backend support. — `odontogram.tsx:372-389`
20. Waiting-list promote-link failure can orphan/double-book; enabling a reminder channel doesn't
    backfill already-booked appointments; caisse date-boundary inclusive/exclusive + local/UTC
    midnight mismatch (all harmless at UTC+1 today).

---

## What genuinely works

- **Wiring is airtight** — every menu item, every screen, every API call resolves. Rare and reassuring.
- **Billing engine is correct** — TVA on HT only, timbre outside the VAT base, overpayment refused at
  the domain, gapless `AAAA-NNNN` numbering, real QuestPDF invoices/receipts.
- **CNAM BS1 bulletin** overlays real reimbursement math (coefficient × VLC × age-rate, 70%/60%) onto
  the genuine `BS1.pdf`, with a live preview. **El Fatoora (TTN)** enqueues on issue, has enable +
  per-invoice status UI, and dispatches offline via the outbox.
- **Odontogram** is proper clinical software: per-clinic procedure catalog, bidirectional plan seeding.
- **Reminders/recall/waiting-list/notification center** — the cleanest subsystem; all loops close,
  correctly no-op until configured, correct effectiveStatus badges.
- **Auth & tenancy (Local)** — lockout before password check, no user-enumeration, PBKDF2, HttpOnly
  cookies, forced-password-change enforced both sides, offline admin-recovery CLI, layered clinic isolation.

---

## Docs-vs-reality surprises

- **Cloud mode has no admin user at all** — `CreateClinicCommand` assigns the Cloud creator `"doctor"`,
  so *every* Cloud admin-gated feature (user management, reminder/catalog writes, backup, WhatsApp
  connect) is unreachable. **Local (the offline-Windows target) is unaffected** since setup mints a
  real admin — but it flatly contradicts the docs' Cloud story. — `CreateClinicCommand.cs:166-168`
- CNAM/medication/dental-act entities are **per-clinic** despite "global reference data" docstrings
  still in the source.
- Invoice PDFs are a **synchronous query**, not `PdfGenerationJob` (which only does medical
  documents) — works, just not as documented.
- CLAUDE.md frames the post-visit prompt and stock realtime as seamless; both have the gaps above.

---

## Verdict

**Adoptable — but the four 🔴 should be fixed before it runs a front desk**, because they touch
scheduling integrity and the money shown to patients. None is architecturally deep:

- **#1 double-booking** — one overlap query in the create/update handler.
- **#3 recurring-series gap** — wire the same three dispatchers the normal path already has.
- **#4 dead deep-link** — read a query param the page already receives.
- **#2 quotes-as-debt** — one status filter (`Accepted`/`InProgress` instead of `!= Cancelled`),
  plus wiring `Complete()` (fixes #5 too).

The 🟠 **devis→facture bridge** is the biggest *product* gap — for a Tunisian practice the
quote-to-invoice-to-CNAM chain is the daily spine, and right now it's three disconnected circuits.

Suggested first fixes (small, high-impact, independently shippable): **#1 double-booking** and
**#2 quotes-counted-as-debt**.
