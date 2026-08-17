# Security posture — 2026-08-16

Whole-application review of the security measures in place, what is **deliberately reduced** for the Render free
tier, and what must be closed before the first real clinic.

**Method.** Source read directly (`file:line` anchors throughout) plus the four security features' own specs and
operator docs. Nothing here is inferred from a name, a comment or a doc that the code contradicts — where a doc
and the code disagree, the code wins and the disagreement is reported in § 5.

**Scope note.** This reads code and configuration. **No dynamic testing, no penetration test and no
dependency-vulnerability scan** was performed. Absence of a finding is not proof of absence.

**Related documents**
- `SECURITY_REVIEW_2026-08.md` — the 2026-08-07 vulnerability review (3 confirmed, 2 refuted). ⚠️ predates the
  hardening below; see § 5.
- `GO-LIVE.md` — the manual go-live checklist. ⚠️ same staleness.
- `follow-up/render-free-tier-transit-relaxation.md` — the authoritative record of § 1.
- `deploy/KEY-CUSTODY.md` · `deploy/RESTORE-DRILL.md` · `deploy/README.md` — the operator side.

---

## Headline

The security layer is unusually complete and — the part that matters — mostly held by **derived tests and
startup refusals** rather than by a configuration key somebody remembered to set. Exactly **one** genuine
reduction is active for Render, it is opt-in, it is logged on every boot, and it has a written restore
procedure.

The real go-live risks are **not** that reduction. They are: four credentials still unrotated in git history, a
restore path that has never been exercised, and secret files whose default location is not gitignored.

---

## 1. What is deferred for Render — and only this

`features/hosted-security-hardening/` was designed against `deploy/docker-compose.hosted.yml`, where the operator
controls the host and can **mount files**. Render's free tier offers no persistent disk, no file mount and no
published CA certificate for its managed PostgreSQL. Three of Part 2/3's guarantees could therefore not be met as
written. **Only one of the three is a reduction.**

### 1.1 ⚠️ The reduction — restore this first

`Security:AllowUnverifiedInternalTls=true`

| | |
|---|---|
| **Kept** | The database hop is still **encrypted**. A passive tap on the network reads nothing. |
| **Given up** | **Identity.** `Require` accepts whatever certificate it is handed, so an impostor between the application and the database would not be detected. |
| **Never given up** | `Disable`, `Allow` and `Prefer` stay refused **with the flag set** — « rien en clair sur le réseau interne » is not the promise being traded. |

- Implemented at `api/ClinicManagement.API/Startup/TransportAssurance.cs:171-201`; the encrypting-modes
  allow-list is `TransportAssurance.cs:104`.
- Opt-in, non-default, and `Program.cs:535-544` logs a French `Log.Warning` **naming the key on every boot**, so
  it cannot become the forgotten default that `Security:EnforceCsp` was for a whole release.
- ⚠️ **Do not set `VerifyFull` without first mounting the CA file.** The startup check would pass and the
  *connection* would fail instead — moving a clear refusal at boot into an obscure failure on the first query.

### 1.2 Two things Render forced that are improvements — keep them

- **`DataProtection:PersistToDatabase=true`** — the Data Protection key ring lives in PostgreSQL rather than on a
  volume. **Not a reduction:** the rows are still encrypted by the deployment's certificate
  (`DataProtection:CertificateBase64`), so a database dump does not disclose the ring. It fixes something worse
  than it costs: with an ephemeral ring, `RequiresAdminSecondFactor` being true on `HostedMultiTenant` means
  **every administrator's TOTP secret dies on redeploy**, locking them out of their own cabinet.
  Moving back to a volume later is a **real migration**, not a config flip: run `reprotect-secrets --rotate`
  under the new arrangement *before* deleting anything, and confirm `verify-schema`'s
  `secrets-protected-under-current-ring` reads zero.
- **`DataProtection:CertificateBase64`** — the key-ring certificate could previously arrive only as a file path,
  which no managed platform can provide. Both routes now converge on one parse with identical checks.

### 1.3 Where the relaxed values actually live

⚠️ **There is no `render.yaml` in this repository.** `deploy/.env.hosted.example` and both compose files already
ship the **hardened** values (`SSL Mode=VerifyFull`, `Security__EnforceCsp: "true"` —
`deploy/docker-compose.hosted.yml:182`). The relaxed values exist **only in the Render dashboard**, and
`follow-up/render-free-tier-transit-relaxation.md` is their only written record.

### 1.4 Restore checklist (from `follow-up/render-free-tier-transit-relaxation.md:99-113`)

