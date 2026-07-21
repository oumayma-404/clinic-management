# Feature Review: fix-appointment-lifecycle

**Status:** INCOMPLETE
**Challenged:** No
**Date:** 2026-07-21
**Parent Branch:** feature/windows-desktop-app
**Merge Base:** 9798b95 (reference); reviewed commit cb49522 (7-fix batch, scoped)
**Review method:** 5 parallel agents adapted to MediatR/`Result<T>` + FE agent.

## Findings

### Finding 1
- **Severity:** Suggestion
- **Category:** Code Quality
- **File:** api/ClinicManagement.Application/Features/Appointments/Commands/UpdateAppointmentCommand.cs
- **Line:** 229-231 (the `becameReactivated`/`dateChanged` block, ~247)
- **Anchor:** `UpdateAppointmentCommandHandler.Handle` (post-commit notification branch)
- **Comment:** The comment "A cancelled→scheduled reactivation calls `Reschedule(sameDateTime)`…" is now stale — this commit changed the reactivation path to call the new `Appointment.Reactivate(...)`, not `Reschedule`. The `dateChanged` guard reasoning is still valid, but the method reference is wrong and sits in the exact branch the fix altered. Update the comment to say `Reactivate`.

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 0 |
| Minor | 0 |
| Suggestion | 1 |
| **Total** | 1 |
