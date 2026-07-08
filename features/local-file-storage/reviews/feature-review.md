# Feature Review: local-file-storage

**Status:** INCOMPLETE
**Challenged:** No
**Date:** 2026-07-08
**Parent Branch:** feature/windows-desktop-app (base: origin/main)
**Merge Base:** working-tree review — Phase-2 changes are uncommitted on top of `feature/windows-desktop-app` HEAD
**Files Reviewed:** 9 production files (UploadPatientFileCommand, CreateMedicalDocumentCommand, UpdateMedicalDocumentCommand, UpdateClinicCommand, Extensions.cs, new LocalDiskFileStorage.cs, deleted IFileStorageService + LocalFileStorageService, UnitTests.csproj) + 2 CLAUDE.md docs. Tests + `features/**` excluded from the reviewable diff.
**Review method:** 5 parallel stack-adapted agents (Code Quality, Error-Handling/CQRS [ROP dropped — repo uses `Result<T>`], Business Logic, Breaking Changes, Security). Security agent added because the feature centers on path-traversal-safe local file storage.

## Findings

### Finding 1
- **Severity:** Major
- **Category:** Business Logic
- **File:** api/ClinicManagement.Application/Features/Documents/Commands/UpdateMedicalDocumentCommand.cs
- **Line:** ~106
- **Anchor:** `UpdateMedicalDocumentCommandHandler.Handle` — `await _fileStorage.DeleteAsync(oldFile.StorageKey, cancellationToken)`
- **Comment:** Independently flagged by 3 of 5 agents. When replacing an existing document PDF, the OLD blob is physically deleted (`_fileStorage.DeleteAsync(oldFile.StorageKey)`) *inside* the try but *before* `SaveChangesAsync`; the old `PatientFile` row is only staged for deletion (committed by that save). If `SaveChangesAsync` throws, the new orphan-cleanup catch deletes the just-uploaded NEW blob and rethrows — but the DB transaction rolls back, so the document still references `oldFile`, whose blob is already irrecoverably gone. This is the inverse of the orphan the feature prevents: a DB record pointing at a missing blob → `DownloadPatientFileQuery` 404s for that document. The delete-old-before-save ordering is **pre-existing**; the Phase-2 change wrapped it in the try/catch but preserved the ordering (documented as scoped-out in progress.md). Fix: delete the old blob only *after* `SaveChangesAsync` succeeds (keep the old-file DB delete inside the transaction), so a failed commit leaves the still-referenced old blob intact.

### Finding 2
- **Severity:** Minor
- **Category:** Security
- **File:** api/ClinicManagement.Application/Features/Files/Commands/UploadPatientFileCommand.cs
- **Line:** ~115 (outer catch)
- **Anchor:** `UploadPatientFileCommandHandler.Handle` — `return Result<...>.Failure($"Error uploading file: {ex.Message}")`
- **Comment:** With the new local-disk backend, IO failures (permission denied, path-too-long, disk full) throw `IOException`/`UnauthorizedAccessException` whose `.Message` embeds the absolute server path (e.g. `Could not find a part of the path 'C:\...\Files\...'`). That raw message is returned to the API caller via `Result.Failure($"...{ex.Message}")`, disclosing server filesystem layout. Under MinIO the exception text was S3-side and path-free, so switching to disk newly surfaces this. Same `{ex.Message}` interpolation exists in CreateMedicalDocumentCommand, UpdateMedicalDocumentCommand, and UpdateClinicCommand. Fix: log `ex` server-side, return a generic constant message to the client. (Pre-existing pattern; the disk backend is what newly makes it path-revealing.)

### Finding 3
- **Severity:** Minor
- **Category:** Code Quality
- **File:** api/ClinicManagement.Application/Features/Documents/Commands/UpdateMedicalDocumentCommand.cs
- **Line:** ~64–130 (multiple)
- **Anchor:** `UpdateMedicalDocumentCommandHandler.Handle` — `System.Diagnostics.Debug.WriteLine(...)` calls
- **Comment:** This handler is peppered with `System.Diagnostics.Debug.WriteLine(...)` trace statements that the Phase-2 change relocated into the new try block. They bypass the injected structured-logging convention used everywhere else (incl. the new `LocalDiskFileStorage`), compile out of Release builds, and are debug noise. The sibling `CreateMedicalDocumentCommand` has none. Since the change touches these exact lines, remove them (or convert to `ILogger.LogDebug` with message templates). Pre-existing noise, but in the blast radius.

