# Feature Specification: Clinical Workflow Depth (Recall, Recurring, Scheduling, Caisse)

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-23
**Scope:** Full
**Feature:** Close the clinical/practice-management gaps a Tunisian dentist expects — patient recall, recurring appointments, per-practitioner/chair scheduling, a waiting list, dental-lab work orders, and expense/caisse accounting — so the product covers a real clinic's day, not just the core record.

## Overview
The core clinical product is strong and fully wired (patients, persistent odontogram, dental records/actes, treatment plans + devis, TND invoicing + payments + receipts, CNAM BS1 + reimbursement, e-invoicing, prescriptions with a drug catalog, stock, reminders). The gaps that would stop a dentist from buying are concentrated in **scheduling depth and practice operations**. This feature adds six capabilities, ordered by value. They are grouped into one spec per the request; each sub-feature has its own acceptance criteria and can be implemented/verified independently.

Verified starting points:
- **Recall/relance is absent.** The only reminders are appointment reminders X hours before a booked slot (`ReminderScheduler`); nothing flags patients due for a 6-month checkup or détartrage.
- **Recurring appointments are an orphan.** `api/.../Domain/Entities/RecurringAppointment.cs`, the `RecurringAppointments` table, and `Appointment.RecurringAppointmentId` FK exist, but there is no command/handler/controller/UI; `CreateAppointmentCommand.cs:142` always passes `recurringAppointmentId: null`.
- **Scheduling is single-lane.** `Appointment.DoctorId` is a loose `string?` (not an FK to `Doctor`); `GetAppointmentsQuery.cs` filters only by clinic + date range (no doctor filter); the calendar has no per-practitioner lanes/filter; working hours are per-clinic only (`api/.../Domain/Entities/Clinic.cs:34` `WorkingHoursJson`), not per-dentist. The create dialog already picks a real doctor (`web/components/create-appointment-dialog.tsx:110`).
- **No waiting list.** Zero support for parking a patient who wants an earlier slot or a day-of arrival queue.
- **No dental-lab tracking.** No entity for prosthetics work sent to a prothésiste (sent/due/cost/status).
- **No expenses / caisse.** Only revenue and receivables are computed (`GetInvoiceRevenueQuery`, `GetReceivablesQuery`); there is no expense/purchase/petty-cash tracking, so no net-profit or daily-cash view.

## What Changes

### 1. Patient recall / relance (highest value)
- Add a recall concept: for each patient, a next-recall due date driven by a configurable interval (e.g. 6 months) and/or the last completed visit, plus an optional recall reason (checkup, détartrage, follow-up).
- Add a "patients à relancer" query/list (due or overdue), surfaced on the dashboard and/or a dedicated screen, with the ability to act (book an appointment, mark contacted/snoozed).
- Reuse the existing reminder channel plumbing (`IReminderChannelSender`) so a recall can optionally be sent by SMS/WhatsApp, gated by connectivity and per-clinic reminder settings, distinct from booking reminders.

### 2. Recurring / series appointments
- Implement the existing `RecurringAppointment` aggregate: a command/handler + controller endpoint to create a **series** (frequency, interval, count/until, weekday/time), which expands into individual `Appointment` rows linked via the existing `RecurringAppointmentId` FK.
- Add edit/cancel semantics for "this occurrence" vs "this and following" vs "the whole series".
- Frontend: a "répéter" option in the create-appointment dialog and a way to view/cancel a series. `CreateAppointmentCommand` stops always passing `recurringAppointmentId: null` when part of a series.

### 3. Per-practitioner / per-chair scheduling
- Make `Appointment.DoctorId` a proper FK to `Doctor` (migrate existing loose string values).
- Add a doctor filter to `GetAppointmentsQuery.cs` and a per-practitioner (and, if rooms/chairs are modeled, per-chair) view/lane + filter in the calendar UI.
- Add per-dentist working hours (extend beyond the clinic-wide `Clinic.WorkingHoursJson`) so availability/validation is per practitioner.
- (Optional, if chairs are in scope) a lightweight operatory/room/chair entity to book against; otherwise explicitly deferred.

### 4. Waiting list / salle d'attente
- Add a waiting-list entry (patient, desired timeframe/practitioner, priority, note) and a day-of arrival/queue view; support promoting an entry into a booked appointment when a slot frees up.

### 5. Dental-lab / prosthetics work-order tracking
- Add a lab work-order entity (patient, linked act/tooth, prothésiste, sent date, expected/received date, cost, status: sent / in-progress / received / fitted), with a per-patient and clinic-wide list.

### 6. Expense / caisse accounting
- Add an expense/cash-out entry (date, category, amount TND, payment method, note) and integrate it with the existing revenue/receivables so the clinic gets a **caisse (daily cash)** view and a net (revenue − expenses) figure.

## Acceptance Criteria

**Recall (1)**
- **AC-1.1:** Each patient has a computed/next recall due date and optional reason; the interval is configurable per clinic (default 6 months).
- **AC-1.2:** A "patients à relancer" list shows due/overdue patients, scoped to the clinic, with actions to book, mark contacted, or snooze; it updates as visits complete.
- **AC-1.3:** A recall can optionally be sent via the existing SMS/WhatsApp channel, connectivity-gated and using per-clinic reminder settings, and is distinguishable from a booking reminder in the outbox.

