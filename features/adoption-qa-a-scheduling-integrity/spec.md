# Spec: Adoption QA — Batch A (scheduling integrity)

**Status:** APPROVED
**Type:** Small (forced multi-item pass — user fed the full adoption-QA blueprint)
**Created:** 2026-07-24
**Scope:** Full
**Branch:** feature/windows-desktop-app (existing)
**Feature:** Close the scheduling loops flagged 🔴 in DENTIST_ADOPTION_QA_REPORT.md — double-booking, recurring-series parity, Google-control gating, dashboard timezone.

## Overview
The scheduler currently lets two patients take one slot, silently drops all side effects for recurring series, shows Google Calendar controls to users who get a 403, and miscounts today's appointments near midnight. All four are correctness fixes with an in-repo pattern to copy — no new endpoints, no schema.

## What Changes
- **A1 — Double-booking block.** `CreateAppointmentCommand` and `UpdateAppointmentCommand` reject a save that overlaps an existing non-cancelled appointment for the **same doctor** in the same clinic. FE: `use-appointment-overlap.ts` scopes by `doctorId`; the create dialog disables Save on a hard same-doctor clash (soft/other-doctor overlaps keep the amber hint). Copy the overlap query + `Overlaps()` helper already in `CreateRecurringSeriesCommand.cs:147-176,202-203`.
- **A2 — Recurring-series parity.** `CreateRecurringSeriesCommand` fires the same three post-commit side effects the single path has (`CreateAppointmentCommand.cs:171-188,212`): in-app "created" + ~24h reminder + post-visit review (`INotificationGenerator`), SMS/WhatsApp (`IReminderScheduler`), Google push (`IAppointmentGoogleSyncDispatcher`). Per-occurrence, best-effort.
- **A3 — Google controls admin-gated.** In `app/appointments/page.tsx:205-232` and `appointment-calendar.tsx` push control, render connect/import/push only when `useSession().user?.role === "admin"` (endpoints are `AdminOnly`).
- **A4 — Dashboard day-count TZ.** `use-dashboard-stats.ts:7` sends UTC instants (`toISOString()`) like `use-appointments.ts:22-29`, so the count card and the agenda agree at UTC+1.

## Acceptance Criteria
- **AC-1:** Creating an appointment whose time overlaps an existing non-cancelled appointment for the same doctor/clinic is rejected with a distinct "créneau déjà pris" error; no row is created.
- **AC-2:** Updating an appointment into an overlapping slot is rejected; the appointment being edited is excluded from its own clash check.
- **AC-3:** Overlap is scoped by `DoctorId` — an overlap with a *different* doctor does not block booking.
- **AC-4:** The create dialog disables Save on a hard same-doctor clash; other-doctor/soft overlaps keep the non-blocking amber hint.
- **AC-5:** Each recurring-series occurrence gets an SMS/WhatsApp reminder, a post-visit review, an in-app "created" notification, and a Google push — and shows the same synced status as a normal booking.
- **AC-6:** Google connect/import/push controls appear only for `role === "admin"`; other roles never see them.
- **AC-7:** The dashboard "Rendez-vous du jour" count matches the agenda list for appointments near local midnight (UTC+1).

## Out of Scope
- Room/chair-level (non-doctor) conflict rules; overbooking overrides.
- Relaxing the backend Google policy (we gate the UI, not the policy).

## Edge Cases (Critical only)
- Half-open overlap: appointments that merely touch at a boundary (`aStart == bEnd`) do **not** clash.
- A2 side effects are best-effort — one occurrence's failure must not abort the series or roll back created rows.
- Update path: changing only duration (not start) must still re-check the clash.
