# Feature Review: windows-desktop-app — Phase 5 (Packaging, Installers & Manual Backup)

**Status:** COMPLETE
**Challenged:** Yes
**Date:** 2026-07-09
**Challenged Date:** 2026-07-09
**Parent Branch:** main
**Merge Base:** 9798b95d31f55ee07f2ad5e0af5550c4c2831022
**Scoped To:** Phase 5 commits only — `0077a35..HEAD` (5fc7b16, 8ae265b, 87dfdcd). Full merge-base diff is 201 files / +15230 (Phases 1–4 + docs); **reviewed = 49 code files, +2758 / -61** after excluding `features/**` pipeline docs and lock files. Phases 1–4 are COMPLETE and archived under `phase-1/`, `phase-3/`, `phase-4/` — not re-reviewed.
**Review method:** 6 parallel agents adapted to the MediatR + `Result<T>` stack (no ROP): Code Quality/Architecture, Business Logic & Error Handling, Security, Breaking Changes & Regression, Frontend (Next.js), Packaging (PowerShell + Inno Setup). The default ROP agent was dropped (not this stack); Security/Frontend/Packaging agents were added for the auth/crypto/secrets + large FE + installer surface.

> **Scope note:** S5–S7 packaging artifacts (WebView2 shell, Inno Setup installers) are **operator-verified (R-1)** — this environment cannot run a Windows installer or WebView2, so packaging findings are from **static review** and should be prioritized for operator confirmation. All Phase 5 behavior is additive and gated to Local mode; the Cloud path was verified byte-for-byte behaviorally unchanged (no regression findings survived).

## Challenge Summary

| Metric | Count |
|--------|-------|
| Original findings | 18 |
| Confirmed | 17 |
| Confirmed (adjusted) | 1 |
| Dismissed (false positive) | 0 |
| Dismissed (pre-existing) | 0 |
| **Final findings** | 18 |

**Challenge method:** each finding was verified against the actual source file (not the diff), reading full context around the cited line and the cross-referenced files (e.g. the CA subject CN in `CertificateProvisioner.cs` for Finding 3, the handler catch-all convention documented in `ClinicManagement.Application/CLAUDE.md` for Finding 7). This was a high-accuracy review — every finding reproduced against source. Only Finding 7's severity was adjusted (it matches the codebase's documented catch-all convention, so the narrower-catch fix is optional polish rather than a defect).

## Findings

### Finding 1
- **Severity:** Major
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Services/PgDumpBackupService.cs
- **Line:** 79
- **Anchor:** `PgDumpBackupService.CreateBackupAsync`
- **Comment:** A failed backup leaves a partial, corrupt-looking artifact on disk. The timestamped `backupFolder` is created before `RunPgDumpAsync` and the file copy. If pg_dump exits non-zero, times out, or the copy throws, the method throws `InvalidOperationException` but never removes the already-created `clinic-backup-<ts>` folder / partial `database.dump`. An operator then sees a folder that looks like a completed backup and could restore from a truncated dump — the opposite of the "no silent partial success" intent (AC-8.2/8.3). Fix: wrap dump + copy in try/catch that best-effort deletes `backupFolder` on any failure before rethrowing, so only complete backups remain.

### Finding 2
- **Severity:** Major
- **Category:** Security
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/Program.cs
- **Line:** 288
- **Anchor:** Program.cs — Local-mode `ConfigureKestrel` block (`if (isLocalAuthMode)`)
- **Comment:** In Local mode the plain-HTTP port is bound with `kestrel.ListenAnyIP(httpPort)` — `0.0.0.0:5000`, every LAN interface — not loopback. This contradicts the documented posture ("the Node web port and the API's plain-HTTP port stay loopback-only"; only the HTTPS port is opened on the firewall). The sole control keeping LAN clients off the cleartext API is one `netsh` rule in `clinic-server.iss::OpenFirewall`; if that rule is removed/reordered or the firewall is disabled, the entire API — including `POST /api/auth/login` with plaintext credentials — is reachable over unencrypted HTTP, and the body is on the wire before `UseHttpsRedirection` can 307 it. The only legitimate HTTP consumer is the co-located Next BFF over `http://localhost:5000`. Fix: bind HTTP to loopback — `kestrel.ListenLocalhost(httpPort)` — keeping `ListenAnyIP(httpsPort, …)` for the LAN-facing TLS front door, so the loopback guarantee is a property of the bind, not of a breakable firewall rule.

