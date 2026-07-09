# Story 1: [Full] Phase 5 — Packaging, Installers & Manual Backup

**Status:** APPROVED
**Story Status:** reviewed (2026-07-09 — see `../reviews/story-1.md`, 100/100, 1 Minor security-hardening finding fixed; backend build 0/57-baseline, web tsc 0. S1–S4 implemented & building green; S5–S7 packaging committed, shell builds 0/0; installer compilation + end-to-end stand-up operator-verified per `packaging/README.md`, R-1)
**Layer:** Full
**Depends On:** None (Phases 1–4 COMPLETE; artifacts archived under `../phase-1/`, `../phase-3/`, `../phase-4/`)
**Blocks:** None (final phase of the umbrella spec)

> **Departure from the single-layer rule (explicit user decision, 2026-07-09):** the entire Phase 5 plan is delivered as **one full-stack story** rather than split BE/FE. Per the break-plan convention, this story is tagged `Layer: Full` and its **Steps are grouped into ordered slices (S1–S7)** — mirroring the plan's US-1…US-7 — so the internal build order stays clear. Implement the slices in the order given; later slices depend on earlier ones (see the Slice Dependency note in each slice).

## Objective

Turn the hardened Local-mode app (Phases 1–4) into **installable Windows software**. On the backend, add an admin-only one-click **"Backup now"** (DB dump + file copy), make the API run as an **auto-starting Windows service** with clear startup-failure messaging, have the server **self-generate its HTTPS certificate** (fail-loud on transport misconfig), and stand up a **same-origin Kestrel front door** so one web build serves any install. On top of that, deliver a thin **WebView2 desktop client**, a **server installer** (bundled PostgreSQL + auto-start services + generated cert), and a **client installer** (shell + CA trust). All behavior is **additive and gated to Local mode** (`Auth:Mode = Local`); **Cloud stays byte-for-byte unchanged**.

## Acceptance Criteria

_From spec:_

Server install (spec US-1 / FR-F):
- [ ] **AC-1.1** — One Windows installer sets up the server PC (API, hosted web, bundled PostgreSQL, file-storage folder) as auto-starting background services.
- [ ] **AC-1.2** — First launch presents first-run setup that creates the clinic + first admin, no internet/Auth0 required.
- [ ] **AC-1.2a** — First-run setup reachable **only from localhost**; closed once the first admin exists.
- [ ] **AC-1.3** — After setup, the app is reachable from other LAN PCs.
- [ ] **AC-1.4** — Install/uninstall requires no manual PostgreSQL / storage / port / certificate configuration.

Client install (spec US-2 / FR-F2, FR-E2):
- [ ] **AC-2.1** — Lightweight client installer places an app with a Start-menu/taskbar icon.
- [ ] **AC-2.2** — First launch asks for the server address (IP/hostname); value stored and reused.
- [ ] **AC-2.3** — Server address editable later ("change server") without reinstalling.
- [ ] **AC-2.4** — Unreachable server → clear non-technical "Cannot reach the clinic server" screen with retry — never a blank page or raw browser error.
- [ ] **AC-2.5** — The server PC can also run the client (pointing at localhost).

Backup (spec US-8 / FR-G):
- [ ] **AC-8.1** — Admin-only "Backup now" writes a consistent DB + files backup to a configurable destination folder.
- [ ] **AC-8.2** — Reports success (with location) or a clear failure reason (unwritable, disk full) — never silent.
- [ ] **AC-8.3** — Storage/DB failures surface clear, non-silent operator messaging.
- [ ] **AC-8.4** — Restore is a documented manual procedure (no in-app restore UI).

Transport / hosting carry-over (FR-E1 finalize, FR-E2, FR-F3, FR-F4, FR-F5):
- [ ] **FR-E2** — LAN traffic served over HTTPS; server generates local CA + server cert at install; client installer imports the CA so the shell connects without trust warnings.
- [ ] **FR-F3** — Migrations apply automatically on server startup; a fresh install comes up empty and is populated via first-run setup.
- [ ] **FR-F4** — No real secrets bundled in either installer; secrets generated on the target machine.
- [ ] **FR-F5** — Server startup failures (DB down, port in use) surface clear operator-facing messages, not silent failure.
- [ ] **FR-B6** — Offline admin-recovery CLI works from any working directory (packaged-install path resolution).

