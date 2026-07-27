# Progress — Security Hardening (Audit Section 2)

**Story:** [story-1-security-hardening.md](./story-1-security-hardening.md) — one story, five ordered parts
**Branch:** `feature/security-hardening`

## Status

| Part | Status |
|---|---|
| P1 Installer filesystem posture | in progress |
| P2 Backup output posture | pending |
| P3 Auth & session | pending |
| P4 Authorization | pending |
| P5 Hygiene | pending |

## Working tree note (start of session, 2026-07-27)

Untracked paths present at branch point that are **not part of this story** and are excluded from every commit here. Files are staged explicitly by path — never `git add -A`/`.`:

| Path | Note |
|---|---|
| `features/data-and-money-integrity/` | Contains `exploration.md` + `spec.md`. Not authored by this session — appears to be parallel work on audit § 1 (Data loss & money correctness). Left untouched. |
| `CODEBASE_AUDIT_2026-07.md` | The audit itself, untracked. The spec's "correct § 2's index count 12 → 14" step would be the first thing to commit it; flagged to the user rather than committed unilaterally. |

## Deviations

### DEV-1: Data Protection key-ring configuration extracted to a shared helper
**Date:** 2026-07-27
**Story:** 1 (Part 1)
**Category:** Technical
**Original Plan:** `plan.md` listed `DbCredentialProtector` and the console verbs, but did not name a file for the Data Protection configuration — implying the verb would configure its own.
**Actual Implementation:** New `Infrastructure/Security/LocalDataProtection.cs` owns the configuration (`SetApplicationName` + `PersistKeysToFileSystem` + `ProtectKeysWithDpapi`); `AddInfrastructure` now calls it instead of configuring inline, and the console verbs call it to build a standalone provider over the same key ring.
**Justification:** The plan's console verb forces a **second** configuration site to exist, because a verb runs outside the web host's DI container. Two definitions can drift, and the failure mode is severe and silent: the installer writes ciphertext the API cannot read, so an existing cluster becomes unreachable (the EC-4 unrecoverable path). `LEARNINGS.md` records this exact convention for the JWT signing key — "the **same** config path is used by both the issuer and the validator so they can never drift" — and implement-story Step 6.7 directs preferring the project rule over the plan's file list. Behaviour is unchanged; the moved block is verbatim.
**Impact:** One edited block in `Extensions.cs` (behaviour-neutral). Makes P2 and any future verb reuse the same key ring for free.
**Approved:** Yes — confirmed by the user before coding.

## Auto-Approved Deviations

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| Converted a **pre-existing** `{ }` comment in `clinic-server.iss` (`StartAndExportCa`, line 442 at HEAD) to `//` | Trivial | Comment-only, zero behaviour change. Inno's Pascal `{ }` comments do not nest, so the first `}` — the one closing `{app}` — ends the comment and leaves `\api), so the CA is at` to be parsed as code. This repo has hit that exact bug before. It sits in the file Part 1 ships its changes through, so if it does break ISCC it blocks the whole part; the fix is free. **Not compile-verified** — ISCC is not installed here (R-1). |
| Added `EnsureLocalModeConfig` to the installer, seeding a minimal `appsettings.Production.json` before the first verb call | Trivial | Internal to the `.iss`, no API or behaviour change. Forced by an ordering fact: `appsettings.json` ships `Auth:Mode=Cloud` and `Local` only arrives via the generated Production overlay, which `WriteProductionConfig` cannot write until the DB password exists — so a verb invoked from `EstablishDbCredentials` would refuse as "not Local mode". The alternative (passing `Auth__Mode=Local` as an env var through `cmd /C set … &&`) adds fragile nested shell quoting to a file that cannot be executed or parse-tested here. Pure Pascal was the lower-risk choice. |
| ACL policy implemented as `icacls` invocations rather than managed `System.Security.AccessControl` APIs | Trivial | The plan left the mechanism unspecified. Both projects target `net8.0` (not `net8.0-windows`), so managed ACL APIs would require a new `PackageReference` and arguably a TFM change — a dependency change needing approval. `icacls` is what `clinic-server.iss` already uses (with the same well-known-SID convention) and the codebase already has a `Process` shell-out precedent in `PgDumpBackupService`. Zero new dependencies; testable via an injected runner. |
| Well-known SIDs (`*S-1-5-18`, `*S-1-5-32-544`, `*S-1-5-32-545`, `*S-1-1-0`, `*S-1-5-20`) rather than account names | Trivial | Internal to the new class. Account names are localized — a French Windows has `BUILTIN\Utilisateurs`, not `Users` — so name-based ACL edits would silently no-op on the target machines. `clinic-server.iss` already uses the SID form for exactly this reason. |
| No programmatic "assert no Users/Everyone" verification pass | Trivial | Internal scope. `icacls` resolves SIDs to localized display names in its listing output, so a name-matching assertion would be fragile on non-English Windows. The fail-loud requirement (AC-1.4) is met by strict exit-code checking on each invocation; the resulting posture is printed by the verb for the installer log, and AC-1.1's listing check is an operator-checklist item (R-1). |