### Finding 3
- **Severity:** Major
- **Category:** Packaging
- **Verdict:** Confirmed
- **File:** packaging/client/clinic-client.iss
- **Line:** 58
- **Anchor:** `[UninstallRun]`
- **Comment:** Uninstall runs `certutil -delstore Root "Clinic Management CA"`, but the CA the server generates has subject CN **`Clinic Management Local CA`** (`CertificateProvisioner.cs:101`, asserted in `CertificateProvisionerTests.cs:52`). `"Clinic Management CA"` is not a contiguous substring of that CN, so `certutil -delstore` matches nothing and the self-signed root CA stays permanently trusted in the machine Root store after uninstall — a lingering trust anchor on every staff PC. Fix: `-delstore Root "Clinic Management Local CA"` (or delete by thumbprint).

### Finding 4
- **Severity:** Major
- **Category:** Packaging
- **Verdict:** Confirmed
- **File:** packaging/server/clinic-server.iss
- **Line:** 75
- **Anchor:** `[UninstallRun]`
- **Comment:** `OpenFirewall` adds inbound rule `name="Clinic Management HTTPS"` (TCP 5001) on install, but `[UninstallRun]` never removes it — the LAN firewall hole persists after uninstall. Fix: add an uninstall step `netsh advfirewall firewall delete rule name="Clinic Management HTTPS"` (with a `RunOnceId`).

### Finding 5
- **Severity:** Major
- **Category:** Packaging
- **Verdict:** Confirmed
- **File:** packaging/server/clinic-server.iss
- **Line:** 173
- **Anchor:** `SetupPostgres` / `CurStepChanged`
- **Comment:** `SetupPostgres` sets `Result := True` unconditionally and ignores exit codes of `sc start {#ServiceDb}`, `pg_isready`, `CREATE ROLE`, and `CREATE DATABASE` (the last run with `ON_ERROR_STOP=0`, which swallows all errors, not just "already exists"). If the DB service fails to start or role/DB creation fails, the function still returns True, later steps run against a missing role/database, and Inno reports "completed successfully" while the API fails at boot (migrations against a nonexistent DB). initdb failure likewise only skips the rest but still exits success. Fix: capture and check the return code on each step, guard `CREATE DATABASE` with a `SELECT` and `ON_ERROR_STOP=1`, and abort the install (surface the error) on any hard failure instead of swallowing it.

