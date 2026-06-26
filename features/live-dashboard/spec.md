# Feature Specification: Live Dashboard

**Status:** APPROVED
**Type:** Small
**Created:** 2026-06-25
**Scope:** Full
**Feature:** Replace the dashboard's hardcoded KPI cards and sample appointment list with real, clinic-scoped data.

## Overview
The dashboard home page (`web/app/page.tsx`) currently shows fabricated numbers in its KPI cards and a static list of fake patients in "Today's Appointments". This makes the first screen every user sees look fake and erodes trust. This feature wires the KPI cards to a new lightweight stats endpoint and the appointment panel to the real appointments API, with proper loading/empty/error states.

## What Changes
- Add a backend `GET /api/dashboard/stats` endpoint returning five clinic-scoped counts in one call.
- KPI cards render real values from that endpoint: **Today's Appointments**, **Total Patients**, **Pending**, **This Week's Appointments**, **Urgent** (5 cards).
- "Pending" = count of **all upcoming** appointments (now or later) with status `Pending`.
- "This Week's Appointments" = count of appointments within the current week.
- "Urgent" = count of patients with at least one **active flag**, shown in the existing `urgent` (red) card variant.
- "Today's Appointments" panel (`components/appointment-list.tsx`) lists today's **real** appointments via the existing appointments API (`useAppointments`).
- Remove the fake "+2 from yesterday"-style delta subtitles; replace with static descriptive labels.
- Add loading states (numeric skeleton/spinner for cards; spinner for the list) and an empty state ("No appointments today").

## Acceptance Criteria
- **AC-1:** All five KPI cards display real, clinic-scoped counts from `GET /api/dashboard/stats`; no hardcoded numbers remain in `app/page.tsx`.
- **AC-2:** "Pending" counts every appointment with status `Pending` whose datetime is now or in the future (not limited to today).
- **AC-3:** "This Week's Appointments" counts appointments falling in the current week; "Today's Appointments" card counts today's appointments.
- **AC-4:** "Urgent" counts patients with ≥1 active flag and renders in the `urgent` variant.
- **AC-5:** The "Today's Appointments" panel lists today's real appointments (patient name, time, procedure type, status badge); the static sample array is removed.
- **AC-6:** While data loads, KPI values and the appointment list show a loading state; when there are no appointments today, the panel shows "No appointments today".
- **AC-7:** On API failure the dashboard shows an error (toast) and never displays fabricated numbers (cards show a dash/empty, not fake values).

## API Contract
### GET /api/dashboard/stats
Clinic-scoped (resolved from the JWT via `IClinicContext`, like other controllers). Implemented as a MediatR query handler.
Response 2XX:
```
{
  "todaysAppointments": number,
  "totalPatients": number,
  "upcomingPending": number,
  "thisWeekAppointments": number,
  "urgentPatients": number
}
```
Errors: `401 Unauthorized` (no/invalid token); `400` with the standard `Result` failure shape if the clinic context cannot be resolved.

## Out of Scope
- Notifications panel (`notifications-list.tsx`) and the header bell — separate feature (SF-03).
- Charts/graphs and any historical trend/delta computation ("+N from yesterday").
- Making the cards clickable / drill-down navigation.
- Pagination or filtering of the appointment list.

## Edge Cases (Critical only)
- "Today" / "this week" boundaries follow the same local-day handling already used by `useAppointments` (`startOfDay`/`endOfDay`) so the card counts match the list.
- Appointments with a null `patientId` (walk-ins) still display their `patientName` in the panel.
- A clinic with zero patients/appointments shows `0` on cards (not blank) and the empty state on the panel.
