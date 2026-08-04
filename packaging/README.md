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
| `AUTH_COOKIE_SECURE` | `true` | **Runtime, server-only.** Forces the `Secure` flag on the auth session cookie. Required because the Node server sits behind the HTTPS front door on a plain-HTTP loopback hop — without this the BFF login handler would derive a non-Secure request scheme and drop `Secure` even though the browser transport is HTTPS. Set by the server installer's web-service registration. |

```bash
cd web
NEXT_PUBLIC_API_URL=/api AUTH_MODE=local API_INTERNAL_URL=http://localhost:5000/api npm run build
# Serve the standalone output with AUTH_COOKIE_SECURE=true so the session cookie is Secure behind the
# HTTPS front door; Next listens HTTP on localhost:<WebPort> only (never LAN-facing).
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

### Backup — automatic, and verified (L4)

**Automatic, every day.** A recurring job backs the clinic up without anyone pressing anything. The schedule
lives on the clinic, not in this file — **Paramètres → Sauvegarde**: on/off, the hour (clinic-local, default
**02:00**), how many copies to keep (default **7**) and after how long without a success the admins are told
(default **48 h**). The job runs **hourly** and asks each clinic whether it is due, which is what also covers a
clinic PC that is switched off overnight: a fixed 02:00 cron on such a machine never runs at all.

**Manual, any time.** An administrator can still run **Paramètres → Sauvegarde → « Sauvegarder maintenant »**.

Either way, into a timestamped `clinic-backup-<yyyyMMdd-HHmmss>` subfolder of the destination:

- `database.dump` — a PostgreSQL custom-format dump (via the bundled `pg_dump.exe`), and
- `files/` — a recursive copy of the file-storage folder.

Three things are new and worth knowing:

- **The dump is verified readable before the backup is called a success.** `pg_dump` exiting 0 is not proof, and
  the old implementation only measured the folder's size — a truncated or zero-byte dump has a size too. The
  service now runs `pg_restore --list` on the output and requires a non-empty table of contents; the **object
  count** is recorded, because « 3 objets » where the schema has thirty-eight tables is a detectable disaster.
  A failed verification is a **failed backup**: the partial folder is deleted.
- **There is a real default destination.** `<install>\api\Backups`, created by the installer and written into
  `appsettings.Install.json`. Leaving the destination field blank in the app now works, which is what the screen
  had always said. ⚠️ The app **warns prominently** when the destination is on the same volume as the live data —
  which the default necessarily is. Point it at an external disk or a network share.
- **Every attempt is recorded**, success or failure, and **Paramètres → Sauvegarde** leads with
  « Dernière sauvegarde réussie ». Past the staleness threshold the admins get an in-app notification. A week of
  nightly failures therefore reads as « it is trying and failing », not as « nobody has backed up » — two
  entirely different conversations. `GET /api/backup/history` serves the same rows (admin-only, paged).

**Retention.** After a successful run, folders beyond the clinic's retention count are pruned **oldest first**.
It matches only `clinic-backup-*` names (your own folders in the same destination are not its business) and
**never deletes the last surviving backup**, whatever the count says.

**Before a migration.** An upgrade that carries a schema change now takes a backup **first** and **aborts the
migration if that backup fails**, with a French message on the console, in the log and in the Event Log. The
last migration is lossy by design with an empty `Down()`, so « rollback means restoring this backup » — a
sentence that used to be true only if such a backup happened to exist.

### Restore (`restore-backup`)

There **is** a restore now, as a console verb — the same family as `reset-admin-password`, `reconcile-money`
and `verify-schema`. It is not an in-app button on purpose: a restore drops and recreates every table the
application is holding open, so an endpoint inside the app being replaced is the wrong shape.

```powershell
sc stop ClinicManagementApi
& "<install>\api\ClinicManagement.API.exe" restore-backup "<backup-folder>\clinic-backup-YYYYMMDD-HHMMSS"
sc start ClinicManagementApi
```

**Paramètres → Sauvegarde** prints that command with the resolved folder already filled in.

What it does, in this order — every refusal happens **before** anything is destroyed:

1. Validates the folder: `database.dump` present, non-empty, and readable by `pg_restore --list`.
2. **Refuses if the application is still running** (it checks the app's own ports), naming the service to stop.
3. **Takes a safety dump of the current state** into `clinic-pre-restore-<timestamp>` — deliberately *not* a
   `clinic-backup-*` name, so retention can never prune it. If that dump fails, **the restore is abandoned**.
4. `pg_restore --clean --if-exists --no-owner` into the configured database.
5. Copies `files/` back. **Refuses** if the live file folder is not empty, unless you pass `--force`.
6. **Invalidates every live session** (bumps each user's token version), so nobody keeps operating on a token
   minted against the newer state. Everyone logs in again.
7. Prints a French summary and the next steps. Exit code **0** restored, **1** refused or failed.

Credentials come from the install's own configuration — no password is typed or prompted for.

Restore over a quiet system for a consistent result.

*(Concurrent edits from two client PCs are **no longer** last-write-wins — see « Money reconciliation » below.
Each record now carries a concurrency token, and the second person to save gets a French « rechargez » message
instead of silently discarding the first person's work.)*

---

## Money reconciliation (`reconcile-money`)

A read-only, per-clinic report that cross-checks every money figure the app stores against the rows it is
derived from. **It never mutates anything.** Run it before and after any upgrade that carries a data
migration, and diff the two.

```powershell
"C:\Program Files\Clinic Management\api\ClinicManagement.API.exe" reconcile-money
```

or from a source checkout:

```bash
dotnet run --project ClinicManagement.API -- reconcile-money
```

Output goes to stdout **and** to a timestamped file next to the backup folder, so the before/after pair can be
diffed. Exit codes: **0** everything agrees · **1** the report could not run (no database, bad connection
string) · **2** drift was found. `2` is not an emergency — it is a list of rows for a human to look at.

What it checks, per clinic:

| Check | Why it exists |
|---|---|
| `invoice-ledgers-agree` | Σ non-voided `Payment.Amount` vs the stored `Invoice.AmountCollected` |
| `installment-ledger-agrees` | Σ `InstallmentPayment.Amount` vs the stored `Installment.AmountPaid` |
| `plan-schedule-balances` | Per plan: Σ `Installment.Amount` vs `TotalPlanned` — an invariant plan amendments can break |
| `monthly-attribution-unchanged` | 24 months of « encaissé » computed the **old way and the new way**. This is the line that proves the installment-ledger migration moved no closed month. |
| `bridge-carry-over-complete` | Bridged invoices still carrying un-transferred plan money |
| `credit-notes-within-collected` | Invoices credited for more than was ever collected |
| `one-bridge-invoice-per-plan` | Duplicate non-cancelled bridge invoices for one devis |
| `contact-sentinels` | The four retired placeholder literals, plus a separate **near-miss** count (e.g. `no-email@example.com`) for manual review — those are deliberately *not* auto-cleaned |
| `no-orphaned-rows` | Invoices and treatment plans whose `PatientId` matches no patient (neither has an FK), plus orphaned tooth states and notifications |

> **Local mode only.** The verb is intercepted before the web host boots, alongside `reset-admin-password` and
> `provision-cert`, so it runs on a stopped app.

---

## Upgrading across the `data-and-money-integrity` release

That release carries **five migrations**, and the last one is order-sensitive.

1. **Back up first** (Settings → Sauvegarde, or `pg_dump` by hand). The last migration is **lossy by design**
   and its `Down()` is deliberately empty — rollback means restoring this backup.
2. **Capture a baseline**: run `reconcile-money` and keep the output file.
3. **Upgrade and start the service.** Migrations apply automatically. Note that in Local mode they run *after*
   Kestrel is already serving, so there is a brief window where the app is up and the data is mid-migration;
   this is why the null-safe code ships ahead of the blanking `UPDATE` and not with it.
4. **Run `reconcile-money` again and diff.** Expect:
   - all four **sentinel counts at zero** (the near-miss count may be non-zero — review those by hand);
   - `monthly-attribution-unchanged` reporting **no closed month moved**;
   - `installment-ledger-agrees` clean, i.e. the ledger backfill matched every stored `AmountPaid`.
5. Anything else in the diff is a pre-existing inconsistency the report has simply made visible for the first
   time. It is a work list, not a failed upgrade.

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
   registers it as the auto-start **`ClinicManagementDb`** service, and creates `clinic_user` +
   the `clinic_management` database. On a **fresh install** the `clinic_user` + `postgres` passwords are
   randomly generated and **persisted** to `.local/db-credentials`; on a **reinstall over an existing
   cluster** those persisted passwords are **reused** so authentication keeps working and the data is kept
   (see *Fresh install vs in-place reinstall* below).
3. Writes **`appsettings.Production.json`** on the target with the machine's connection string (the
   persisted password), the `Hosting` ports, and `Backup:PgDumpPath` pointing at the bundled `pg_dump.exe`.
   The signing key is self-generated on first boot into `.local/`; the **HTTPS cert is provisioned at
   install time** — the installer runs `api\ClinicManagement.API.exe provision-cert` (idempotent; no DB
   connection) so the API service reuses it on first boot instead of generating it under the SCM start
   timeout. Nothing secret is bundled.
4. Registers the **`ClinicManagementApi`** (self-contained exe + `UseWindowsService`) and
   **`ClinicManagementWeb`** (Node via NSSM) services. **Dependency order: DB → Web → API.** The Node web
   server listens **HTTP on `localhost:3000` only**; **Kestrel is the sole LAN-facing endpoint**
   (**HTTPS `5001`**) and reverse-proxies non-`/api` routes to Node.
5. **Secures the data directories** — before the services start, the installer runs
   `api\ClinicManagement.API.exe harden-permissions` over `api\.local`, `api\Files`, `api\logs` and `pgdata`:
   ACL inheritance is broken and access is reserved to the service account, `LocalSystem` and
   `Administrators`, with any grant to `BUILTIN\Users` or `Everyone` removed recursively. See
   *Permissions & data at rest* below.
6. Opens **only** TCP `5001` on the LAN firewall (`3000` and the API's plain-HTTP `5000` stay loopback-only).
7. Starts the services and **exports the generated CA** (`api\.local\ca.crt`) to
   `%ProgramData%\ClinicManagement\ca.crt` for the client installer.

First-run setup is served **localhost-only** (AC-1.2a, enforced by the API) — open `https://localhost:5001`
on the server PC to create the clinic + first admin.

