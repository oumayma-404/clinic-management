# Feature Review: fix-single-dentist-identity

**Status:** INCOMPLETE
**Challenged:** No
**Date:** 2026-07-21
**Parent Branch:** feature/windows-desktop-app
**Merge Base:** 9798b95 (reference); reviewed commit cb49522 (7-fix batch, scoped)
**Review method:** 5 parallel agents adapted to MediatR/`Result<T>` + FE agent.

## Findings

### Finding 1
- **Severity:** Minor
- **Category:** Code Quality
- **File:** api/ClinicManagement.Application/Features/Clinics/Commands/CreateClinicCommand.cs
- **Line:** 291
- **Anchor:** `CreateClinicCommandHandler.CreateLocalFirstRunAsync` (practitioner name validation)
- **Comment:** The #15 guard uses a non-empty `Specialty` purely as the "this is a practitioner setup" trigger and then requires FirstName+LastName — but the failure message says "First name, last name, and specialty are required for the practitioner." The message overclaims: when `Specialty` is empty the guard is skipped (admin-only path), so a specialty requirement is advertised but never enforced. Either also validate `Specialty`, or reword the message/comment so the trigger and the stated requirement match. (Behavior is correct — the guard condition exactly matches the doctor-creation condition, so no nameless Doctor is persisted; only the message is misleading.)

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 0 |
| Minor | 1 |
| Suggestion | 0 |
| **Total** | 1 |
