# Implementation Plan: Windows Desktop / Offline-LAN — Phase 5 (Packaging, Installers & Manual Backup)

**Status:** APPROVED
**Challenged:** Yes (2026-07-09 — 4 issues resolved: TLS front door, `/api` namespace collision, `new URL()` GET breakage, offline admin-recovery CWD; see Challenge Summary below)
**Created:** 2026-07-09
**Spec:** [spec.md](./spec.md) (APPROVED · Challenged) — this plan covers **Phase 5 only** (FR-F + FR-G + the FR-E2 cert-provisioning carry-over). Phases 1 (pluggable auth), 2 (local-disk storage), 3 (connectivity awareness) and 4 (LAN hosting & security gates) are **COMPLETE**; their planning artifacts are archived under [phase-1/](./phase-1/), [phase-3/](./phase-3/) and [phase-4/](./phase-4/) (Phase 2 was a single feat commit, no pipeline archive). This is the **final phase** of the umbrella spec.

## Overview

Phase 5 turns the hardened Local-mode app (Phases 1–4) into **installable Windows software**: a **server installer** that stands up the whole stack as auto-starting Windows services on one clinic PC, a **lightweight client installer** that places a thin WebView2 desktop shell pointing at that server, and an admin-only **one-click "Backup now"**. All behavior stays **additive and gated to Local mode** (`Auth:Mode = Local`); Cloud remains byte-for-byte unchanged, consistent with Phases 1–4.

**Key architectural decisions made during planning (approved 2026-07-09):**

1. **Packaging scope:** one plan covers all of FR-F + FR-G. Stories split cleanly into **in-repo code** (testable/verifiable: backup, service hosting, startup-failure UX, cert self-generation, same-origin web) and **packaging artifacts** (committed-but-not-executed here: WebView2 shell project, Inno Setup installer scripts, bundled-Postgres layout). Packaging stories are verified by an operator against a documented checklist, not by this environment (which cannot run a Windows installer, and where Smart App Control blocks freshly-built test DLLs — see R-1).
2. **Same-origin web via a single Kestrel front door (biggest technical change; TLS + routing revised during challenge 2026-07-09):** the browser bundle bakes the API URL at build time via 8 `NEXT_PUBLIC_API_URL` reads, so one hosted build could not serve clients at arbitrary server IPs. Fix: build the **Local** web with `NEXT_PUBLIC_API_URL=/api` (relative → same-origin), and make **Kestrel the single browser-facing HTTPS endpoint** (bound to the generated cert from US-3): it serves `/api/*` in-process and **reverse-proxies every other route** (`/`, `/_next/*`, static assets, and the BFF routes below) to the co-located Next server on `http://localhost:<webPort>`. The Next (Node) server therefore listens **HTTP on localhost only** and is never LAN-facing. This terminates TLS **once, inside the audited .NET app** (no Node TLS, no `next.config` rewrites needed), is install-independent, eliminates cross-origin CORS for browser calls, and makes AC-2.4 a shell-navigation concern. Cloud keeps its absolute URL and its current hosting (no reverse proxy) — unchanged, gated on `isLocalAuthMode`. *(Rejected alternatives: Next terminating TLS via a custom `server.js`, and a separate bundled reverse proxy — both push TLS outside the .NET layer or add a 4th service.)*
   - **`/api` namespace split (challenge finding):** Next's own BFF route handlers currently live under `app/api/auth/*` and would collide with backend controllers in a shared `/api` namespace — notably `/api/auth/change-password` exists on **both** the backend `AuthController` (`AuthController.cs:152`) and as a Next handler, and ASP.NET endpoint routing would match its own controller first, shadowing the Next handler that does the session-cookie work. They are therefore **relocated to `/bff/auth/*`** (`local-login`, `token`, `session`, `local-logout`, `change-password`) so the front door routes unambiguously: `/api/*` → Kestrel controllers in-process; `/bff/*` + pages + assets → reverse-proxy to Next. The BFF handlers still reach the backend directly via a **server-only** `API_INTERNAL_URL` (default `http://localhost:<apiHttpPort>/api`), not through the front door.
