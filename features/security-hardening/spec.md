# Feature Specification: Security Hardening (Audit Section 2)

**Status:** APPROVED
**Challenged:** Yes
**Created:** 2026-07-27
**Scope:** Full-stack + packaging
**Source:** `CODEBASE_AUDIT_2026-07.md` § 2 « Security » — every finding re-verified against source on 2026-07-27
**Feature:** Close every security finding the July 2026 audit raised — the offline-Windows installer exposing patient records and the JWT signing key to every local account, the missing rate limiting and token revocation, four unguarded admin surfaces, and the unvalidated upload / missing-header / leaked-exception hygiene gaps.

## Overview

A clinic that installs the Windows server build today ends up with every patient record, every uploaded radiograph, and the per-install JWT signing key readable by **any local account on the machine** — including the receptionist's non-admin Windows login and anyone who walks up to an unlocked session. Reading the signing key is enough to forge an admin token for any clinic. That is the headline: four separate P0 filesystem findings that all trace to the installer adding permissions without ever removing the inherited defaults.

The rest is the surface a hostile-or-careless LAN client reaches over the network. There is no rate limiting anywhere, so anyone on the clinic Wi-Fi can burn five login attempts per account in a loop and keep the entire staff — admin included — permanently locked out. The browser is handed the raw 12-hour JWT, and nothing revokes it: changing your password does not log out whoever stole it. Four admin-only surfaces carry no role policy at all, so a secretary can rewrite the clinic's legal billing settings (matricule fiscal, TVA, timbre — values frozen onto every invoice issued afterwards), rewrite the practitioner roster, reprice the procedure catalog, or change any doctor's working hours. And a handful of hygiene gaps round it out: MinIO silently authenticating with committed `minioadmin`/`minioadmin` defaults instead of failing loud, patient-file uploads accepting any declared content type with no size cap, no security response headers at all, and raw `ex.Message` text returned verbatim to clients.

This feature fixes all fourteen. Nothing here adds a user-visible capability — success is that the clinic notices nothing except that a secretary can no longer change the TVA rate.

> **Note on the audit's own count.** § 2's index row reads « 5 / 4 / 3 = 12 » but the section lists **14** bullets (5 🔴, 5 🟠, 4 🟡). This spec covers all 14. The index row should be corrected to 5/5/4 = 14.

### Traceability — audit bullet → user story

| # | Sev | Audit finding | Story |
|---|:--:|---|---|
| 1 | 🔴 | `BUILTIN\Users` Full Control over the whole PostgreSQL data directory, never revoked | US-1 |
| 2 | 🔴 | `{app}\api\.local` readable by every local user — incl. the HS256 signing key | US-2 |
| 3 | 🔴 | `{app}\api\Files` — every radiograph and scan — readable by every local user | US-2 |
| 4 | 🔴 | Plaintext DB + `postgres` superuser passwords under Program Files with inherited `Users: Read` | US-3 |
| 5 | 🔴 | No rate limiting anywhere; a LAN client can lock out the whole clinic | US-4 |
| 6 | 🟠 | BFF hands the raw 12-hour JWT to browser JS; no revocation, no refresh, no token version | US-5 |
| 7 | 🟠 | `PUT /api/clinics` has no role policy — legal billing settings writable by anyone | US-6 |
| 8 | 🟠 | Three more unguarded admin surfaces: `PUT /api/clinics/doctors`, procedure-type catalog writes, `PUT /api/patients/recalls/settings` | US-7 |
| 9 | 🟠 | `SetDoctorWorkingHoursCommand` checks same-clinic only, not own-or-admin | US-8 |
| 10 | 🟡 | Catalog mutators skip the per-handler `ClinicId` check, relying on the fail-open EF filter | US-9 |
| 11 | 🟠 | `MinIO:AccessKey`/`SecretKey` committed as `minioadmin`; "non-empty" treated as configured | US-10 |
| 12 | 🟡 | Patient-file upload: no content-type allow-list, no magic bytes, no size cap; type echoed on download | US-11 |
| 13 | 🟡 | No HSTS / CSP / `X-Content-Type-Options` / `X-Frame-Options` anywhere | US-12 |
| 14 | 🟡 | Raw `ex.Message` interpolated into client-facing failures and returned verbatim as the 400 body | US-13 |
| — | 🔴 | **Not in the audit** — one-click backup writes the full DB dump + every patient file to a folder with default inherited ACLs, reopening US-1/US-2 | US-14 |

---

## Group A — Local Windows install: filesystem exposure

> All four findings are **Local mode / `packaging/` only**. Per `packaging/CLAUDE.md` R-1 this area is **operator-verified, not CI-runnable** — so each story below carries an explicit operator verification step, and where logic can be lifted out of Inno Pascal into unit-testable app code it should be.

### US-1: The patient database is not readable by other local accounts

As a **clinic owner**, I want the PostgreSQL cluster holding every patient record to be unreadable by the other Windows accounts on the server PC, so that a receptionist login — or anyone at an unlocked session — cannot copy the whole patient database.

The installer must grant `BUILTIN\Users` Full Control over `{app}\pgdata` because PostgreSQL's `initdb` deliberately drops administrator privileges and then runs as the de-privileged interactive user, which cannot write under Program Files. That grant is legitimate **for the duration of `initdb`** — the defect is that it is never taken away.

**Acceptance Criteria:**
- **AC-1.1:** After a successful fresh install, `{app}\pgdata` grants **no** access to `BUILTIN\Users`. An `icacls {app}\pgdata` listing shows only `LocalSystem`, `NetworkService`, `Administrators` (and inherited-and-then-broken defaults), never `*S-1-5-32-545`.
- **AC-1.2:** The de-privileged `initdb` step still succeeds — the grant is present while `initdb` runs and revoked immediately after, before the installer proceeds.
- **AC-1.3:** Revocation is **recursive** — it applies to the directory and every file and subdirectory `initdb` created, not just the top level.
- **AC-1.4:** If revocation fails, the installer **fails loud** in French and aborts rather than completing with the grant in place. A completed install never silently leaves the cluster world-readable.
- **AC-1.5:** PostgreSQL starts and the API connects after revocation — the DB service account retains the access it needs.
- **AC-1.6 (reinstall / upgrade):** Re-running the installer over an existing install re-asserts the tightened permissions. An install created by the *previous* installer version is remediated on upgrade, not left as-is. This is the path most existing clinics will take, so it must be tested explicitly.
- **AC-1.7:** An aborted or failed install does not leave the grant behind — the revocation runs on the failure path too.

### US-2: Per-install secrets, patient files and logs are readable only by the service

As a **clinic owner**, I want the app's own secret material, the uploaded patient files and the log files to be readable only by the service account and administrators, so that a local user cannot forge an admin token, browse patients' radiographs, or read PHI out of a log.