**Recurring appointments (2)**
- **AC-2.1:** An authenticated user can create a recurring series (frequency, interval, end condition); the correct individual `Appointment` rows are generated and linked by `RecurringAppointmentId`.
- **AC-2.2:** Editing/cancelling supports "this occurrence", "this and following", and "whole series", with the expected rows affected.
- **AC-2.3:** Series creation respects working hours and does not create appointments in the past; conflicts are surfaced, not silently created.

**Scheduling depth (3)**
- **AC-3.1:** `Appointment.DoctorId` is an FK to `Doctor`; existing appointments migrate without data loss.
- **AC-3.2:** Appointments can be filtered by practitioner in the API and the calendar; a multi-dentist clinic can view a per-practitioner agenda.
- **AC-3.3:** Working hours can be set per dentist and are honored in availability/validation; the clinic-wide hours remain a fallback.

**Waiting list (4)**
- **AC-4.1:** A waiting-list entry can be created, listed (clinic-scoped), prioritized, and promoted into a booked appointment; promotion removes it from the list.

**Dental lab (5)**
- **AC-5.1:** A lab work order can be created against a patient (and optionally a tooth/act), tracked through its statuses with sent/due/received dates and cost, and listed per patient and clinic-wide.

**Caisse / expenses (6)**
- **AC-6.1:** An expense can be recorded (date, category, amount TND, method); the billing/dashboard view shows a caisse (daily cash in/out) and a net figure combining revenue and expenses.

**Cross-cutting**
- **AC-X.1:** Every new entity is clinic-scoped and respects the tenant-isolation model (fail-closed once `features/cloud-security-and-tenant-isolation` lands); no cross-clinic leakage.
- **AC-X.2:** All new UI is French and uses `fr-TN` date/number formatting (consistent with `features/french-localization-and-cleanup`).
- **AC-X.3:** Build is 0 errors / 0 warnings; new behavior is covered by integration tests; existing tests pass.

## API Contract
New endpoints (thin MediatR pass-throughs on `ApiControllerBase`, following existing conventions), indicatively:
- Recall: `GET /api/patients/recalls` (due/overdue), `POST/PUT` to set interval / mark contacted / snooze.
- Recurring: `POST /api/appointments/recurring` (create series), `PUT/DELETE` with an occurrence-scope parameter.
- Scheduling: `GET /api/appointments?doctorId=…` (extend existing), doctor working-hours GET/PUT.
- Waiting list: `GET/POST/PUT/DELETE /api/waiting-list` (+ promote-to-appointment).
- Lab: `GET/POST/PUT /api/lab-orders`.
- Caisse: `GET/POST /api/expenses`, extend billing-summary with net/caisse.
Exact routes finalized at implementation; all return the canonical `{ error }` failure shape.

## Data / Schema Changes
- **Recall:** recall interval/next-due + reason (on `Patient` or a small recall entity) + a recall reminder type.
- **Recurring:** activate the existing `RecurringAppointment` table (add series-definition fields if missing: frequency, interval, end condition); no new table if the existing one suffices.
- **Scheduling:** `Appointment.DoctorId` → FK to `Doctor` (migration to backfill existing string values); per-dentist working hours storage; optional chair/room entity.
- **Waiting list:** new `WaitingListEntry` table.
- **Lab:** new `LabWorkOrder` table.
- **Caisse:** new `Expense` table.
All new tables carry `ClinicId` and EF configuration consistent with existing aggregates; each ships with an EF migration.

## Out of Scope
- **Periodontal charting** (pocket depths / bleeding / mobility) — explicitly deferred; the odontogram remains condition + surface only.
- Patient-facing portal / online booking / inbound reminder-reply handling (reminders stay outbound-only).
- Multi-site / multi-clinic membership for a single user (`User.ClinicId` stays scalar).
- Full accounting/ledger, supplier management, or payroll — the caisse sub-feature is expense + net-cash only, not a general ledger.
- Any change to the existing billing/CNAM/e-invoicing logic beyond adding the expense/net view.

## Edge Cases (Critical only)
- **Recall vs booked appointment:** a patient with a future booked appointment should not appear as "à relancer"; completing/cancelling a visit must recompute the next-due correctly.
- **Recurring series across DST / clinic-closed days:** series expansion must skip or flag occurrences that land outside working hours or on closed days rather than creating invalid slots; a long/unbounded series needs a sane cap.
- **DoctorId migration:** existing appointments with a null or non-matching `DoctorId` string must migrate to a valid FK (or a clear "unassigned" doctor) without dropping rows; the create dialog's current string values must map cleanly.
- **Waiting-list promotion race:** promoting an entry when the target slot was taken concurrently must fail safely (no double-booking), consistent with existing appointment conflict handling.
- **Caisse currency:** expenses use the same TND millime precision as invoices; the net figure must not mix units or introduce rounding drift against the existing revenue/receivables totals.
- **Tenant scoping of new entities:** every new list/query must be clinic-scoped from day one so it is not a new fail-open surface (coordinate with the tenant-isolation feature).
