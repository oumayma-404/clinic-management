# Packaging — Local / offline-LAN Windows delivery (Phase 5)

Build recipes + Inno Setup installers that turn the app into a self-contained offline-LAN Windows product. **Local mode only — Cloud is unaffected.** **R-1: operator-verified, not CI-runnable** — the scripts and `.iss` are committed, reviewable build recipes; they run on an operator build machine (Windows + .NET 8 SDK + Node + Inno Setup 6) and cannot execute in CI.

> **Operator procedures live in `README.md`** — build prereqs, run steps, backup/restore, offline admin recovery, fresh-vs-reinstall, ports, and the per-AC verification checklist. This file is the AI map; jump to `README.md` for the how-to.

## Files
| Path | Role |
|------|------|
| `fetch-build-tools.ps1` | One-time build-machine helper — downloads PostgreSQL 16 binaries → `build-tools/pgsql/` and NSSM → `server/tools/nssm.exe`. Does **not** fetch Inno Setup (needs elevated `winget install JRSoftware.InnoSetup`). |
| `publish-server.ps1` | Build & publish orchestration. Publishes the API (self-contained `win-x64`, `UseWindowsService`) + the `desktop/` shell; builds the Next.js standalone bundle with same-origin env (`NEXT_PUBLIC_API_URL=/api`, `AUTH_MODE=local`, `API_INTERNAL_URL`); stages Node + PostgreSQL `bin/lib/share` runtimes; **scrubs real-looking secrets** from the *published* `appsettings.json` (source untouched); optionally compiles both `.iss`. Stages into `build-output/` (gitignored). Third-party runtimes are not fabricated — pass `-PostgresDir` / `-NodeDir`. |
| `server/clinic-server.iss` | Server installer. One pass: bundled PostgreSQL 16 (fresh cluster or reuse persisted creds), API + Node web as auto-start services (dep order **DB→Web→API**), `appsettings.Production.json`, install-time cert provisioning (`provision-cert`), firewall (opens only `5001`, removed on uninstall), CA export to `%ProgramData%\ClinicManagement\ca.crt`. Logic is in `[Code]` Pascal: `EstablishDbCredentials`, `WriteProductionConfig`, `SetupPostgres`, `SetupAppServices`, `OpenFirewall`, `StartAndExportCa`. |
| `server/tools/nssm.exe` | NSSM — hosts the Node web server as a Windows service. Operator-supplied (via `fetch-build-tools.ps1`); pulled by the server `.iss` from here (not `build-output/`). Gitignored. |
| `client/clinic-client.iss` | Client installer. Installs the WebView2 shell, ensures the WebView2 runtime (bundled offline installer, run only if missing — `EnsureWebView2`), imports the server CA into the machine **Root** store (`ImportCa`). One installer fits every clinic — the shell prompts for the server address. |
| `build-tools/` | Fetched build-time tools (PostgreSQL, NSSM). Gitignored. |
| `build-output/` | Staged payloads + compiled `ClinicManagement{Server,Client}Setup-1.0.0.exe`. Gitignored. |

## Flow
1. `fetch-build-tools.ps1` (once) → runtimes into `build-tools/` + `server/tools/nssm.exe`.
2. `publish-server.ps1 -PostgresDir … -NodeDir …` → stages `build-output/server/{api,web,node,postgres}` and `build-output/client/{shell,ca,webview2}`, then compiles the two `.iss` (unless `-SkipInstallers`).
3. Operator drops the server-exported `ca.crt` into `build-output/client/ca/` and the offline WebView2 installer into `build-output/client/webview2/` before compiling the client installer (`publish-server.ps1` only creates the empty dir + prints a reminder — these two payloads are not auto-staged).

## Key facts (verified)
- **Kestrel is the sole LAN-facing endpoint** — HTTPS `5001` open on the firewall; Node web `3000`, API HTTP `5000`, PostgreSQL `5432` are all loopback-only.
- **No real secret ships** — `publish-server.ps1` blanks Google/HuggingFace/Auth0/MinIO + the connection-string password in the published `appsettings.json`; runtime secrets self-generate into the per-install, gitignored `api/.local/` on the target. PostgreSQL passwords are CSPRNG-generated, scram-sha-256.
- **CA subject is `CN=Clinic Management Local CA`** (`api/…/Security/CertificateProvisioner.cs`) — the client `.iss` `certutil -delstore Root` uninstall match depends on this exact name.
- **`provision-cert` / `reset-admin-password`** are API console commands (`api/ClinicManagement.API/Maintenance/`), invoked by the server installer / operator respectively.

> When packaging code changes, keep this map and `README.md` in sync. (Known README drift as of this writing: it doesn't mention `fetch-build-tools.ps1` and its output tree omits `build-output/client/webview2/`.)