The `[Dirs]` entries currently use Inno's `Permissions: service-modify`, which **adds** an ACE for the service and leaves the inherited `Users: Read & Execute` from `{autopf}` fully intact. Add-only is the bug: three directories need inheritance broken, not extended.

What `{app}\api\.local` actually contains makes this the most serious of the three — it is the install's entire trust store:

| File | Reading it gives an attacker |
|---|---|
| `signing-key` | The HS256 JWT signing key → **forge an admin token for any clinic** |
| `server.pfx` + `server-cert-password` | The LAN HTTPS server key → impersonate the server, decrypt captured traffic |
| `dataprotection-keys/` | The Data Protection key ring → **decrypt the stored per-clinic reminder channel secrets** |
| `teif-signing.pfx` | The e-invoice signing certificate → sign fraudulent TTN e-invoices as the clinic |
| `db-credentials` | Both DB passwords (see US-3) |

**Acceptance Criteria:**
- **AC-2.1:** After install, `{app}\api\.local` and everything under it grants **no** access to `BUILTIN\Users` or `Everyone`. Inheritance from the parent is **broken**, not merely supplemented.
- **AC-2.2:** Same for `{app}\api\Files` — the patient-file blob store (radiographs, scans, referrals).
- **AC-2.3:** Same for `{app}\api\logs` — Serilog output can carry patient names and appointment detail.
- **AC-2.4:** A non-admin local user attempting to read `{app}\api\.local\signing-key`, any file under `{app}\api\Files`, or any file under `{app}\api\logs` is denied by the OS.
- **AC-2.5:** The API service — running under its configured account — can still read and write all three directories: log rotation works, file upload/download works, and first-boot generation of the signing key / certificate still succeeds.
- **AC-2.6 (files created after install):** Blobs and logs the app writes *after* install inherit the tightened ACL. A radiograph uploaded next month is no more readable than one uploaded on day one.
- **AC-2.7 (reinstall / upgrade):** As AC-1.6 — re-running the installer remediates an install created by the previous version.
- **AC-2.8 (adjacent, in scope):** `{app}\initdb.log` is written directly into the world-readable `{app}` root. It is either written somewhere already protected or removed once the install succeeds; it is never left readable by `BUILTIN\Users`.
- **AC-2.9:** If any ACL step fails the installer fails loud in French and aborts (as AC-1.4).
- **AC-2.10 (uninstall retains data — deliberate):** `clinic-server.iss` has no `[UninstallDelete]` section, so uninstalling removes the tracked program files and the services but **leaves** `pgdata`, `api\.local`, `api\Files`, `api\logs` and `appsettings.Production.json` on disk. That stays the behaviour — an uninstall that deleted the patient database would be catastrophic — and the tightened ACLs survive uninstall so the retained data stays protected. What is missing is the operator's knowledge of it.
- **AC-2.11 (decommissioning procedure):** `packaging/README.md` gains a « Mise hors service » procedure to run before a clinic PC is resold, repaired, or otherwise leaves the clinic: (1) take and verify a backup, (2) securely erase those five paths, (3) remove the clinic CA from the client machines. Without it a decommissioned PC still holds every record, every radiograph, the JWT signing key and both DB passwords.

### US-3: Database passwords are not recoverable from a copy of the folder

As a **clinic owner**, I want the database passwords not to be readable in plaintext from a copy of the install folder, so that a stolen disk, a backup image, or an admin-level foothold does not hand over the `postgres` superuser account.

Two files carry them today: `.local\db-credentials` (both the `clinic_user` **and** the `postgres` superuser password, so the installer can reuse them on reinstall) and `appsettings.Production.json` (the password inside the connection string).

**Acceptance Criteria:**
- **AC-3.1:** `.local\db-credentials` is stored **encrypted at rest, machine-scoped** — the same class of protection the Data Protection key ring already uses on this install. Its plaintext is not recoverable by copying the file to another machine.
- **AC-3.2:** A reinstall over an existing cluster still recovers both passwords and connects successfully — the existing "reuse persisted credentials" path in the installer keeps working end to end.
- **AC-3.3:** An install created by the previous installer version, holding a **plaintext** `db-credentials`, is migrated to the encrypted form on upgrade — and the reinstall path still reads it correctly during the migration release.
- **AC-3.4:** If the credentials file cannot be decrypted (e.g. the machine was rebuilt) the installer reports the existing clear French guidance — restore from backup or deliberately delete `pgdata` to start fresh — and aborts. It never silently regenerates passwords against a live cluster.
- **AC-3.5 (stated residual limit):** `appsettings.Production.json` keeps the connection-string password in readable form, because Kestrel and the DbContext need it at boot before any decryption service exists. It is protected by the US-2 ACL work only. This limitation is written into `packaging/README.md` so the operator knows what an admin-level compromise yields.
- **AC-3.6:** No password (DB, superuser, cert) is ever passed on a command line, written to `initdb.log`, or logged. *(Verified already true for `pg_dump` via `PGPASSWORD` and for `initdb` via `--pwfile`; this AC pins it against regression.)*

### US-14: A backup is no more exposed than the data it copies

As a **clinic owner**, I want a backup to be protected the same way the live data is, so that clicking « Sauvegarder » does not undo everything US-1 and US-2 achieve.

**This finding is not in the audit.** `PgDumpBackupService` writes a full `pg_dump` of every patient record plus a **recursive copy of the entire `Files/` tree** into an admin-chosen destination, created with a plain `Directory.CreateDirectory` — default inherited ACLs, no hardening whatsoever. Group A protects the live cluster and blob store; the first backup to `C:\Backups` hands an identical copy to every local account.

**Acceptance Criteria:**
- **AC-14.1:** The `clinic-backup-<timestamp>` folder and everything written into it is created with the same restricted posture as `{app}\api\Files` — inheritance broken, no `BUILTIN\Users`, no `Everyone`.
- **AC-14.2:** The restriction is applied to the folder **before** the dump and the file copy are written into it, not tightened afterwards — no window exists where the dump sits readable.
- **AC-14.3:** When the chosen destination is a removable or network drive, where NTFS ACLs cannot be relied on, the backup still proceeds but the result carries a French warning telling the admin the copy cannot be protected on that medium and must be kept somewhere safe. The warning is shown in the UI, not just logged.
- **AC-14.4:** If the ACL cannot be applied on a destination where it *should* work (a local fixed drive), the backup **fails loud** and the partial folder is deleted — consistent with the service's existing never-a-silent-partial contract. It does not fall back to writing an unprotected backup.
- **AC-14.5:** The existing backup behaviour is otherwise unchanged: DB first then files, unique timestamped folder, the pre-checks for writability and free space, and the operator-facing failure messages.
- **AC-14.6:** The operator verification checklist covers the backup folder alongside AC-1.1/AC-2.1 — an `icacls` listing on a freshly created backup shows no `Users`/`Everyone`.

---