### Fresh install vs in-place reinstall

The server installer is safe to re-run over an existing install **without wiping the database**:

- **Fresh install** (no `pgdata` cluster yet) — new random `clinic_user` + `postgres` passwords are
  generated and written to **`{app}\api\.local\db-credentials`** (two lines: `clinic_user`, then
  `postgres`). The HTTPS cert is provisioned at install time.
- **In-place reinstall** (an existing `pgdata` cluster is present) — the installer **reuses** the passwords
  from `db-credentials` instead of regenerating them, so the bundled role/DB step and the written
  `appsettings.Production.json` match the existing cluster; the data is kept. The existing HTTPS cert set is
  reused (idempotent `provision-cert`), so the CA that clients already trust stays stable.
- **Existing cluster but `db-credentials` is missing or unreadable** — the installer **stops with a clear
  message** rather than silently generating new passwords that would not match the cluster. Recover by
  restoring `{app}\api\.local\db-credentials` from a backup, **or** — accepting data loss — delete the
  `{app}\pgdata` folder to let the next install `initdb` a fresh cluster.
  - **Upgrading from an older installer** (released before this credentials-persistence change) always
    lands here on its first reinstall: that machine has a `pgdata` cluster but no `db-credentials` file, so
    the installer stops by design. Before wiping data, note that the **`clinic_user`** password is still
    recoverable in cleartext from the connection string in `{app}\api\appsettings.Production.json` — write
    both passwords into a fresh `db-credentials` file (line 1 = `clinic_user`, line 2 = `postgres`) and
    re-run. The **`postgres` superuser** password, however, is **not** recoverable if it was lost; in that
    case wiping `{app}\pgdata` (accepting data loss) is the only path, which is exactly why the installer
    fails loud instead of guessing.