_Story-specific:_

- [ ] Every new behavior is gated on `isLocalAuthMode`; Cloud startup, hosting, and web build are unchanged.
- [ ] Testable in-repo logic (backup, startup UX, cert generation, same-origin proxy) has unit/integration coverage; packaging/shell artifacts (S5–S7) are committed and **operator-verified** against `packaging/README.md` (they cannot be built/run in this environment — see R-1).
- [ ] Build: 0 errors / 0 new warnings.

## Entry Criteria

Before starting this story, ensure:

- [ ] Phases 1–4 are COMPLETE (Local auth, local-disk storage, connectivity awareness, LAN hosting & security gates all merged).
- [ ] `plan.md` is APPROVED and Challenged (2026-07-09).
- [ ] Docker Postgres + MinIO available for local dev runs; `dotnet build` and `npm run build` currently pass on the branch.
- [ ] Familiarity with the Phase 4 gating pattern (`AuthorizationPolicies.ConfigurePolicies(options, isLocalMode)`, `isLocalAuthMode`) and the "gate on mode, not capability" learning.

## Steps

> Slices are ordered by the plan's story ordering: **backup → server hardening (service, then cert) → same-origin web → desktop shell → server installer → client installer.** Installers come last because they package everything the earlier slices produce.

### Slice S1 — One-click admin backup (plan US-1 · FR-G · US-8) — *Slice dep: none*

1. **Add the backup seam + DTO**
   - `api/ClinicManagement.Application/Common/Interfaces/IBackupService.cs` — seam so the `pg_dump` shell-out is mockable.
   - `api/ClinicManagement.Application/DTOs/BackupResultDto.cs` — destination path, byte size, timestamp.

2. **Add the admin-gated command + handler**
   - `api/ClinicManagement.Application/Features/Backup/Commands/BackupNowCommand.cs` — resolve caller via `IClinicContext.GetUserId()` → `GetByAuth0SubAsync` → enforce `admin.IsAdmin()` (mirror `ResetUserPasswordCommand`), then call `IBackupService`. Wrap in `try/catch` → `Result<BackupResultDto>.Failure(...)`.

3. **Implement the pg_dump-backed service**
   - `api/ClinicManagement.Infrastructure/Services/PgDumpBackupService.cs` — parse `ConnectionStrings:DefaultConnection` via `NpgsqlConnectionStringBuilder`; locate `pg_dump.exe` from `Backup:PgDumpPath`; run via `Process.Start` **with an argument list (no shell string)**, `PGPASSWORD` via env, redirect stdout/stderr, capture exit code, enforce a **timeout + kill** (R-7); recursively copy `FileStorage:BasePath`; write both under `<destination>/clinic-backup-<yyyyMMdd-HHmmss>/`. Pre-check destination writability + free space; surface disk-full / unwritable / `pg_dump`-missing as **distinct** errors (AC-8.2/8.3). Dump DB first, then copy files (R-3).

4. **Register + configure**
   - `api/ClinicManagement.Infrastructure/Extensions.cs` — register `IBackupService` → `PgDumpBackupService`.
   - `api/ClinicManagement.API/appsettings.json` — add `Backup:PgDumpPath` (resolve relative to bundled PG) + `Backup:DefaultDestination`.

5. **Expose the endpoint**
   - `api/ClinicManagement.API/Controllers/BackupController.cs` — `[Authorize(Policy = AdminOnly)]` thin controller → `BackupNowCommand`; `IsFailure → BadRequest(Error)`, else `Ok(Value)`.
   - `api/ClinicManagement.API/Models/BackupRequest.cs` — destination-folder body.

6. **Wire the admin UI**
   - `web/lib/api/backup.ts` — API client for the endpoint.
   - `web/components/backup-settings.tsx` — "Sauvegarde" card: destination input + "Sauvegarder maintenant" button with `working` state; `toast.success` with path / `toast.error` with reason; reuse the `user-management.tsx` result-dialog pattern.
   - `web/components/clinic-settings.tsx` — mount `<BackupSettings/>` in an admin-gated section (`mode==='local' && role==='admin'`).

7. **Document restore** in `packaging/README.md` (FR-G3, AC-8.4) — manual `pg_restore` + folder-copy procedure.

### Slice S2 — Windows service + startup-failure messaging (plan US-2 · FR-F1 partial, FR-F5) — *Slice dep: none*

