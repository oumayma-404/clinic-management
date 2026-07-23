# Feature Specification: French Localization, Branding & Dead-Code Cleanup

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-23
**Scope:** Full
**Feature:** Make the product read as a finished, French, Tunisia-targeted application — remove the English/French split, fix locale formatting and generic branding — and delete the three inert "parallel-universe" subsystems so the codebase matches what actually ships.

## Overview
The app is functionally complete but presents like an unfinished template on the surfaces a dentist touches daily, and it carries three dead subsystems that make features look supported when they are not. This feature is a coherence/credibility pass across every mode (the frontend polish affects Cloud and Local equally). Nothing here changes business logic; it changes user-facing language, formatting, branding, and removes dead code.

Verified problems:

**A. Pervasive French/English split** on daily screens (product is sold as a French, Tunisia-targeted UI):
- Dashboard: "Dashboard", "Welcome back!…", "Today's Appointments / Total Patients / Pending / This Week / Urgent" (`web/app/page.tsx:37-77`) — while two cards in the same grid are French ("Recettes (mois)", "Créances", `:80,87`).
- Account menu: "My Account / Settings / Change password / Log out" (`web/components/dashboard-header.tsx:233-252`).
- Patients page (`web/app/patients/page.tsx:37-69`), Appointments tabs/buttons (`web/app/appointments/page.tsx:171-206`), Records (`web/app/records/page.tsx:123-166`), Stock (`web/app/stock/page.tsx:74-81`), Login (`web/app/login/page.tsx:42-140`) — all English.
- Form-validation strings are English while success toasts are French **in the same dialog**: `web/components/edit-patient-dialog.tsx:301-313` ("First name is required") vs `:433,484` ("Patient créé avec succès"); `web/components/create-appointment-dialog.tsx:308-338` ("Please select a patient", "End time must be after start time").

**B. Wrong-locale date/number formatting** for a dd/mm/yyyy market: patient detail formats `MMM d, yyyy` / `h:mm a` and shows "N/A" / "Not provided" (`web/app/patients/[id]/page.tsx:93,100,111,127`); records search formats DOB en-US (`web/app/records/page.tsx:96-100`).

**C. Generic / leaked branding:** the product is "MediCare Clinic" (`web/app/layout.tsx:17`, `web/components/dashboard-sidebar.tsx:72`, `web/app/login/page.tsx:42`), and `metadata.generator: "v0.app"` (`web/app/layout.tsx:19`) leaks the scaffolding tool into page source. Two `clinic@example.com` placeholders exist (`clinic-settings.tsx:666`, `setup-wizard.tsx:418`).

**D. Three inert "parallel-universe" subsystems** that make the code (and any future maintainer) believe features exist:
- **Domain-events pipeline — DELETE.** Aggregates raise events (`api/.../Domain/Entities/Appointment.cs:69,82,157`; `Patient.cs:125`) into `AggregateRoot._domainEvents`, but `ApplicationDbContext.SaveChangesAsync` (`:218-222`) never drains them and there are zero `INotificationHandler` implementations. Notifications/reminders are already produced inline (`ReminderScheduler`, `NotificationGenerator`), so the whole `Domain/Events/*` + dispatch scaffolding is dead weight.
- **Google→App recurring sync job — remove the dead scaffolding.** The recurring registration is removed at boot and commented out (`Program.cs:471` `RemoveIfExists("sync-google-calendar")`, `:474-477` commented `AddOrUpdate`). The `GoogleCalendarSyncJob` class is only referenced by that dead registration (the manual `sync-from-google` endpoint calls `IGoogleCalendarSyncService` directly). Remove the commented block and the orphaned job class; keep the manual endpoint.
- **`RecurringAppointment` orphan — LEAVE IN PLACE (do not cut).** The entity/table/FK exist with no code. It is intentionally **not** removed here because it is the foundation for the recurring-appointments feature in `features/clinical-workflow-depth`. Called out so cleanup does not delete it.

**E. Documentation drift.** The root `CLAUDE.md` (and sub-guides) document a smaller, different app — they never mention the billing, CNAM, treatment-plan, or El Fatoora e-invoicing subsystems, which are among the deepest, fully-wired parts of the product. Stale docs are how real features drift into a "parallel universe." Update the map.

**F. Minor UX:** the dashboard KPI row uses `lg:grid-cols-7` (`web/app/page.tsx:42`) — seven cards on one row are cramped on a laptop; the login CTA "Have a clinic code? → Create an account" (`login/page.tsx:132-135`) conflates "have a code" with "create account".

## What Changes

### Frontend — full French localization (problem A)
- Translate all remaining English user-facing strings to French on: dashboard (`app/page.tsx`), patients (`app/patients/page.tsx`), appointments (`app/appointments/page.tsx`), records (`app/records/page.tsx`), stock (`app/stock/page.tsx`), login (`app/login/page.tsx`), and the account menu (`dashboard-header.tsx:233-252`).
- Translate all **form-validation** strings to French so they match the already-French success toasts, at minimum in `edit-patient-dialog.tsx` and `create-appointment-dialog.tsx`, and audit the other dialogs for the same split.
- Result: no English error/label/heading/button text renders anywhere in the UI in either auth mode. (This complements the FE localization already done in `graceful-error-handling`, which covered error/network strings; this pass finishes the labels/headings/validation.)

### Frontend — locale-correct formatting (problem B)
- Format all dates as `dd/mm/yyyy` and times/currency for the Tunisian locale (`fr-TN`) across patient detail (`app/patients/[id]/page.tsx:93,100,111,127`) and records search (`app/records/page.tsx:96-100`); replace "N/A" / "Not provided" with French equivalents ("Non renseigné").
- Prefer a single shared date/number formatting helper so the convention is consistent and not re-implemented per screen.