> Back up `{app}\api\.local\db-credentials` (or the whole `{app}\api\.local` folder — it also holds the
> signing key, HTTPS cert, CA, and the Data Protection key ring) alongside your database backups. It is
> gitignored and generated on the target — losing it while keeping `pgdata` blocks a clean reinstall.
>
> **The credentials file is now encrypted and machine-bound.** Back up the **whole `.local` folder**, not
> just `db-credentials` — the file is decryptable only with that machine's Data Protection key ring, which
> also lives in `.local`. A copy of the file alone, on a different machine, is unreadable (that is the point).

## Permissions & data at rest

Closes the four P0 findings in § 2 of `CODEBASE_AUDIT_2026-07.md`. Before this release every local Windows
account on the clinic PC could read the entire patient database, every uploaded radiograph, the logs, and the
whole `.local/` trust store — the JWT signing key (enough to forge an admin token for any clinic), the HTTPS
server key, the Data Protection key ring, and the e-invoice signing certificate.

### What the installer now does

| Path | Posture after install |
|---|---|
| `{app}\pgdata` | Inheritance broken; `LocalSystem` / `NetworkService` / `Administrators` only. The Full Control that `BUILTIN\Users` needs so de-privileged `initdb` can run is granted **only for that step** and revoked immediately after — on the failure path too, so an aborted install never leaves the cluster readable. |
| `{app}\api\.local` | Same, and applied **before** the generated passwords are written into it, so plaintext is never readable even momentarily. |
| `{app}\api\Files` | Same — patient radiographs, scans and referrals. |
| `{app}\api\logs` | Same — Serilog output can carry patient names and appointment detail. |
| `{app}\api\.local\db-credentials` | Encrypted at rest, machine-scoped, via the same Data Protection key ring the API uses. Never readable from a copy taken off the machine. |
| `{app}\initdb.log` | Deleted once the install succeeds (it sits in the install root, which stays readable). |

