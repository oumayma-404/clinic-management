# Progress: Local-Disk File Storage (Windows Desktop — Phase 2)

**Started:** 2026-07-08
**Type:** Small
**Branch:** feature/windows-desktop-app

## Status
- [x] Implementation
- [x] Quality checks (build, lint, typecheck)
- [x] Tests (added — 4 classes, 17 tests; build 0/0; see Tests Run)

## Tests Run
| Suite | Filter | Result |
|-------|--------|--------|
| Unit | `LocalDiskFileStorageTests` | **10 passed, 0 failed** (real temp-dir round-trip; AC-1/AC-4/AC-5 + path-traversal) |
| Unit | `InfrastructureFileStorageWiringTests` | ⚠ blocked by env (2 tests) — AC-1/AC-2 mode-branch |
| Unit | `UploadPatientFileAtomicityTests` | ⚠ blocked by env (2 tests) — AC-3 |
| Unit | `UpdateClinicLogoAtomicityTests` | ⚠ blocked by env (3 tests) — AC-3 + first-time-logo guard |

**Test build:** `dotnet build` → 0 errors / 0 warnings. All 17 tests compile and are AC-traced.

**Environmental blocker (not a test defect).** 7 of the 17 tests fail at load with
`FileLoadException … 'ClinicManagement.Domain.dll'. An Application Control policy has blocked this file (0x800711C7)`.
**Windows Smart App Control is ON** (`VerifiedAndReputablePolicyState=1`), which quarantines freshly-built unsigned assemblies. This blocks **every** test that loads `Domain.dll` — including untouched pre-existing tests (verified: `CreateClinicLocalSetupTests` fails identically). `Unblock-File` does not override SAC. The 10 `LocalDiskFileStorageTests` pass because they don't load `Domain.dll`.
**To run the blocked tests:** disable Smart App Control (Windows Security → App & browser control → Smart App Control → Off; requires an OS reset — user decision), then:
```
cd api && dotnet test ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj \
  --filter "FullyQualifiedName~InfrastructureFileStorageWiringTests|FullyQualifiedName~UploadPatientFileAtomicityTests|FullyQualifiedName~UpdateClinicLogoAtomicityTests"
```

## Quality checks
- `dotnet build ClinicManagement.sln` → 0 errors. No non-CS8632 warnings in any changed file (verified via scoped `--no-incremental` build). Also removed a pre-existing CS8604 in `CreateMedicalDocumentCommand`.
- Frontend untouched (Scope: BE) — no typecheck/lint needed.

## Test Plan
| AC | Action | Target file | Notes |
|----|--------|-------------|-------|
| AC-1, AC-4, AC-5 | New test class | `ClinicManagement.UnitTests/Infrastructure/Storage/LocalDiskFileStorageTests.cs` | Real temp-dir round-trip: upload→download; download/delete missing key; customPath deterministic + overwrite; path-traversal rejection. |
| AC-1, AC-2 | New test class | `ClinicManagement.UnitTests/Infrastructure/InfrastructureFileStorageWiringTests.cs` | `AddInfrastructure` resolves `LocalDiskFileStorage` for `Auth:Mode=Local`, `MinioFileStorage` for `Cloud` + MinIO config. |
| AC-3 | New test class | `ClinicManagement.UnitTests/Features/Files/UploadPatientFileAtomicityTests.cs` | Save fails after upload → failure + blob deleted; success → no delete, key persisted. |
| AC-3 | New test class | `ClinicManagement.UnitTests/Features/Clinics/UpdateClinicLogoAtomicityTests.cs` | First-time logo + save fails → new blob deleted; save fails with no new logo → no delete (guard); success → no delete. |

Test infra note: `ClinicManagement.UnitTests` previously referenced only Application; added a ProjectReference to `ClinicManagement.Infrastructure` so the new `IFileStorage` disk backend and the DI mode-branch can be tested. No integration/Testcontainers project exists in this repo — tests are xUnit + Moq against a real temp folder (no Docker/WSL needed).

## Review Fixes Applied (/apply-review-fixes)
- **#1 Major (FIXED):** `UpdateMedicalDocumentCommand` — old blob deleted only after the update commits (was deleted before save → dangling ref on failure).
- **#2 Minor/Security (FIXED):** 4 file handlers now log exceptions via injected `ILogger<T>` and return generic messages (no server-path disclosure).
- **#3 Minor (FIXED):** removed `Debug.WriteLine` traces from `UpdateMedicalDocumentCommand`.
- **#4 Suggestion (SKIPPED):** kept inline orphan-cleanup (no shared helper).
- **#5 Suggestion/Security (SKIPPED):** upload size quota out of scope this phase.

Handlers gained an `ILogger<THandler>` ctor dependency; the two atomicity test constructors were updated (`NullLogger<T>.Instance`). Build 0 errors / 0 new warnings; `LocalDiskFileStorageTests` 10/10 pass.

## Working tree note (start of session)
Only `features/local-file-storage/` was untracked (this feature's own artifacts). No unrelated uncommitted files.

## Files Changed
- (new) `api/ClinicManagement.Infrastructure/Storage/LocalDiskFileStorage.cs` — `IFileStorage` on local disk.
- `api/ClinicManagement.Infrastructure/Extensions.cs` — mode-branch `IFileStorage`; drop orphaned `IFileStorageService` registration.
- (deleted) `api/ClinicManagement.Application/Common/Interfaces/IFileStorageService.cs`
- (deleted) `api/ClinicManagement.Infrastructure/Services/LocalFileStorageService.cs`
- `api/ClinicManagement.Application/Features/Files/Commands/UploadPatientFileCommand.cs` — orphan-blob cleanup (FR-C3).
- `api/ClinicManagement.Application/Features/Documents/Commands/CreateMedicalDocumentCommand.cs` — orphan-blob cleanup (FR-C3).
- `api/ClinicManagement.Application/Features/Documents/Commands/UpdateMedicalDocumentCommand.cs` — orphan-blob cleanup (FR-C3).
- `api/ClinicManagement.Application/Features/Clinics/Commands/UpdateClinicCommand.cs` — orphan-blob cleanup (FR-C3, first-time-logo case).

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| FR-C3 orphan-cleanup scoped to the upload→save paths (not the delete paths) | The spec overview/AC-3 define the gap as "an upload can leave an orphaned blob if the following DB save fails." Delete paths have no just-uploaded blob; the parenthetical enumeration lists all IFileStorage write paths as context, not extra behavior. Delete handlers already tolerate storage failures. |
| Clinic-logo cleanup only deletes the new blob when the persisted key differs from the pre-existing one | The logo key is deterministic (`{clinicId}/logo`); deleting it on a re-upload save-failure would break the still-referenced existing logo. Orphan only arises on first-time set, which the guard targets. |
| `DownloadAsync` buffers into a `MemoryStream` (not a raw `FileStream`) | Mirrors `MinioFileStorage` (seekable, position 0, no file handle held) for behavioral parity across modes. |

## Significant Deviations
None.