### Finding 6
- **Severity:** Major
- **Category:** Packaging
- **Verdict:** Confirmed
- **File:** packaging/server/clinic-server.iss
- **Line:** 191
- **Anchor:** `SetupAppServices`
- **Comment:** The API service is created with a hard dependency `depend= {#ServiceDb}/{#ServiceWeb}`, but the Web service is registered later in the same procedure and **only if** `nssm.exe` is present (operator-supplied, `skipifsourcedoesntexist`). When nssm is absent the code shows a MsgBox and continues — so `ClinicManagementApi` depends on a service that was never created and `sc start` fails with error 1068 (dependency missing), leaving the API dead while the installer reports success. Fix: register the Web service before the API service, and make the `{#ServiceWeb}` dependency conditional on nssm actually having installed it (drop the dependency, or fail the install, when the web service can't be created).

### Finding 7
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Backup/Commands/BackupNowCommand.cs
- **Line:** 66
- **Anchor:** `BackupNowCommandHandler.Handle`
- **Comment:** The handler wraps the whole body in `catch (Exception ex) { return Result<BackupResultDto>.Failure(ex.Message); }`. `IBackupService` documents that expected failures surface as `InvalidOperationException`, so arguably only that type should become an operator-facing `Result.Failure`. Catching bare `Exception` masks genuine bugs (a DB/repository fault, `NullReferenceException`) as a benign failure with a raw internal message instead of a 500, and swallows `OperationCanceledException` on cancellation. Fix: `catch (InvalidOperationException ex)` and let anything else propagate to the global exception middleware.
- **Challenge note:** Severity lowered Major-effort→Suggestion (reviewer set Minor). The bare-`Exception` catch that converts to `Result.Failure` is the **documented codebase convention** (`ClinicManagement.Application/CLAUDE.md`: "almost every handler … wraps its body in try/catch, and converts exceptions/business errors into `Result.Failure(...)`"), so the current code is not a defect against project pattern. The narrower catch is a reasonable, isolated improvement (cancellation propagation + not masking bugs), hence kept as optional polish rather than dropped.

### Finding 8
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Services/PgDumpBackupService.cs
- **Line:** 78
- **Anchor:** `PgDumpBackupService.CreateBackupAsync`
- **Comment:** Concurrent or rapid repeated backups collide on the destination folder. The name has whole-second granularity (`clinic-backup-{timestamp:yyyyMMdd-HHmmss}`) and `Directory.CreateDirectory` is a no-op when the folder exists. Two backups in the same second (admin double-click, or overlapping runs) resolve to the same folder: the second pg_dump truncates the first `database.dump` and the recursive copy runs with `overwrite: true` into the shared `files/` tree, producing an inconsistent backup with no error surfaced. Fix: make the folder unique (append a short GUID/counter), or fail loud if the target already exists, and/or guard against overlapping runs.

### Finding 9
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Services/PgDumpBackupService.cs
- **Line:** 206
- **Anchor:** `PgDumpBackupService.EnsureSufficientFreeSpace`
- **Comment:** The free-space pre-check estimates only the file-storage copy size plus a fixed 128 MB margin; it never accounts for the database dump size. On a clinic with a large DB but small file storage, the check passes and pg_dump then fills the disk mid-dump — still "fails loud", but via the generic non-zero-exit path rather than the distinct "espace disque insuffisant" message the pre-check exists to provide (AC-8.2/8.3 want recognizable failure reasons). Fix: factor an approximate DB-size estimate (or a larger margin) into the required-space calculation.

### Finding 10
- **Severity:** Minor
- **Category:** Security
- **Verdict:** Confirmed
- **File:** packaging/server/clinic-server.iss
- **Line:** 148
- **Anchor:** `SetupPostgres` (initdb invocation)
- **Comment:** The cluster is initialized with `initdb … -A trust`, so any OS user/process on the clinic server PC can connect to PostgreSQL as any role — including `postgres` superuser — with no password, despite the installer generating a 24-char `clinic_user` password and baking it into the connection string (never actually enforced). On a shared/multi-account Windows host this is local escalation to full DB (all PHI) access. Fix: initialize with `-A scram-sha-256` (or `md5`) so the generated password is enforced, and ensure `pg_hba.conf` requires password auth for the `127.0.0.1/32` host lines. (Static review only.)

### Finding 11
- **Severity:** Minor
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/lib/api/client.ts
- **Line:** 90
- **Anchor:** `apiGet`
- **Comment:** *(Flagged independently by both the Frontend and Breaking-Change agents.)* `new URL(\`${API_BASE_URL}${endpoint}\`, window.location.origin)` evaluates `window.location.origin` unconditionally — including in the Cloud build where `API_BASE_URL` is absolute and the base arg is ignored anyway. Any invocation of `apiGet` during an SSR render pass (client components still render on the server), in `generateMetadata`, or in a Node unit test importing a `lib/api/*` module now throws `ReferenceError: window is not defined` before the URL is built — a path that previously succeeded. Latent today only because all fetching lives in `useEffect`/handlers (a source comment documents that assumption). Fix: `const base = typeof window !== 'undefined' ? window.location.origin : undefined; new URL(\`${API_BASE_URL}${endpoint}\`, base);` (base is ignored for absolute Cloud URLs, so Cloud is unaffected).

### Finding 12
- **Severity:** Minor
- **Category:** Packaging
- **Verdict:** Confirmed
- **File:** packaging/server/clinic-server.iss
- **Line:** 100
- **Anchor:** `NewDbPassword`
- **Comment:** `NewDbPassword` builds the DB password with Inno's non-cryptographic `Random` and never calls `Randomize`, so it starts from the default seed and yields a predictable, identical-across-installs password — failing the CSPRNG requirement for generated secrets. Impact is muted only because `-A trust` means the password is never used for auth (see Finding 10), which also makes the "random per-install password" claim illusory. Fix: at minimum call `Randomize`; better, source bytes from CryptoAPI — or fix Finding 10 and document that the password is decorative until then.

### Finding 13
- **Severity:** Minor
- **Category:** Packaging
- **Verdict:** Confirmed
- **File:** packaging/publish-server.ps1
- **Line:** 102
- **Anchor:** Scrubbing bundled secrets (step 1)
- **Comment:** The scrub is a fixed allow-list and doesn't blank every secret in the published appsettings.json: `MinIO.AccessKey`/`SecretKey` (`minioadmin`) and the `ConnectionStrings.DefaultConnection` password (`clinic_password`) remain in the shipped file (low impact — MinIO is unused in Local and the connection string is overridden by the installer's `appsettings.Production.json` — but weakens the "no secret shipped" guarantee). Separately, `$cfg.Auth.Mode = 'Local'` is unguarded (unlike the Google/HuggingFace/Auth0 assignments) and will throw if a future appsettings lacks an `Auth` section. Fix: guard the `Auth` assignment (`if ($cfg.Auth)`) and blank the MinIO keys (and/or connection-string password) too.

