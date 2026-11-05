# « non synchronisé » shows on every visit of a cabinet that never connected Google

**Feature:** google-calendar (seen on demo-cabinet) · **Type:** ux · **Priority:** low · **Created:** 2026-11-03

## What happens
- `components/appointment-calendar.tsx` `renderSyncControls` draws a warning badge « non synchronisé » (and, for an
  admin, the « Envoyer » push button) on every open visit whose `isSyncedToGoogle` is false.
- It never asks whether the cabinet has a Google connection at all, so a cabinet that never connected — the demo
  included — wears a warning on every visit of the agenda, and the admin's push button can only fail.

## Why it was not fixed in demo-cabinet
- Product-wide agenda behaviour, off the demo story's plan.
- The comment above it says the badge is shown to **everyone** on purpose, while `GET /googlecalendar/status` is
  read by admins only on `app/appointments/page.tsx` (its class policy is `AdminOnly`, `status` itself
  `AnyClinicRole` per `api/ClinicManagement.API/CLAUDE.md`). Gating on the page's `isGoogleCalendarAuthorized` would
  hide the badge from reception even when connected — the decision the comment records.

## Suggested fix
- Serve « is this cabinet connected? » to every role (the `status` action is already `AnyClinicRole`, or carry a
  flag on an existing clinic read) and render the badge + push only when connected.
- Check `/appointments` at 1440 and 390 as an admin and a secretary, connected and not.