### Finding 4
- **Severity:** Suggestion
- **Category:** Code Quality
- **File:** api/ClinicManagement.Application/Features/Files/Commands/UploadPatientFileCommand.cs
- **Line:** ~117 (representative)
- **Anchor:** orphan-cleanup `catch { try { DeleteAsync } catch {} throw; }` — all four handlers
- **Comment:** The best-effort orphan-cleanup idiom is copy-pasted verbatim across four handlers (UploadPatientFile, CreateMedicalDocument, UpdateMedicalDocument, and the guarded variant in UpdateClinic). Consider a small shared helper (e.g. an `IFileStorage` extension `DeleteQuietlyAsync(storageKey, ct)` that swallows+logs) to DRY the pattern and guarantee identical best-effort semantics. Minor — each occurrence is small and surrounding control flow differs.

### Finding 5
- **Severity:** Suggestion
- **Category:** Security
- **File:** api/ClinicManagement.Infrastructure/Storage/LocalDiskFileStorage.cs
- **Line:** ~53
- **Anchor:** `LocalDiskFileStorage.UploadAsync` — `await file.CopyToAsync(destination, cancellationToken)`
- **Comment:** `UploadAsync` streams the entire input to disk with no size cap or content-type enforcement. An authenticated user could fill the server volume (disk-exhaustion DoS); in Local mode the blob store shares the host disk with Postgres. Lower priority for a trusted offline single-clinic LAN, and a quota is arguably out of scope for this phase, but there is no limit anywhere in the new path. Consider a config-driven max-bytes limit enforced during the copy (best at the request/handler boundary).

## Positively verified (no findings)
- **Path-traversal defense (`ResolveWithinBase`)** — sound on Windows and Linux: absolute/UNC/drive-relative keys and `..` all fail closed via `Path.GetFullPath` + separator-terminated prefix check; the sibling-prefix attack (`C:\data` vs `C:\data-evil`) is defeated. Storage keys are never user-controlled (guid or server-built `{clinicId}/logo`).
- **AC-1/AC-2 DI mode-branch** — Local resolves `LocalDiskFileStorage` (no MinIO); Cloud preserves the exact prior MinIO wiring including the throw-when-unconfigured stub.
- **AC-3 upload→save atomicity** — correct in all four handlers; store failure aborts before any DB write; the second `MedicalDocument` save (after PatientFile+blob commit) is correctly NOT an orphan.
- **AC-4/AC-5** — download-missing throws (clean handler failure); delete-missing idempotent; customPath deterministic + overwrite-in-place.
- **`UpdateClinicCommand` guard** — `logoUrl != originalLogoUrl` correctly cleans the first-time-set orphan while never deleting a blob the persisted row still references.
- **`DownloadAsync` seekability** — buffers into a `MemoryStream` at position 0, matching MinIO.
- **Deleted `IFileStorageService`/`LocalFileStorageService`** — no dangling references anywhere in `api/`.

## Applied Fixes (via /apply-review-fixes, 2026-07-08)
Each finding was challenged against the actual code before any change.
- **#1 (Major) — FIXED.** `UpdateMedicalDocumentCommand`: the replaced file's blob is no longer deleted before the save. Its record is still removed inside the transaction; the physical blob is deleted best-effort only *after* the whole update commits (`previousStorageKey`). A failed save now leaves the document pointing at an intact blob.
- **#2 (Minor, Security) — FIXED** (user opted to fix in these 4 handlers now). `UploadPatientFileCommand`, `CreateMedicalDocumentCommand`, `UpdateMedicalDocumentCommand`, `UpdateClinicCommand` now inject `ILogger<THandler>`, log the exception server-side, and return a generic message (no `ex.Message` / server paths over the wire).
- **#3 (Minor) — FIXED.** Removed the `System.Diagnostics.Debug.WriteLine` trace calls from `UpdateMedicalDocumentCommand` (matches sibling handler; uses `ILogger` instead).
- **#4 (Suggestion) — SKIPPED** (user confirmed). Inline orphan-cleanup kept; no shared `DeleteQuietlyAsync` helper (copies are tiny, control flow differs).
- **#5 (Suggestion, Security) — SKIPPED.** Upload size/type quota is out of scope for this phase (trusted offline single-clinic LAN; quota not in spec). Candidate for a later hardening phase.

Build after fixes: `dotnet build ClinicManagement.sln` → 0 errors, 0 new warnings. Test constructors updated for the new `ILogger` param; `LocalDiskFileStorageTests` 10/10 pass; handler atomicity tests remain Smart-App-Control-blocked (environmental — see progress.md).

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 1 |
| Minor | 2 |
| Suggestion | 2 |
| **Total** | 5 |