### Finding 14
- **Severity:** Minor
- **Category:** Packaging
- **Verdict:** Confirmed
- **File:** packaging/publish-server.ps1
- **Line:** 113
- **Anchor:** Scrubbing bundled secrets (step 1)
- **Comment:** `Set-Content $ApiSettings -Encoding UTF8` under Windows PowerShell 5.1 writes UTF-8 **with a BOM**. ASP.NET Core's stream-based JSON config provider tolerates it, but any consumer that reads the file as a string into `System.Text.Json` (e.g. the `reset-admin-password` console path or tooling) throws on the leading BOM, and a BOM in appsettings is fragile/non-idiomatic. Fix: write BOM-less UTF-8, e.g. `[System.IO.File]::WriteAllText($ApiSettings, ($cfg | ConvertTo-Json -Depth 32), (New-Object System.Text.UTF8Encoding($false)))`.

### Finding 15
- **Severity:** Minor
- **Category:** Packaging
- **Verdict:** Confirmed
- **File:** packaging/server/clinic-server.iss
- **Line:** 189
- **Anchor:** `SetupAppServices` / `CurStepChanged`
- **Comment:** On re-install/upgrade, service creation isn't idempotent-aware: `sc create` returns 1073 ("service already exists") and `nssm install` errors when present, and both results are discarded. The stack won't be double-registered, but an upgrade that changes a binPath/env silently keeps the **old** service definition. Fix: on upgrade, `sc config` (or delete-then-create) the existing services and re-apply NSSM settings rather than relying on create-only.

### Finding 16
- **Severity:** Minor
- **Category:** Packaging
- **Verdict:** Confirmed
- **File:** packaging/client/clinic-client.iss
- **Line:** 40
- **Anchor:** `[Files]`
- **Comment:** The client installer ships only the shell + CA; it neither bundles nor checks for the WebView2 Evergreen runtime. On an offline LAN PC without that runtime already present, the WebView2 shell fails to render with no in-installer remedy (no internet to fetch the bootstrapper). Fix: bundle the WebView2 fixed-version runtime (or the offline Evergreen standalone installer) and install/verify it, or document a hard prerequisite in the operator checklist.

### Finding 17
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Extensions.cs
- **Line:** 138
- **Anchor:** `Extensions.AddInfrastructure`
- **Comment:** `services.AddSingleton<Security.CertificateProvisioner>()` is never resolved — Program.cs constructs it manually pre-Build with a `NullLogger` (the cert is needed before `builder.Build()`). This is a dead DI registration (comment: "Registered for completeness"), and the manual `NullLogger` silently discards the provisioner's own generate-vs-reuse log lines, so the "Reusing existing certificate" path is invisible in logs. Fix: remove the unused registration, and either pass a real logger to the manual construction or log the generate/reuse outcome from Program.cs.

### Finding 18
- **Severity:** Suggestion
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/components/backup-settings.tsx
- **Line:** 40
- **Anchor:** `BackupSettings.handleBackup`
- **Comment:** The `setWorking(false)`/`setLastResult(...)` calls in the try/finally after `await backupApi.backupNow()` run unconditionally even if the card unmounts mid-request (long operation; user navigates away from /settings). React 19 no longer warns on setState-after-unmount so it's harmless, but for consistency with the codebase's guarded-async pattern (`LocalSessionProvider`'s `let active = true` effect in `session.tsx`) consider an `active`/`AbortController` guard. Optional.

## Review Summary (post-challenge)

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 6 |
| Minor | 9 |
| Suggestion | 3 |
| **Total** | 18 |

**Theme:** The in-repo C# (backup service, cert provisioner, front door) is solid — command injection is avoided (`ArgumentList` + `PGPASSWORD`), the CA private key is ephemeral/never shipped, cert generation is RSA-2048/SHA-256, authorization is enforced (`[Authorize(AdminOnly)]` + handler defense-in-depth), and the Cloud path is behaviorally unchanged. The **highest-risk cluster is the operator-verified installers** (5 of 6 Majors are in `.iss`/transport): a wrong CA name on client uninstall, an un-removed firewall rule, swallowed Postgres-setup failures reported as success, a broken service dependency when nssm is absent, plus a LAN-exposed cleartext HTTP bind. These should be the priority for the operator to confirm/fix since this environment can't execute the installers. The challenge confirmed all 18 findings against source; only Finding 7 was re-graded (Minor → Suggestion) because it matches the project's documented handler catch-all convention.
