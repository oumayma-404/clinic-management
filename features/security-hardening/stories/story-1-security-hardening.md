# Story 1: Close every finding in audit § 2

**Status:** in progress
**Spec:** [../spec.md](../spec.md) · **Plan:** [../plan.md](../plan.md)

As a **clinic owner**, I want the security findings from the July 2026 audit closed, so that patient records, the JWT signing key and the clinic's legal billing settings are not exposed to anyone who can reach the clinic PC or the clinic network.

> **One story by explicit user decision.** Worked through in five ordered parts; commit at each part boundary (plan risk **R-1**). Parts are vertical increments, not technical layers.

## Entry criteria

- [x] `spec.md` APPROVED, Challenged
- [x] `plan.md` APPROVED, Challenged
- [x] On `feature/security-hardening`
- [x] Codebase assumed stable (tests passing) at branch point

---

## Part 1 — Installer filesystem posture (US-1, US-2, US-3 · 🔴×4)

Closes the four P0s live on already-shipped clinic PCs. **Operator-verified, not CI-runnable** (`packaging/` R-1).

- [ ] **P1.1** `DirectoryAclHardener` (Infrastructure/Security) — the single implementation of the ACL policy: grant `LocalSystem`/`NetworkService`/`Administrators`, break inheritance, remove `Users`/`Everyone` recursively. Strict exit-code checking on every `icacls` call; failure throws with the command output in the message.
- [ ] **P1.2** `LocalDataProtection` (Infrastructure/Security) — one definition of the Data Protection key-ring configuration, used by both `AddInfrastructure` and the console verbs so they cannot drift (**DEV-1**).
- [ ] **P1.3** `DbCredentialProtector` (Infrastructure/Security) — protect/read the credentials payload; legacy plaintext detected and reported for migration (AC-3.3).
- [ ] **P1.4** `harden-permissions` console verb (API/Maintenance) — Local-only, exit code, prints the resulting posture.
- [ ] **P1.5** `protect-credentials` / `read-credentials` console verbs — encrypt in place; decrypt to a caller-supplied file (never a password on a command line, AC-3.6); migrate legacy plaintext on read.
- [ ] **P1.6** `Program.cs` — intercept the three new verbs alongside `reset-admin-password` / `provision-cert`.
- [ ] **P1.7** `clinic-server.iss` — harden `.local` **before** writing credentials (no plaintext window); protect after writing; read via the verb on reinstall; harden `pgdata` after `initdb` succeeds **and on its failure path** (AC-1.7); harden `.local`/`Files`/`logs` before services start; remove `initdb.log` (AC-2.8); drop the add-only `Permissions:` reliance from `[Dirs]`.
- [ ] **P1.8** Docs — `packaging/README.md` (permissions verification, AC-3.5 residual, AC-2.11 « Mise hors service »), `packaging/CLAUDE.md` key facts.
- [ ] **P1.9** Tests — `DirectoryAclHardenerTests`, `DbCredentialProtectorTests`, `LocalDataProtectionTests`.

**Verification (operator, on a Windows build machine):** fresh install **and** upgrade-over-previous both show no `Users`/`Everyone` on `pgdata`, `.local`, `Files`, `logs`; a non-admin account is denied on `signing-key`, a file blob and a log; `db-credentials` is ciphertext; PostgreSQL starts, API connects, upload and login work; a file uploaded and a log written **after** install inherit the tightened ACL (AC-2.6); a deliberately failed install leaves no `pgdata` grant (AC-1.7).

---

## Part 2 — Backup output posture (US-14 · 🔴)

- [ ] **P2.1** `PgDumpBackupService` calls `DirectoryAclHardener` on the timestamped folder **before** the dump (AC-14.2).
- [ ] **P2.2** Non-fixed destination drive → proceed with a French warning (AC-14.3); ACL failure on a fixed drive → fail loud + delete the partial folder (AC-14.4).
- [ ] **P2.3** Warning threaded through `BackupResultDto` → `BackupNowCommand` → `clinic-settings.tsx` so it is visible, not only logged.
- [ ] **P2.4** Tests — `PgDumpBackupServiceAclTests`.

---

## Part 3 — Auth & session (US-4, US-5 · 🔴 + 🟠)

- [ ] **P3.0** **The client-IP chain first** — BFF propagates the inbound `X-Forwarded-For`; API `ForwardedHeaders` with `KnownProxies` restricted to loopback; `LocalRequest.IsLoopback` stays on the raw peer and its `null ⇒ true` fail-open is fixed. See the plan section; **build this before the limiter** or the limiter is built against a constant.
- [ ] **P3.1** Rate limiter — per-IP on anonymous auth endpoints; generous global per-user; exemptions for `/api/connectivity`, the OAuth callback, `/hub/*`; 429 → `{ error }` + `Retry-After`; distinct French UI message (AC-4.5).
- [ ] **P3.2** `ILoginAttemptTracker` (`IMemoryCache`, keyed `(userId, clientIp)`); `User.FailedLoginAttempts` kept as the durable per-account backstop.
- [ ] **P3.3** Token version on `User` + migration; bumped in `SetPassword` and `Deactivate`; **never** in `UpgradePasswordHash` (AC-5.11).
- [ ] **P3.4** Two token kinds — access (~30 min, API audience) and refresh (`clinic-refresh` audience the API rejects).
- [ ] **P3.5** `LocalAuthEnforcementMiddleware` compares the version on the account it already loads; rejects versionless tokens (AC-5.15).
- [ ] **P3.6** Frontend — cookie holds the refresh token; `/bff/auth/token` exchanges; renewal once on 401 then retry; hub reconnect; **consolidate the seven private `/bff/auth/token` fetches** onto the shared helper (plan risk **R-4**).
- [ ] **P3.7** Tests — incl. `ClientIpResolutionTests` (two addresses → two buckets; spoofed XFF rejected on `setup`/`/hangfire`).

---

## Part 4 — Authorization (US-6, US-7, US-8, US-9 · 🟠×3 + 🟡)

- [ ] **P4.1** `UpdateClinicCommand` — extend the desired-vs-current gate to matricule fiscal, TVA applicable/rate, timbre enabled/amount; refuse before the logo upload; unchanged values are not a change.
- [ ] **P4.2** `AdminOnly` on `PUT /api/clinics/doctors`, the four procedure-type writes, `PUT /api/patients/recalls/settings`.
- [ ] **P4.3** Own-or-admin in `SetDoctorWorkingHoursCommand`.
- [ ] **P4.4** Tenant check in all **10** catalog commands (plan's US-9 table), verified **with the query filter inactive**.
- [ ] **P4.5** Frontend gating for the newly admin-only controls.
- [ ] **P4.6** Role-policy coverage guard extending `ControllerAuthorizationCoverageTests` as an exact-set assertion.

---

## Part 5 — Hygiene (US-10, US-11, US-12, US-13 · 🟠 + 🟡×3)

- [ ] **P5.1** MinIO — empty **and** known-default rejected outside Development; warn once in Development (AC-10.5); `appsettings.json` scrubbed.
- [ ] **P5.2** `FileContentValidation` extracted from the cachet path; applied before the blob write; `nosniff` + `attachment` on download.
- [ ] **P5.3** `SecurityHeadersMiddleware` ahead of the proxy; CSP report-only → page walk on a **production** build → enforcing; `next.config.ts` `headers()` conditioned on `AUTH_MODE` so Local emits exactly one CSP.
- [ ] **P5.4** Six raw-exception messages → French + `LogError`; sweep for others; leak guard test.
- [ ] **P5.5** Upload form states formats + limit before selection (AC-11.7).