## Group B — Auth & session

### US-4: A hostile LAN client cannot lock the clinic out or brute-force accounts

As a **clinic administrator**, I want a single misbehaving or hostile device on the clinic network to be unable to lock my staff out of the app, so that a prank or an infected laptop cannot stop the clinic working.

`AddRateLimiter`/`UseRateLimiter` are absent from `Program.cs`. The only brake is `User.MaxFailedLoginAttempts = 5` with a 15-minute lockout, and because it is keyed on the **account** alone, anyone who can reach the login endpoint can keep every account — admin included — locked indefinitely. A per-IP limiter alone does not fix this: five attempts is below any sane rate limit.

**Acceptance Criteria:**
- **AC-4.1:** The anonymous auth endpoints (`POST /api/auth/login`, `/api/auth/setup`, `/api/auth/register`) are rate-limited **per client IP**. Exceeding the limit returns **429** with a `Retry-After` header and the canonical `{ "error": "..." }` body carrying a French message.
- **AC-4.2:** Failed-login attempts are counted **per account *and* source**, not per account. A host that burns five attempts against `admin@clinic.tn` locks only itself out; a colleague signing in as `admin@clinic.tn` from a different machine is unaffected.
- **AC-4.3:** A per-account backstop still exists at a much higher threshold, so a genuinely distributed guessing attack is still stopped — but no single source can trip it.
- **AC-4.4:** The authenticated API carries a global per-user limiter generous enough that normal use never hits it: a cold dashboard load, a patient-detail page opening its tabs, and a bulk document generation all complete without a 429. It exists to bound a runaway client loop or scraping, not to shape normal traffic.
- **AC-4.5:** The frontend surfaces a 429 as a distinct, actionable French message ("trop de tentatives, réessayez dans N minutes") rather than the generic failure toast.
- **AC-4.6:** Limits are configurable (window, permit count) with secure defaults, so an operator can loosen them for an unusually busy cabinet without a rebuild.
- **AC-4.7:** Rate limiting is active in **both** auth modes — Cloud is internet-facing and needs it at least as much as Local.
- **AC-4.8:** The offline `reset-admin-password` console recovery path is unaffected and remains the documented escape hatch.

### US-5: A stolen or stale token stops working

As a **staff member**, I want changing my password to immediately invalidate every existing session, so that if my token was stolen the thief loses access the moment I react.

Today `/bff/auth/token` returns the raw 12-hour JWT to browser JavaScript, so the HttpOnly `local_session` cookie buys no XSS protection. There is no token version and no refresh, and `LocalAuthEnforcementMiddleware` revokes only *deactivated* and *must-change-password* accounts — a **voluntary** password change leaves every existing token valid for its full remaining lifetime.