- [ ] Choose a **Tunisian host** — primary **and** a separate offsite
- [ ] Declare `RESIDENCY_ALLOWED_EGRESS_HOSTS_*`; confirm the undeclared-residency boot warning is gone
- [ ] Set `WALG_S3_ENDPOINT` to the real Tunisian endpoint; verify `BACKUP_REMOTE`'s host **by hand** in `rclone.conf`
- [ ] Mount the database CA; connection string to `SSL Mode=VerifyFull;Root Certificate=/certs/ca.crt`
- [ ] **Delete `Security__AllowUnverifiedInternalTls`**; confirm the boot warning is gone
- [ ] `MinIO__UseSSL=true` + `MinIO__RootCertificate` if an object store is configured
- [ ] Remove the `RateLimiting__Auth__*` stopgaps; set `Security__TrustedProxies` to the compose subnet
- [ ] `Security__EnforceCsp=true` (30 routes walked, 0 violations)
- [ ] Decide the key ring's home (database is fine); if moving, `reprotect-secrets --rotate` first
- [ ] Custody the PKCS#12 properly and remove it from any developer machine
- [ ] Run `verify-schema` and diff against the last capture
- [ ] Perform the first restore drill and write it down
- [ ] Budget the **annual ANCS cybersecurity audit** (`Décret-loi 2023-17`) — applies wherever you host

---

## 2. Controls actually in place

### 2.1 Identity

| Control | Detail | Anchor |
|---|---|---|
| Second factor | **TOTP mandatory for clinic `admin`** on `HostedMultiTenant`, decided by deployment **kind** — never by a setting an operator can change. Doctors/secretaries may enrol voluntarily. | `Infrastructure/Deployment/SecondFactorPolicy.cs` |
| No pre-enrolment session | A correct password from an un-enrolled admin is refused `totp_enrolment_required` — **no token is issued** | `Application/Features/Auth/Commands/LoginCommand.cs` |
| Password hashing | **PBKDF2** via ASP.NET `PasswordHasher<User>` (v3 format), with rehash-on-login | `Infrastructure/Auth/LocalAuthService.cs:20` |
| Password floor | 12 characters, enforced when a password is **set**, served by the server so no client can drift | — |
| Recovery codes | **8**, stored as hex **SHA-256** (never readable back), single-use, and **spent even when the accompanying sign-in fails** | `Domain/Entities/UserRecoveryCode.cs:81-82` |
| Session replay | Sign-in opens a **session family**; refresh rotates and the family accepts current + immediate predecessor. An older credential ends **that family only** and notifies the user | — |
| Revocation | `User.TokenVersion` checked per request; role/`IsActive` resolved from the **DB**, not the JWT claim | `Middleware/AccountStateMiddleware.cs` |
| Cookies | `HttpOnly` + `Secure` + `SameSite=Lax`, both session cookies written and cleared together | `web/lib/auth/session-cookie.ts:113-131` |
| JWT validation | issuer / audience / lifetime / signing key all validated, `ClockSkew = TimeSpan.Zero`; refresh and access tokens non-interchangeable via distinct audiences | `Program.cs` |
| Console isolation | The vendor console has its **own signing key, issuer and audience**, so a token on the wrong surface is **401, not 403** | `PlatformAuthConfig` |

**Rate limiting** — `api/ClinicManagement.API/Startup/RateLimiting.cs:67-89`

| Policy | Limit | Window | Partition |
|---|---|---|---|
| Auth | **30** | 300 s | the **submitted account** |
| Auth (address ceiling) | 150 | 300 s | client address |
| General API | 600 | 60 s | client address |
| **Archive export** | **3** | 600 s | user |
| CSP report | 60 | 60 s | address |

⚠️ Auth is keyed on the **account**, not the address, because a whole practice reaches a hosted deployment through
one NAT address — one colleague mistyping used to spend everybody's budget. The address ceiling still applies, so
one attacker cannot buy a fresh budget per address. The archive limit exists because that endpoint used to fall to
the general window, permitting **600 full-practice exports a minute**.

### 2.2 Transit

- Caddy terminates public TLS; **HSTS** `max-age=31536000; includeSubDomains` — `deploy/Caddyfile:40`
- Every internal hop encrypted **and verified** against a deployment-private 10-year CA: API↔PostgreSQL
  (`SSL Mode=VerifyFull;Root Certificate=/certs/ca.crt`), API↔MinIO (`MinIO__UseSSL` + `MinIO__RootCertificate`),
  and both the `backup` and `pitr` sidecars (`PGSSLMODE=verify-full`)
- PostgreSQL runs `ssl=on` with a baked `pg_hba.conf` offering **`hostssl` only** — the server itself refuses
  cleartext, so the application's setting is not merely a courtesy
- **Startup refuses** if any of this is unsatisfied, gated on the deployment **kind** and never on whether a
  certificate happens to be present — `Startup/TransportAssurance.cs:128-139`. Every problem is reported, not the
  first.
