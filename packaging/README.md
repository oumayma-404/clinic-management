# Clinic Management — Packaging & Operations (Local / Offline-LAN)

Operator reference for the Local (offline-LAN) Windows deployment (Phase 5). Cloud deployments are
unaffected by anything here.

> **Status:** This document currently covers the parts delivered by slices **S1–S4** (backup/restore,
> packaged admin recovery, Local build environment). The **server + client installers, bundled PostgreSQL,
> the WebView2 desktop shell, and the full per-AC operator verification checklist (slices S5–S7)** are
> delivered in a follow-up packaging session and will be appended here.

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
