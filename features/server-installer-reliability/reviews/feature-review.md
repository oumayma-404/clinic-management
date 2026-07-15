# Feature Review: server-installer-reliability

**Status:** COMPLETE
**Challenged:** Yes
**Date:** 2026-07-15
**Challenged Date:** 2026-07-15
**Parent Branch:** feature/windows-desktop-app (feature is uncommitted)
**Merge Base:** n/a — untracked/uncommitted working-tree review (feature files are `??`/` M`, absent from any merge-base diff)
**Files Reviewed:** 5 (api/ClinicManagement.API/Program.cs +10; api/ClinicManagement.API/Maintenance/ProvisionCertCommand.cs new; packaging/server/clinic-server.iss +113/-17; packaging/README.md +37/-12; api/ClinicManagement.UnitTests/Api/Maintenance/ProvisionCertCommandTests.cs new)

**Review method:** Deep-trace inline review (agents skipped). The substance is an Inno Setup Pascal `[Code]` script + a thin C# CLI wrapper — the default four C#/ROP/Marten agents (which only see diff text) would produce noise on Inno Pascal and miss the cross-file findings. Reviewed the working tree directly and traced every value across files: `CertificateProvisioner.EnsureServerCertificate` and `LocalInstallPaths.LocalDir` (to verify the CA-export path fix), `WriteProductionConfig`/`SetupPostgres` (to verify credential flow), and `AdminPasswordResetCommand` (the precedent the CLI mirrors).

## Challenge Summary

| Metric | Count |
|--------|-------|
| Original findings | 4 |
| Confirmed | 3 |
| Confirmed (adjusted) | 1 |
| Dismissed (false positive) | 0 |
| Dismissed (pre-existing) | 0 |
| **Final findings** | 4 |

**Verified correct (not findings, recorded so no one re-derives them):**
- **AC-3 path fix is correct.** `CertificateProvisioner` writes to `LocalInstallPaths.LocalDir` = `AppContext.BaseDirectory/.local`; the API exe is `{app}\api\ClinicManagement.API.exe`, so the CA lands at `{app}\api\.local\ca.crt` — exactly the corrected `CaSrc`. The old `{app}\.local\ca.crt` was genuinely wrong (left the export empty).
- **Credential reuse is sound.** On reinstall the persisted `postgres` superuser password matches the existing cluster because `initdb` is skipped (cluster present) and never resets it; the `clinic_user` DO-block skips `CREATE ROLE` when the role exists, and the reused password matches. `pgpass.conf` uses the reused values → auth succeeds.
- **Password alphabet** (`NewRandomPassword`) has no special characters, so baking it into the connection string / pgpass needs no escaping.
- **provision-cert Local-mode gating + Cloud unchanged.** The Program.cs interception fires only on the exact `provision-cert` verb (only the installer passes it); the command refuses in Cloud. Additive.

## Findings

### Finding 1
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** packaging/server/clinic-server.iss
- **Line:** 123
- **Anchor:** `DbCredentialsFile`
- **Comment:** The DB credentials persist to `{app}\.local\db-credentials`, but every API-managed per-install secret (signing key, `server.pfx`, `ca.crt`, `google-refresh-token`) lives in a **different** directory — `{app}\api\.local` (`AppContext.BaseDirectory`, confirmed via `LocalInstallPaths.LocalDir` and the AC-3 CA path fix at iss:440). So the install has **two** `.local` folders with different owners. The `DbCredentialsFile` comment ("same folder family as the other per-install secrets", iss:119-120) is therefore inaccurate, and the README's backup guidance "back up `{app}\.local\db-credentials` (or the whole `.local/`)" (README:200) is ambiguous about *which* `.local` — a backup of `{app}\.local` misses the API secrets under `{app}\api\.local`. Recommend either colocating the credentials file at `{app}\api\.local\db-credentials` (feasible — just `ForceDirectories('{app}\api\.local')` in `EstablishDbCredentials` instead of `{app}\.local`) or fixing the comment + README to name both folders explicitly. Functionally harmless today (the API never reads this file), but a real trip-hazard for an operator doing backup/restore.

### Finding 2
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** packaging/README.md
- **Line:** 195
- **Anchor:** "Existing cluster but `db-credentials` is missing or unreadable"
- **Comment:** The very first reinstall on a machine installed with the **previous** server installer (which never wrote `.local/db-credentials`) will always hit the fail-loud "cluster exists but no credentials file" abort (iss:171-179), because that machine has a `pgdata` cluster but no credentials file. This is correct per the spec's edge case, but the README (lines 195-198) frames the missing-credentials case only as corruption / backup-loss — it does not mention the realistic "upgrading from an older installer version" path. Worth documenting explicitly, and noting that the existing `clinic_user` password is still recoverable in cleartext from `{app}\api\appsettings.Production.json` (its connection string, written at iss:205/222), whereas the `postgres` superuser password is unrecoverable — which is precisely why fail-loud (and, for many operators, wiping `pgdata`) is the only safe path. Documentation-only; the code behavior is intended.

### Finding 3
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — description corrected)
- **File:** packaging/server/clinic-server.iss
- **Line:** 430
- **Anchor:** `StartAndExportCa`
- **Comment:** `Rc` is passed to the `provision-cert` `RunWait` without being explicitly initialized, and the failure branch shows `IntToStr(Rc)` to the operator. `RunWait` returns `Exec(...) and (ResultCode = 0)`; if `Exec` fails to *launch* the exe it returns `False` without necessarily setting `ResultCode`. Initialize `Rc := -1;` before the call so a launch failure reports a distinguishable value rather than a misleading `code 0`. Cosmetic (the warning-not-abort behavior is correct either way).
- **Challenge note:** Severity unchanged (Suggestion), but the original claim that the shown code "can be stale/garbage" is inaccurate — Inno Setup's Pascal Script zero-initializes local variables, so on a launch failure `Rc` is `0`, and the MsgBox would show `code 0`, which reads as *success* (more misleading than garbage, not less). The recommended `Rc := -1;` fix stands and is the point of the finding.

### Finding 4
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.UnitTests/Api/Maintenance/ProvisionCertCommandTests.cs
- **Line:** 40
- **Anchor:** `Run_refuses_and_returns_nonzero_when_not_in_local_mode`
- **Comment:** The test mutates the **process-global** `Auth__Mode` environment variable (line 40) and `Console.Error` (line 41). It restores both in a `finally` (lines 50-51), and no other test currently reads that env var, so it passes today — but xUnit runs test *collections* in parallel across classes by default, and process-global mutation is an isolation smell that will bite if any future test reads the auth mode concurrently. Consider marking the class with a dedicated `[Collection]` (to serialize it against any env-var-sensitive tests) or refactoring `ProvisionCertCommand.Run` to accept an injected `IConfiguration` so the guard can be tested without touching the environment. Low priority.

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 0 |
| Minor | 2 |
| Suggestion | 2 |
| **Total** | 4 |