- Forwarded headers are honoured **bounded to the proxy's own address** (`Security__TrustedProxies`); unset ⇒
  headers ignored entirely + a startup warning. The two loopback-only gates (first-run `setup`, `/hangfire`) are
  decided by the **real TCP peer**, never by a header — `Infrastructure/LocalRequest.cs`, `OriginalPeer`

### 2.3 At rest

**Key ring** — `Infrastructure/Security/LocalDataProtection.cs`
- `HostedMultiTenant`: encrypted by an operator-supplied **PKCS#12 certificate**; **required**, startup fails
  without it (`LocalDataProtection.RequiresProtectingCertificate`). Previous certificates are retained for
  decryption so rotation is not data loss.
- `SelfHostedLan`: machine-scoped **DPAPI**
- Development only: an unprotected ring is tolerated (`TolerateUnprotectedKeyRing`) — every deployed environment
  refuses.

**Application-level encrypted columns** — five protectors, each with its **own purpose string** so one kind of
ciphertext is not decryptable by the code that reads another (the framework enforces this via key derivation):

| Protector | Protects | Anchor |
|---|---|---|
| `UserSecretProtector` | clinic user **TOTP secrets** | `Infrastructure/Security/UserSecretProtector.cs` |
| `PlatformSecretProtector` | vendor console **TOTP secrets** | `Infrastructure/Security/PlatformSecretProtector.cs` |
| `ReminderSecretProtector` | per-clinic SMS / WhatsApp / SMTP credentials | `Infrastructure/Services/ReminderSecretProtector.cs` |
| `GoogleTokenProtector` | `Clinic.GoogleRefreshToken` | `Infrastructure/Security/GoogleTokenProtector.cs` |
| `DbCredentialProtector` | the Local install's `.local/db-credentials` file | `Infrastructure/Security/DbCredentialProtector.cs` |

⚠️ **A failed decrypt refuses the operation — it never degrades.** For a second factor specifically,
« could not decrypt » must never become « sign in without one ». Recovery paths are named in the log line
(`reset-user-totp`, `platform-account --reset-totp`, re-connect Google Agenda).

⚠️ **Patient / PHI columns are NOT encrypted at application level.** This is a deliberate, written rejection
(`features/hosted-security-hardening/spec.md:338`): it breaks accent-insensitive database free-text search
(without which a patient on page seven reads as « aucun résultat »), duplicate detection, and ordering a paged
list by name. PHI is protected by volume encryption + tenancy + authorization instead. Revisit only if a
compliance rule requires it, and then as its own feature.

**Volume and backups**
- **LUKS** on the volume holding `postgres_data` + `minio_data`, unlocked at boot from a keyfile on the host's own
  boot volume. ⚠️ This protects a **stolen, snapshotted or decommissioned disk**. It does **not** protect against
  someone who already has root on the running host — `deploy/README.md:339-341` says so in those words.
- Nightly off-site dump encrypted with an **`age`** key pair; continuous PITR WAL stream encrypted with
  **`WALG_LIBSODIUM_KEY`**. Each run **verifies what it just uploaded** — decrypt and confirm it parses — and a
  verification failure **fails the backup run**.
- A database backup carries a marker identifying the key-ring generation in force; a mismatch on restore is
  **refused with both generations named**.

### 2.4 Multi-tenancy

- **31 entity types** carry EF Core global query filters, and the predicate is
  `IsSystemWide || x.ClinicId == ScopedClinicId` — `Persistence/ApplicationDbContext.cs:247+`
- **Fail-closed**: a three-valued `ITenantScope` (`Unset` | `Clinic(id)` | `SystemWide(reason)`), where `Unset`
  resolves `ScopedClinicId` to `Guid.Empty` and therefore returns **zero rows** — `ApplicationDbContext.cs:235-236`.
  A path that never established a scope reads **nothing** rather than every clinic.
- The scope is set from the **DB-resolved** `User.ClinicId`, **never** from the JWT claim —
  `Middleware/TenantScopeMiddleware.cs:7,48`
- The seven clinical children of `Patient` each carry a denormalised `ClinicId` as a **required positional
  constructor parameter**, so a new write path that forgets it is a compile error, not a silent leak.
  `verify-schema`'s `clinical-child-clinic-matches-patient` catches both drift directions.
- Every background job and console verb declares its scope explicitly (`UseSystemWide(reason)` /
  `UseClinic(id)`); `SystemWideCallerCoverageTests` derives that set by reflection, so a **new** job that forgets
  fails on the day it is written.