## Learnings

- `LEARNINGS.md`'s note that `ClinicManagement.UnitTests` references **only** `Application` is **stale** — the `.csproj` references `Application`, `Infrastructure` **and** `API`. Testable logic can therefore live in its natural layer rather than being pushed into Application for testability. Worth correcting in `LEARNINGS.md` during `/capture-learnings`.
- The audit's § 2 index row (`5/4/3 = 12`) undercounts its own bullets (`5/5/4 = 14`). Recorded in the spec; the correction itself is an in-scope step.

## Session log

### 2026-07-27 — Part 1
- Cleared prerequisites: `plan.md` DRAFT → APPROVED; created `feature/security-hardening`; created `stories/`.
- Verified before coding: both projects target `net8.0`; `Microsoft.AspNetCore.DataProtection` 8.0.0 already referenced in Infrastructure; the Local key ring is already `ProtectKeysWithDpapi(protectToLocalMachine: true)`, so Data Protection ciphertext is machine-bound exactly as AC-3.1 words it; `ReminderSecretProtector` is the in-repo precedent for a purpose-scoped protector.
- Implemented P1.1–P1.9. New: `DirectoryAclHardener`, `LocalDataProtection`, `DbCredentialProtector` (Infrastructure/Security); `HardenPermissionsCommand`, `CredentialProtectionCommand` (API/Maintenance); three verb interceptions in `Program.cs`; `clinic-server.iss` rewired; `packaging/README.md` + `CLAUDE.md` updated.

#### Quality gates — Part 1

| Gate | Result |
|---|---|
| Backend build (`--no-incremental`, full recompile) | **0 errors**, 58 warnings — all pre-existing baseline (`CS8618` on existing Domain entities/value objects, `CS8981` on lowercase migrations). The only warning in a file this part touched is `Program.cs(269)` `CS0618` (Hangfire `UsePostgreSqlStorage` obsolete), pre-existing and ~200 lines from the edit. **0 new warnings.** |
| Test project build | 0 errors, 0 warnings |
| New tests | **33/33 passing** (`DirectoryAclHardenerTests` 11, `DbCredentialProtectorTests` 17, `LocalDataProtectionTests` 5) |
| Full unit suite | 818 passed / **8 pre-existing failures** — see below |
| `.iss` parse on target runtime | **Not possible here** — ISCC is not installed (R-1). Verified instead by inspection: UTF-8 **BOM present** (so ISCC reads the French strings reliably); `begin`/`end` balance checks out (37/38 raw, less one `BEGIN` and two `END`s inside a SQL string literal = 36/36); no `{ }` comment contains a brace constant. |

**Pre-existing test failures (8), proven not mine.** Stashed `Extensions.cs` + `Program.cs`, rebuilt and re-ran the full suite: **identical** 8 failed / 818 passed / 826 total, same test names. Then restored.

| Failing test | Note |
|---|---|
| `DoctorCachetTests` (×4) | ⚠️ **Relevant to Part 5** — P5.2 extracts `FileContentValidation` out of `UpdateDoctorProfileCommand`, i.e. exactly this code. These need triaging *before* that extraction, or a broken baseline gets attributed to the refactor. |
| `DocumentTypeAndFilenameTests.Create_With_Supported_Type_Passes_The_Type_Guard` | Unrelated to this feature. |
| `ReminderSchedulerTests` (×3) | Pure-Moq tests, unrelated. Not time-bombs (dates are `UtcNow`-relative). |

**Test bug found and fixed during the run:** `Harden_uses_well_known_sids_not_localized_account_names` initially failed because the temp directory lives under `C:\Users\…`, so the target path matched the "no account names" filter. Fixed by scoping the assertion to the ACL arguments (skipping the path element) — the production code was correct; the assertion was measuring the wrong thing.

**Note on the killed process:** a `ClinicManagement.API` (PID 41568) was holding its `bin` DLLs, so the first build failed with `MSB3021`/`MSB3027` file locks (not compile errors). Stopped it and rebuilt clean. The dev API will need restarting.
