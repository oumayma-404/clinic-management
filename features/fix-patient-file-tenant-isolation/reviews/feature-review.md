# Feature Review: fix-patient-file-tenant-isolation

**Status:** INCOMPLETE
**Challenged:** No
**Date:** 2026-07-21
**Parent Branch:** feature/windows-desktop-app
**Merge Base:** 9798b95 (reference); reviewed commit cb49522 (7-fix batch, scoped)
**Review method:** 5 parallel agents adapted to MediatR/`Result<T>` + FE agent.

## Findings

### Finding 1
- **Severity:** Minor
- **Category:** Business Logic / Data Integrity
- **File:** api/ClinicManagement.Application/Features/Files/Commands/DeletePatientFileCommand.cs
- **Line:** 65
- **Anchor:** `DeletePatientFileCommandHandler.Handle`
- **Comment:** The single-file delete still deletes the blob (`_fileStorage.DeleteAsync`) BEFORE staging the DB row removal and committing (`SaveChangesAsync`). If the commit fails after the blob delete succeeds, the `PatientFile` metadata row survives while its blob is gone — an orphaned record whose later download 404s. This contradicts the DB-first/blob-after ordering this same commit introduced for `DeletePatientFolderCommand` (#18/AC-3). Apply the same ordering here: stage the DB delete, `SaveChangesAsync`, then delete the blob best-effort (logged).

### Finding 2
- **Severity:** Suggestion
- **Category:** Code Quality
- **File:** api/ClinicManagement.Application/Features/Files/Commands/DeletePatientFileCommand.cs
- **Line:** 40
- **Anchor:** clinic-resolution guard (repeated across the 5 Files handlers)
- **Comment:** `clinicResult.Error ?? "Unable to resolve current clinic"` is a duplicated literal across the five Files handlers, and the `?? "..."` fallback is effectively dead (a failed `Result<Guid>` always carries an `Error`). Return `clinicResult.Error` directly or use a shared constant. (Matches an existing repo pattern, so low priority.)

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 0 |
| Minor | 1 |
| Suggestion | 1 |
| **Total** | 2 |