3. **Thin client shell:** a new **.NET WPF + WebView2** project (`ClinicManagement.DesktopShell`) — same toolchain as the API, no new language. It stores/edits the server address, navigates WebView2 to the **Kestrel front door** `https://<server>:<httpsPort>`, and renders a native "Cannot reach the clinic server" screen with retry on nav failure (AC-2.4). The server PC can point it at localhost (AC-2.5). No web assets are bundled in the client (spec Resolved Decision #3).
4. **HTTPS cert generation (FR-E2 carry-over):** the **API self-generates** a self-signed CA + server certificate into `.local/` on first Local-mode boot when no cert is configured (SANs = server hostname + all LAN IPv4 addresses), and exports the CA `.crt` for the client installer to import into the Windows trust store. This keeps crypto logic in testable C# rather than installer script. Phase 4's two confirmed HTTPS findings are folded in here: transport now **fails loud/closed** (a set-but-missing cert path refuses the silent HTTP downgrade and logs the posture on startup), and `Https:CertPassword` moves out of committed `appsettings.json` into the per-install `.local/` store.
5. **Bundled Postgres:** the server installer ships **EnterpriseDB Postgres 16 Windows binaries** (no Docker dependency), `initdb`s a fresh cluster, registers it as an auto-start service, and creates `clinic_management` + `clinic_user`. Its `pg_dump.exe` is reused by the backup feature.
6. **Backup mechanism:** `BackupNowCommand` shells out to the bundled `pg_dump.exe` (first `Process.Start` in the codebase) for a DB dump and recursively copies `FileStorage:BasePath`, both into a timestamped destination folder; success reports the exact path, failure reports a clear reason. Restore is a documented manual `pg_restore` + folder-copy procedure (FR-G3).

**Story ordering:** backup (fully testable, immediate value) → server hardening for hosting (service + startup UX, then cert self-gen) → same-origin web → desktop shell → server installer → client installer. Installers come last because they package everything the earlier stories produce.

---

## Files to Modify/Create

### Files to Create

| File | Purpose |
|------|---------|
| `api/ClinicManagement.Application/Features/Backup/Commands/BackupNowCommand.cs` | Admin-only command + handler: `pg_dump` + recursive file copy to a timestamped folder; returns `Result<BackupResultDto>`. |
| `api/ClinicManagement.Application/DTOs/BackupResultDto.cs` | Backup result payload (destination path, byte size, timestamp). |
| `api/ClinicManagement.Application/Common/Interfaces/IBackupService.cs` | Seam for the backup mechanism (so the handler stays testable and the `pg_dump` shell-out is mockable). |
| `api/ClinicManagement.Infrastructure/Services/PgDumpBackupService.cs` | `IBackupService` impl: locate/invoke `pg_dump.exe`, `PGPASSWORD` via env, copy files, disk-space/writability checks. |
| `api/ClinicManagement.API/Controllers/BackupController.cs` | `[Authorize(Policy = AdminOnly)]` thin controller → `BackupNowCommand`. |
| `api/ClinicManagement.API/Models/BackupRequest.cs` | Request body (destination folder). |
| `api/ClinicManagement.Infrastructure/Security/CertificateProvisioner.cs` | Self-signed CA + server-cert generation (SANs: hostname + LAN IPv4), writes `.local/ca.crt` + `.local/server.pfx`, exports CA `.crt`. |
| `web/components/backup-settings.tsx` | Admin-only "Sauvegarde" card: destination input + "Sauvegarder maintenant" + result/error display. |
| `web/lib/api/backup.ts` | API client for the backup endpoint. |
| `desktop/ClinicManagement.DesktopShell/` (new WPF project) | Thin WebView2 shell: server-address store/edit, navigation, AC-2.4 unreachable screen with retry. |
| `packaging/server/clinic-server.iss` | Inno Setup script: bundle API + web + Postgres + cert; register services (DB→API→web); export CA. |
| `packaging/client/clinic-client.iss` | Inno Setup script: place the shell, import server CA into Windows trust store, Start-menu shortcut. |
| `packaging/publish-server.ps1` | Build/publish orchestration: `dotnet publish -r win-x64 --self-contained` (API) + `next build` standalone; stages Postgres binaries + web `node` runtime for the installer. |
| `packaging/README.md` | Operator build + install + **manual backup-restore** procedure (FR-G3), and the packaging-story verification checklist. |

### Files to Modify

| File | Changes |
|------|---------|
| `api/ClinicManagement.API/ClinicManagement.API.csproj` | Add `RuntimeIdentifier`/`SelfContained`/`UseAppHost` publish settings (or drive via publish args), the `Microsoft.Extensions.Hosting.WindowsServices` package, and `Yarp.ReverseProxy` (front-door reverse proxy to the localhost Next server, Local-gated). |
| `api/ClinicManagement.API/Program.cs` | `AddWindowsService()` (gated, dev/console unaffected); **Local-mode reverse-proxy** — map `/api/*` to controllers in-process and reverse-proxy all other routes to `http://localhost:<webPort>` (Next), so Kestrel is the sole browser-facing HTTPS endpoint (Decision #2); wrap `Database.Migrate()` + Kestrel bind in operator-facing failure handling (FR-F5, distinct DB-down vs port-in-use messages → console/log/Event Log — **Local-gated so Cloud startup stays byte-for-byte**); invoke `CertificateProvisioner` on first Local boot with no cert; **fail-loud** transport posture + startup log; read `Https:CertPassword` from `.local`/env; resolve `.local/`, `Files/`, `logs/` from `AppContext.BaseDirectory` not CWD (service working-dir safety, R-6). |
| `api/ClinicManagement.API/Maintenance/AdminPasswordResetCommand.cs` | Resolve `appsettings.json` (`SetBasePath`) and the signing key from `AppContext.BaseDirectory` (not `Directory.GetCurrentDirectory()`), so the packaged recovery exe works from any CWD (R-6, 5th site; sole offline admin-recovery path). |
| `api/ClinicManagement.Infrastructure/Extensions.cs` | Register `IBackupService` → `PgDumpBackupService`; register `CertificateProvisioner`. |
| `api/ClinicManagement.API/appsettings.json` | Add `Backup:PgDumpPath` (resolve relative to bundled PG), `Backup:DefaultDestination`; **remove** the committed `Https:CertPassword` value. |
| `web/lib/api/client.ts`, `clinics.ts`, `google-calendar.ts`, `medical-documents.ts`, `patient-files.ts`, `lib/connectivity/connectivity.tsx` | Make each work with a **relative** `NEXT_PUBLIC_API_URL=/api` (same-origin). **`client.ts:86` must be fixed**: `apiGet` does `new URL(`${API_BASE_URL}${endpoint}`)`, which **throws `TypeError: Invalid URL`** on a relative base — pass `window.location.origin` as the base (`new URL(…, window.location.origin)`). The template-string `fetch()` / `window.location.href` sites (POST/PUT/DELETE/upload, `google-calendar.ts:33`) already resolve relative URLs against the page origin and need no change. |
| `web/app/bff/auth/*` (renamed from `web/app/api/auth/*`: `local-login`, `token`, `session`, `local-logout`, `change-password`) | **Relocate off the `/api` namespace to `/bff/auth/*`** to avoid colliding with backend controllers behind the single front door (Decision #2). Update the browser call sites that post to them (the `LocalSessionProvider` in `lib/auth/session.tsx`, the login and force-password-change flows). The two backend-calling handlers (`local-login`, `change-password`) use a **server-only** `API_INTERNAL_URL` (default `http://localhost:<apiHttpPort>/api`), not the browser-relative value. |
| `web/next.config.ts` | **No `rewrites()` needed** (the Kestrel front door proxies to Next, not the reverse). Keep `output: 'standalone'` (already set) for the bundled Node server. |
| `web/components/clinic-settings.tsx` | Mount `<BackupSettings/>` in an admin-gated section (`mode==='local' && role==='admin'`). |
| `.gitignore` | Ensure `.local/ca.crt`, `.local/server.pfx`, and `packaging/**/build-output` are ignored; keep committed `.iss`/`.ps1` scripts. |

---

## Implementation Stories

### US-1: One-click admin backup (FR-G / US-8)

**Goal:** A clinic admin clicks "Sauvegarder maintenant" in settings and gets a consistent DB + files backup written to a chosen folder, with an explicit success path or a clear failure reason.
**Blocked by:** None
**Layers:** Service, API, UI

**Steps:**
1. Add `IBackupService` in `Application/Common/Interfaces/` and `BackupResultDto` in `Application/DTOs/`.
2. Create `BackupNowCommand` + handler in `Application/Features/Backup/Commands/`: resolve caller via `IClinicContext.GetUserId()`, load user, enforce `admin.IsAdmin()` (mirror `ResetUserPasswordCommand`), then call `IBackupService`. Wrap in `try/catch` → `Result<BackupResultDto>.Failure(...)`.
3. Implement `PgDumpBackupService` in `Infrastructure/Services/`: parse `ConnectionStrings:DefaultConnection` via `NpgsqlConnectionStringBuilder`; locate `pg_dump.exe` from `Backup:PgDumpPath`; run it with `Process.Start` (redirect stdout/stderr, `PGPASSWORD` env, capture exit code); recursively copy `FileStorage:BasePath`; write both under `<destination>/clinic-backup-<yyyyMMdd-HHmmss>/`. Pre-check destination writability + free space; surface disk-full/unwritable/`pg_dump`-missing as distinct clear errors (AC-8.2, AC-8.3).
4. Register `IBackupService` in `Infrastructure/Extensions.cs`. Add `Backup:PgDumpPath` + `Backup:DefaultDestination` to `appsettings.json`.
5. Create `BackupController` (`[Authorize(Policy = AdminOnly)]`) → sends `BackupNowCommand`; `IsFailure → BadRequest(Error)`, else `Ok(Value)`.
6. Add `web/lib/api/backup.ts` and `web/components/backup-settings.tsx` (destination input, button with `working` state, `toast.success` with path / `toast.error` with reason, reuse the `user-management.tsx` result-dialog pattern). Mount admin-gated in `clinic-settings.tsx`.
7. Document the manual restore procedure in `packaging/README.md` (FR-G3, AC-8.4).

**Validation:**
- [ ] Admin sees the backup card; non-admin / Cloud mode does not.
- [ ] A successful backup writes DB dump + a full copy of `Files/` and reports the exact path (AC-8.1).
- [ ] Unwritable destination / disk full / missing `pg_dump` each produce a distinct, non-silent error (AC-8.2/8.3).
- [ ] Unit tests: handler admin-gate (non-admin → Failure); `IBackupService` mocked. Integration test: `PgDumpBackupService` against a temp dir with a fake `pg_dump` shim.
- [ ] Build 0 errors / 0 new warnings.

### US-2: Server runs as a Windows service with clear startup-failure messaging (FR-F1 partial, FR-F5)

**Goal:** The API can run as an auto-starting Windows service, and when Postgres is down or a port is already in use at boot it surfaces a clear operator message instead of a bare crash.
**Blocked by:** None
**Layers:** API (host)

**Steps:**
1. Add the `Microsoft.Extensions.Hosting.WindowsServices` package and `builder.Host.UseWindowsService()` in `Program.cs`, gated so console/dev runs are unaffected.
2. Add self-contained publish settings to `ClinicManagement.API.csproj` (or document the `dotnet publish -r win-x64 --self-contained /p:UseAppHost=true` args in `packaging/publish-server.ps1`).
3. Wrap `context.Database.Migrate()` in a try/catch that detects an unreachable DB (Npgsql connection exception) and logs an operator-facing message ("La base de données n'est pas joignable…") to console, Serilog, and the Windows Event Log; exit with a non-zero code. **Local-gate this handling** (`isLocalAuthMode`) so Cloud startup stays byte-for-byte (Cloud keeps its current fatal-rethrow), consistent with R-9 and the "gate on mode, not capability" learning.
4. Detect port-in-use on Kestrel bind (`IOException`/`AddressInUseException`) and log a distinct clear message naming the port (Local-gated). Log the chosen transport/ports posture on successful startup (observability, ties to US-4 fail-loud).
5. Resolve `.local/`, `Files/`, `logs/` from `AppContext.BaseDirectory` (not `Directory.GetCurrentDirectory()`) so a service whose CWD is `System32` still finds them (R-6). **Also fix the offline admin-recovery CLI** (`Maintenance/AdminPasswordResetCommand.cs`): its `SetBasePath(Directory.GetCurrentDirectory())` and signing-key resolution must use `AppContext.BaseDirectory`, so `ClinicManagement.API.exe reset-admin-password` works from any working directory — this is the **sole** offline recovery path (FR-B6) and must not silently read a stale/empty `.local`. Document the packaged invocation in `packaging/README.md` (the `dotnet run --` form is dev-only).

**Validation:**
- [ ] `dotnet run` (console) behaves exactly as today; service mode is opt-in.
- [ ] DB down at boot → clear message + non-zero exit, no raw stack trace as the primary signal (FR-F5).
- [ ] Port in use → distinct clear message naming the port.
- [ ] Secrets/storage/logs resolve correctly when launched from an arbitrary working directory.
- [ ] `ClinicManagement.API.exe reset-admin-password` recovers the admin when run from a directory other than the install dir (reads the install's `.local`/config, not a stale one).
- [ ] Cloud mode startup unchanged (DB-down/port-in-use handling does not fire in Cloud). Build clean.

### US-3: Server self-generates its HTTPS certificate and fails loud on transport misconfig (FR-E2, Phase 4 HTTPS findings)

**Goal:** On first Local boot with no cert, the server generates a trusted-on-the-LAN certificate and serves HTTPS; a misconfigured cert now refuses to silently fall back to cleartext.
**Blocked by:** US-2
**Layers:** Infrastructure, API (host)

**Steps:**
1. Implement `CertificateProvisioner` in `Infrastructure/Security/`: generate a self-signed **CA** and a **server leaf** (SANs = machine hostname + every non-loopback IPv4), export `.local/server.pfx` (random per-install password stored in `.local/`) and `.local/ca.crt`.
2. In `Program.cs` (Local mode, no `Https:CertPath` set): invoke the provisioner, point Kestrel at the generated `.pfx`. If `Https:CertPath` **is** set but the file is missing → log an error and refuse the silent HTTP downgrade (fail loud/closed — Phase 4 Finding 2); gate all of this on `isLocalAuthMode`, never on `httpsConfigured` alone (LEARNINGS: gate on mode, not capability).
3. Read `Https:CertPassword` from `.local`/env (Phase 4 Finding 5); remove the committed value from `appsettings.json`.
4. Log the transport posture (scheme + ports + cert source) on startup.
5. Extend `ControllerAuthorizationCoverageTests`-style discipline is N/A here; add `CertificateProvisionerTests` (SANs present, CA signs leaf, idempotent on second boot).

**Validation:**
- [ ] First Local boot with no cert → `.local/server.pfx` + `.local/ca.crt` created; API serves HTTPS on the LAN IP without a self-generated-cert crash.
- [ ] Second boot reuses the existing cert (idempotent).
- [ ] `Https:CertPath` set to a missing file → startup fails loud (logged), does not serve cleartext.
- [ ] `appsettings.json` no longer carries a cert password.
- [ ] Cloud unchanged (no generation, prior redirect behavior intact). Unit tests pass; build clean.

### US-4: One web build serves every install via a same-origin Kestrel front door (FR-E1 finalize, enables FR-F2)

**Goal:** The hosted Next.js build works unchanged on any server IP/hostname because the browser calls its own origin; **Kestrel is that origin** — it serves `/api/*` in-process and reverse-proxies everything else to the localhost Next server. TLS is terminated once, in the .NET app (Decision #2).
**Blocked by:** None for the FE audit; the reverse-proxy wiring shares `Program.cs` host setup with US-2/US-3.
**Layers:** UI (web) + API (host reverse proxy)

**Steps:**
1. Make the 6 browser call sites (`client.ts`, `clinics.ts`, `google-calendar.ts`, `medical-documents.ts`, `patient-files.ts`, `connectivity.tsx`) work with a **relative** base (`/api`). The one real breakage is **`client.ts:86`** — `apiGet` builds `new URL(`${API_BASE_URL}${endpoint}`)`, which **throws `TypeError: Invalid URL`** when the base is relative (`/api/patients`), breaking *every* GET. Fix by passing an origin base: `new URL(`${API_BASE_URL}${endpoint}`, window.location.origin)` (query-param building via `searchParams` is unchanged). The `fetch()` template-string sites and the `google-calendar.ts:33` `window.location.href` redirect already resolve relative URLs against the page origin — no change.
2. **Relocate the Next BFF route handlers** from `app/api/auth/*` → `app/bff/auth/*` (`local-login`, `token`, `session`, `local-logout`, `change-password`) so they don't collide with backend `/api/*` controllers behind the front door (notably `/api/auth/change-password`). Update the browser callers (`lib/auth/session.tsx` `LocalSessionProvider`, login + force-password-change flows) to the new `/bff/auth/*` paths.
3. **Add the Kestrel front-door reverse proxy** (`Yarp.ReverseProxy`, or minimal `MapFallback` forwarder) in `Program.cs`, **gated on `isLocalAuthMode`**: `/api/*` → controllers in-process; all other routes (`/`, `/_next/*`, assets, `/bff/*`) → `http://localhost:<webPort>` (the Next server). Cloud installs no proxy (byte-for-byte unchanged).
4. Introduce `API_INTERNAL_URL` (server-only, default `http://localhost:<apiHttpPort>/api`) and point the two backend-calling BFF handlers (`local-login`, `change-password`) at it — they run in the Node process and must call Kestrel directly, not loop through the front door.
5. Document the Local build env (`NEXT_PUBLIC_API_URL=/api`, `AUTH_MODE=local`, `API_INTERNAL_URL`, `webPort`/`apiHttpPort`/`httpsPort`) in `packaging/README.md`.
6. Verify multipart uploads (patient files) and the connectivity poll — these now hit Kestrel **directly same-origin** (`/api/...`, no Node hop), so the earlier Node-proxy body-size concern (old R-5) does not apply.

**Validation:**
- [ ] With `NEXT_PUBLIC_API_URL=/api` and the front door up, login (`/bff/auth/*`), patients, file upload/download, connectivity badge, and AI/calendar gating all work over the single HTTPS origin.
- [ ] Browser `GET`s work (the `new URL` fix) — not just POST/PUT/DELETE.
- [ ] Page routes, `_next/*` assets, and `/bff/*` are reverse-proxied to Next; `/api/*` is served by controllers; no path is shadowed.
- [ ] Cloud build (absolute URL, no proxy) is byte-for-byte unchanged.
- [ ] BFF handlers reach the API via `API_INTERNAL_URL`. `npm run build` succeeds (standalone output).

### US-5: Thin WebView2 desktop client (US-2 spec / FR-F2, AC-2.2–2.5)

**Goal:** A staff member launches a lightweight Windows app, enters the clinic server address once, and uses the system like normal software — with a clear recoverable screen when the server is unreachable.
**Blocked by:** US-4 (URL model)
**Layers:** New desktop project *(committed-but-not-executed here — see R-1)*

**Steps:**
1. Create `desktop/ClinicManagement.DesktopShell/` — a .NET WPF app hosting the `Microsoft.Web.WebView2` control.
2. First-run screen: prompt for server address (IP/hostname); store it in a per-user file under `%AppData%\ClinicManagement\server.json` (AC-2.2). Provide a "Changer de serveur" menu action (AC-2.3).
3. Navigate WebView2 to the Kestrel front door `https://<server>:<httpsPort>` (Decision #2 — the browser-facing HTTPS origin, **not** the internal Next `webPort`); the server PC can enter `localhost` (AC-2.5).
4. Handle `NavigationCompleted`/`CoreWebView2` failure + connection errors → render a native **"Impossible de joindre le serveur de la clinique"** screen with a **Réessayer** action (AC-2.4) — never a blank page or raw browser error.
5. Add the project to the solution and to `packaging/publish-server.ps1`'s build (published self-contained; consumed by the client installer in US-7).

**Validation:** *(operator-verified per `packaging/README.md`; not runnable in this environment)*
- [ ] First launch asks for the server address and persists it; subsequent launches reuse it.
- [ ] "Change server" edits the address without reinstalling.
- [ ] Unreachable server shows the friendly retry screen, not a browser error (AC-2.4).
- [ ] Project builds (`dotnet build`) as part of the solution.

### US-6: Server installer — one-click stand-up of the whole stack (FR-F1, FR-F3, FR-F4, FR-E2 CA export)

**Goal:** Running one installer on a clinic PC produces a working server: API + hosted web + bundled Postgres + storage + generated cert, all as auto-starting services in the right order, populated via first-run setup.
**Blocked by:** US-1, US-2, US-3, US-4
**Layers:** Packaging *(committed-but-not-executed here — see R-1)*

**Steps:**
1. `packaging/publish-server.ps1`: `dotnet publish` the API self-contained `win-x64`; `next build` (standalone) + stage a Node runtime; stage EDB Postgres 16 binaries.
2. `packaging/server/clinic-server.iss` (Inno Setup): install files to a stable program dir with writable `.local/`, `Files/`, `logs/`, and the Postgres data dir.
3. Postgres: `initdb` a fresh cluster, register an auto-start Windows service, create `clinic_management` DB + `clinic_user` with the connection-string credentials.
4. Register the API and Node-web as auto-start services (the API via its self-contained exe + `UseWindowsService`; Node via a service wrapper such as NSSM). The **Node web server listens HTTP on `localhost:<webPort>` only**; the **API/Kestrel is the sole LAN-facing HTTPS endpoint** (`httpsPort`) and reverse-proxies non-`/api` routes to Node (Decision #2). **Dependency order DB → web(Node) → API front door** (the API needs the DB to migrate and needs Node up as its proxy target); the API auto-migrates on startup (FR-F3) and self-generates the cert (US-3). Open only `httpsPort` on the LAN firewall — `webPort` and `apiHttpPort` stay loopback-only.
5. Write the Local config on the target (`Auth:Mode=Local`, `Hosting:*`, `NEXT_PUBLIC_API_URL=/api`, `AUTH_MODE=local`, `API_INTERNAL_URL`), generating the signing key + cert **on the machine** — **no real secrets bundled** (FR-F4); scrub the committed Google/HuggingFace secrets out of the shipped config.
6. Export the generated CA (`.local/ca.crt`) to a shareable location for the client installer (US-7).
7. First-run setup remains reachable **only from localhost** (AC-1.2a, already enforced) — verify post-install.

**Validation:** *(operator-verified per `packaging/README.md`)*
- [ ] Fresh install → services start on boot in DB→API→web order; app reachable from a LAN PC (AC-1.3).
- [ ] First launch shows first-run setup (localhost only); creates clinic + admin with no internet (AC-1.2/1.2a).
- [ ] No PostgreSQL/port/cert manual configuration required (AC-1.4).
- [ ] Installed config contains no real secrets (FR-F4).

### US-7: Client installer — shell + CA trust (FR-F2, FR-E2 client trust, AC-2.1)

**Goal:** A lightweight client installer places the desktop shell with a Start-menu icon and imports the server's CA so HTTPS to the server has no trust warnings.
**Blocked by:** US-3 (CA), US-5 (shell)
**Layers:** Packaging *(committed-but-not-executed here — see R-1)*

**Steps:**
1. `packaging/client/clinic-client.iss`: bundle the published `ClinicManagement.DesktopShell`; create a Start-menu/taskbar shortcut (AC-2.1).
2. Import the server's CA `.crt` into the Windows **Root** trust store (`certutil -addstore Root` or PowerShell `Import-Certificate`), so the WebView2 shell connects to the server by IP/hostname without cert warnings (FR-E2).
3. Document (in `packaging/README.md`) how the operator obtains the CA from the server (exported in US-6) and how re-running the installer updates the shell (auto-update is out of scope, spec).

**Validation:** *(operator-verified per `packaging/README.md`)*
- [ ] Client install places a working shell with a Start-menu icon.
- [ ] After install, the shell reaches the server over HTTPS with no certificate warning.
- [ ] Uninstall removes the shell (CA-removal behavior documented).

---

## Testing Strategy

### Unit Tests
- `BackupNowCommandHandler`: non-admin caller → `Failure`; admin → delegates to `IBackupService`; service failure surfaces its message (US-1).
- `CertificateProvisioner`: CA signs the leaf; SANs include hostname + a LAN IPv4; second run is idempotent (US-3).
- Startup-failure classification helper: maps an Npgsql connect exception → "DB unreachable" message; an address-in-use exception → "port in use" message (US-2).

### Integration Tests
- `PgDumpBackupService` against a temp destination with a **fake `pg_dump` shim** and a seeded `Files/` dir: verifies the timestamped folder layout, that files are copied, and that unwritable-destination / missing-`pg_dump` produce distinct errors (US-1). *(Note: real `pg_dump` and Windows-service behavior are not exercisable in this environment — Smart App Control blocks freshly-built test DLLs; see R-1.)*
- Same-origin proxy: a Local-config `next build` + a request to `/api/connectivity` proxied to a stub API (US-4), if runnable; otherwise a documented manual check.

### E2E Tests
- Backup happy path (Local mode): admin → settings → "Sauvegarder maintenant" → success toast with path; verify folder on disk (US-1) — runnable if an E2E harness for Local mode exists, else manual.
- Packaging stories (US-5/6/7) are **operator-verified** against the `packaging/README.md` checklist — not automatable here.

*(Per project preference, no Postman/Newman API collections are produced.)*

## Risk Register

| ID | Risk | Likelihood | Impact | Story | Mitigation |
|----|------|------------|--------|-------|------------|
| R-1 | Packaging/shell stories can't be built/run/verified in this environment | High | Med | US-5,6,7 | Push testable logic into the API (US-1/2/3/4); treat installers/shell as committed scripts + an operator verification checklist. |
| R-2 | `pg_dump.exe` missing or version-mismatched at runtime | Med | Med | US-1 | Config `Backup:PgDumpPath` resolved relative to bundled PG; clear "backup tool not found" error; bundle the matching PG major version. |
| R-3 | Files copied separately from the DB dump can diverge under concurrent writes | Low | Low | US-1 | Dump DB first then copy files; single-clinic low-concurrency; document as last-write/best-effort snapshot (spec edge case). |
| R-4 | Self-signed cert still warns if the client uses an address not in the cert SANs | Med | Med | US-3,7 | Generate SANs for hostname + all LAN IPv4; document that changing the server IP requires re-export/re-import of the CA. |
| R-5 | Front-door reverse proxy mishandles streaming/large bodies | Low | Med | US-4 | **Reduced by Decision #2**: `/api/*` (incl. multipart uploads/downloads) now hits Kestrel **directly, same-origin** — no Node hop, so the old Next-proxy body-size concern is gone (and `serverActions.bodySizeLimit` was never relevant to a proxy anyway — it only bounds Server Actions). The reverse proxy carries only page/asset/`/bff/*` traffic; verify YARP streams `_next` assets and BFF POSTs without buffering issues. |
| R-6 | A Windows service's CWD is `System32`, breaking CWD-relative `.local/`/`Files/`/`logs/` | High | High | US-2,6 | Resolve those paths from `AppContext.BaseDirectory`; set the service `WorkingDirectory` in the installer as belt-and-suspenders. |
| R-7 | `Process.Start`/`pg_dump` is the first external-process use — injection / hung process | Med | Med | US-1 | Pass args as an argument list (no shell string), `PGPASSWORD` via env not CLI, enforce a timeout + kill, capture stderr for the error message. |
| R-8 | Hosting the Next Node server as a Windows service (Node isn't natively a service) | Med | Med | US-6 | Use NSSM (or a thin wrapper); document the chosen mechanism; health-gate the API dependency. |
| R-9 | Cloud regression from the same-origin change or `UseWindowsService`/cert gating | Low | High | US-2,3,4 | Gate every new behavior on `isLocalAuthMode`; Cloud web keeps its absolute URL build; re-run the Phase 4 auth-coverage + Cloud-path checks. |
| R-10 | Bundled Postgres size/`initdb`-at-install fragility (permissions, existing cluster) | Med | Med | US-6 | Install into a fresh data dir owned by the service account; detect an existing cluster and skip/repair; document disk needs. |

### R-1: Packaging stories not verifiable in-environment
- **Description:** This session cannot execute a Windows installer, build a self-contained exe reliably (Smart App Control blocks freshly-built test DLLs — see project memory), or run a WebView2 shell. So US-5/6/7 land as committed artifacts without runtime proof.
- **Likelihood:** High · **Impact:** Medium
- **Story:** US-5, US-6, US-7
- **Mitigation:** Concentrate the genuinely testable logic in the API/web stories (backup, service startup UX, cert generation, same-origin proxy) so the shippable behavior is covered; write installer/shell code to be reviewable and keep a precise `packaging/README.md` operator checklist for each AC.
- **Contingency:** Flag every packaging story explicitly as operator-verified in `progress.md`; the feature review scopes them to code/script review, not runtime pass/fail.

### R-6: Service working-directory breaks per-install paths
- **Description:** The signing key, Google token, file storage, and logs all resolve under `Directory.GetCurrentDirectory()`. A Windows service starts with CWD `C:\Windows\System32`, so those would be created/looked-for in the wrong place — silently losing secrets/storage.
- **Likelihood:** High · **Impact:** High
- **Story:** US-2, US-6
- **Mitigation:** Switch path resolution to `AppContext.BaseDirectory` (the install dir) in `LocalAuthConfig`, `FileGoogleTokenStore`, `Extensions.cs` file-storage base, Serilog log path, **and the offline recovery CLI `Maintenance/AdminPasswordResetCommand.cs`** (its `SetBasePath` + signing-key resolution — otherwise the sole admin-recovery path silently reads the wrong config when not run from the install dir); additionally set the service `WorkingDirectory` in the installer.
- **Contingency:** If a path is still wrong post-install, the startup posture log (US-2/US-3) surfaces the resolved locations for quick diagnosis.

## Breaking Changes

**Local mode only — Cloud is unchanged.**

### Change 1: Local web build uses a relative API base + proxy
- **What breaks:** The Local hosted web now expects `NEXT_PUBLIC_API_URL=/api` and a Next `/api` → localhost rewrite; a Local deployment built with an absolute API URL and no proxy would not reach the API the same way.
- **Who is affected:** Only new Local installs (built by the Phase 5 installer). Cloud builds keep their absolute URL and are untouched.
- **Handling:** The server installer sets the correct Local build env; `packaging/README.md` documents it.

### Change 2: `Https:CertPassword` removed from committed `appsettings.json`
- **What breaks:** Nothing at runtime (the value was empty); any external process that read that key must now read it from `.local`/env.
- **Who is affected:** Local installs (which now generate the password per-install). Cloud never used it.
- **Handling:** Provisioner writes/reads the password under `.local/`.

## Migrations

**None required.** No schema changes in Phase 5 — the auto-migrate-on-startup behavior (reused from prior phases) applies existing migrations; a fresh install comes up empty and is populated via first-run setup (FR-F3). Config "migration" for Local installs (mode/URLs/paths) is performed by the server installer, not an EF migration.

## Challenge Summary (2026-07-09)

Codebase-verified challenge of the Phase 5 plan. 4 issues raised and resolved; plan updated in place.

| # | Severity | Issue | Resolution |
|---|----------|-------|------------|
| 1 | **Critical** | **TLS-termination mismatch.** US-3 bound the generated cert to Kestrel, but the same-origin design had the browser/shell hitting the **Next Node server** (`https://<server>:<webPort>`), which `next start` serves as plain HTTP — so LAN traffic would be cleartext (violating FR-E2, the reason for the cert + CA-trust stories) or fail. | **Kestrel is the single browser-facing HTTPS endpoint** (bound to the US-3 cert): serves `/api/*` in-process, reverse-proxies all other routes to the localhost-only Next server. TLS terminated once in the audited .NET app; no Node TLS, no `next.config` rewrites. Rewrote Decision #2, US-4, US-5, US-6; added `Yarp.ReverseProxy`. |
| 2 | **Major** | **`/api` namespace collision.** Behind one front door, Next's own BFF handlers (`app/api/auth/*`) share `/api` with backend controllers — `/api/auth/change-password` exists on **both** (`AuthController.cs:152` + a Next route), and ASP.NET would match its controller first, shadowing the Next cookie logic. | **Relocated the 5 Next BFF handlers to `/bff/auth/*`.** Front door routes unambiguously: `/api/*` → controllers; `/bff/*` + pages + assets → Next. BFF handlers reach the backend via server-only `API_INTERNAL_URL`. |
| 3 | **Major** | **`new URL()` breaks all GETs.** `client.ts:86` `apiGet` builds `new URL(`${API_BASE_URL}${endpoint}`)`, which throws `TypeError: Invalid URL` on the relative `/api` base — breaking the app's primary read path. The plan's "fix string concatenation" hint pointed the wrong way (concatenation + `fetch()` work relative; `new URL()` is what breaks). | US-4 step 1 now names `client.ts:86` explicitly and fixes it with `new URL(…, window.location.origin)`. |
| 4 | **Major** | **Offline admin-recovery breaks on a packaged install.** R-6/US-2 switched the 4 web-host paths to `AppContext.BaseDirectory` but omitted `Maintenance/AdminPasswordResetCommand.cs`, which loads config via `SetBasePath(Directory.GetCurrentDirectory())` — the sole FR-B6 recovery path would silently read stale/empty `.local` if not run from the install dir. | Added the recovery CLI as R-6's 5th site (→ `AppContext.BaseDirectory`); documented the packaged invocation `ClinicManagement.API.exe reset-admin-password` (the `dotnet run --` form is dev-only) + an operator checklist item. |

**Folded-in minors (no separate decision):** the startup DB-down/port-in-use handling (US-2) is **Local-gated** to keep Cloud byte-for-byte (per the "gate on mode, not capability" learning); **R-5 downgraded** (multipart now hits Kestrel directly same-origin, no Node proxy; `serverActions.bodySizeLimit` never bounded a proxy anyway); the `Https:CertPassword` committed value is an **empty placeholder** (not a real secret) — the real committed Google/HuggingFace secrets are scrubbed from *shipped* config in US-6 (repo/git-history remediation remains pre-existing debt, out of Phase 5 scope).

**Verified-and-accepted (no change):** `AdminOnly` policy exists (`AuthorizationPolicies.cs`); admin-gate pattern (`GetUserId()` → `GetByAuth0SubAsync` → `IsAdmin()`) matches `ResetUserPasswordCommand`; `Result<T>.Failure(string)` + `BadRequest(result.Error)` shape; no existing `Process.Start` (backup is the first); the test project already references Infrastructure + API (cert/backup tests compile); `ControllerAuthorizationCoverageTests` needs no update if `BackupController` is `[Authorize]`; `.local/` already git-ignored.
