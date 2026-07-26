# Progress: Adoption QA — Batch A (scheduling integrity)

**Started:** 2026-07-24
**Type:** Small
**Branch:** feature/windows-desktop-app

## Status
- [x] Implementation
- [x] Quality checks (dotnet build 0/0; web tsc 0; next build ok)
- [ ] Tests (handled by /test-small-feature)

## Working tree note (start of session)
Pre-existing unrelated changes excluded from this feature: modified `*/CLAUDE.md` files,
`DENTIST_ADOPTION_QA_REPORT.md`, `FUNCTIONAL_ADOPTION_REVIEW.md`, `api/.../UnitTests/CLAUDE.md`, `packaging/CLAUDE.md`.

## Files Changed
- `api/.../Features/Appointments/Commands/CreateAppointmentCommand.cs` — A1 same-doctor overlap reject + `Overlaps`/`NormalizeUtc` helpers.
- `api/.../Features/Appointments/Commands/UpdateAppointmentCommand.cs` — A1 overlap guard on schedule change (excludes self) + helper.
- `api/.../Features/Appointments/Commands/CreateRecurringSeriesCommand.cs` — A2 post-commit parity (notification/reminder/post-visit/Google) per occurrence; injected IClinicContext/INotificationGenerator/IReminderScheduler/IAppointmentGoogleSyncDispatcher.
- `web/lib/hooks/use-appointment-overlap.ts` — doctor-scoped; returns `{ warning, blocking }`.
- `web/components/create-appointment-dialog.tsx` / `edit-appointment-dialog.tsx` — pass doctorId; disable Save on hard clash; red vs amber.
- `web/app/appointments/page.tsx` / `web/components/appointment-calendar.tsx` — A3 Google controls gated on `role === "admin"`.
- `web/lib/hooks/use-dashboard-stats.ts` — A4 UTC-instant ranges (`toISOString`).

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| `useAppointmentOverlap` return type changed string → `{warning,blocking}` (2 consumers updated) | Required by AC-4 (block Save only on same-doctor clash); both consumers updated in-file. |