1. **Enable Windows-service hosting**
   - Add `Microsoft.Extensions.Hosting.WindowsServices` package; `builder.Host.UseWindowsService()` in `Program.cs`, **gated** so console/dev runs are unaffected.
   - Add self-contained publish settings to `ClinicManagement.API.csproj` (or document the `dotnet publish -r win-x64 --self-contained /p:UseAppHost=true` args in `packaging/publish-server.ps1`).

2. **Operator-facing startup failure handling (Local-gated)**
   - Wrap `context.Database.Migrate()` in try/catch: unreachable DB (Npgsql connection exception) → clear operator message ("La base de données n'est pas joignable…") to console, Serilog, and Windows Event Log; non-zero exit. **Gate on `isLocalAuthMode`** so Cloud keeps its fatal-rethrow (R-9).
   - Detect port-in-use on Kestrel bind (`IOException`/`AddressInUseException`) → distinct message naming the port (Local-gated). Log the transport/ports posture on successful startup.

3. **CWD-safe path resolution (R-6)**
   - Resolve `.local/`, `Files/`, `logs/` from `AppContext.BaseDirectory` (not `Directory.GetCurrentDirectory()`).
   - **Fix the offline admin-recovery CLI** `api/ClinicManagement.API/Maintenance/AdminPasswordResetCommand.cs`: `SetBasePath(...)` + signing-key resolution use `AppContext.BaseDirectory`, so `ClinicManagement.API.exe reset-admin-password` works from any CWD (sole FR-B6 path). Document the packaged invocation in `packaging/README.md` (the `dotnet run --` form is dev-only).

### Slice S3 — HTTPS cert self-generation + fail-loud transport (plan US-3 · FR-E2, Phase 4 findings) — *Slice dep: S2 (shares `Program.cs` host setup)*

1. **Implement the provisioner**
   - `api/ClinicManagement.Infrastructure/Security/CertificateProvisioner.cs` — generate a self-signed **CA** + **server leaf** (SANs = machine hostname + every non-loopback IPv4); export `.local/server.pfx` (random per-install password stored in `.local/`) and `.local/ca.crt`.

2. **Boot integration (Local, no `Https:CertPath`)**
   - In `Program.cs`: invoke the provisioner, point Kestrel at the generated `.pfx`. If `Https:CertPath` **is** set but the file is missing → log error and **refuse the silent HTTP downgrade** (Phase 4 Finding 2). Gate on `isLocalAuthMode`, never on `httpsConfigured` alone.

3. **Move cert secrets out of committed config**
   - Read `Https:CertPassword` from `.local`/env (Phase 4 Finding 5); **remove** the committed value from `appsettings.json`.

4. **Log transport posture** (scheme + ports + cert source) on startup.

5. **Tests** — `CertificateProvisionerTests`: SANs present, CA signs leaf, idempotent on second boot.

### Slice S4 — Same-origin Kestrel front door (plan US-4 · FR-E1 finalize, enables FR-F2) — *Slice dep: none for FE audit; proxy wiring shares `Program.cs` with S2/S3*

1. **Make browser call sites work with a relative base (`/api`)**
   - `web/lib/api/client.ts` — **fix `client.ts:86`**: `apiGet` builds `new URL(`${API_BASE_URL}${endpoint}`)`, which throws `TypeError: Invalid URL` on a relative base — pass an origin base: `new URL(`${API_BASE_URL}${endpoint}`, window.location.origin)`. (`searchParams` building unchanged.)
   - `clinics.ts`, `google-calendar.ts`, `medical-documents.ts`, `patient-files.ts`, `lib/connectivity/connectivity.tsx` — verify template-string `fetch()` / `window.location.href` sites already resolve relative URLs against the page origin (no change expected).

2. **Relocate the Next BFF handlers off `/api`**
   - Move `web/app/api/auth/*` → `web/app/bff/auth/*` (`local-login`, `token`, `session`, `local-logout`, `change-password`) to avoid colliding with backend `/api/*` controllers (notably `/api/auth/change-password`).
   - Update browser callers (`lib/auth/session.tsx` `LocalSessionProvider`, login + force-password-change flows) to `/bff/auth/*`.