Any permission or encryption step that cannot be applied **aborts the install with a French message**. A
completed install never silently leaves patient data readable.

Re-running the installer re-asserts all of the above, so an install created by an **earlier** installer
version is remediated on upgrade — which is how most existing clinics will receive this fix. A plaintext
`db-credentials` left by an older installer is migrated to the encrypted form on that first reinstall.

### Known residual limit

`{app}\api\appsettings.Production.json` still contains the `clinic_user` password in readable form inside the
connection string. Kestrel and the DbContext need it at boot, before any decryption service exists, so it is
protected by the directory ACL only. An attacker who reaches **Administrator** on the clinic PC can read it.
The `postgres` superuser password is **not** exposed there — only in the encrypted `db-credentials`.

### Mise hors service (decommissioning a clinic PC)

Uninstalling **deliberately keeps** the data: it removes the program files and services but leaves `pgdata`,
`api\.local`, `api\Files`, `api\logs` and `appsettings.Production.json` in place (an uninstall that deleted
the patient database would be catastrophic). The tightened ACLs survive, so the retained data stays
protected — but it is still there.

Before a clinic PC is **resold, sent for repair, or otherwise leaves the clinic**:

1. Take a backup (in-app « Sauvegarder ») and **verify it restores** on another machine.
2. Securely erase — not just delete — `{app}\pgdata`, `{app}\api\.local`, `{app}\api\Files`,
   `{app}\api\logs`, and `{app}\api\appsettings.Production.json`.
3. Remove the clinic CA from every client machine (the client uninstaller does this via
   `certutil -delstore Root "Clinic Management Local CA"`).

Skipping step 2 hands over every patient record, every radiograph, the JWT signing key and both database
passwords with the hardware.

### Ports

| Port | Bind | Purpose | Firewall |
|------|------|---------|----------|
| `5001` | all interfaces (HTTPS) | Kestrel front door — the only browser-facing endpoint for the app | **open (LAN)** |
| `5080` | all interfaces (**HTTP**) | Device-trust page only — `/api/trust`; every other path is refused on this port | **open (LAN)** |
| `5000` | loopback | API plain HTTP (redirects to HTTPS) / internal BFF target | closed |
| `3000` | loopback | Next web server (proxied by Kestrel) | closed |
| `5432` | loopback | PostgreSQL | closed |

`5080` is cleartext **by necessity**: a phone cannot be asked to download the fix for a certificate it does not
trust over that same certificate. It is not a second door into the API — the application refuses every path
except `/api/trust` on that port (`TrustPortGate`), so what it exposes is a CA's *public* certificate, install
instructions and a QR of an address the LAN already broadcasts. Set `Hosting:TrustPort` to `0` in
`appsettings.Production.json` to switch the page off entirely (staff phones then need the CA installed by hand,
or by the client installer on a Windows PC).

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

## Phones & tablets — the device-trust page (AC-44 / AC-45)

The client installer above covers **Windows PCs**. A phone or tablet has no installer, so it gets the CA from
a page the server publishes for exactly that purpose.

**The flow.** On the device, open **`http://<adresse-du-serveur>:5080/api/trust`**. The page is in French,
works offline, and needs no login — it cannot require one, because the device has no way to log in until it
trusts the server. It offers:

- **iPhone / iPad** → a generated `.mobileconfig` profile. Tap it, then **Réglages → Profil téléchargé →
  Installer**.
- **Android** → the raw `ca.crt`, plus the Settings path for importing a CA certificate.
- **A QR code** of the page's own LAN address, so the operator can open the page on the server PC and let staff
  scan it instead of dictating an IP address.

Once the certificate is trusted, the device uses the normal front door at `https://<adresse-du-serveur>:5001`.
The trust page is not the application and never shows clinic data.

> The `.mobileconfig` is **generated on request, never staged into the installer**. On a reinstall the CA is
> reused; on a fresh install it is newly minted. A profile baked into a build artefact would install a root
> that signs nothing.

### When it does not work

Four states, each with one action. The first is by far the most common.

| Symptom | Cause | What to do |
|---|---|---|
| iPhone: profile installed, browser **still** warns | On iOS a profile-installed root is **inert until separately enabled**. This is an Apple design decision, not a bug, and nothing the server sends can flip the switch. | **Réglages → Général → Informations → Certificats de confiance** → enable the switch named after the clinic. |
| Browser refuses to load and offers **no** "continue anyway" | **HSTS** is remembered for that host, so the browser will not let anyone click through. Only possible if an operator set `Security:EnableHsts` — it is **off by default** in Local mode for exactly this reason. | Clear site data for that address (or use another browser), install the CA, then reload. If it recurs, set `Security:EnableHsts` to `false`. |
| It worked, then every device started warning again | **The server was reinstalled or `.local/` was cleared**, so a *new* CA was generated. Devices still trust the old one. | Remove the old profile/certificate on each device, then redo the trust page. The `.mobileconfig`'s identity is derived from the CA, so a new CA is correctly seen as a new profile rather than silently replacing the stale one. |
| **CA is installed and correct, and HTTPS still fails** | ⚠️ **The server's IP changed** (new DHCP lease). The certificate's SANs are captured **when it is generated** and the certificate is then reused unchanged, so an address obtained later is not in it. The CA is fine; the *address* is not covered. | Give the server PC a **static IP or a reserved DHCP lease**, then reach it at that address. If the address must change, delete `api\.local\` and reprovision — which regenerates the CA, so every device must re-trust (row above). |

⚠️ Two limits worth knowing before deploying: the certificate covers **IPv4 addresses only**, and there is
**no `.local`/mDNS name** — devices must reach the server by IPv4 literal, or by a hostname the local network
already resolves.

### Certificate lifetime (AC-46 — open)

The server leaf is minted for **5 years**. Apple refuses TLS server certificates valid for more than **398
days** — but explicitly **exempts certificates that chain to a user-installed root**, which is precisely this
case. That exemption is believed to apply here and **has not been verified on a physical iPhone**; treat it as
open until it is.

If it turns out not to hold, **do not simply shorten the leaf**: `EnsureServerCertificate` never checks expiry
and the CA's private key is not persisted, so a short leaf would expire with nothing able to renew it, and
re-provisioning regenerates the **CA** — breaking trust on every device (row 3 above). Shortening the lifetime
therefore requires persisting the CA key and adding leaf-only renewal first.

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

### Permissions & data at rest (security-hardening Part 1 — audit § 2 findings 1–4)

**Not CI-runnable (R-1).** Needs a Windows build machine, and a **second, non-administrator local account**
to prove denial from. Run the whole list twice: once on a **fresh** install, once on an **upgrade over a
previous install** (that second pass is the one most existing clinics will take).

- [ ] **AC-1.1 / AC-2.1–2.3** — `icacls "<path>"` for `{app}\pgdata`, `{app}\api\.local`, `{app}\api\Files`
      and `{app}\api\logs` lists **no** `BUILTIN\Users` (`Utilisateurs`) and no `Everyone` (`Tout le monde`)
      entry. Only `SYSTEM`, `Administrateurs` and `NETWORK SERVICE` appear.
- [ ] **AC-2.4** — Signed in as the **non-admin** account: opening `{app}\api\.local\signing-key`, any file
      under `{app}\api\Files`, and any file under `{app}\api\logs` is **denied**.
- [ ] **AC-1.2 / AC-1.5** — The install completed (so de-privileged `initdb` ran), PostgreSQL starts, and the
      API connects. Login works and a patient file uploads and downloads.
- [ ] **AC-2.6** — **After** install: upload a patient file and let the API write a log line, then re-run
      `icacls` on the new blob and the current log file. Both inherit the tightened ACL — no `Users`.
- [ ] **AC-3.1** — `type "{app}\api\.local\db-credentials"` shows the `CMDPAPI1` marker followed by
      ciphertext, **not** two readable passwords.
- [ ] **AC-3.1 (machine-bound)** — Copy `db-credentials` to another machine and run
      `ClinicManagement.API.exe read-credentials --out out.txt` there → it **fails** with the French
      restore-from-backup message. Delete `out.txt` afterwards.
- [ ] **AC-3.2** — Re-run the installer over the existing install → it reuses the credentials, the DB still
      authenticates, and the data is intact.
- [ ] **AC-3.3** — On a machine installed with the **previous** installer (plaintext `db-credentials`),
      re-running this installer migrates the file to the `CMDPAPI1` form and still connects.
- [ ] **AC-1.7** — Force `initdb` to fail (e.g. leave a stray non-cluster file in an empty `pgdata`), let the
      install abort, then `icacls "{app}\pgdata"` → **no** `Users` grant left behind.
- [ ] **AC-2.8** — `{app}\initdb.log` is gone after a successful install.
- [ ] **AC-1.4 / AC-2.9 (fail loud)** — Make a hardening step fail (e.g. hold an exclusive handle on a file
      under `pgdata`) → the installer shows a French error and **aborts**; it does not report success.
- [ ] **Mise hors service** — The decommissioning procedure above is understood and the five paths are on the
      operator's erase list.

### Phones & tablets — device trust (P8, AC-44…AC-46)

**Not CI-runnable, and not runnable in the development environment either.** Needs a **physical iPhone or
iPad** and a **physical Android device** on the clinic LAN. Nothing substitutes: iOS's certificate-trust
behaviour is the thing under test.

- [ ] **AC-44** — From a phone on the LAN, `http://<server-ip>:5080/api/trust` loads **before** any certificate
      is installed, in French, with no login.