- Blob keys are `clinics/{clinicId}/…`, composed in exactly one place, and **`IFileStorage.UploadAsync` requires a
  `Guid clinicId` in its signature** — an unprefixed key is not something a caller can write.
  `ClinicStorageKeyTests` derives that off the interface, so a third overload without one fails.
- Authorization: **all 32 route controllers carry a class-level named policy**; there are no bare `[Authorize]`
  attributes left. Four policies — `Authenticated`, `AnyClinicRole`, `AdminOrDoctor`, `AdminOnly`.
- The vendor console can read every cabinet, so what it may return is a **closed set of field names**
  (`PlatformReadShape`), enforced by a **build-failing** reflection test in both directions. It cannot read a
  patient record.

### 2.5 Evidence

- **Tamper-evident audit chain**: each entry carries an **HMAC-SHA256** over itself and its predecessor, keyed by
  `Audit:ChainKey` — a secret **the database does not hold** — compared with `CryptographicOperations.FixedTimeEquals`
  — `Domain/Services/AuditChain.cs:112-201`. Minimum key length 32 bytes. Chains are per clinic and appends are
  serialised by a PostgreSQL advisory lock (`Persistence/AuditChainAppender.cs:62`).
- ⚠️ Audit writes stay **best-effort** — a failed audit write must never roll back the clinical or financial
  operation it describes — so a **declared gap** is recorded instead. `verify-schema` reports breaks and declared
  gaps separately; a restore records a declared boundary rather than something that reads as tampering.
- `AuditSaveChangesInterceptor` writes one row per mutated **aggregate root** (actor, clinic, entity, action,
  changed-field summary), read at `GET /api/audit` (`AdminOnly`, paged).
- Full-cabinet **download and restore both require the password again** (step-up), on their own failure counter so
  a mistype cannot lock the practice's only administrator out mid-day. The export ledger row is **not**
  best-effort: if it cannot be written, the download does not happen, and the entry states whether the archive was
  **delivered**, not merely requested.
- The vendor console keeps its own append-only `PlatformAccessEntry` ledger; opening one cabinet's file is
  recorded, listing the portfolio deliberately is not.
- Logs are written to a durable 30-day volume with **PHI scrubbed**, enforced by `LogTemplateCoverageTests`.

### 2.6 Browser surface

Emitted on every response by `api/ClinicManagement.API/Middleware/SecurityHeadersMiddleware.cs:133-165`:

```
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: strict-origin-when-cross-origin
Permissions-Policy: <every capability denied; fullscreen=(self)>
Cross-Origin-Opener-Policy: same-origin
Cross-Origin-Resource-Policy: same-site
Reporting-Endpoints: csp-endpoint="/api/csp-report"
Strict-Transport-Security: max-age=31536000        (when Security:EnableHsts and the request is HTTPS)
Content-Security-Policy[-Report-Only]:
  default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline';
  img-src 'self' data: blob:; font-src 'self' data:; connect-src 'self';
  object-src 'self' blob:; frame-src 'self' blob:; frame-ancestors 'none';
  base-uri 'self'; form-action 'self'; report-uri /api/csp-report; report-to csp-endpoint
```

- `Security:EnforceCsp` changes only the header **name** — the policy string is the one the report-only walk
  validated (`SecurityHeadersMiddleware.cs:156`). It is read **once** at construction, so a mid-session config
  reload cannot change the header a page's assets are already loading under.
- The middleware **does not overwrite** a CSP an upstream component already set — two CSP headers make the browser
  enforce the *intersection*, which is a silent breakage.
- The policy is stated in three places (this middleware, `deploy/Caddyfile`'s two sites,
  `console/next.config.ts`) and held **byte-identical** by a build-failing test.
- Violation reports are **scrubbed to the route pattern** before storage — this app's URLs contain patient
  identifiers, so a violation report is itself patient data — and the receiving endpoint is rate-bounded.
- The third-party analytics script (`@vercel/analytics`) was **removed**: it loaded from an external origin and
  sent page views from a medical-records application to a third party.

### 2.7 Input and injection