3. **Add the Kestrel front-door reverse proxy (Local-gated)**
   - `api/ClinicManagement.API/ClinicManagement.API.csproj` — add `Yarp.ReverseProxy`.
   - `Program.cs` — `/api/*` → controllers in-process; all other routes (`/`, `/_next/*`, assets, `/bff/*`) → `http://localhost:<webPort>` (Next). Cloud installs no proxy.

4. **Introduce `API_INTERNAL_URL`** (server-only, default `http://localhost:<apiHttpPort>/api`) — the two backend-calling BFF handlers (`local-login`, `change-password`) call Kestrel directly via it, not through the front door.

5. **Keep `web/next.config.ts`** with `output: 'standalone'` (already set); **no `rewrites()` needed** (Kestrel proxies to Next, not the reverse).

6. **Document the Local build env** in `packaging/README.md` (`NEXT_PUBLIC_API_URL=/api`, `AUTH_MODE=local`, `API_INTERNAL_URL`, `webPort`/`apiHttpPort`/`httpsPort`). Verify multipart uploads + connectivity poll hit Kestrel directly same-origin (R-5 downgraded).

### Slice S5 — Thin WebView2 desktop client (plan US-5 · spec US-2, AC-2.2–2.5) — *Slice dep: S4 (URL model)* — **committed-but-not-executed (R-1)**

1. Create `desktop/ClinicManagement.DesktopShell/` — a .NET WPF app hosting `Microsoft.Web.WebView2`.
2. First-run screen: prompt for server address; store in `%AppData%\ClinicManagement\server.json` (AC-2.2). "Changer de serveur" menu action (AC-2.3).
3. Navigate WebView2 to the Kestrel front door `https://<server>:<httpsPort>` (**not** the internal Next `webPort`); server PC can enter `localhost` (AC-2.5).
4. On `NavigationCompleted`/`CoreWebView2` failure → native **"Impossible de joindre le serveur de la clinique"** screen with a **Réessayer** action (AC-2.4).
5. Add the project to the solution and to `packaging/publish-server.ps1`'s build (self-contained; consumed by S7).

### Slice S6 — Server installer (plan US-6 · FR-F1/F3/F4, FR-E2 CA export) — *Slice dep: S1, S2, S3, S4* — **committed-but-not-executed (R-1)**

1. `packaging/publish-server.ps1` — `dotnet publish` API self-contained `win-x64`; `next build` (standalone) + stage a Node runtime; stage EDB Postgres 16 binaries.
2. `packaging/server/clinic-server.iss` (Inno Setup) — install to a stable program dir with writable `.local/`, `Files/`, `logs/`, and the Postgres data dir.
3. Postgres — `initdb` a fresh cluster, register an auto-start Windows service, create `clinic_management` + `clinic_user`.
4. Register API and Node-web as auto-start services (API via self-contained exe + `UseWindowsService`; Node via NSSM or a wrapper). Node listens **HTTP on `localhost:<webPort>` only**; **Kestrel is the sole LAN-facing HTTPS endpoint** (`httpsPort`). Dependency order **DB → web(Node) → API front door**. Open only `httpsPort` on the LAN firewall.
5. Write Local config on the target (`Auth:Mode=Local`, `Hosting:*`, `NEXT_PUBLIC_API_URL=/api`, `AUTH_MODE=local`, `API_INTERNAL_URL`); generate signing key + cert **on the machine** — **no real secrets bundled** (FR-F4); scrub committed Google/HuggingFace secrets from shipped config.
6. Export the generated CA (`.local/ca.crt`) to a shareable location for S7.
7. Verify first-run setup remains reachable **only from localhost** post-install (AC-1.2a).

### Slice S7 — Client installer (plan US-7 · FR-F2, FR-E2 client trust, AC-2.1) — *Slice dep: S3 (CA), S5 (shell)* — **committed-but-not-executed (R-1)**

1. `packaging/client/clinic-client.iss` — bundle the published `ClinicManagement.DesktopShell`; create a Start-menu/taskbar shortcut (AC-2.1).
2. Import the server's CA `.crt` into the Windows **Root** trust store (`certutil -addstore Root` or PowerShell `Import-Certificate`) so the shell connects without cert warnings (FR-E2).
3. Document (in `packaging/README.md`) how the operator obtains the CA from the server (exported in S6) and how re-running the installer updates the shell (auto-update out of scope).

## Files to Create/Modify

### Files to Create