**Acceptance Criteria:**
- **AC-5.1:** Every issued token carries a version tied to the account. A token whose version no longer matches the account is rejected with **401** on the next request.
- **AC-5.2:** The version changes — and therefore every existing token is invalidated immediately — on: a **voluntary** password change, an **admin password reset**, the **offline `reset-admin-password`** recovery, and **account deactivation**.
- **AC-5.3:** The token the browser holds is valid for **≈30 minutes**, not 12 hours.
- **AC-5.4:** A staff member working continuously is **never** bounced to the login screen at the 30-minute mark. Renewal is silent and driven by the durable HttpOnly session cookie.
- **AC-5.5:** The durable session credential in the cookie is **distinct from the access token** and is not itself accepted as a bearer token by the API. Stealing the short-lived access token does not yield a long-lived one.
- **AC-5.6:** Renewal is server-validated against live account state — a deactivated, must-change-password, or version-bumped account cannot renew, and gets the same 401/403 treatment it gets today.
- **AC-5.7:** A page left open past token expiry recovers on its own: the first 401 triggers one renewal and a retry of the failed request, with no error shown to the user and no lost form input.
- **AC-5.8:** The SignalR realtime connection survives expiry — it renews its token and reconnects, and live refresh keeps working. A peer's edit still updates the screen after an hour idle.
- **AC-5.9:** The existing 30-minute inactivity auto-logout in `LocalSessionProvider` keeps working and is not turned into a hard 30-minute cap on active use.
- **AC-5.10:** Cloud/Auth0 sessions are out of scope for the version claim (those tokens are Auth0's, not ours) — the Cloud path is unchanged and does not regress.

**Where the version is bumped — and the one place it must not be.** All four password paths funnel through `User.SetPassword` (`ChangePasswordCommand:67`, `ResetUserPasswordCommand:79`, `AdminPasswordRecoveryService:83`, plus first-set), which makes it a single clean choke point alongside `Deactivate()`. There is a trap next to it:

- **AC-5.11:** `SetPassword` and `Deactivate` bump the version. `UpgradePasswordHash` — the rehash-on-login path that re-stores the password under an upgraded hash format after a *successful* verification — **must not**. Bumping it there would invalidate the token the login just issued, logging the user out on every sign-in that upgrades a hash. Pinned by a test.

**Blast radius the cookie change carries (verified).** The `local_session` cookie currently *is* the access JWT, and two seams depend on that:

- **AC-5.12:** `GET /bff/auth/session` today base64-decodes the cookie as a JWT to return `{ name, email, role }` for the header. It must keep returning the same display identity without treating the cookie as a JWT. The signed-in user's name and role still render in the header.
- **AC-5.13:** `middleware.ts` gates every protected route on the mere presence of the `local_session` cookie. That gate keeps working against the new durable credential — a signed-in user is never bounced to `/login` while their session is valid, and an unauthenticated visitor is still redirected as today (through `frontDoorRedirect()`).
- **AC-5.14:** The `local_must_change_password` cookie and the forced-password-change redirect keep working across a renewal — a user mid-forced-change is not silently released from it by a token refresh.

**Deployment cutover.** On the release that introduces the version, every in-flight token is a versionless 12-hour token:

- **AC-5.15:** A token carrying **no** version claim is rejected with 401. The frontend cannot renew it, so it redirects to `/login` with a French « session expirée, reconnectez-vous » notice. Staff sign in once and every pre-upgrade token dies at deploy time rather than up to 12 hours later.
- **AC-5.16:** `packaging/README.md` records the clinic-wide re-login as expected upgrade behaviour, so the operator is not surprised by it.

---

## Group C — Authorization gaps

> All four are reachable today by a plain `secretary`.

### US-6: Only an administrator can change the clinic's legal billing settings

As a **clinic administrator**, I want the values that get frozen onto every invoice — matricule fiscal, TVA, timbre fiscal — to be changeable only by me, so that nobody else can silently alter what appears on a legal document a patient pays against.

`PUT /api/clinics` carries no role policy; the handler admin-gates *only* the TTN e-invoicing toggle. The chosen shape follows that existing per-field precedent rather than closing the whole endpoint, so a secretary can still correct the clinic phone number.

**Acceptance Criteria:**
- **AC-6.1:** A non-admin may still update: name, address, city, phone, email, logo, working hours.
- **AC-6.2:** A non-admin attempting to change **matricule fiscal**, **TVA applicable**, **TVA rate**, **timbre fiscal enabled**, or **timbre fiscal amount** is refused with a French admin-required message, and **nothing in the request is saved** — not even the fields they were allowed to change.
- **AC-6.3:** The refusal is decided **before** any side effect — in particular before a logo upload writes a blob to storage.
- **AC-6.4:** A request that leaves a gated field at its current value is **not** treated as a change (matching how the existing TTN gate compares desired-vs-current), so a non-admin round-tripping the settings form does not get a spurious refusal.
- **AC-6.5:** The existing TTN toggle/environment gate keeps working exactly as before.
- **AC-6.6:** An admin can change everything, as today.

### US-7: The three remaining unguarded admin surfaces require an administrator

As a **clinic administrator**, I want the practitioner roster, the procedure catalog and the clinic-wide recall interval to be admin-only, so that clinic-wide configuration matches the access control already applied to every comparable surface.

**Acceptance Criteria:**
- **AC-7.1:** `PUT /api/clinics/doctors` (rewrite the practitioner roster) requires an administrator; a non-admin gets **403**.
- **AC-7.2:** Procedure-type catalog **writes** require an administrator — create, update, delete, and `initialize-defaults` — matching the CNAM nomenclature, dental-act and medication catalogs. Procedure-type **reads** are unchanged for all staff.
- **AC-7.3:** `PUT /api/patients/recalls/settings` (clinic-wide recall interval) requires an administrator, finally matching its own doc comment claiming "Admin-editable". The recall **list** and the per-patient actions (`contacted`, `snooze`, `send`) stay available to all staff — those are day-to-day work, not configuration.
- **AC-7.4:** The frontend hides or disables these controls for non-admins rather than letting them submit and fail, consistent with how the existing admin-only screens behave.
- **AC-7.5 (adjacent, in scope):** A guard test enumerates every controller action and asserts each is either in an explicit "no role policy needed" allow-list or carries one — so the next missed surface fails the build instead of shipping. This follows the existing `ControllerAuthorizationCoverageTests` / `TreatmentPlansControllerAuthorizationTests` pattern, which already fails on any unclassified new action.

### US-8: Only the practitioner or an administrator can change a practitioner's schedule

As a **dentist**, I want my working hours to be changeable only by me or an administrator, so that a colleague or a secretary cannot silently move my availability and have patients booked outside it.

`SetDoctorWorkingHoursCommand` checks same-clinic only. Its sibling `UpdateDoctorProfileCommand` already does the own-or-admin check correctly — the pattern exists and was simply not applied here.

**Acceptance Criteria:**
- **AC-8.1:** A doctor can set their own working hours.
- **AC-8.2:** An administrator can set any practitioner's working hours in their clinic.
- **AC-8.3:** Any other staff member — including another doctor — is refused with the same French own-profile-only message `UpdateDoctorProfileCommand` returns.
- **AC-8.4:** A cross-clinic target still reads as "not found", never as "forbidden" (no existence disclosure) — preserving current behavior.
- **AC-8.5:** The check happens **before** any mutation.

### US-9: Catalog mutations verify tenancy in the handler, not just in the query filter

As a **clinic owner**, I want another clinic's admin to be unable to edit my catalog rows, so that tenant isolation does not depend on a claim that may be absent.

The catalog mutators rely entirely on the EF global query filter, which is **fail-open**: `ICurrentClinicProvider` reads the JWT `clinic_id` claim, and when no clinic is in scope the filter is inactive. Since the Auth0 `app_metadata` push is best-effort and its failure is swallowed, a token minted without `clinic_id` deactivates the filter and lets an admin edit another clinic's rows by id. Every other command in the codebase does the authoritative DB-resolved check; these were missed.

The audit names two commands as examples. Enumerating every catalog mutator and checking each for a tenant reference gives the **complete set of 10** (verified 2026-07-27) — the audit's "plus …" list was short by five, including two commands it never names:

| Area | Missing the tenant check | Has it |
|---|---|---|
| DentalActs | `UpdateDentalActCommand`, `DeactivateDentalActCommand`, `ConfirmDentalActsCommand` | `CreateDentalActCommand` |
| CnamNomenclature | `UpdateCnamEntryCommand`, **`DeactivateCnamEntryCommand`**, **`UpdateCnamLetterValueCommand`**, `ConfirmCnamDataCommand` | `CreateCnamEntryCommand` |
| Medications | `UpdateMedicationCommand`, `DeactivateMedicationCommand`, `ConfirmMedicationDataCommand` | `CreateMedicationCommand` |

*(ProcedureTypes commands all carry the check already — US-7 for that area is purely a missing role policy, not a tenancy gap.)*

**Acceptance Criteria:**
- **AC-9.1:** All **10** commands in the table above resolve the caller's clinic from the **DB user record** and verify the target row's `ClinicId` before mutating.
- **AC-9.2:** A cross-clinic target reads as "not found" in French, matching the convention everywhere else.
- **AC-9.3:** The behavior holds **with the query filter inactive** — a request whose principal carries no `clinic_id` claim still cannot reach another clinic's rows. This is the actual finding and must be the test; a test that passes only with the filter active proves nothing.
- **AC-9.4:** The three `Confirm*` commands are included deliberately. Catalogs became per-clinic in `AddPerClinicCatalogs`, so a bulk confirm running with the filter inactive is the same cross-clinic write as a single-row edit — across many rows at once.
- **AC-9.5:** Normal same-clinic catalog editing is unaffected.

---

## Group D — Config, uploads, headers

### US-10: A Cloud deployment that forgets its storage credentials fails loud

As an **operator**, I want a missing MinIO configuration to stop the app at startup, so that a deployment never comes up quietly authenticating with published default credentials.

`appsettings.json` still ships `MinIO:AccessKey`/`SecretKey` as `minioadmin`/`minioadmin`, and the DI check treats merely **non-empty** as configured. So a Cloud deploy that forgets the env vars authenticates with the well-known default instead of failing loud like every other scrubbed secret does.

**Acceptance Criteria:**
- **AC-10.1:** Tracked `appsettings.json` carries **empty** `MinIO:AccessKey`/`SecretKey` with the `// SECRET` comment convention the other retired secrets already use.
- **AC-10.2:** In Cloud mode, a missing MinIO access key or secret **fails startup loud** with a clear operator message and a non-zero exit — matching how an empty DB connection string already behaves.
- **AC-10.3:** A credential explicitly set to a **known default** (`minioadmin`) is treated as **not configured** and fails startup the same way — **when the environment is not Development**. Setting the env var to the default value is not an escape hatch in Production or Staging.
- **AC-10.4:** Local mode is unaffected — it uses `LocalDiskFileStorage` and needs no MinIO at all. A Local install must not start demanding MinIO credentials.
- **AC-10.5 (Development carve-out — required, not a convenience):** `Auth:Mode` in the tracked `appsettings.json` is **`Cloud`**, `docker-compose.yml` ships MinIO as `minioadmin`/`minioadmin`, and `appsettings.Development.json` carries **no** MinIO override. So a strict reading of AC-10.3 would make `dotnet run` fail at boot on a fresh clone for every developer. In **Development** the app therefore starts against the default credentials and logs a single warning (« identifiants MinIO par défaut ») instead of failing. The `docker-compose` flow must work unchanged on a fresh clone with no extra setup — this is an acceptance criterion, not an aspiration.
- **AC-10.6 (rotation):** Any real MinIO deployment that has been running on `minioadmin` has its credentials **rotated** as part of shipping this, since the value is in git history.

### US-11: Uploaded patient files are validated

As a **clinic owner**, I want the app to accept only real medical documents and images as patient files, so that a malicious or malformed upload cannot be stored and later served back from the app's own origin.

`UploadPatientFileCommand` accepts any client-declared content type — no allow-list, no magic-byte check, no size cap — and `PatientFilesController.DownloadFile` echoes it verbatim. The doctor-cachet path in `UpdateDoctorProfileCommand` already does all three checks correctly, so the safe pattern exists in the repo and should be reused.

**Acceptance Criteria:**
- **AC-11.1:** Only `application/pdf`, `image/png` and `image/jpeg` are accepted. Everything else — notably `image/svg+xml`, `text/html`, `application/octet-stream` — is refused with a French message naming the accepted formats.
- **AC-11.2:** The **declared type and the actual bytes must agree**: the file's magic signature is verified (`%PDF-`, `\x89PNG`, `\xFF\xD8\xFF`). A `.exe` renamed and declared `application/pdf` is refused.
- **AC-11.3:** Files over **25 MB** are refused with a French message stating the limit.
- **AC-11.4:** An empty (zero-byte) file is refused.
- **AC-11.5:** A refused upload leaves **no blob** in storage and **no row** in the database. Validation happens before the blob is written — no orphan cleanup required.
- **AC-11.6:** Download serves the **stored, verified** content type — never a value re-derived from the request — and sends `X-Content-Type-Options: nosniff` plus `Content-Disposition: attachment`, so a stored file cannot execute in the app's origin.
- **AC-11.7:** The upload form states the accepted formats and the size limit **before** the user picks a file, and rejects an oversized selection client-side without a wasted upload. The server check remains authoritative.
- **AC-11.8:** `image/jpg` is normalized to `image/jpeg` on the way in, as the cachet path already does.
- **AC-11.9:** Existing stored files with content types outside the new allow-list still download — the allow-list gates **uploads**, and historical patient records are never made unreachable. Such a download is served as `attachment` with `nosniff` (AC-11.6), so it is inert.

### US-12: Every response carries baseline security headers

As a **clinic owner**, I want the app to send the standard browser-protection headers, so that a stored-content or injection problem is contained by the browser rather than fully exploitable.

The only `nosniff` in the whole codebase is set inline on the single doctor-cachet endpoint.

**Acceptance Criteria:**
- **AC-12.1:** Every response carries `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY` (with the matching `frame-ancestors`), and `Referrer-Policy: strict-origin-when-cross-origin`.
- **AC-12.2:** A `Content-Security-Policy` is shipped in **`Report-Only`** mode first, so violations appear in the browser console without breaking the UI.
- **AC-12.3:** Every page in the app is walked with the report-only CSP active and the violations resolved — dashboard, appointments (day/week/month), patient detail with all tabs, the document editor, treatment-plan workspace, factures, caisse, settings, the three catalog pages, login/setup/join. A clean console across all of them is the gate. Three specific cases must be checked deliberately, because each fails in a way that does **not** look like a CSP problem:
  - **`@vercel/analytics`** is mounted in the root layout (Cloud) — needs `script-src` plus `connect-src` for its beacon, or the CSP silently kills analytics with no visible symptom.
  - **`docx` + `file-saver`** client-side export generates `blob:` URLs — needs `blob:` in the relevant directive, or « Télécharger » does nothing but log to the console.
  - **`POST /api/medical-documents/generate-pdf-download`** returns an **inline** PDF — needs `object-src`/`frame-src`, or the document preview renders blank.
- **AC-12.4:** Once AC-12.3 is clean, the CSP is flipped to **enforcing**. This is part of this feature, not a follow-up.
- **AC-12.5:** In **Local** mode the headers cover the proxied Next pages as well as `/api/*` — Kestrel is the single front door, so one middleware placed ahead of the reverse proxy covers everything.
- **AC-12.6:** In **Cloud** mode, page responses served by Next (not through Kestrel) carry the equivalent headers too, so the Cloud UI is not left without them.
- **AC-12.7 (HSTS — see EC-7):** `Strict-Transport-Security` is sent on HTTPS responses in **Cloud**. In **Local** it is **config-gated and off by default**, because the LAN build uses a self-generated CA: HSTS on a device that has not imported that CA turns a click-through certificate warning into a **non-bypassable** hard failure. If the operator enables it, `packaging/README.md` must state that CA import becomes mandatory for every device.
- **AC-12.8:** HSTS is never sent on the loopback HTTP hop the Next BFF uses.
- **AC-12.9:** The existing inline `nosniff` on the cachet endpoint becomes redundant and is removed in favour of the global header.

### US-13: Client-facing errors never leak internal detail

As a **clinic owner**, I want error messages shown to staff to be plain French guidance, so that a stack-trace fragment, a SQL constraint name or an internal path is never exposed to a browser.

Six handlers interpolate raw `ex.Message` into `Result.Failure`, and `ApiControllerBase.HandleFailure` returns it verbatim as the 400 body: `CreateAppointmentCommand:240`, `UpdateAppointmentCommand:463`, `DeletePatientCommand:61`, `DeleteDentalRecordCommand:68`, `GetDashboardStatsQuery:126`, `MarkNotificationReadCommand:71`.

**Acceptance Criteria:**
- **AC-13.1:** None of the six returns `ex.Message` to the client. Each returns a **French, actionable** message describing what failed, consistent with the `graceful-error-handling` contract already in the repo.
- **AC-13.2:** The full exception — type, message, stack — is **logged at Error** with enough context to diagnose. Nothing is silently swallowed; the detail moves from the response body to the log.
- **AC-13.3:** The existing *deliberate* French messages next to these catch-alls are preserved — e.g. `DeletePatientCommand`'s specific "des données liées existent" branch keeps its wording.
- **AC-13.4:** The response body still matches the canonical `{ "error": "<message>" }` contract; the status code each action chooses is unchanged.
- **AC-13.5 (adjacent, in scope):** The codebase is swept for any *other* `{ex.Message}` interpolation reaching a client-facing `Result.Failure`, and those are fixed in the same pass. The six cited are the known set, not necessarily the complete one.
- **AC-13.6:** A guard test asserts no client-facing failure message contains raw exception text, so the pattern cannot creep back.

---

## API Contract

No endpoint is added or removed. Contract changes are all authorization and error-shape:

```
PUT /api/clinics
  Non-admin changing matriculeFiscal | vatApplicable | vatRate |
  stampDutyEnabled | stampDutyAmount | ttn*
  → 403  { "error": "Seul un administrateur peut modifier ..." }
  Nothing in the request is persisted.

PUT    /api/clinics/doctors                       → AdminOnly   (was: any authenticated)
POST   /api/procedure-types                       → AdminOnly   (was: any authenticated)
PUT    /api/procedure-types/{id}                  → AdminOnly
DELETE /api/procedure-types/{id}                  → AdminOnly
POST   /api/procedure-types/initialize-defaults   → AdminOnly
PUT    /api/patients/recalls/settings             → AdminOnly
  Non-admin → 403 { "error": "..." }
  GET on all of the above unchanged.

PUT /api/doctors/{id}/working-hours
  Caller is neither the practitioner nor an admin
  → 403 { "error": "Vous ne pouvez modifier que votre propre profil." }

POST /api/auth/login | /setup | /register
  Per-IP limit exceeded
  → 429 + Retry-After: <seconds>
        { "error": "Trop de tentatives. Réessayez dans N minutes." }

Any authenticated endpoint
  Token version stale (password changed / reset / account deactivated)
  → 401
  Global per-user limit exceeded
  → 429 + Retry-After

POST /api/patients/{id}/files
  Disallowed type / magic-byte mismatch / > 25 MB / empty
  → 400 { "error": "<French message naming accepted formats or the limit>" }

GET /api/patients/{patientId}/files/{fileId}/download
  → 200, Content-Type = the stored verified type,
         X-Content-Type-Options: nosniff,
         Content-Disposition: attachment

Token renewal (US-5) — one new BFF/API exchange whose exact shape is an
implementation choice. Contract requirements only:
  · the browser never holds a credential valid beyond ~30 min
  · the durable cookie credential is NOT accepted as an API bearer token
  · renewal re-validates live account state (version, IsActive, MustChangePassword)
```

## Data / Schema Changes

- **Token version on the account** (US-5) — a value on `User` that changes on password set/reset and deactivation, stamped into issued tokens and compared per request. Migration required.
- **Per-source failed-attempt tracking** (US-4) — failed attempts keyed by account *and* source rather than the single `User.FailedLoginAttempts` counter. Lockout state is transient (15-minute window), so a durable table is not required; the existing counter may remain as the AC-4.3 per-account backstop.
- **No other schema change.** Nothing in Groups A, C or D touches the database.

## Scope

### In Scope
- All **14** findings in `CODEBASE_AUDIT_2026-07.md` § 2, plus the adjacent gaps named in AC-1.6, AC-2.7/2.8, AC-3.3, AC-7.5, AC-9.4, AC-10.6, AC-11.9, AC-13.5.
- **US-14 — the backup-output exposure, which the audit does not contain.** Found while challenging this spec: hardening the live data (US-1/US-2) is pointless while a documented one-click path copies all of it somewhere unprotected.
- **The decommissioning gap** (AC-2.10/2.11) — uninstall deliberately retains the patient data, and nothing currently tells the operator to erase it before the PC leaves the clinic.
- Correcting § 2's index count from 12 to 14 in the audit document.
- The upgrade/reinstall remediation path for Group A — existing installs, not just fresh ones.
- Frontend changes where a server change would otherwise show as a broken screen: 429 messaging (AC-4.5), silent renewal + 401 retry (AC-5.7/5.8), admin-gated controls (AC-7.4), upload constraints in the form (AC-11.7), CSP compatibility (AC-12.3).
- Operator documentation updates in `packaging/README.md` for the new ACL posture, the encrypted credentials file, the stated `appsettings.Production.json` residual (AC-3.5), and HSTS if enabled (AC-12.7).
- Guard tests: role-policy coverage (AC-7.5), tenant checks with the filter inactive (AC-9.3), no-raw-exception-text (AC-13.6).

### Out of Scope
- Everything in audit sections 1 and 3–10 — data loss, money correctness, silent no-ops, timezone, unreachable backends, product gaps, UX, localization, realtime/schema/performance, build. Each gets its own spec.
- Items § 2 explicitly marked **"Verified clean — do not re-investigate"**: controller `[Authorize]` coverage, the `[AllowAnonymous]` allow-list, loopback-gated setup, Google OAuth `state`, loopback-only Hangfire, `LocalDiskFileStorage` traversal blocking, absence of raw SQL, absence of tokens in `localStorage`, BFF cookie flags, PBKDF2 password hashing.
- Replacing the bearer-token architecture with a full BFF proxy (considered and rejected — it fights the Phase 5 same-origin front door, double-hops all traffic including uploads, and SignalR still needs a query-string token).
- Making the Cloud authorization `FallbackPolicy` fail-closed — a known residual the audit records elsewhere, and a much wider blast radius than this feature.
- Git-history scrubbing. Rotation is required (AC-10.6); rewriting history is separate and optional.
- Antivirus / malware scanning of uploads. Type and size validation only.
- Two-factor authentication, password-complexity changes, or session-management UI.
- DICOM support in the upload allow-list (considered; the clinic's imaging exports as JPEG/PNG today).

## Edge Cases

### EC-1: `initdb` needs the very grant we are removing
- **Scenario:** `initdb` drops admin privileges and runs as the de-privileged interactive user, so it genuinely requires write access under Program Files.
- **Expected:** The grant is scoped to the `initdb` step and revoked immediately afterwards. Ordering is the whole fix — a fresh install must still succeed, and AC-1.5 (PostgreSQL starts, API connects) is the proof.

### EC-2: The service account cannot read its own directories after tightening
- **Scenario:** Inheritance is broken but the API service account is not among the ACEs granted.
- **Expected:** The service still reads and writes `.local`, `Files` and `logs` — first-boot key/cert generation, uploads, and log rotation all work. The installer verifies write access after applying the ACL and fails loud if it cannot (AC-2.9), rather than producing an install that starts and then cannot log in.

### EC-3: Existing installs
- **Scenario:** A clinic already running the previous installer upgrades. Its `pgdata` still carries the `Users` grant, its `.local` still inherits `Users: Read`, and its `db-credentials` is plaintext.
- **Expected:** Upgrade remediates all three in place (AC-1.6, AC-2.7, AC-3.3) with no data loss and no re-`initdb`. This is the path most clinics take and must be tested explicitly, not assumed.

### EC-4: Machine rebuilt after DPAPI encryption
- **Scenario:** Windows is reinstalled; `pgdata` and the encrypted `db-credentials` survive on disk, but the machine-scoped DPAPI key does not.
- **Expected:** The installer reports the existing clear French guidance — restore the credentials file from backup, or deliberately delete `pgdata` to start fresh, with the data-loss consequence stated — and aborts. It never regenerates passwords against a live cluster it cannot authenticate to.

### EC-5: The anonymous connectivity poll must not be rate-limited into failure
- **Scenario:** `ConnectivityProvider` polls the anonymous `GET /api/connectivity` every 15 s **per browser tab**. Several LAN clients with several tabs each, all behind the same source, generate steady anonymous traffic.
- **Expected:** The limiter never 429s legitimate connectivity polling — a 429 there would make the app look offline and disable AI + Google Calendar. `/api/connectivity` and the one-shot Google OAuth callback are exempt or carry a limit generous enough for many tabs. The `/hub/clinic` WebSocket, a long-lived connection, is never limited.

### EC-6: Whole clinic behind one source address
- **Scenario:** A per-IP limiter, but the clinic's devices reach the server through one address (NAT, or the desktop shells all on one host).
- **Expected:** Login limits are sized so an entire clinic starting its day — every staff member signing in within a few minutes — never trips the limit. AC-4.6's configurability is the escape hatch for an unusually large cabinet.

### EC-7: HSTS versus the self-generated LAN certificate
- **Scenario:** A tablet or phone on the clinic Wi-Fi opens the app without having imported the clinic CA (only the desktop client installer imports it).
- **Expected:** With HSTS off (the Local default, AC-12.7) the user gets the normal bypassable certificate warning and can proceed. If an operator enables HSTS, that warning becomes a hard, unbypassable failure — so enabling it is documented as requiring CA import on every device first. **This is the one place where the "enforce HSTS immediately" decision is deliberately softened for Local; flag it if you disagree.**

### EC-8: Token expiry mid-action
- **Scenario:** A staff member has a long invoice or document form open for over 30 minutes and then submits.
- **Expected:** The submit succeeds. The 401 triggers one silent renewal and a retry; the form is not cleared, no error is shown, and the user never sees a login screen (AC-5.7).

### EC-9: Password changed in another tab
- **Scenario:** A user changes their password in one tab while a second tab is open.
- **Expected:** The second tab's token is version-stale. It either renews cleanly against the new state or is sent to login with a clear French explanation — it never sits in a silent 401 loop or shows a raw error.

### EC-10: Renewal while offline
- **Scenario:** In an offline-LAN install, renewal is attempted while the server is briefly unreachable (service restart).
- **Expected:** The client distinguishes "cannot reach the server" from "session invalid". A transient network failure does not log the user out; the request retries and the app recovers when the server returns.

### EC-11: Non-admin round-trips the clinic settings form
- **Scenario:** A secretary opens clinic settings, edits only the phone number, and submits the whole form — including matricule fiscal and TVA at their existing values.
- **Expected:** Saved successfully. Unchanged gated fields are not treated as an attempted change (AC-6.4). Only an actual *change* to a gated field is refused.

### EC-12: Deactivation now revokes tokens instantly
- **Scenario:** AC-5.2 makes deactivation revoke every existing token immediately, which changes the blast radius of an action that already has a guard.
- **Already handled — do not re-implement:** `SetUserActiveCommand:62-64` **already** refuses an admin deactivating their own account, calling it "an unrecoverable lockout". A clinic cannot reach zero active admins that way.
- **Expected:** That existing refusal still fires unchanged, pinned by a test. An admin deactivated by *another* admin does lose access immediately — that is the intent. The offline `reset-admin-password` CLI remains the documented recovery for every other lockout.

### EC-13: A stored file whose type is no longer allowed
- **Scenario:** A `.docx` or `.tiff` uploaded before this change is opened from a patient's file list.
- **Expected:** It downloads normally (AC-11.9) — served as `attachment` with `nosniff` so it is inert. Historical patient records are never made unreachable by a new upload allow-list.

### EC-14: CSP breaks a page only in production
- **Scenario:** The enforcing CSP passes in development but a production-only inline style or the Next hydration payload violates it, leaving a page visually broken for the clinic.
- **Expected:** AC-12.3's page-by-page walk is performed against a **production** build, not a dev server, before AC-12.4 flips to enforcing.

## Non-Functional Hints

- **Security:** This whole feature is security. The single highest-value item is the `.local` ACL (US-2) — it holds the JWT signing key, the HTTPS server key, the Data Protection key ring, and the e-invoice signing certificate. Ship it first.
- **Performance:** Token renewal must not add a round trip to normal requests — renewal happens on expiry or 401, not per call. The per-request token-version comparison should ride the account lookup `LocalAuthEnforcementMiddleware` already performs, adding no new query. Upload validation buffers under a hard 25 MB cap so a large file cannot be used to exhaust memory.
- **Compatibility:** Local-mode changes must not regress Cloud, and vice versa. The existing `ControllerAuthorizationCoverageTests` allow-list must still pass. Group A is `packaging/` only and cannot break the running app.
- **Operability:** Every installer failure path reports a French operator message and a non-zero exit. A half-applied ACL or a half-migrated credentials file must never look like success.
- **Accessibility:** The new 429 and session-expiry messages follow the existing French error conventions and are announced like other form errors.

## Dependencies

- **`features/cloud-security-and-tenant-isolation`** — established the fail-open `ICurrentClinicProvider` that US-9 works around, the loopback-only Hangfire filter, and the "secrets are empty + `// SECRET`" convention US-10 follows.
- **`features/graceful-error-handling`** — defines the canonical `{ "error": "..." }` body and the French frontend error handling US-13, AC-4.5 and AC-11.1 conform to.
- **`features/windows-desktop-app`** (Phases 4–5) — owns the installer, `LocalInstallPaths`, `CertificateProvisioner`, the console verbs (`provision-cert`, `reset-admin-password`) and the YARP front door that Groups A, B and D all touch.
- **`features/server-installer-reliability`** — the prior `clinic-server.iss` hardening pass; its findings are referenced by number in the file's comments and must not be undone.
- **`packaging/README.md`** — the operator guide requiring updates for AC-3.5, AC-12.7 and the per-AC verification checklist.
- **Operator build machine** — Group A cannot be verified in CI (R-1). It needs a Windows box with Inno Setup, a fresh install, an upgrade over a previous install, and a non-admin local account to test denial from.

## Verification & Tests

Written into this spec rather than deferred, so the test work is scoped with the change.

| Area | Test type | What it pins |
|---|---|---|
| US-1 – US-3, US-14 | **Operator checklist** in `packaging/README.md` | Fresh install and upgrade-over-previous: `icacls` output for `pgdata`, `.local`, `Files`, `logs` **and a freshly created backup folder** shows no `Users`/`Everyone`; a non-admin login is denied on `signing-key`, a file blob and a log; `db-credentials` is ciphertext; PostgreSQL starts, API connects, upload and login work. Not CI-runnable (R-1). |
| US-14 | Unit + operator | Backup folder restricted **before** the dump is written (AC-14.2); ACL failure on a local fixed drive fails loud and deletes the partial folder (AC-14.4); a removable/network destination proceeds with the French warning surfaced in the UI (AC-14.3); existing DB-first ordering, uniqueness and pre-checks unchanged. |
| US-3 | Unit | Encrypt/decrypt round-trip of the credentials payload; plaintext→encrypted migration; undecryptable file surfaces the operator error, never a silent regenerate. |
| US-4 | Unit + integration | Per-IP limit returns 429 + `Retry-After`; per-(account, source) counter locks the offending source only while another source signs in fine; per-account backstop trips only under multi-source pressure; `/api/connectivity` and `/hub/clinic` are never limited (EC-5). |
| US-5 | Unit + integration | Version-stale token → 401; each of the four version-bumping events (voluntary change, admin reset, CLI reset, deactivation) invalidates an existing token; **`UpgradePasswordHash` does NOT bump the version** (AC-5.11) — a login that upgrades a hash must not invalidate the token it just issued; the cookie credential is rejected as an API bearer token; renewal refuses a deactivated / must-change / version-bumped account; a **versionless** token is rejected (AC-5.15). |
| US-5 | Frontend | 401 → one silent renewal → retry, with no user-visible error and no lost input (EC-8); hub reconnects after expiry (AC-5.8); `/bff/auth/session` still renders the header identity without decoding the cookie as a JWT (AC-5.12); `middleware.ts` route gate and the `local_must_change_password` redirect survive renewal (AC-5.13/5.14). |
| US-6 | Unit | Each gated field refused for a non-admin with **nothing persisted**; unchanged gated fields are not a change (EC-11); refusal precedes the logo upload (AC-6.3); admin path unaffected. |
| US-7 | Unit + **guard test** | 403 for a non-admin on each of the six endpoints; the role-policy coverage guard fails the build on any unclassified new action (AC-7.5). |
| US-8 | Unit | Own → allowed; admin → allowed; other doctor and secretary → refused; cross-clinic → "not found" (AC-8.4); refusal precedes mutation. |
| US-9 | Unit | All **10** commands refuse a cross-clinic id **with the global query filter inactive** — the principal carries no `clinic_id` claim. This is the finding; a test that passes only with the filter active proves nothing. |
| US-10 | Unit | Cloud + Production + empty key → startup fails, non-zero exit; Cloud + Production + `minioadmin` → same (AC-10.3); Cloud + real credentials → starts; **Cloud + Development + `minioadmin` → starts with one warning** (AC-10.5); **Local → starts with no MinIO config at all** (AC-10.4). Plus a smoke check that `docker compose up` + `dotnet run` works on a fresh clone with no extra setup. |
| US-11 | Unit + integration | Each disallowed type refused; magic-byte mismatch refused (`.exe` declared as PDF); >25 MB refused; zero-byte refused; refusal leaves no blob and no row (AC-11.5); download returns the stored type + `nosniff` + `attachment`; a pre-existing out-of-allow-list file still downloads (EC-13). |
| US-12 | Integration + manual | Headers present on an API response, a proxied page response, and an error response; HSTS absent on the loopback HTTP hop and absent in Local by default; the AC-12.3 page walk against a **production** build (EC-14) before flipping to enforcing. |
| US-13 | Unit + **guard test** | Each of the six returns a French message with no exception text while logging the full exception at Error; `DeletePatientCommand`'s deliberate branch keeps its wording; the guard test rejects any new raw-exception interpolation. |

## Open Questions

- [x] Split into multiple specs? — **Resolved:** one spec covering all of § 2, per user decision 2026-07-27.
- [x] Token model — **Resolved:** account-tied version claim + ~30 min lifetime with silent renewal (US-5).
- [x] Lockout DoS — **Resolved:** per-IP limiter plus failed attempts keyed per (account, source), with a per-account backstop (US-4).
- [x] `PUT /api/clinics` gate shape — **Resolved:** extend the existing field-level gate to all legal/billing fields, not endpoint-wide AdminOnly (US-6).
- [x] Upload allow-list — **Resolved:** PDF + PNG + JPEG, magic-byte verified, 25 MB cap. DICOM and Office formats out of scope (US-11).
- [x] Security headers — **Resolved:** cheap headers + HSTS enforced; CSP report-only first, then flipped to enforcing within this feature (US-12).
- [x] Procedure-type catalog policy — **Resolved:** AdminOnly, matching the other three catalogs (AC-7.2).
- [x] DB credentials at rest — **Resolved:** ACL + machine-scoped encryption for `db-credentials`; `appsettings.Production.json` stays ACL-protected only, documented as a residual (US-3).
### Resolved during `/challenge-spec` (2026-07-27)

- [x] **MinIO default-credential rejection vs. local development** — **Resolved:** the known-default rejection applies outside Development only; Development starts with a warning so `docker compose up` + `dotnet run` keeps working on a fresh clone (AC-10.5). *Was Critical — as originally written, AC-10.3 broke every developer's environment.*
- [x] **Backup output reopens Group A** — **Resolved:** new US-14. Not an audit finding; surfaced by challenging the spec.
- [x] **Catalog tenant-check surface** — **Resolved:** 10 commands, enumerated and verified, not the 5 the audit implies (US-9 table).
- [x] **US-5 blast radius** — **Resolved:** AC-5.11 (rehash must not bump the version), AC-5.12/5.13/5.14 (the `/bff/auth/session` and `middleware.ts` seams that depend on the cookie being the JWT).
- [x] **Token cutover on the upgrade release** — **Resolved:** versionless tokens rejected; one clinic-wide re-login, documented (AC-5.15/5.16).
- [x] **EC-12 described existing behaviour** — **Resolved:** rewritten as a regression guard over the self-deactivation refusal that already exists at `SetUserActiveCommand:62`.
- [x] **CSP breakages that don't look like CSP** — **Resolved:** `@vercel/analytics`, `blob:` exports and the inline PDF named explicitly in AC-12.3.
- [x] **Decommissioning** — **Resolved:** AC-2.10/2.11 — uninstall keeps the data by design; `packaging/README.md` gains a « Mise hors service » procedure.
- [x] **HSTS in Local mode** — **Resolved:** approved as specified on 2026-07-27. HSTS is enforced in Cloud; in Local it is config-gated and **off by default**, because the Local build uses a self-generated CA and HSTS on a device that never imported it converts a bypassable certificate warning into a hard, unbypassable failure (AC-12.7, EC-7). Enabling it is an operator choice documented as requiring CA import on every device first.