- [ ] **AC-44** — The QR on the page, scanned from a second device, opens the same page on that device.
- [ ] **AC-44 (iOS)** — « iPhone / iPad » installs the profile; enabling it under **Certificats de confiance**
      then makes `https://<server-ip>:5001` load with **no** interstitial.
- [ ] **AC-44 (Android)** — « Android » downloads `ca.crt`; importing it as a CA certificate then makes
      `https://<server-ip>:5001` load with no interstitial.
- [ ] **AC-44 (Cloud)** — With `Auth:Mode=Cloud`, all four trust routes return **404**.
- [ ] **R-11 (the security assertion — do not skip)** — On the trust port, every non-trust path is refused:
      `curl -i http://<server-ip>:5080/api/auth/mode` → **404**, and likewise `/api/patients`, `/hangfire`, `/`.
      Only `/api/trust*` answers. Confirm the app itself is still fully reachable on `5001`.
- [ ] **Ports** — Firewall shows `5001` **and** `5080` inbound, and nothing else; `3000` / `5000` / `5432`
      remain unreachable from the LAN.
- [ ] **Uninstall** — Both firewall rules ("Clinic Management HTTPS" and "Clinic Management Trust") are gone.
- [ ] **AC-45** — Walk the four failure states in the table above and confirm each stated action resolves it.
      The DHCP row is the one most likely to be met in the field and the least likely to be guessed.
- [ ] **AC-46 (open)** — On a **real iPhone**, confirm the 5-year leaf is accepted once the CA is trusted
      (Apple's user-installed-root exemption). If it is rejected, **do not just shorten the leaf** — read the
      lifetime note above first; the change needs CA-key persistence and leaf-only renewal, and without them a
      short leaf expires with nothing able to renew it.

### Client installer (S7)

- [ ] **AC-2.1** — Installs a working shell with a Start-menu icon.
- [ ] **FR-E2** — After install, the shell reaches the server over HTTPS **with no certificate warning** (CA imported into Root).
- [ ] Uninstall removes the shell; the imported CA is removed (or its removal is documented).

### Cloud regression (all slices)

- [ ] A **Cloud**-mode run (`Auth:Mode=Cloud`, absolute `NEXT_PUBLIC_API_URL`) is unchanged: no reverse proxy, no cert self-gen, no Windows-service/DB-down handling firing; Phase 4 auth-coverage tests still green.