| File | Purpose |
|------|---------|
| `api/ClinicManagement.Application/Features/Backup/Commands/BackupNowCommand.cs` | Admin-only backup command + handler → `Result<BackupResultDto>`. |
| `api/ClinicManagement.Application/DTOs/BackupResultDto.cs` | Backup result payload (path, size, timestamp). |
| `api/ClinicManagement.Application/Common/Interfaces/IBackupService.cs` | Backup seam (mockable `pg_dump` shell-out). |
| `api/ClinicManagement.Infrastructure/Services/PgDumpBackupService.cs` | `IBackupService` impl: `pg_dump` + recursive file copy + disk/writability checks. |
| `api/ClinicManagement.API/Controllers/BackupController.cs` | `[Authorize(Policy = AdminOnly)]` → `BackupNowCommand`. |
| `api/ClinicManagement.API/Models/BackupRequest.cs` | Destination-folder request body. |
| `api/ClinicManagement.Infrastructure/Security/CertificateProvisioner.cs` | Self-signed CA + server cert (SANs: hostname + LAN IPv4); writes `.local/ca.crt` + `.local/server.pfx`. |
| `web/components/backup-settings.tsx` | Admin-only "Sauvegarde" card. |
| `web/lib/api/backup.ts` | Backup API client. |
| `desktop/ClinicManagement.DesktopShell/` | New WPF + WebView2 shell project. |
| `packaging/server/clinic-server.iss` | Inno Setup server installer. |
| `packaging/client/clinic-client.iss` | Inno Setup client installer. |
| `packaging/publish-server.ps1` | Build/publish orchestration (API self-contained + Next standalone + Postgres/Node staging). |
| `packaging/README.md` | Operator build/install + manual backup-restore (FR-G3) + packaging verification checklist. |

### Files to Modify

| File | Changes |
|------|---------|
| `api/ClinicManagement.API/ClinicManagement.API.csproj` | Publish settings; `Microsoft.Extensions.Hosting.WindowsServices`; `Yarp.ReverseProxy`. |
| `api/ClinicManagement.API/Program.cs` | `UseWindowsService()` (gated); Local reverse-proxy front door; Local-gated DB-down/port-in-use startup handling; invoke `CertificateProvisioner`; fail-loud transport posture + startup log; read `Https:CertPassword` from `.local`/env; resolve `.local/`/`Files/`/`logs/` from `AppContext.BaseDirectory`. |
| `api/ClinicManagement.API/Maintenance/AdminPasswordResetCommand.cs` | Resolve config + signing key from `AppContext.BaseDirectory` (R-6, 5th site). |
| `api/ClinicManagement.Infrastructure/Extensions.cs` | Register `IBackupService` → `PgDumpBackupService`; register `CertificateProvisioner`. |
| `api/ClinicManagement.API/appsettings.json` | Add `Backup:PgDumpPath` + `Backup:DefaultDestination`; **remove** committed `Https:CertPassword`. |
| `web/lib/api/client.ts` | **Fix `client.ts:86`** (`new URL(..., window.location.origin)`); relative `/api` base. |
| `web/lib/api/clinics.ts`, `google-calendar.ts`, `medical-documents.ts`, `patient-files.ts`, `lib/connectivity/connectivity.tsx` | Work with relative `NEXT_PUBLIC_API_URL=/api` (same-origin). |
| `web/app/bff/auth/*` (renamed from `web/app/api/auth/*`) | Relocate `local-login`, `token`, `session`, `local-logout`, `change-password` off `/api`; update browser callers; backend-calling handlers use `API_INTERNAL_URL`. |
| `web/components/clinic-settings.tsx` | Mount `<BackupSettings/>` (admin + Local gated). |
| `web/next.config.ts` | Keep `output: 'standalone'`; no `rewrites()` needed. |
| `.gitignore` | Ensure `.local/ca.crt`, `.local/server.pfx`, `packaging/**/build-output` ignored; keep `.iss`/`.ps1`. |

## Verification Steps

After completing this story, verify:

