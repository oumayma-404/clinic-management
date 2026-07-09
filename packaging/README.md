# Clinic Management — Packaging & Operations (Local / Offline-LAN)

Operator reference for the Local (offline-LAN) Windows deployment (Phase 5). Cloud deployments are
unaffected by anything here.

> **Status:** Complete for Phase 5. Covers the backup/restore, packaged admin recovery, and Local build
> environment (slices S1–S4) **and** the build & publish orchestration, server + client installers, bundled
> PostgreSQL, the WebView2 desktop shell, and the per-AC operator verification checklist (slices S5–S7).
>
> **R-1 — packaging is operator-verified.** The S5–S7 artifacts (`publish-server.ps1`, the two `.iss`
> installers, the WebView2 shell) are committed, reviewable build recipes. They cannot be executed in the
> development/CI environment (no Windows installer execution; Smart App Control blocks freshly-built DLLs),
> so their acceptance is proven by an operator running the [verification checklist](#operator-verification-checklist-s5s7)
> on a real build/target machine — not by a CI pass. The shell **compiles** clean (`dotnet build`); the
> installers and end-to-end stand-up are checked by the operator.

---

## Local build environment (S4 — same-origin front door)

In Local mode the **Kestrel API is the single browser-facing HTTPS endpoint**. It serves `/api/*` in-process
and reverse-proxies every other route (pages, `/_next/*`, static assets, `/bff/*`) to the co-located Next
server on `http://localhost:<WebPort>`. One hosted web build therefore serves clients at any server IP.

### Web build (Next.js — standalone)

Build with these environment variables so the browser bundle uses same-origin, relative URLs:

| Variable | Value | Why |
|----------|-------|-----|
| `NEXT_PUBLIC_API_URL` | `/api` | Relative → same-origin. The browser hits the Kestrel front door; no baked-in server IP. |
| `AUTH_MODE` | `local` | Selects the Local session provider + Local `/bff/auth/*` routes. |
| `API_INTERNAL_URL` | `http://localhost:5000/api` | **Server-only.** The `local-login` / `change-password` BFF route handlers call the API with this absolute URL (a relative `/api` has no origin server-side). |

```bash
cd web
NEXT_PUBLIC_API_URL=/api AUTH_MODE=local API_INTERNAL_URL=http://localhost:5000/api npm run build
# Serve the standalone output; Next listens HTTP on localhost:<WebPort> only (never LAN-facing).
```

### API configuration (`appsettings.json` / environment)

| Key | Meaning |
|-----|---------|
| `Auth:Mode` = `Local` | Enables all Local-mode behavior (fail-closed auth, front door, cert self-gen, service hosting). |
| `Hosting:HttpsPort` (default `5001`) | The **only** LAN-facing port — HTTPS front door. Open just this one on the firewall. |
| `Hosting:HttpPort` (default `5000`) | HTTP; redirects to HTTPS. |
| `Hosting:WebPort` (default `3000`) | localhost port the Next server listens on; the front door proxies non-`/api` routes here. |
| `Https:CertPath` | Leave **empty** to self-generate a CA + server cert into `.local/` (FR-E2). If set, the file must exist or the server refuses to start (no silent HTTP downgrade). |

Secrets (signing key, cert password, Google refresh token) live in the gitignored per-install `.local/`
folder, generated on the target machine — never committed (FR-F4). The exported CA is `.local/ca.crt`
(imported into client trust by the client installer, S7).

---

## Offline admin-password recovery (S2 — FR-B6)

The sole offline recovery path when an administrator is locked out. Runs on the **server PC** as a one-shot
console command (Local mode only) and works **from any working directory** (paths resolve against the
install directory, R-6):

```powershell
# Packaged install (self-contained exe):
"C:\Program Files\Clinic Management\ClinicManagement.API.exe" reset-admin-password [admin-email]

# Development (from the api/ solution folder):
dotnet run --project ClinicManagement.API -- reset-admin-password [admin-email]
```

It resets the (sole, or named) local admin's password to a fresh temporary value and forces a change at next
login. No internet, email, or cloud service is involved.

---

## Backup & restore (S1 — US-8 / FR-G)

### Backup (in-app)

An administrator runs **Settings → Sauvegarde → "Sauvegarder maintenant"** (Local mode only). This writes,
into a timestamped `clinic-backup-<yyyyMMdd-HHmmss>` subfolder of the chosen destination:

- `database.dump` — a PostgreSQL custom-format dump (via the bundled `pg_dump.exe`), and
- `files/` — a recursive copy of the file-storage folder.

The app reports the exact destination path and size on success, or a clear reason on failure (unwritable
destination, insufficient disk space, `pg_dump` missing) — never silently (AC-8.2/8.3).

### Restore (manual — FR-G3 / AC-8.4)

There is **no in-app restore**. To restore a backup onto a server PC:

1. **Stop the Clinic Management API service** (so nothing writes while restoring).
2. **Restore the database** with `pg_restore` (bundled with PostgreSQL). Custom-format dumps support
   `--clean --if-exists` to drop and recreate objects:
   ```powershell
   $env:PGPASSWORD = "<clinic_user password>"
   & "<postgres>\bin\pg_restore.exe" `
       --host localhost --port 5432 --username clinic_user `
       --dbname clinic_management --clean --if-exists `
       "<backup-folder>\clinic-backup-YYYYMMDD-HHMMSS\database.dump"
   ```
   (If the database was dropped entirely, create an empty `clinic_management` owned by `clinic_user`
   first, then run `pg_restore` without `--clean`.)
3. **Restore the files**: copy the contents of `<backup-folder>\...\files\` back into the file-storage
   base folder (`FileStorage:BasePath`, under the install directory), overwriting existing files.
4. **Start the Clinic Management API service.**

Concurrent edits from two client PCs are last-write-wins (documented v1 behavior); restore over a quiet
system for a consistent result.

---

## Building the installers (S6/S7 — operator build machine)

Everything the two installers bundle is staged by **`packaging/publish-server.ps1`** into
`packaging/build-output/` (gitignored). The script does **not** fabricate the third-party runtimes —
point it at local copies.

### Prerequisites (build machine)

- **.NET 8 SDK** (`dotnet` on PATH) — publishes the API and the shell self-contained `win-x64`.
- **Node.js** (`npm` on PATH) — builds the Next.js standalone bundle. Also keep a **Node runtime folder**
  (containing `node.exe`) to ship as the web server's runtime → `-NodeDir`.
- **EnterpriseDB PostgreSQL 16** Windows binaries (extracted folder with `bin\`, `lib\`, `share\`) → `-PostgresDir`.
- **Inno Setup 6** (`ISCC.exe`) — compiles the `.iss` scripts.
- **NSSM** (`nssm.exe`) — hosts the Node web server as a service (R-8). Drop it into
  `packaging/server/tools/nssm.exe` before compiling the server installer.

### Run

```powershell
cd packaging
.\publish-server.ps1 -PostgresDir C:\pgsql-16 -NodeDir C:\node-v20
# add -SkipInstallers to stage payloads without compiling the .iss (e.g. no Inno Setup on this box)
```

This produces:

```
packaging/build-output/
  server/  api/  web/  node/  postgres/          → bundled by clinic-server.iss
  client/  shell/  ca/                            → bundled by clinic-client.iss
  ClinicManagementServerSetup-1.0.0.exe          (if Inno Setup present)
  ClinicManagementClientSetup-1.0.0.exe
```

`publish-server.ps1` also **scrubs the real-looking secrets** (Google/HuggingFace/Auth0) out of the
*published* `appsettings.json` and sets `Auth:Mode=Local` — no real secret is shipped (FR-F4). The
committed source `appsettings.json` is untouched.

## Server installer (`server/clinic-server.iss`)

Installs to `C:\Program Files\Clinic Management\` and, in one pass:

1. Lays down `api/`, `web/`, `node/`, `postgres/` and creates writable `.local/`, `api/Files/`, `api/logs/`,
   and `pgdata/` (all under the install dir — the API resolves them via `AppContext.BaseDirectory`, so the
   service's `System32` CWD is irrelevant, R-6).
2. **PostgreSQL** — `initdb` a fresh loopback-only cluster (skipped if `pgdata` already initialised),
   registers it as the auto-start **`ClinicManagementDb`** service, and creates `clinic_user` (random
   per-install password) + the `clinic_management` database.
3. Writes **`appsettings.Production.json`** on the target with the machine's connection string (that
   generated password), the `Hosting` ports, and `Backup:PgDumpPath` pointing at the bundled `pg_dump.exe`.
   The signing key + HTTPS cert are **self-generated on first boot** into `.local/` — nothing secret is bundled.
4. Registers the **`ClinicManagementApi`** (self-contained exe + `UseWindowsService`) and
   **`ClinicManagementWeb`** (Node via NSSM) services. **Dependency order: DB → Web → API.** The Node web
   server listens **HTTP on `localhost:3000` only**; **Kestrel is the sole LAN-facing endpoint**
   (**HTTPS `5001`**) and reverse-proxies non-`/api` routes to Node.
5. Opens **only** TCP `5001` on the LAN firewall (`3000` and the API's plain-HTTP `5000` stay loopback-only).
6. Starts the services and **exports the generated CA** (`.local/ca.crt`) to
   `%ProgramData%\ClinicManagement\ca.crt` for the client installer.

First-run setup is served **localhost-only** (AC-1.2a, enforced by the API) — open `https://localhost:5001`
on the server PC to create the clinic + first admin.

### Ports

| Port | Bind | Purpose | Firewall |
|------|------|---------|----------|
| `5001` | all interfaces (HTTPS) | Kestrel front door — the only browser-facing endpoint | **open (LAN)** |
| `5000` | loopback | API plain HTTP (redirects to HTTPS) / internal BFF target | closed |
| `3000` | loopback | Next web server (proxied by Kestrel) | closed |
| `5432` | loopback | PostgreSQL | closed |

## Client installer (`client/clinic-client.iss`)

A lightweight per-PC installer for staff machines:

1. Places the published WebView2 **desktop shell** with a Start-menu shortcut (+ optional desktop icon, AC-2.1).
2. **Imports the server's CA** (`ca.crt`) into the Windows **Root** store via `certutil -addstore Root`, so
   the shell reaches `https://<server>:5001` with no certificate warning (FR-E2).

Obtain the CA from the server PC at **`%ProgramData%\ClinicManagement\ca.crt`** (exported by the server
installer) and drop it into `packaging/build-output/client/ca/ca.crt` **before** compiling the client
installer — or import it manually on the client into "Autorités de certification racines de confiance".

**One client installer fits every clinic:** the shell asks for the server address on first launch
(AC-2.2) and stores it in `%AppData%\ClinicManagement\server.json`; change it any time via **Serveur →
Changer de serveur…** (AC-2.3). On the server PC, point it at `localhost` (AC-2.5). If the server is
unreachable the shell shows a friendly **"Impossible de joindre le serveur de la clinique"** screen with
**Réessayer** — never a blank page or raw browser error (AC-2.4). Re-running the client installer updates
the shell in place (auto-update is out of scope).

---

## Operator verification checklist (S5–S7)

Run on a real build machine + target PC(s). Each item maps to a spec acceptance criterion.

### Desktop shell (S5)

- [ ] **AC-2.2** — First launch prompts for the server address; after entering it and connecting, relaunch → it reconnects without asking.
- [ ] **AC-2.3** — **Serveur → Changer de serveur…** edits the address without reinstalling.
- [ ] **AC-2.4** — Point the shell at an offline/wrong address → the friendly "Impossible de joindre le serveur de la clinique" screen with **Réessayer** appears (never a blank page or raw browser error). Fix the address / start the server, click **Réessayer** → it connects.
- [ ] **AC-2.5** — On the server PC, `localhost` connects successfully.
- [ ] Build: `dotnet build desktop/ClinicManagement.DesktopShell.sln` → 0 errors / 0 warnings.

### Server installer (S6)

- [ ] **AC-1.1** — One run installs API + web + PostgreSQL + storage as **auto-start services**; reboot the PC → all three come up (check `services.msc`: `ClinicManagementDb`, `ClinicManagementWeb`, `ClinicManagementApi` = Running/Automatic).
- [ ] **AC-1.2 / AC-1.2a** — `https://localhost:5001` shows first-run setup and creates clinic + first admin **with no internet**; the setup route is refused from another LAN PC (localhost-only).
- [ ] **AC-1.3** — After setup, `https://<server-ip>:5001` is reachable from another LAN PC (via the shell or a browser that trusts the CA).
- [ ] **AC-1.4** — No manual PostgreSQL / port / certificate configuration was required.
- [ ] **FR-E2** — `.local/ca.crt` + `.local/server.pfx` were generated on first boot; the cert's SANs include the hostname + LAN IPv4; `%ProgramData%\ClinicManagement\ca.crt` was exported.
- [ ] **FR-F3** — Migrations applied automatically on first start (fresh DB comes up empty, populated via setup).
- [ ] **FR-F4** — The installed `appsettings.json` / `appsettings.Production.json` contain **no real secrets** (Google/HuggingFace/Auth0 blank).
- [ ] **FR-F5** — Stop the DB service and start the API → a clear operator message (Event Log / `api/logs/`), not a bare stack trace; a port already in use → a distinct message naming the port.
- [ ] **FR-B6** — `"C:\Program Files\Clinic Management\api\ClinicManagement.API.exe" reset-admin-password` recovers the admin when run from an arbitrary directory (e.g. `C:\`).
- [ ] Firewall shows **only** `5001` inbound; `3000` / `5000` / `5432` are not reachable from the LAN.

### Client installer (S7)

- [ ] **AC-2.1** — Installs a working shell with a Start-menu icon.
- [ ] **FR-E2** — After install, the shell reaches the server over HTTPS **with no certificate warning** (CA imported into Root).
- [ ] Uninstall removes the shell; the imported CA is removed (or its removal is documented).

### Cloud regression (all slices)

- [ ] A **Cloud**-mode run (`Auth:Mode=Cloud`, absolute `NEXT_PUBLIC_API_URL`) is unchanged: no reverse proxy, no cert self-gen, no Windows-service/DB-down handling firing; Phase 4 auth-coverage tests still green.