| Class | Finding |
|---|---|
| **SQL injection** | One `ExecuteSqlRawAsync` in the whole solution, positionally parameterized (`AuditChainAppender.cs:62`). No `FromSqlRaw`. `SqlSearch`/`SearchTerm` escape LIKE wildcards — an unescaped `%` would match every row. |
| **XSS sinks** | **Zero** `dangerouslySetInnerHTML`, `innerHTML =` or `eval(` in `web/app`, `web/components`, `web/lib` or `console/`. |
| **Path traversal** | `ClinicStorageKey.Normalize` rejects `..` and leading `/`; `LocalDiskFileStorage` re-checks the resolved path against the base directory. |
| **Command injection** | `PgDumpBackupService` and `RestoreBackupCommand` use `ProcessStartInfo.ArgumentList` (never a shell string), `UseShellExecute = false`. |
| **Upload validation** | `Application/Common/Files/FileTypeCatalog.cs` — keyed on **extension**, with per-format size caps and **magic-byte signature rules at declared offsets** (`Required` / `Advisory` / `None(reason)`), which is what makes DICOM's `DICM` at byte 128 expressible. Never on the declared content type. |
| **SSRF** | Tenant-settable integration URLs validated at the write boundary (`Domain/Common/OutboundEndpoint`): absolute `https`, no loopback / link-local / RFC1918 / CGNAT / unique-local / single-label host. Private addresses permitted on `SelfHostedLan` alone. ⚠️ Not DNS-rebinding-proof — that needs a `SocketsHttpHandler.ConnectCallback` re-checking the *resolved* address. |
| **TLS validation in clients** | No bypass anywhere: Android `onReceivedSslError` cancels, iOS uses `.performDefaultHandling`, no `ServerCertificateCustomValidationCallback`, no `NSAllowsArbitraryLoads`. |

### 2.8 Attack surface reduction

- `api`, `web` and the console container publish **no host ports** — only Caddy reaches them.
- The vendor console is published on **`127.0.0.1:9443` only**, reached via `ssh -L 9443:127.0.0.1:9443 <host>`.
  With `CONSOLE_PORT` unset the port is `0` and every console path 404s everywhere — **absent, not
  present-and-refusing**.
- `/hangfire` is **loopback-only in every profile**.
- Self-registration by clinic code is **off** on `HostedMultiTenant` (`AllowsSelfRegistration = false`); the only
  public door is e-mail-verified signup with a 32-byte CSPRNG single-use token, SHA-256 in the row and plaintext
  only in the mail.
- `POST /api/auth/signup` returns a **byte-identical** neutral response whether the address is free, taken or
  already pending — no enumeration oracle.
- Google OAuth `state` is 32 CSPRNG bytes, double-submitted via HttpOnly cookie + server cache, consumed once.
- **The AI assistant was deleted whole** (~2 400 lines), removing the product's only per-request egress of
  clinic-authored text to a US third party (`router.huggingface.co`). No code in this product now reaches an
  inference endpoint.
- `DataResidencyAssurance` **refuses to start** a hosted deployment whose visible egress destinations are not on
  a declared allow-list (`Residency:AllowedEgressHosts`) — `api/ClinicManagement.API/Startup/DataResidencyAssurance.cs`.
  It is a **declaration**, never a geolocation lookup.

---

## 3. Gaps to close before the first real clinic

Ordered by what would hurt most.

### 3.1 🔴 Four credentials are exposed in git history and unrotated

Google client secret, Google refresh token, HuggingFace API key, **database password**. Blanking the working tree
does not undo the exposure — the history is 306 commits deep. Rotate all four **at the provider**.

`SECURITY_REVIEW_2026-08.md:216-224` (⬜ Outstanding) · originally
`features/cloud-security-and-tenant-isolation/progress.md:123-125`

### 3.2 🔴 `.gitignore` does not cover the secret files the operator is told to create

`deploy/docker-compose.hosted.yml:406-418` defaults six Docker secrets to `./secrets/*` — i.e. **inside the
repository** — including the key-ring **PKCS#12 and its password**, the **audit chain key** and **both JWT signing
keys**. `deploy/KEY-CUSTODY.md` instructs the operator to run
`openssl pkcs12 -export … -out deploy/secrets/keyring-certificate.pfx`.

`.gitignore` contains **no** entry for `deploy/secrets/`, `clinic-keys/`, `deploy/rclone/rclone.conf`, or
`*.pfx` / `*.p12` / `*.pem` / `*.key`. Nothing is committed today (verified: `git ls-files` finds no key
material), but a single `git add -A` would commit the key that decrypts every clinic's credentials and every
administrator's second factor.

**Fix — add to `.gitignore`:**
```
# Key material and per-deployment secrets — NEVER committed
deploy/secrets/
deploy/rclone/rclone.conf
clinic-keys/
*.pfx
*.p12
*.pem
*.key
```

### 3.3 🔴 No restore drill has ever been performed

`deploy/RESTORE-DRILL.md:122-124` says so in place of an empty table: "This deployment's restore path is
**unproven**." Cadence is defined (quarterly, and after every schema-migration batch) with a seven-part pass
condition — it has simply never been run. A backup you have never restored is not a backup.

Also unrun against a live deployment: `verify-schema`'s `key-ring-protection` and
`secrets-protected-under-current-ring` — the only two checks that say the certificate protection is actually **in
force** rather than merely configured.

### 3.3b Known-vulnerable dependencies — found and half-closed 2026-08-17

