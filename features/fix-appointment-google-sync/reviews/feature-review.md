# Feature Review: fix-appointment-google-sync

**Status:** INCOMPLETE
**Challenged:** No
**Date:** 2026-07-21
**Parent Branch:** feature/windows-desktop-app
**Merge Base:** 9798b95 (reference); reviewed commit cb49522 (7-fix batch, scoped)
**Review method:** 5 parallel agents adapted to MediatR/`Result<T>` + FE agent.

## Findings

### Finding 1
- **Severity:** Minor
- **Category:** Code Quality / Architecture
- **File:** api/ClinicManagement.Application/Features/Appointments/Commands/CreateAppointmentCommand.cs
- **Line:** 182
- **Anchor:** `CreateAppointmentCommandHandler.Handle` (post-commit Google-sync block)
- **Comment:** The ~35-line fire-and-forget sync block (CreateScope → resolve logger → 5-min CTS → `IInternetProbe` gate → resolve `IGoogleCalendarSyncService` → sync → two catches) is duplicated near-verbatim between the create and update handlers, and embeds a service-locator + unobserved `Task.Run` inside a CQRS handler. Extract into one collaborator (e.g. `IAppointmentGoogleSyncDispatcher.DispatchAsync(appointmentId)`) injected into both handlers — removes the copy-paste and the service-location and gives the background work a single testable home. (The duplication was a deliberate "mirror the update path" per spec; this is the cleanup.)

### Finding 2
- **Severity:** Minor
- **Category:** Code Quality
- **File:** api/ClinicManagement.Application/Features/Appointments/Commands/CreateAppointmentCommand.cs
- **Line:** 200
- **Anchor:** `CreateAppointmentCommandHandler.Handle` (sync catch clause)
- **Comment:** `catch (InvalidOperationException ex) when (ex.Message.Contains("not configured"))` couples control flow to a substring of an exception message — a reworded/localized message would silently reclassify "not configured" as a hard error logged at Error. Prefer a typed signal (a `GoogleCalendarNotConfiguredException`, or an up-front config check). (Mirrors the pre-existing update-path catch; fix both together.)

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 0 |
| Minor | 2 |
| Suggestion | 0 |
| **Total** | 2 |