### Frontend — branding (problem C)
- Replace "MediCare Clinic" with the real product/clinic brand everywhere it is hardcoded (`layout.tsx:17`, `dashboard-sidebar.tsx:72`, `login/page.tsx:42`); where the displayed name should be the clinic's own name, source it from clinic settings rather than a constant.
- Remove `metadata.generator: "v0.app"` from `app/layout.tsx:19`.
- Leave input `placeholder=` hints as-is (they are legitimate), but the two `clinic@example.com` example values in `clinic-settings.tsx:666` / `setup-wizard.tsx:418` should read as obvious placeholders, not defaults.

### Backend / codebase — delete dead subsystems (problem D)
- Remove the domain-events subsystem: `Domain/Events/*`, the `IDomainEvent`/`AggregateRoot._domainEvents` raise calls (`Appointment.cs:69,82,157`, `Patient.cs:125`), and any dispatch scaffolding in `SaveChangesAsync`. Verify no handler or test depends on it before removal.
- Remove the commented-out `sync-google-calendar` recurring registration (`Program.cs:471-477`) and the now-orphaned `GoogleCalendarSyncJob` class; keep `IGoogleCalendarSyncService` and the manual `sync-from-google` endpoint.
- Do **not** touch `RecurringAppointment` (reserved for the clinical-depth feature).

### Documentation (problem E)
- Update the root `CLAUDE.md` and the nearest sub-`CLAUDE.md` files to document the billing / invoicing / payments, CNAM nomenclature + BS1 bulletin + reimbursement, treatment plans + devis + installments, and El Fatoora (TTN) e-invoicing subsystems, and to correct the "active jobs" list (`NotificationJob` **and** `EInvoiceOutboxJob` are the active recurring jobs). Note the domain-events removal.

### Frontend — minor UX (problem F)
- Reflow the dashboard KPI cards so they are readable on a laptop (e.g. responsive `grid-cols-2/3/4` rather than a fixed 7-wide row) — `app/page.tsx:42`.
- Reword the login CTA so "have a clinic code" clearly routes to *joining* a clinic, not "create an account" — `login/page.tsx:132-135`.

## Acceptance Criteria
- **AC-1 (no English UI):** No English user-facing text (labels, headings, buttons, menu items, validation messages, empty/placeholder text) renders on any screen in either auth mode. Spot-verified on dashboard, patients, appointments, records, stock, login, the account menu, and the patient/appointment dialogs.
- **AC-2 (validation in French):** Form-validation messages are French and consistent with the French success toasts in the same dialog (e.g. `edit-patient-dialog`, `create-appointment-dialog`).
- **AC-3 (locale formatting):** All dates display as `dd/mm/yyyy`, times and currency in the `fr-TN` convention; no `MMM d, yyyy` / `h:mm a` / en-US formatting remains; "N/A"/"Not provided" replaced with French.
- **AC-4 (branding):** No "MediCare Clinic" string remains hardcoded; the app shows the real brand / the clinic's own name; `generator: "v0.app"` is gone from page metadata/source.
- **AC-5 (dead code gone):** The domain-events subsystem and the orphaned Google→App recurring-job scaffolding are removed; the solution builds with **0 errors / 0 warnings** and all existing tests pass. `RecurringAppointment` is untouched.
- **AC-6 (docs match reality):** Root `CLAUDE.md` documents billing/CNAM/treatment-plans/e-invoicing and the correct active-jobs list; the domain-events removal is reflected.
- **AC-7 (UX polish):** The dashboard KPI row is readable on a standard laptop width; the login CTA wording is unambiguous.
- **AC-8 (no behavior change):** No business logic, endpoint, or data behavior changes; this is presentation + dead-code only. Existing E2E/integration/authorization tests still pass.

## API Contract
None. No endpoints added, removed, or reshaped.

## Data / Schema Changes
None. (Deleting the domain-events code does not touch the schema; `RecurringAppointment`'s table is intentionally retained.)

## Out of Scope
- Backend business-message translation to French (backend `Result.Failure` messages remain English per the decision recorded in `features/graceful-error-handling`; a future code→FR table can address them).
- Any new feature or new screen — this is polish + cleanup only.
- Building recurring appointments / recall / scheduling depth — see `features/clinical-workflow-depth`.
- The Cloud security/tenant work — see `features/cloud-security-and-tenant-isolation`.
- A full i18n framework / language switcher: the product is French-only; strings are localized in place, not extracted into a translation system, unless one already exists.

## Edge Cases (Critical only)
- **Clinic-name vs product-name:** where "MediCare Clinic" was standing in for the *clinic's* name (sidebar/header), the replacement must come from clinic settings and fall back gracefully when the clinic name is empty (first-run/setup) — no blank or "undefined" brand.
- **Date formatting with missing values:** patient records with null DOB / no last-visit must render the French "Non renseigné", not a crashed/`Invalid Date` formatter.
- **Dead-code removal safety:** before deleting `Domain/Events/*`, confirm nothing (including tests, `NotificationGenerator`, or `ReminderScheduler`) references `AggregateRoot.DomainEvents` or an `IDomainEvent` type; the raise-call removals must not change any current side effect (there are none today).
- **Mixed-locale regression:** the two already-French dashboard cards ("Recettes", "Créances") must stay French and consistent with the newly-translated neighbors — no double-translation or English reintroduced.