Added `.github/workflows/ci.yml`'s `dependencies` job and ran it. Nothing had ever scanned this repo, and
the first run was not clean: **1 critical + 5 high on NuGet, 8 high on npm.**

#### ✅ NuGet — CLOSED and verified

| Package | Was | Severity | Advisory | Fixed by |
|---|---|---|---|---|
| `System.Text.Encodings.Web` | 4.5.0 | **CRITICAL** | GHSA-ghhp-997w-qr28 | parent `Microsoft.AspNetCore.Http.Abstractions` 2.2.0 → **2.3.12** |
| `Npgsql` | 8.0.0 | High | GHSA-x9vc-6hfv-hg8c | `Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.0 → **8.0.11** |
| `Microsoft.Extensions.Caching.Memory` | 8.0.0 | High | GHSA-qj66-m88j-hmgj | `Microsoft.EntityFrameworkCore` 8.0.0 → **8.0.11** |
| `System.Text.Json` | 8.0.0 | High | GHSA-hh2w-p6rv-4g7w | transitive pin **8.0.6** + `Configuration.Json` → 8.0.1 |
| `System.Net.Http` | 4.3.0 | High | GHSA-7jgj-8wvc-jh57 | transitive pin **4.3.4** (test project) |
| `System.Text.RegularExpressions` | 4.3.0 | High | GHSA-cmhx-cq75-c4mj | transitive pin **4.3.1** (test project) |

**Verification:** all five projects report *no vulnerable packages*; `dotnet build -c Release` gives **0
errors / 13 warnings** (the pre-existing `CS8618`/`CS8602` baseline); the unit suite is **3 362 passed,
0 failed**.

⚠️ **`Microsoft.AspNetCore.Http.Abstractions` is on 2.3.x, not 8.0.x, and that is correct** — the package
was retired into the ASP.NET Core shared framework, so 2.3.12 is the newest that exists and is the
maintained security line. The alternative was `<FrameworkReference Include="Microsoft.AspNetCore.App" />`,
which is the modern answer but makes the Clean Architecture Application layer reference the whole web
framework.

⚠️ **EF Core is pinned to 8.0.11, not 8.0.30**, deliberately: the Npgsql provider's latest 8.0.x depends on
EF Relational 8.0.11, and mixing the two produced **56 MSB3277 assembly-unification warnings**. Matching
versions is what keeps the build at its baseline. EF 9 was rejected outright — a major upgrade on a schema
with 100+ migrations is not a security patch.

⚠️ **The two test-project pins are shims, not real dependencies.** xunit 2.5.3 → `NETStandard.Library`
1.6.1 drags in the 4.3.0 versions; net8 provides both types itself so the assemblies are never loaded, but
the advisory is real in the graph and the CI job reads the graph. The cleaner long-term fix is **xunit ≥ 2.9**,
which drops `NETStandard.Library` entirely — a test-harness bump for a 3 300-test suite, deserving its own pass.

#### ✅ npm — CLOSED via the Next 16 upgrade (2026-08-17)

| Package | Was | Severity | Cleared by |
|---|---|---|---|
| `sharp` | < 0.35.0 | High ×4 (libvips CVE-2026-33327 / -33328 / -35590 / -35591) | `next` 15.5 → **16.3.1** |
| `postcss` | ≤ 8.5.22 | High ×4 (XSS via unescaped `</style>`; arbitrary `.map` read) | same |
| `lodash` | ≤ 4.17.23 | High ×3 (code injection via `_.template`; prototype pollution) | `npm audit fix` (via `recharts`) |
| `nanoid` | 4.0.0–5.1.15 | High (non-secure generator loops on negative size) | `npm audit fix` (via `docx`) |
| `@auth0/nextjs-auth0` | 4.12.0–4.17.0 | Moderate (improper proxy cache lookup) | `npm audit fix` |

⚠️ **`sharp` was the one that mattered, and it was a runtime exposure rather than build-only.** `next/image`
is used in six components including `patients/files/file-thumbnail.tsx` and `clinic-settings.tsx`, so Next's
image optimizer passed **uploaded patient files and the clinic logo** through libvips.

⚠️ **My earlier report named only `sharp` and `postcss`, and that was incomplete** — `lodash`, `nanoid` and the
Auth0 advisory were below a truncated `tail` in the first reading. All five are now cleared; **`web` and
`console` both report `found 0 vulnerabilities`.**

**Verification (both apps):** `npx tsc --noEmit` clean · `check:responsive` **17/17** (web) and **14/14**
(console) · clean `next build` · `docker build` succeeds for `web`, `console` **and** `api`.

⚠️ **Still owed: the eye pass** at 320 / 390 / 820 / 1180 / 1440 plus a landscape phone. The mechanical gate is
green, but nothing in `web/` can assert a layout — the manual walk is the load-bearing half of that gate.

**Consequence for CI:** the `dependencies` job is now expected to pass on all three package graphs.

#### Two things the upgrade surfaced

- **`next.config.ts`'s `eslint.ignoreDuringBuilds` is gone.** Next 16 removed the built-in ESLint integration,
  so the key is not a valid `NextConfig` property — it failed `tsc --noEmit`. Deleting it cannot re-enable
  linting during a build, because Next 16 does not run ESLint during one. Corrected in `web/CLAUDE.md` and
  `.claude/rules/frontend-web.md`, both of which asserted the old behaviour.
- **Node 20 is end-of-life (2026-04-30)**, and both Dockerfiles plus all three CI node jobs were pinned to it —
  an unpatched runtime carrying a medical-records app. Next 16 requires `>= 20.9`, which `node:20-alpine`
  (v20.20.2) does satisfy, so this was a **latent** finding rather than a build break. All five sites moved to
  **Node 22 LTS**, verified by rebuilding all three images.

### 3.3c 🔴→✅ The vendor console's Docker image could never be built (found 2026-08-17)

`console/Dockerfile` runs `COPY --from=builder /app/public ./public`, and **`console/public/` has never
existed or been tracked** — so `docker build` failed with « /app/public: not found » at that step, every time.
`deploy/docker-compose.hosted.yml:325` builds the console from that context, which means the vendor console
image was unbuildable on the one deployment kind that serves it.

Invisible to every other layer: `npm run build` succeeds (Next does not need a `public/`), the typecheck and
responsive gate pass, and no CI job builds container images. It only appears if somebody actually runs
`docker build` — which is what verifying the Next 16 upgrade in a container did.

Fixed with a tracked `console/public/.gitkeep` rather than by deleting the `COPY`, so the first static asset
added there still ships. Both images now build.

**npm** (`web/` and `console/`, `--audit-level=high`):

| Package | Severity | Note |
|---|---|---|
| `sharp` (< 0.35.0) | High ×4 | libvips CVE-2026-33327 / -33328 / -35590 / -35591 |
| `postcss` (≤ 8.5.22) | High ×4 | XSS via unescaped `</style>`; arbitrary `.map` file read via `sourceMappingURL` |

⚠️ **`sharp` is a runtime exposure here, not build-only.** `next/image` is used in six components
including `patients/files/file-thumbnail.tsx` and `clinic-settings.tsx`, so Next's image optimizer passes
**uploaded patient files and the clinic logo** through libvips. Treat this as the more urgent of the two.

⚠️ `postcss` processes CSS authored by the team, not user input, so its practical exposure in this app is
close to nil — but it is fixed by the same bump.

⚠️ **The full npm fix requires `next@16.3.1`, a breaking major**, on both `web/` and `console/`. That
needs the whole frontend gate re-run (`tsc --noEmit` · `check:responsive` · `build`) plus an eye pass at
the five viewports — it is its own pass, not a drive-by.

**Consequence for CI:** the `dependencies` job is **red on the next push** until these land. That is
deliberate — the alternative was tuning the threshold until it passed, which is the
« present and inert » failure this repository names elsewhere.

### 3.4 🟠 The key-ring certificate is self-signed, laptop-generated, and uncustodied

Generated on a developer machine into `clinic-keys/` (2036 expiry). `deploy/KEY-CUSTODY.md`'s custody table is
still `_(name, role)_` placeholders for all five keys. That file states it is a **deliverable, not a note**: "If
it is not filled in with real names and real locations before the deployment carries a real practice's records,
the deployment is not ready."

Losing key 1 makes **every 2FA secret and every clinic's reminder credentials permanently unreadable**. Keys 2
and 3 must not be stored with what they encrypt.

### 3.5 🟠 Enforcing CSP is not XSS protection, and the flag's name oversells it

`script-src` carries `'unsafe-inline'`, so inline `<script>` and `javascript:` handlers are permitted. The code
states this plainly at `SecurityHeadersMiddleware.cs:60-67`: turning the key on constrains resource **origins**;
it does not stop XSS in a product that renders free-text clinical notes and patient names. Getting there needs
Next's nonce/hash support with `strict-dynamic` — a separate change with its own page walk.

Mitigating: zero XSS sinks exist in the codebase today (§ 2.7), and React escapes by default.

### 3.6 🟠 Data residency is a legal blocker, not a preference

Under *loi organique 2004-63* art. 51–52 a transfer of health data abroad needs prior **INPDP** authorization, and
art. 90's exposure falls on the **cabinet**, not on the vendor. Render is not in Tunisia. `DataResidencyAssurance`
already refuses to boot without a declared allow-list, but declaring a foreign host satisfies the code and not the
law. `deploy/README.md` § « Résidence des données » carries the reasoning and a provider shortlist.

Budget the **annual ANCS cybersecurity audit** (`Décret-loi 2023-17`) — it applies wherever you host.

### 3.7 🟡 Sidecar secrets still arrive as environment variables

`POSTGRES_PASSWORD` and `MINIO_ROOT_PASSWORD` are shared with `postgres`, `minio`, `backup` and `pitr` — non-.NET
containers with their own per-image mechanisms (`POSTGRES_PASSWORD_FILE`, `MINIO_ROOT_PASSWORD_FILE`,
`PGPASSFILE`). ⚠️ **wal-g has no `_FILE` convention at all** and needs a wrapper entrypoint or a stated exception.

Deliberately deferred rather than half-done: moving only the API's copy would leave the same password in three
other containers' environments *while the compose file implied it had left* — converting a visible gap into an
invisible one. Full plan at `follow-up/hosted-secrets-to-files.md`.

### 3.8 🟡 Standing items with no owner yet

- **No database-backed integration tests.** Tenant isolation is verified against mocks plus `verify-schema`; the
  unit suite (2 825 tests) is the backend's only automated check. Six of the archive-restore guards are SQL and
  are structurally invisible to it — `follow-up/archive-restore-real-database-checks.md`.
- **No dynamic testing, no penetration test, no dependency-vulnerability scan** has ever been run.
- **SignalR hub methods run with no tenant scope.** Safe today only because `ClinicHub` reads `User`, which is
  unfiltered; the next hub method that reads a filtered entity must set a scope explicitly.
- **`CloudBrowser` keeps a null authorization `FallbackPolicy`** — a controller without `[Authorize]` there is
  still anonymous. Local fails closed; hosted profiles are covered because every controller now carries an
  explicit policy, held by `ControllerAuthorizationCoverageTests`.
- **Meta app credentials do not exist on any deployment**, so the WhatsApp template has never been submitted, the
  signed webhook never called, and `X-Hub-Signature-256` verification never exercised against real traffic.

---

## 4. Verified clean

Checked and found sound, with no findings — see § 2.7 for anchors.

SQL injection · command injection · path traversal · multi-tenant isolation · TLS validation in all four clients ·
secrets in the working tree · CSPRNG usage for every security token · JWT mechanics (`ClockSkew = Zero`, distinct
audiences, no default signing key) · Google OAuth CSRF · XSS sinks · role self-assignment on every write path.

---

## 5. ⚠️ Two documents in this repository are stale

Both **predate** `features/hosted-security-hardening/`, which landed 2026-08-12→14.

| Document | Last touched | Stale claim |
|---|---|---|
| `GO-LIVE.md` | 2026-08-07 (`d4515d9`) | **`:257` "No MFA on staff accounts"** — false. Admin TOTP shipped with Part A. |
| `GO-LIVE.md` | | **`:167` "It ships report-only"** — false. `Security__EnforceCsp: "true"` is in both hosted compose files (`docker-compose.hosted.yml:182`, `docker-compose.prod.yml:190`). |
| `SECURITY_REVIEW_2026-08.md` | 2026-08-08 (`cf903f1`) | **`:263`** defers "CSP still report-only, no MFA, no restore drill" to `GO-LIVE.md`. The first two are now done; only the restore drill stands. |

Everything else in both files still holds — in particular `SECURITY_REVIEW_2026-08.md`'s Fix #4 (§ 3.1 above) and
its "Refuted — do not re-raise" table.

---

## 6. One-line summary per layer

| Layer | State |
|---|---|
| **Identity** | Admin TOTP + PBKDF2 + session-replay detection + DB-resolved role. **Strong.** |
| **Transit (public)** | TLS + HSTS + full header set. **Strong.** |
| **Transit (internal)** | Encrypted everywhere; **identity verification deferred on Render only**, logged every boot. **Good, one known reduction.** |
| **At rest — secrets** | Key ring certificate-encrypted; 5 protector classes; fail-refuse never fail-open. **Strong.** |
| **At rest — PHI** | Volume-level (LUKS) only; **no column encryption, by documented decision.** **Adequate, understood.** |
| **Tenancy** | 31 fail-closed query filters + DB-resolved scope + derived guard tests. **Strong.** |
| **Evidence** | HMAC-SHA256 audit chain with an off-database key + attributable exports. **Strong.** |
| **Browser** | Full header set; **CSP constrains origins, not scripts.** **Partial — honestly documented.** |
| **Input** | No injection sinks found in any class. **Strong.** |
| **Operations** | **Weakest link:** unrotated history credentials, no restore drill, uncustodied keys, un-gitignored secret paths. |