**Testable in-environment (S1–S4):**
- [ ] Backup: admin sees the card, non-admin / Cloud does not; success writes DB dump + full `Files/` copy and reports the exact path (AC-8.1); unwritable / disk-full / missing-`pg_dump` each produce a distinct non-silent error (AC-8.2/8.3).
- [ ] Startup: `dotnet run` (console) behaves exactly as today; DB-down → clear message + non-zero exit; port-in-use → distinct message naming the port; secrets/storage/logs resolve from an arbitrary CWD; `reset-admin-password` recovers from a directory other than the install dir.
- [ ] Cert: first Local boot with no cert → `.local/server.pfx` + `.local/ca.crt` created, HTTPS served on the LAN IP; second boot is idempotent; `Https:CertPath` → missing file fails loud (no cleartext); `appsettings.json` carries no cert password.
- [ ] Same-origin: with `NEXT_PUBLIC_API_URL=/api` + front door up, login (`/bff/auth/*`), patients, file upload/download, connectivity badge, AI/calendar gating all work over one HTTPS origin; browser GETs work (the `new URL` fix); `/api/*` → controllers, `/bff/*`+pages+assets → Next, nothing shadowed.
- [ ] **Cloud unchanged:** Cloud build keeps its absolute URL + no proxy (byte-for-byte); DB-down/port-in-use handling does not fire in Cloud; re-run Phase 4 auth-coverage + Cloud-path checks.

**Operator-verified (S5–S7), per `packaging/README.md` — not runnable here (R-1):**
- [ ] Shell: first launch asks for + persists the server address; "change server" edits without reinstall; unreachable → friendly retry screen (AC-2.4); project builds as part of the solution.
- [ ] Server installer: fresh install → services start on boot in DB→web→API order; reachable from a LAN PC (AC-1.3); first-run setup localhost-only creates clinic+admin offline (AC-1.2/1.2a); no manual PG/port/cert config (AC-1.4); installed config has no real secrets (FR-F4).
- [ ] Client installer: places a working shell with a Start-menu icon (AC-2.1); shell reaches the server over HTTPS with no cert warning; uninstall removes the shell.

**Verification commands:**
```bash
# Backend build + unit/integration tests (S1–S3)
cd api && dotnet build ClinicManagement.sln
dotnet test   # BackupNowCommandHandler, CertificateProvisioner, PgDumpBackupService (fake pg_dump shim), startup-classification helper
              # NOTE: Smart App Control may block freshly-built test DLLs (0x800711C7) — environmental, see project memory / R-1

# Web build (S4) — standalone output for the bundled Node server
cd web && npm run build

# Cloud regression sanity (S2/S3/S4 gating): confirm no Local-only behavior fires in Cloud mode
```

## Exit Criteria

This story is complete when:

- [ ] All in-environment verification steps (S1–S4) pass; S5–S7 artifacts are committed and their operator-verification checklist is documented in `packaging/README.md`.
- [ ] All new behavior is gated on `isLocalAuthMode`; Cloud is verified byte-for-byte unchanged.
- [ ] Backend builds with 0 errors / 0 new warnings; `npm run build` succeeds (standalone).
- [ ] Unit + integration tests for backup, cert provisioning, and startup classification pass (or are documented-skipped only for the SAC/R-1 environmental limitation).
- [ ] `packaging/README.md` documents: Local build env, packaged `reset-admin-password` invocation, manual restore (FR-G3), and the per-AC packaging verification checklist.
- [ ] Reviewed and approved.

## Notes

- **R-1 (packaging not verifiable here):** S5–S7 land as committed, reviewable artifacts + an operator checklist — not runtime pass/fail in this environment (no Windows installer execution; Smart App Control blocks freshly-built test DLLs). Flag S5–S7 explicitly as operator-verified in `progress.md`.
- **Gate on mode, not capability:** every new behavior keys off `isLocalAuthMode`, never off `httpsConfigured`/`pg_dump present`/etc. — consistent with the Phase 1–4 learning and R-9.
- **Decision #2 (single front door):** Kestrel is the only browser-facing HTTPS endpoint; the Next Node server is localhost-HTTP-only. Do not bind the shell or LAN clients to `webPort`.
- **R-7 (first `Process.Start`):** argument list not shell string; `PGPASSWORD` via env; timeout + kill; capture stderr for the error message.
- **Challenge (2026-07-09) resolved 4 issues** now folded into the slices above: TLS front door (S4), `/api` namespace collision → `/bff/*` (S4), `new URL()` GET breakage (S4 step 1), offline admin-recovery CWD (S2 step 3). See `plan.md` Challenge Summary.
