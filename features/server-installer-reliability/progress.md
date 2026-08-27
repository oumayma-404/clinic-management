# Progress: Server Installer Reliability (Fresh Install + In-Place Upgrade)

**Started:** 2026-07-15
**Type:** Small
**Branch:** feature/windows-desktop-app

## Status
- [x] Implementation
- [x] Quality checks (build, lint, typecheck)
- [x] Tests (added new + verified existing — see Test Plan / Tests Run below)

## Test Plan
| AC | Action | Target file | Notes |
|----|--------|-------------|-------|
| AC-1 (Local-mode guard, exit code, no DB, command verb) | New test class | `Api/Maintenance/ProvisionCertCommandTests.cs` | Cloud mode → returns 1 + refusal message, no cert/DB; `CommandName` verb matches the installer's `provision-cert` invocation |
| AC-1 (idempotent generate/reuse, loadable PFX+CA, CA path) | Verify existing | `Infrastructure/Security/CertificateProvisionerTests.cs` | The command delegates verbatim to `CertificateProvisioner.EnsureServerCertificate()`; its 4 tests already cover loadable PFX+CA, CA→leaf signing, SAN coverage, and idempotent reuse (same thumbprint/password on 2nd call) |

### Coverage notes (ACs with no C# unit surface — recorded, not contrived)
- **CLI wrapper precedent.** `ProvisionCertCommand` is a thin console wrapper with no injection seam (hardcodes the appsettings base path + the default install-dir cert path), exactly mirroring `AdminPasswordResetCommand`, which the repo deliberately does **not** unit-test directly — its substance is tested via the underlying service (`AdminPasswordRecoveryServiceTests`). Same here: the substance is `CertificateProvisioner`, already covered. The new tests pin only the wrapper's own new logic (verb + Local-mode guard); the full generate→print→exit-0 round trip from the packaged EXE is operator/integration-verified.
- **AC-2 (fresh install → service Running on first boot):** installer + live-service runtime behavior (`clinic-server.iss`, SCM). **DEFERRED — operator-verified (R-1).** The cert-reuse premise it relies on (first boot reuses the install-time-provisioned set instead of regenerating) is covered by the `CertificateProvisionerTests` idempotency test. Verify on a Windows install box: run the server installer, then `Get-Service` shows the API service **Running** and `https://<host>:5001` serves with no manual `Start-Service`.
- **AC-3 (CA exported to `%ProgramData%\ClinicManagement\ca.crt` from `{app}\api\.local\ca.crt`):** producer↔consumer contract — the command (via `CertificateProvisioner`) writes the public CA to `.local/ca.crt` under the install dir (`AppContext.BaseDirectory`), and the installer's `StartAndExportCa` copies from `{app}\api\.local\ca.crt`. Path match verified by hand-review (Inno Setup not C#-testable). Verify on install: `%ProgramData%\ClinicManagement\ca.crt` is present and non-empty after a fresh install.
- **AC-4 / AC-5 (persist + reuse DB creds on reinstall / fresh install generates new):** Inno Setup `[Code]` (`SaveStringToFile`/`LoadStringsFromFile` credential persistence). **DEFERRED — operator-verified (R-1).** Verify: (AC-4) reinstall over an existing cluster completes without the "Échec de la création du rôle/de la base" abort and the API connects; (AC-5) a fresh install with no persisted creds file generates new random passwords as before.
- **Edge case (pgdata exists but creds file missing/unreadable → fail loud, no silent regen):** Inno Setup `[Code]` guard. **DEFERRED — operator-verified (R-1).** Verify: with `pgdata` present but the persisted creds file removed, the installer aborts with a clear operator message rather than generating mismatched passwords.
- **AC-6:** `dotnet build ClinicManagement.sln` → **0 errors, 0 warnings** (this session). `packaging/README.md` fresh-install + in-place-reinstall docs verified by review.

## Tests Run
| Suite | Filter | Result |
|-------|--------|--------|
| Unit | `FullyQualifiedName~ProvisionCertCommandTests` | 2 passed, 0 failed |
| Unit | `FullyQualifiedName~CertificateProvisionerTests` (verify — command's delegated substance) | 4 passed, 0 failed |

Command: `dotnet test ClinicManagement.UnitTests --no-build --filter "FullyQualifiedName~ProvisionCertCommandTests|FullyQualifiedName~CertificateProvisionerTests"` → **6 passed, 0 failed, 0 skipped**. No Smart App Control (`0x800711C7`) block this run — the freshly-built DLL loaded. Final full-solution build: **0 errors, 0 warnings**.

## Quality checks
- `dotnet build ClinicManagement.sln` → **0 errors**, 57 warnings (all pre-existing: CS8618 nullable ctors,
  CS8981 lowercase migration names, CS8602/CS8600/CS8604 nullable derefs, CS0618 Hangfire obsolete). None
  originate in the new `ProvisionCertCommand.cs` or the `Program.cs` CLI branch.
- `packaging/server/clinic-server.iss` — Inno Setup script; **not** locally compilable (no ISCC.exe in this
  environment; R-1 "committed-but-not-executed here" per the script header). Reviewed by hand: all functions
  used (`LoadStringsFromFile`, `TArrayOfString`, `GetArrayLength`, `Trim`, `ForceDirectories`,
  `SaveStringToFile`) are valid Inno APIs; index access nested under the length guard (no short-circuit
  assumption); producer/consumer contract verified — installer invokes `provision-cert` (matches
  `ProvisionCertCommand.CommandName`) and reads the CA from `{app}\api\.local\ca.crt` (where the API writes
  it via `AppContext.BaseDirectory`). Must be compiled with ISCC on the operator build machine before release.

## Working tree note (start of session)
Unrelated pre-existing uncommitted/untracked files — EXCLUDED from this feature's commits:
- `.gitignore` (M)
- `api/ClinicManagement.API/appsettings.json` (M)
- `define-small-feature-prompt.md` (M)
- `features/LEARNINGS.md` (M)
- `features/notification-center/stories/progress.md` (M)
- `web/Dockerfile` (M)
- `features/notification-center/retrospective.md` (??)
- `features/notification-center/reviews/feature-review.md` (??)

## Files Changed
- `api/ClinicManagement.API/Maintenance/ProvisionCertCommand.cs` (new) — idempotent `provision-cert` CLI branch (AC-1)
- `api/ClinicManagement.API/Program.cs` — intercept `provision-cert` before the web host boots (AC-1)
- `packaging/server/clinic-server.iss` — run `provision-cert` at install time before `sc start` (AC-2), fix CA-export source path to `{app}\api\.local\ca.crt` (AC-3), persist + reuse DB credentials (AC-4/AC-5), fail loud on cluster-without-credentials (edge cases)
- `packaging/README.md` — document fresh-install + in-place-reinstall behavior (AC-6)

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|

## Significant Deviations
