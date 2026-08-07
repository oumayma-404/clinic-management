# Feature Review: multi-tenant-cloud

**Status:** COMPLETE
**Challenged:** Yes
**Date:** 2026-08-06
**Challenged Date:** 2026-08-06
**Branch:** `feature/audit-sections-3-to-10`
**Parent Branch:** `main`
**Merge Base:** `9798b95d31f55ee07f2ad5e0af5550c4c2831022`

## Challenge Summary

| Metric | Count |
|--------|-------|
| Original findings | 42 |
| Confirmed | 36 |
| Confirmed (adjusted) | 6 |
| Dismissed (false positive) | 0 |
| Dismissed (pre-existing) | 0 |
| **Final findings** | **42** |

**Nothing was dismissed**, and that is worth stating rather than glossing: every finding's file was opened, 30+
lines of context read around the cited line, and **every precedent claim the review made was checked against the
cited file verbatim**. All of them held — `ClientIp`'s loopback-only trust rule, the absent
`UseForwardedHeaders` (zero occurrences outside docstrings), the absent `Auth__Local__SigningKey` (zero matches
under `deploy/`), `services.AddSingleton(profile)` at `Extensions.cs:30`, `select.tsx:42`'s `w-fit`,
`app/join/page.tsx:189–198`'s own annotation of the identical clipping structure, `LoginAttemptTracker`'s
`MaxAttemptsPerSource = 5`, the twelve positional `new DataMigrationCounts(...)` calls, and the fourteen
`DeploymentProfile` bool capabilities against `CLAUDE.md`'s « 13 ». Six severities were corrected.

⚠️ **Findings keep their original numbers and original order**, because nine of them cross-reference each other by
number (« see Finding 6 », « Compounded by Finding 1 »). The numbering is already sequential 1–42 with no gaps —
nothing was removed — but since six severities changed **in place**, the file is no longer strictly
severity-sorted. Read the `Severity` line, not the position.

## Scoping (auditable)

The branch is a long-lived one carrying several features; the full merge-base diff is **1 690 files / +389 489**,
almost none of it this feature. The reviewable diff was therefore scoped to **this feature's own six commits**,
which are *non-contiguous* (interleaved with `mobile-native-shells` and a test-baseline commit):

| Part | Plan | Commit | Scoped stat (code only) |
|---|---|---|---|
| A | US-1 | `a4a336e` | 18 files, +737 / −140 |
| B | US-2 | `7f3760e` | 43 files, +1 301 / −189 |
| C | US-3 | `65a72e6` | 21 files, +1 762 / −91 |
| D | US-4 | `832ee58` | 26 files, +945 / −78 |
| E | US-5 | `18f8a6c` | 18 files, +335 / −54 |
| F | US-6 | `b06cdee` | 40 files, +2 495 / −76 |

**Reviewed:** ~7 575 added lines across `api/`, `deploy/` and `web/` (src 6 704 diff lines · tests 4 644 · web 593).
**Excluded:** `features/**` (the feature's own docs), all `*.md`, `*.Designer.cs`, `ApplicationDbContextModelSnapshot.cs`
and lock files. The excluded EF artifacts were **read directly from the repo** for the migration-verification mandate
rather than skipped.

⚠️ **One capability referenced by Part D is not in Part D's commit.** `DeploymentProfile`'s 14th capability
`SharesInstallWideTtnIdentity` was swept into a parallel session's commit `999b877` (recorded in `progress.md`), so it
was read from the current file rather than the diff.

## Review method

Six agents, not the skill's default four. The default set was adapted to this stack:

- **Agent 2's ROP mandate was replaced** — this repo has no `Extensions.ROP`; its idiom is MediatR + `Result<T>` with a
  canonical `{ error }` body, so the agent was repointed at that plus CQRS placement and post-commit best-effort rules.
- **A dedicated Security agent was added** — the feature *is* a tenant-isolation, secrets and hosting change.
- **Agent 5 (Device & UX) was required** — Part C touches four `.tsx` files.
- Each agent was given the story's Out-of-Scope list so settled decisions were not re-raised.

**Three findings came from orchestrator-level cross-boundary tracing that no diff-scoped agent could perform** —
findings 2, 26 and 40 (code ↔ `docker-compose` ↔ `.env`, and a server DTO ↔ TypeScript type). Finding 2 was
independently reproduced by the Breaking Changes agent, which raises confidence rather than duplicating it.

**Convergence is recorded per finding.** Nine findings were raised by 2–3 agents independently; those are marked.

---

## Findings

### Finding 1
- **Severity:** Critical
- **Category:** Security / Breaking Change
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.API/Startup/RateLimiting.cs`
- **Line:** 141 (and 217)
- **Anchor:** `RateLimiting.AddConfiguredRateLimiter` (global `authip:` branch) / `RateLimiting.AuthAttemptPartitionKey`
- **Raised by:** Security, Breaking Changes, Business Logic (**3 agents**); trust rule verified directly against source
- **Comment:** Every address-keyed rate-limit partition collapses to **one bucket for the entire multi-tenant service**.
  `ClientIp.Resolve` (`Infrastructure/ClientIp.cs:49`) honours `X-Forwarded-For` **only when the immediate TCP peer is
  loopback** — its docstring says "our own front door or BFF", which was true in `SelfHostedLan` where Next runs on
  loopback. In `deploy/docker-compose.hosted.yml` it is false on both paths: browser traffic arrives from the `caddy`
  container and BFF traffic from the `web` container (`API_INTERNAL_URL: http://api:5000/api`), both Docker-bridge
  peers. There is no `UseForwardedHeaders` anywhere in the solution (deliberately — see `ClientIp`'s docstring).
  Two consequences, both live on first deploy: **(a)** `POST /api/auth/refresh` carries no email, so
  `AuthAttemptPartitionKey` falls back to `ip:{web-container}` — a single **30 permits / 5 min** bucket shared by every
  sliding-session refresh of every clinic, so ordinary load 429s and staff read it as a dead session; **(b)** the new
  per-address ceiling (150 / 5 min) is one deployment-wide bucket an **unauthenticated** caller can exhaust, 429-ing
  every clinic out of logging in for the rest of the window. This is the "whole practice behind one NAT address"
  lockout US-6 set out to remove, reproduced one layer up and at service scale.
  **Fix:** attribute the rate-limit address through a trusted-proxy list (a configured `KnownProxies`/bridge CIDR)
  rather than a hard-coded loopback test, keeping `LocalRequest.IsLoopback` on the raw `Connection.RemoteIpAddress` so
  the `setup` and `/hangfire` gates stay structural. Add a test that a non-loopback-but-trusted peer yields distinct
  partitions.
- **Challenge verification:** `ClientIp.cs:47–58` reads exactly as described (loopback peer ⇒ trust XFF, else the raw
  peer). `web/app/bff/auth/token/route.ts:38` **does** forward `forwardedForHeader(request)`, which makes the defect
  sharper rather than milder: the header is sent and then ignored, because the BFF's own peer address is the `web`
  container. Repo-wide grep for `UseForwardedHeaders` returns only docstrings explaining its absence.

### Finding 2
- **Severity:** Major
- **Category:** Breaking Change
- **Verdict:** Confirmed
- **File:** `deploy/docker-compose.hosted.yml`
- **Line:** 69
- **Anchor:** `services.api.volumes`
- **Raised by:** orchestrator cross-boundary trace + Breaking Changes agent (independently)
- **Comment:** The hosted API container gets **no `Auth__Local__SigningKey` and no durable volume for its install
  directory**. `HostedMultiTenant` sets `UsesLocalAccounts: true`, so `Program.cs:256` validates bearer tokens with
  `LocalAuthConfig.SecurityKey`, which falls through to generating a 512-bit key into
  `{AppContext.BaseDirectory}/.local/signing-key` = `/app/.local/signing-key` on the container's **ephemeral layer**
  (`api/Dockerfile` sets no `USER`, so the write succeeds and the failure is silent). Confirmed: zero matches for
  `SigningKey|signing-key|Auth__Local` anywhere under `deploy/`, and no `Auth:Local:SigningKey` in any committed
  `appsettings*.json`.
  **(a)** Every `docker compose up -d --build` — the command this file's own header prescribes — mints a new key,
  invalidating every access token *and* every 12-hour HttpOnly refresh cookie across every clinic, so a routine
  redeploy signs the whole fleet out mid-shift with nothing in any log naming the cause. **(b)** Scaling `api` past one
  replica breaks authentication outright (a token minted by one replica fails signature validation on the other) —
  and `MigrationLock` was added in this very part *because* "a scaled deployment, or simply a redeploy before the old
  container exits" is contemplated.
  ⚠️ This is precisely the failure US-6 step 17 closed for the Data Protection ring (required `KeyRingPath` + the
  `dataprotection_keys` volume), applied to one of the two per-install secrets in the container and not the other —
  the repo's own `fixes-dont-propagate` shape. The story's R-6 note even states the pattern: *"a key ring with no
  volume works until the first redeploy."* It was invisible because the hosted compose was derived from the
  `CloudBrowser` one, where Auth0 issues the tokens and no signing key exists.
  **Fix:** set `Auth__Local__SigningKey` from `.env` (base64, ≥32 bytes) or mount a named volume at `/app/.local`;
  document it in `.env.hosted.example` beside the existing key-ring warning, and in `deploy/README.md` (which has
  **zero** mentions of "signing").
- **Challenge verification:** `LocalAuthConfig.LoadSigningKey` (lines 88–142) confirms the three-step cascade ending in
  `RandomNumberGenerator.GetBytes(64)` written to `LocalInstallPaths.LocalFile("signing-key")`. The api service's only
  volume is `dataprotection_keys:/keys`; `api/Dockerfile` declares no `USER`.

### Finding 3
- **Severity:** Major
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Infrastructure/Services/TtnIdentityProvider.cs`
- **Line:** 53
- **Anchor:** `TtnIdentityProvider.ResolveAsync`
- **Raised by:** Business Logic
- **Comment:** Precedence is decided **solely** by `TtnCertificateKey`, but `Clinic.SetTtnIdentity` deliberately permits
  the other half-identity — its own docstring says "a certificate with no TTN account, **or a TTN account with no
  certificate**, is legal — the signing half and the submitting half are provisioned separately." A clinic given its own
  `TtnUsername`/`TtnApiSecretEncrypted` whose PFX has not been uploaded yet takes the `else` branch into
  `ResolveInstallIdentity`, which returns the install's certificate **and the install's `Ttn:Username`/`Ttn:ApiSecret`** —
  silently discarding the clinic's own TTN account and **filing the declaration under the install-wide matricule**.
  `verify-schema`'s `ttn-identity-is-complete` does not catch it (it flags only secret-without-username and
  password-without-certificate). This is the exact "signed as clinic A, filed under clinic B" state Part D exists to make
  unreachable: the certificate half is protected ("a clinic that HAS a certificate never silently falls back") and the
  credentials half is not.
  **Fix:** `ResolveInstallIdentity` should refuse — or at minimum never override the clinic's own credentials — when the
  clinic carries **any** of the four columns, not only a certificate key.
- **Challenge verification:** `Clinic.SetTtnIdentity` (Domain/Entities/Clinic.cs:256–278) throws only on
  secret-without-username and password-without-certificate, so `TtnUsername` + `TtnApiSecretEncrypted` with a null
  `TtnCertificateKey` is a legal, storable state — and `ResolveAsync:53` sends it straight to
  `ResolveInstallIdentity`, which returns `TtnConfig.Username`/`ApiSecret`.

### Finding 4
- **Severity:** Major
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Application/Features/Users/Commands/CreateClinicUserCommand.cs`
- **Line:** 106
- **Anchor:** `CreateClinicUserCommandHandler.Handle`
- **Raised by:** Business Logic
- **Comment:** The command accepts `Role = "doctor"` and creates only a `User` row — no `Doctor` entity, no `DoctorInfo`
  required, no `doctor.LinkToUser(user.Id)`. Both sibling paths for the same operation do create and link one
  (`JoinClinicCommand.RegisterLocalUserAsync` *requires* `DoctorInfo` for the doctor role; so does
  `LocalClinicProvisioning`). In `HostedMultiTenant` this command is **the only way to add staff** —
  `AllowsSelfRegistration` is false and `register` 404s — so every dentist added after `provision-clinic` has no
  `Doctor` record. Consequences: they never appear in the practitioner roster; « Mon profil » has nothing to edit;
  `PractitionerAttribution.Resolve`'s caller fall-back yields `null`, so their invoices, plans and fiches are
  **unattributed**; and `PractitionerRenderSnapshot` resolves no cachet and no n° d'ordre CNOMDT, so their certificats
  and ordonnances **print with no practitioner identity** — the identical silent defect
  `adoption-qa-i-access-control-and-audit` had to fix for reception.
  **Fix:** require `DoctorInfo` and create the linked `Doctor` when `role == doctor` (mirroring `JoinClinicCommand`), or
  refuse the `doctor` role here and say so in the refusal.
- **Challenge verification:** The handler's only write is `User.CreateLocalUser` + `AddAsync` (lines 106–115); no
  `IDoctorRepository` is even injected. Both cited siblings verified: `JoinClinicCommand.cs:208–217` refuses a doctor
  with no `DoctorInfo` and lines 243–255 construct + `LinkToUser`; `LocalClinicProvisioning.cs:122–134` does the same.

### Finding 5
- **Severity:** Major
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.API/BackgroundJobs/DocumentEmailJob.cs`
- **Line:** 84
- **Anchor:** `DocumentEmailJob.DispatchQueuedEmails` / `DispatchOneAsync`
- **Raised by:** Business Logic
- **Comment:** Part B made this job cross-clinic (`UseSystemWide`) without giving it the fairness bound its sibling
  already has. `GetQueuedAsync(batchSize)` is `Status == Queued`, **oldest-first, `Take(20)`, with no per-clinic cap**,
  and `DispatchOneAsync` deliberately returns a row to the queue *without consuming an attempt* when the clinic's SMTP
  is unconfigured. Those two decisions together are exactly the starvation L3 diagnosed and fixed in the reminder
  outbox: unsendable rows accumulate at the **front** of the scan and, past 20, consume every minutely tick forever —
  so one clinic that never configures SMTP stops « Envoyer par email » for **every** clinic, while the job logs a clean
  run. `NotificationRepository.GetDueForDispatchAsync` already carries both halves of the fix (the non-terminal
  `Blocked` status and a per-clinic bound served oldest-due-first); neither was carried across. Part F's
  `/api/outbox` cannot distinguish it either — `DocumentEmailOutboxDepth` has no blocked figure, so a growing `Queued`
  with an ancient `OldestQueuedUtc` reads identically to R-1's "the dispatcher is not running".
  **Fix:** add the per-clinic bound and a non-terminal blocked state, mirroring `NotificationRepository`.
- **Challenge verification:** `DocumentEmailRepository.GetQueuedAsync` (lines 32–39) is exactly
  `Where(Status == Queued).OrderBy(QueuedAt).ThenBy(Id).Take(batchSize)` — no clinic dimension. The two
  return-without-consuming paths are `DocumentEmailJob.cs:112` (settings not configured) and `:145`
  (`NotConfigured` from the sender). `DocumentEmailOutboxDepth` carries only `Queued`/`Failed`/`OldestQueuedAt`.

### Finding 6
- **Severity:** Major
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Infrastructure/Services/EInvoiceService.cs`
- **Line:** 108
- **Anchor:** `EInvoiceService.ProcessAsync` (`catch (InvalidOperationException)`) / `DispatchAsync`
- **Raised by:** Business Logic
- **Comment:** The refusal `TtnIdentityProvider` raises for "this clinic has no certificate of its own" is routed into
  `RecordTransientFailure`, and `Invoice.RecordEInvoiceFailure` sets `EInvoiceStatus = Failed` with
  `EInvoiceNextAttemptAt = null` once `EInvoiceAttemptCount >= maxAttempts` (default 5). So the contract stated in
  three places — `DeploymentProfile.SharesInstallWideTtnIdentity`, `ITtnIdentityProvider`, and the story's Part D
  acceptance criterion ("the invoice stays `Queued`, and the backlog shows in `GET /api/outbox`") — holds only for
  roughly the first ten minutes; after that the note leaves the outbox **permanently** and needs a manual
  `QueueForElFatoora()`. A missing qualified certificate is a *configuration* state lasting days, not a transient
  network error, and burning a bounded retry budget against it is precisely the defect L3 invented
  `NotificationStatus.Blocked` for — not propagated here even though US-4 makes "clinic has no certificate yet" the
  normal state of every newly provisioned hosted clinic.
  **Fix:** park the row on an identity refusal without consuming an attempt (or give the e-invoice outbox a
  non-terminal blocked state), so a retry works the moment the operator uploads the PFX.
- **Challenge verification:** `Invoice.RecordEInvoiceFailure` (Domain/Entities/Invoice.cs:538–554) sets
  `EInvoiceStatus = Failed; EInvoiceNextAttemptAt = null` at `EInvoiceAttemptCount >= maxAttempts`, and
  `EInvoiceService.cs:108–116` routes every `InvalidOperationException` — including the provider's identity refusal —
  through `RecordTransientFailure`.

### Finding 7
- **Severity:** Major
- **Category:** Breaking Change
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Infrastructure/Services/TtnIdentityProvider.cs`
- **Line:** 78
- **Anchor:** `TtnIdentityProvider.ResolveInstallIdentity`
- **Raised by:** Breaking Changes
- **Comment:** ⚠️ *Both halves of this were individually approved; the finding is their composition.* DEV-19 removes the
  per-install fall-back from `CloudBrowser`, and Part D's scope deliberately ships **no write path** for the four
  `Clinic.Ttn*` columns. Together they mean every El Fatoora dispatch in an **already-shipped** `CloudBrowser`
  deployment stops permanently the moment this lands: previously `XadesEInvoiceSigner` read the per-install
  `.local/teif-signing.pfx` and both Sandbox and Production worked; now `DispatchAsync` resolves the identity *before*
  choosing the client, so even a `Sandbox` clinic (the default `TtnEnvironment`) is refused. Invoices accumulate
  `Queued`, retry and fail every minute (see Finding 6), and the only remedy is direct SQL against `Clinics` plus a
  hand-placed blob — no endpoint, no console verb, no admin screen. This is a real exception to R-2's "the two
  already-shipped profiles behave byte-for-byte as before", and it is unrecoverable in-product.
  **Fix:** ship an operator-facing provisioning path in the same release (a `provision-clinic`-style verb accepting a
  PFX would suffice), and name the migration step for existing `CloudBrowser` deployments in `deploy/README.md`.
- **Challenge verification:** `EInvoiceService.DispatchAsync` (line 198) resolves the identity, and
  `ResolveClient(clinic.TtnEnvironment)` is not reached until line 207 — so the sandbox path is refused too. The story
  itself records DEV-19 as « asked and approved » and Part D as shipping **no write path**, so this is the
  composition of two approved decisions and not a divergence from either.

### Finding 8
- **Severity:** Major
- **Category:** Security
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.API/Startup/RateLimiting.cs`
- **Line:** 217
- **Anchor:** `RateLimiting.AuthAttemptPartitionKey`
- **Raised by:** Security, Business Logic (**2 agents**)
- **Comment:** Keying the tight auth window on the submitted account **alone** introduces a targeted-lockout primitive
  the per-address version did not have. The limiter is consumed **before** authentication, on every attempt regardless
  of outcome, and `RateLimitingTests.The_same_account_shares_one_partition_regardless_of_address` pins exactly that. So
  anyone who knows a staff email — they are printed on ordonnances, certificats and invoices, and `provision-clinic`
  prints the owner's — can send 30 `POST /api/auth/login` bodies naming it every 5 minutes (6 req/min, under both the
  address ceiling and `ILoginAttemptTracker`'s per-account limit of 5) and that account is 429'd
  **indefinitely, from any device, with the correct password**. Locking out a clinic's only admin also removes the
  account that can create staff, reset passwords and read `/api/outbox`. DEV-14 reasons about the compound
  `account+address` key and rejects it for handing an attacker a fresh budget per address — but does not consider that
  the pure-account key hands the budget to whoever *names* the account. One bound was traded for another rather than
  added.
  **Fix:** make the account dimension a penalty on **failed** authentication (the handler knows the outcome and
  `ILoginAttemptTracker` already exists), or use three bounds: tight per (account, address), a moderate per-account
  ceiling well above one person's mistyping, and the per-address ceiling already added.
- **⚠️ Note for `/apply-review-fixes` — do not revert the re-key.** Keying the tight window on the submitted account is
  **mandated by the story** (Part F step 17: « re-key `AnonymousAuthPolicy` on the submitted email (+ address as a
  second dimension) », and DEV-14's reasoning). This finding is kept at full severity because it exposes a
  consequence the spec did **not** consider — a named-account lockout by an unauthenticated caller — not because the
  design is wrong. The fix must **add** a bound (or move the account dimension onto *failed* attempts); returning to
  per-address-only would re-open the NAT lockout US-6 exists to close.
- **Challenge verification:** `RateLimitingTests.cs:153` carries that test name. `LoginAttemptTracker.cs:26` is
  `MaxAttemptsPerSource = 5`, well above the 6 req/min the attack needs. `[EnableRateLimiting(AnonymousAuthPolicy)]`
  sits on the `login` action (`AuthController.cs:69`), so the permit is spent before the handler runs.

### Finding 9
- **Severity:** Major
- **Category:** Security
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.API/Middleware/SecurityHeadersMiddleware.cs`
- **Line:** 97
- **Anchor:** `SecurityHeadersMiddleware.InvokeAsync` — HSTS block
- **Raised by:** Security; **verified directly against source by the orchestrator**
- **Comment:** `Strict-Transport-Security` is **never emitted in either hosted profile**, despite this commit's own
  docstring asserting "a deployment served over a publicly-trusted certificate gets HSTS on" (DEV-3). The guard is
  `if (_hstsEnabled && context.Request.IsHttps)`. Caddy terminates TLS and speaks plain HTTP to `api:5000`
  (`ASPNETCORE_URLS: http://+:5000`), and nothing in the solution consumes `X-Forwarded-Proto` — `UseForwardedHeaders`
  is deliberately absent (confirmed: the only three matches in the repo are docstrings explaining its absence). So
  `IsHttps` is `false` for every request behind the proxy and the header is skipped, while `deploy/Caddyfile` does not
  set it either. A silently inert transport control on an internet-facing medical-records service, with the config
  claiming it is on.
  **Fix:** add `Strict-Transport-Security "max-age=31536000; includeSubDomains"` to the Caddyfile's page block (where
  TLS actually terminates) and/or gate the middleware on the effective scheme via a trusted-proxy `X-Forwarded-Proto`.
- **Challenge verification:** `_hstsEnabled` (line 66) is `true` in both hosted profiles (`SelfSignsCertificate` is
  false there), so the guard that fails is `context.Request.IsHttps`. `deploy/Caddyfile` sets exactly three headers
  (lines 50–52) and no HSTS. `docker-compose.hosted.yml:44` is `ASPNETCORE_URLS: http://+:5000`.

### Finding 10
- **Severity:** Major
- **Category:** Security
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Infrastructure/Security/LocalDataProtection.cs`
- **Line:** 95
- **Anchor:** `LocalDataProtection.AddConfiguredDataProtection`
- **Raised by:** Security; **verified directly against source by the orchestrator**
- **Comment:** In `HostedMultiTenant` the key ring is persisted to `/keys` with **no at-rest protection**:
  `PersistKeysToFileSystem` disables the framework's automatic key encryption (the comment says so), and the only
  re-protection branch is `if (profile.RunsAsWindowsService && OperatingSystem.IsWindows())`, false for this profile.
  Since US-4 those master keys decrypt every clinic's TTN OAuth secret **and every clinic's qualified
  signing-certificate PFX password**, plus all reminder credentials.
  ⚠️ The cleartext-on-disk half is a *stated* decision ("relies on that directory's ACLs (ops responsibility)"). **The
  new problem is the backup guidance**: `deploy/.env.hosted.example:71` and `docker-compose.hosted.yml:150` instruct the
  operator to back this volume up "alongside `postgres_data`", so a single off-site archive contains both the ciphertext
  and the key that opens it — the encryption then provides no protection against the most likely exposure, a leaked or
  stolen backup. Anyone with that archive can forge auth cookies and impersonate any clinic's e-invoicing identity.
  **Fix:** at minimum change the backup guidance so key material is stored separately from the database dump; better,
  `ProtectKeysWithCertificate` (cert supplied out-of-band via env/secret mount) or an external KMS in the hosted profile.
- **Challenge verification:** Both cited lines read as described — `.env.hosted.example:69–71` (« back it up
  alongside postgres_data ») and `docker-compose.hosted.yml:150–153` (« as load-bearing as postgres_data and belongs
  in the same backup policy »). The only `ProtectKeysWith*` call is DPAPI at line 104, gated on `RunsAsWindowsService`.

### Finding 11
- **Severity:** Major
- **Category:** Security
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.API/Startup/HealthChecks.cs`
- **Line:** 54
- **Anchor:** `HealthChecks.Register`
- **Raised by:** Security
- **Comment:** `/health` is anonymous, publicly routed (`deploy/Caddyfile:39`), **explicitly exempt from the global
  limiter** (`RateLimiting.ExemptPathPrefixes` now lists `HealthChecks.Path`), and does real backend work on **every**
  request with no caching: `DatabaseHealthCheck` runs `SELECT 1` on a pooled Npgsql connection and
  `FileStorageHealthCheck` makes a MinIO `BucketExists` round trip (on disk, a file write + delete). An unauthenticated
  caller can drive one DB query and one object-store call per request at any rate against the shared datastore of every
  tenant; a few thousand concurrent requests exhaust the Npgsql pool (default max 100) and starve real traffic — and the
  framework then 503s the probe, which reads to an orchestrator as "unhealthy" and can trigger restarts, turning the
  flood into an outage.
  **Fix:** cache the health report for 5–10 s (`MemoryCache` or a cached publisher) so backend cost is bounded
  regardless of request rate, and/or give `/health` a loose dedicated limiter partition sized above any realistic probe
  interval.
- **Challenge verification:** `HealthChecks.Register` maps with only a `ResponseWriter` and `.AllowAnonymous()` — no
  caching, no dedicated limiter. `RateLimiting.ExemptPathPrefixes` (line 73) lists `HealthChecks.Path` explicitly.
  The anonymous-and-unlimited part is spec-mandated (Part F step 17); the **absence of any bound on backend cost** is
  not something the spec addressed, so this is not a spec-accepted trade-off.

### Finding 12
- **Severity:** Major
- **Category:** Code Quality / Security
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Application/Common/Models/EInvoiceModels.cs`
- **Line:** 76
- **Anchor:** `ResolvedTtnIdentity`
- **Raised by:** Code Quality, Security (**2 agents**)
- **Comment:** `ResolvedTtnIdentity` is a **positional `record`**, so the compiler synthesises `PrintMembers`/`ToString()`
  printing every public member — including `CertificatePassword` (the decrypted PFX password) and `ApiSecret` (the
  decrypted TTN client secret), both plaintext by the time this object exists. Any future
  `_logger.LogDebug("…{Identity}", identity)`, a `{@Identity}` destructuring template, or an exception message
  interpolating it writes a clinic's qualified-signing-certificate password into the Serilog file — one line away in
  `EInvoiceService.DispatchAsync` and `HttpTtnClient.AcquireTokenAsync`, both of which already log around these values.
  This is the one type in the solution deliberately built to carry credentials.
  **Fix:** suppress the generated printer — `private bool PrintMembers(StringBuilder b)` emitting only `Source` and a
  redacted marker — or make it a class with an explicit redacting `ToString()`.
  `LocalClinicRequest` (`Features/Clinics/LocalClinicProvisioning.cs:21`) has the same shape with `PasswordHash` as a
  positional member and needs the same treatment.
- **Challenge verification:** `EInvoiceModels.cs:76–86` is `public sealed record ResolvedTtnIdentity(byte[]
  CertificateBytes, string? CertificatePassword, string? Username, string? ApiSecret, TtnIdentitySource Source)` with
  no `PrintMembers` override. `LocalClinicProvisioning.cs:21–32` is `public sealed record LocalClinicRequest(Guid
  ClinicId, string? Name, string? AdminEmail, string PasswordHash, …)` — same shape, same exposure.

### Finding 13
- **Severity:** Major
- **Category:** Error Handling / Breaking Change
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.API/Startup/MigrationLock.cs`
- **Line:** 59
- **Anchor:** `MigrationLock.RunExclusivelyAsync`
- **Raised by:** Error Handling, Code Quality, Breaking Changes (**3 agents**)
- **Comment:** `pg_advisory_lock` blocks, but it is issued through `ExecuteSqlRawAsync` with **no `CommandTimeout`
  override** — `Extensions.cs` calls `UseNpgsql(connectionString)` with no timeout and the hosted connection string
  carries no `Command Timeout`, so Npgsql's **30 s** default applies. The class's contract is "Blocking on purpose. The
  loser waits rather than skipping" — in practice the loser waits 30 seconds, the acquire throws, the exception escapes
  into `Program.cs`'s fatal-rethrow and the container exits non-zero. That is exactly the scenario the lock exists for
  (a rolling redeploy or a scaled `api` where the winner's migrate-and-backfill exceeds 30 s — and a fresh-DB migration
  exceeding 30 s is the documented reason `DeferredStartupService` exists at all). Under `restart: unless-stopped` the
  loser crash-loops, which looks like a broken deploy rather than a serialised one. `MigrationLockTests` cannot see it
  because it asserts only on the SQL strings.
  **Fix:** `database.SetCommandTimeout(0)` scoped around the acquire (restoring it in the outer `finally`), or poll
  `pg_try_advisory_lock` with a delay and a log line per iteration so the wait is both unbounded and observable. Give
  `MigrateAsync` the same consideration.
- **Challenge verification:** `Extensions.cs:68` is a bare `.UseNpgsql(connectionString)`; the hosted connection
  string (`docker-compose.hosted.yml:50`) sets no `Command Timeout`. `MigrationLock.cs:54–75` sets no command timeout
  anywhere. The eventual self-heal (the loser restarts and finds the lock free) bounds the blast radius to a noisy
  crash-loop during a slow migration rather than a permanent break — but the class's stated contract is broken and, in
  a datacentre with nobody at the console, a crash-looping container is indistinguishable from a failed deploy.

### Finding 14
- **Severity:** Minor
- **Category:** Error Handling
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** `api/ClinicManagement.API/Startup/MigrationLock.cs`
- **Line:** 69
- **Anchor:** `MigrationLock.RunExclusivelyAsync`
- **Raised by:** Error Handling (Major), Code Quality (Minor) (**2 agents**)
- **Comment:** The release (`finally`, line 69) and the close (outer `finally`, line 74) are unguarded `await`s inside
  `finally` blocks. When `work()` fails **and the same cause broke the connection** (a dropped connection
  mid-`MigrateAsync`, a server-side termination), `ExecuteSqlRawAsync(ReleaseSql, …)` throws from the `finally` and
  **replaces** the migration exception. The operator then sees `NpgsqlException: connection is broken` instead of
  `column "X" already exists` — losing the one diagnosis that makes a failed startup actionable, on the exact path this
  class was added to protect. The class's own docstring already concedes the release is "not strictly required —
  closing the connection releases it".
  **Fix:** wrap both in `try { … } catch (Exception ex) { logger.LogWarning(ex, "Could not release/close the startup
  migration lock; the session's end releases it."); }` so the original failure propagates.
- **Challenge note:** Severity lowered Major → Minor. The masking is real but requires a **compound** condition: in the
  ordinary migration failure (a `PostgresException` — duplicate index, existing column — on a still-healthy session)
  the release succeeds and the original exception propagates intact. The review's "frequently" overstates how often the
  connection is also broken. The unguarded-await-in-`finally` anti-pattern is still worth fixing, and the fix is three
  lines.

### Finding 15
- **Severity:** Major
- **Category:** Error Handling / Code Quality
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Application/Features/Outbox/Queries/GetOutboxDepthQuery.cs`
- **Line:** 128
- **Anchor:** `GetOutboxDepthQueryHandler.Handle`
- **Raised by:** Error Handling, Code Quality, Business Logic (**3 agents**)
- **Comment:** The catch-all binds `ex` and discards it — no `ILogger` is injected into this handler and nothing is
  written anywhere before returning the generic French message. The irony is load-bearing: this endpoint exists because
  "a job with no tenant scope reads nothing and logs a clean run", and as written a broken repository read (a missing
  index, a scope problem, a null on the `Min` projection) turns **the one diagnostic endpoint** into a French sentence
  with no trace anywhere. Every sibling in this feature set (`CreateClinicUserCommandHandler` right beside it,
  `CreateClinicCommandHandler`) injects `ILogger<T>` and calls `_logger.LogError(ex, …)` before sanitising; the repo's
  A-8 convention is "the detail belongs in the log, never in the response" — not "nowhere".
  **Fix:** inject `ILogger<GetOutboxDepthQueryHandler>` and log the exception, keeping the sanitised message on the wire.
- **Challenge verification:** Line 128 is `catch (Exception ex) when (ex is not ConflictException)` and the body
  (130–131) returns the French message with no logging call; `ex` is referenced only by the `when` filter, which is why
  it compiles clean. The cited sibling `CreateClinicUserCommandHandler.cs:126–130` does inject `ILogger<T>` and
  `LogError(ex, …)` before sanitising, so the convention claim is verified.

### Finding 16
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** `web/components/user-management.tsx`
- **Line:** 818
- **Anchor:** `UserManagement` — « Mot de passe temporaire » Dialog
- **Raised by:** Device & UX
- **Comment:** At 390×844 (and every phone width) this dialog renders through `DIALOG_MOBILE_BOTTOM` as a ~300 px bottom
  sheet, leaving ~540 px of live `DialogOverlay` above it. Radix's default `onPointerDownOutside` closes on a tap
  anywhere in that band — and `submitCreate` (lines 237–241) opens this dialog **in the same commit** that closes
  « Créer un compte », i.e. exactly while the admin's thumb is still travelling from the button they just pressed, which
  on the bottom sheet sat near the bottom of the screen where the new sheet's content now lands. An off-target tap
  destroys the only rendering of the password, and the dialog's own copy (line 823) asserts « Il n'est affiché qu'une
  seule fois ». On the hosted profile this is the only way to onboard a colleague.
  ⚠️ Two halves to the fix. **(a)** `<DialogContent onInteractOutside={(e) => e.preventDefault()}>` — closes the
  accidental channel only, leaving Escape, the ✕ and « Terminé » as deliberate exits (§ 2 requires Escape to keep
  working). **(b)** The copy currently **denies a recovery that exists**: `usersApi.resetPassword` is gated on the same
  `mode === "local"` condition as `canCreateAccounts`, so « Réinitialiser le mot de passe » on the new row does
  regenerate one. Name it.
- **Challenge note:** Severity lowered Major → Minor, and **half (b) is the sharper half**. The mechanism is exactly as
  described — `DialogContent`'s `mobile` prop defaults to `"bottom"` (`ui/dialog.tsx:126`), and `submitCreate` does
  `setCreateOpen(false)` at line 237 and `setTempPassword(...)` at line 241 in one commit — but the *cost* of the
  accident is bounded: the admin clicks « Réinitialiser le mot de passe » and gets a new one through the same dialog.
  A lost password is a two-click re-issue, not a locked-out colleague, so this is a Minor UX defect whose most
  misleading part is a copy line asserting an irreversibility the product does not have.

### Finding 17
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** `web/components/join-unavailable.tsx`
- **Line:** 20
- **Anchor:** `JoinUnavailable` — outer centring wrapper
- **Raised by:** Device & UX
- **Comment:** `flex items-center justify-center` with a `w-full max-w-md` child is the exact structure
  `app/join/page.tsx` annotates at lines 189–198 as having previously put ~24 px of a card's left edge off-canvas: the
  flex item's `min-width: auto` floor beats `max-w-md`, and because the parent centres, overflow splits to **both**
  sides — the inline-start half is not in the scrollable region (non-negotiable #6: unreachable by any means, not merely
  clipped). This file introduces a longer unbreakable token than its sibling: the `text-2xl font-semibold` title's
  « l'administrateur » (line 29, written with `&apos;` = U+0027, which offers no break opportunity) measures ~171 px.
  ⚠️ **Condition stated honestly:** at a plain 320 px it fits the 240 px content box. It fails at **320 px with 200 %
  browser zoom** (a stated § 0 requirement) — 160 CSS px viewport, 80 px content box, ~30 px of the card's inline-start
  edge off-canvas with no scroll able to reach it, on the screen whose entire job is telling a locked-out user what to do.
  **Fix:** `justify-start` on the wrapper plus `mx-auto` on the inner `w-full max-w-md` div, so overflow lands only at the
  inline-end and inside the scrollable region; add `break-words` to the `CardTitle`.
- **Challenge note:** Severity lowered Major → Minor. The precedent claim is **verified verbatim** —
  `app/join/page.tsx:189–198` carries that exact annotation of this exact structure — and the wrapper at
  `join-unavailable.tsx:20` is `min-h-dvh … flex items-center justify-center p-4 sm:p-6` over a `w-full max-w-md`
  child, so the mechanism is real. It is lowered because the trigger is the **compound** 320 px + 200 % zoom case the
  review itself states (it fits at plain 320 px), the page is informational, and no capability is lost — the CTA and
  the three steps stay reachable while the card's inline-start edge clips.
  ⚠️ **Fix both files.** `app/join/page.tsx` lines 112 and 133 carry the identical wrapper (pre-existing, out of this
  diff), so fixing only the new file leaves the sibling — the page a LAN user actually lands on — still exposed.

### Finding 18
- **Severity:** Minor
- **Category:** Error Handling / Code Quality / Security
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.API/Startup/AuthAttemptAccount.cs`
- **Line:** 76
- **Anchor:** `AuthAttemptAccount.CaptureAsync` — catch block
- **Raised by:** Error Handling, Code Quality, Security (**3 agents**, all Minor)
- **Comment:** The class contract is stated twice as absolute — "Nothing about it may be able to refuse a request",
  "Deliberately silent and total" — but the catch handler itself runs `context.Request.Body.Position = 0;`, which throws
  `NotSupportedException` on a non-seekable stream. That is reachable precisely when the thing that failed was
  `EnableBuffering()` (nothing has made the body seekable yet), and the middleware is registered **before**
  `UseMiddleware<ExceptionMiddleware>()`, so the escape is an unhandled framework 500 on `POST /api/auth/login` that
  bypasses the canonical `{ error }` contract — and in a non-Production environment renders the developer exception page
  to an anonymous caller. Separately, the catch logs **nothing** at any level, so a *systematic* capture failure
  silently reverts the limiter to per-address partitioning — reinstating the very lockout US-6 exists to remove — with
  nothing in any log connecting the two.
  **Fix:** `if (context.Request.Body.CanSeek) context.Request.Body.Position = 0;` (or its own try/catch), add a
  `LogDebug`/`LogWarning`, add an `AuthAttemptAccountTests` case for a non-seekable body, and consider moving
  `UseMiddleware<ExceptionMiddleware>()` above the capture.
- **Challenge verification:** Lines 76–81 are exactly `catch { context.Request.Body.Position = 0; }` — no logging, no
  `CanSeek` guard. The middleware-order claim is verified against `api/ClinicManagement.API/CLAUDE.md`'s pipeline
  listing: `UseAuthAttemptAccountCapture()` → `UseRateLimiter()` → `UseMiddleware<ExceptionMiddleware>`. The
  *silent* half is the solidly reachable one; the throwing half needs `EnableBuffering()` itself to fail, which is
  narrow but is precisely what the "total" contract claims cannot matter.

### Finding 19
- **Severity:** Minor
- **Category:** Code Quality / Breaking Change
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Infrastructure/Storage/MinioFileStorage.cs`
- **Line:** 158
- **Anchor:** `MinioFileStorage.ProbeAsync`
- **Raised by:** Code Quality, Breaking Changes (**2 agents**)
- **Comment:** The docstring says a missing bucket "is reported as reachable-but-unusable rather than as unreachable,
  because the two have different operator answers" — but the only channel available is an exception, so
  `FileStorageHealthCheck` catches it, grades `Degraded` and logs `LogError(ex, "Health check: the file storage is
  unreachable.")`. The promised distinction never reaches the operator. And neither `docker-compose.prod.yml` nor
  `.hosted.yml` has a bucket-initialisation service (no `mc mb`), with `MinIO__BucketName` left at the `clinic-files`
  default, so the bucket exists only after the first `UploadAsync`. A correctly deployed, brand-new hosted stack
  therefore answers `GET /health` with `storage: Degraded` from first boot — the first signal an operator checks reads
  as a fault, and any monitor keyed on `status == "Healthy"` (which `deploy/README.md` points people at) alarms on a
  healthy deployment, while an Error line is emitted every probe tick.
  **Fix:** create the bucket at startup (or add an `mc mb` init service to the compose files), and have `ProbeAsync`
  return normally when the endpoint answers but the bucket is absent.
- **Challenge verification:** `ProbeAsync` (158–169) throws `InvalidOperationException("MinIO is reachable but the
  bucket … does not exist yet.")`, and `FileStorageHealthCheck.CheckHealthAsync` (HealthChecks.cs:141–152) catches
  **every** exception into one `LogError(… "unreachable.")` + `Degraded`. Grep across `deploy/` for
  `mc mb|BucketName|createbucket|minio/mc` returns **no matches**, so neither compose file initialises the bucket.

### Finding 20
- **Severity:** Minor
- **Category:** Error Handling / Code Quality
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Application/Features/Clinics/LocalClinicProvisioning.cs`
- **Line:** 149
- **Anchor:** `LocalClinicProvisioning.ProvisionAsync`
- **Raised by:** Error Handling, Code Quality (**2 agents**)
- **Comment:** `catch { }` around `clinicCatalogSeeder.SeedForClinicAsync` logs nothing. The behaviour was carried over
  verbatim from `CreateClinicCommandHandler`, but the extraction gave it a second caller where the consequence is worse:
  `provision-clinic` prints "Clinic provisioned successfully." and a temporary password while the clinic may have **no
  CNAM, medication or dental-act catalogs at all**, and in `HostedMultiTenant` the cited safety net
  (`SeedAllClinicsAsync`) only runs at the next API restart — potentially months. The repo's contract for best-effort
  post-commit side effects is "log at Error and swallow", not swallow silently (`UpdateDoctorProfileCommand.cs:112-117`
  does the identical thing and logs at Warning).
  **Fix:** take an `ILogger` parameter (both callers have one) and log before swallowing; `provision-clinic` should
  additionally print « catalogues non initialisés — ils seront recréés au prochain démarrage ».
- **Challenge verification:** Lines 145–152 are `try { await clinicCatalogSeeder.SeedForClinicAsync(...); } catch { }`
  with a comment and no logger — and `ProvisionAsync` takes no `ILogger` parameter at all.
  `ProvisionClinicCommand.cs:116` prints "Clinic provisioned successfully." unconditionally afterwards.

### Finding 21
- **Severity:** Minor
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Application/Features/Documents/Commands/UpdateMedicalDocumentCommand.cs`
- **Line:** 199
- **Anchor:** `UpdateMedicalDocumentCommandHandler.Handle`
- **Raised by:** Error Handling
- **Comment:** The new `owningClinicId is null` guard returns « Document médical introuvable. », which misdescribes the
  condition on both counts: the document *was* found (the handler is 190 lines into updating it) and the real cause is
  that the clinic-filtered `Patient` navigation did not materialise under the current tenant scope. Worse, it returns
  **after** line 187 has already run its own `SaveChangesAsync` to commit a newly created "documents" folder, so the
  refusal leaves a committed side effect behind.
  **Fix:** move the guard above the folder block (it depends on nothing computed in it) and give it a message naming the
  real state, e.g. « Le patient de ce document est introuvable dans votre cabinet. »
- **Challenge verification:** The ordering is exactly as described: `await _unitOfWork.SaveChangesAsync(...)` at line
  187 commits the new `PatientFolder`, then lines 198–202 read `document.Patient?.ClinicId` and return
  « Document médical introuvable. » when it is null. The guard depends on nothing the folder block computes.

### Finding 22
- **Severity:** Minor
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Infrastructure/Services/TtnIdentityProvider.cs`
- **Line:** 98
- **Anchor:** `TtnIdentityProvider.ResolveInstallIdentity`
- **Raised by:** Error Handling
- **Comment:** `ITtnIdentityProvider` promises "throws `InvalidOperationException` with a French operator message when no
  usable identity exists", and `EInvoiceService:108` records `ex.Message` on the invoice row for exactly that exception.
  Two of the likeliest operator errors escape the contract: **(1)** `File.ReadAllBytes(certPath)` here is unwrapped, so a
  permissions/IO error throws `IOException`/`UnauthorizedAccessException` into `EInvoiceService`'s generic catch at line
  117, overwriting the row's reason with « Erreur lors de l'envoi à El Fatoora. »; **(2)** a **wrong PFX password** — the
  single most likely misconfiguration of a hand-provisioned identity — throws `CryptographicException` from
  `new X509Certificate2(...)` in `XadesEInvoiceSigner.Sign:55` into the same generic branch. In both cases the operator
  is told nothing about which secret to re-enter, on a queue that retries.
  **Fix:** wrap the file read in the same `catch → InvalidOperationException(French, ex)` shape
  `DownloadCertificateAsync` already uses, and wrap the `X509Certificate2` construction with a French "certificat
  illisible ou mot de passe incorrect". Secondary: prefer `File.ReadAllBytesAsync(certPath, cancellationToken)`.
- **Challenge verification:** Line 98 is a bare `File.ReadAllBytes(certPath)` inside the `return new
  ResolvedTtnIdentity(...)` expression — no try/catch, unlike `DownloadCertificateAsync` (105–125) which does wrap.
  `XadesEInvoiceSigner.cs:55–58` constructs `new X509Certificate2(identity.CertificateBytes,
  identity.CertificatePassword, X509KeyStorageFlags.EphemeralKeySet)` unguarded, and `EInvoiceService.cs:117–123` is
  the generic catch that overwrites the row's reason.

### Finding 23
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.API/Program.cs`
- **Line:** 646
- **Anchor:** `Program.cs` startup migrate-and-backfill block
- **Raised by:** Business Logic
- **Comment:** `RunsStartupBackfills` is evaluated **inside** `if (!profile.DefersMigrations)`, so the two capabilities
  the plan deliberately split apart are still coupled: for `SelfHostedLan` (`DefersMigrations: true`) the flag is
  unreachable, and `DeferredStartupService` runs only `IClinicCatalogSeeder.SeedAllClinicsAsync` —
  `IClinicAdminBackfill.BackfillAsync()` still never runs on a LAN install (recorded as Part A finding #1, but the
  *coupling* is the design critique). The stated reason for two capabilities was that "under one flag a new profile gets
  them right only by accident"; as wired, a future profile declaring `DefersMigrations: true, RunsStartupBackfills: true`
  would silently skip both.
  **Fix:** move the backfill block outside the migration-timing branch and have the deferred path run it too, or delete
  `RunsStartupBackfills` as decorative.
- **Challenge verification:** `Program.cs:625` opens `if (!profile.DefersMigrations)` and line 646 nests
  `if (profile.RunsStartupBackfills)` inside it, so the second flag is unreachable whenever the first is true.
  `DeferredStartupService.cs` contains exactly one of the two backfills (`SeedAllClinicsAsync`, line 79) and no
  `IClinicAdminBackfill` reference at all.

### Finding 24
- **Severity:** Minor
- **Category:** Breaking Change
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.API/Startup/RateLimiting.cs`
- **Line:** 138
- **Anchor:** `RateLimiting.AddConfiguredRateLimiter` — `IsAnonymousAuthPath` branch
- **Raised by:** Breaking Changes, Business Logic (**2 agents**)
- **Comment:** `IsAnonymousAuthPath` is a bare `/api/auth` **prefix** matched on all methods, so the new global ceiling
  captures more than the four brute-forceable POSTs: `GET /api/auth/mode` (anonymous, read on every app start by `/join`
  and `/users`) and the authenticated `POST /api/auth/change-password` (hit by every provisioned account on first login)
  move from the API allowance of 600 permits / 60 s to **150 permits / 300 s** — a 20× reduction in sustained rate,
  shared with every login and refresh on the same partition key. Compounded by Finding 1's partition collapse.
  **Fix:** restrict the global auth branch to `HttpMethods.IsPost`, or exclude the GET-only meta routes explicitly.
- **Challenge verification:** `IsAnonymousAuthPath` (lines 204–205) is `path.StartsWithSegments("/api/auth", …)` with
  no method test, and the global branch at line 138 calls it directly. `AuthController` is routed at `api/auth` and
  carries both `GET mode` (line 54) and `change-password`, so both are inside the prefix. Note the contrast with
  `AuthAttemptAccount.ShouldCapture` (line 51), which *does* test `HttpMethods.IsPost` — the two disagree on what
  « an auth request » is, despite the capture class's own comment claiming they cannot.

### Finding 25
- **Severity:** Minor
- **Category:** Security
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Application/Features/Users/Commands/CreateClinicUserCommand.cs`
- **Line:** 99
- **Anchor:** `CreateClinicUserCommandHandler.Handle` — email uniqueness check
- **Raised by:** Security
- **Comment:** The uniqueness check is deliberately global (`GetByEmailAsync` is not clinic-scoped) and its refusal —
  « Un compte existe déjà avec cet email. » — is returned verbatim. On a hosted backend serving competing practices that
  turns `POST /api/users` (any clinic admin) into an oracle for "does this person hold an account anywhere on this
  service": an admin at clinic A can probe arbitrary addresses and learn which belong to clinic B's staff — a
  cross-tenant inference the threat model excludes. The global check itself is justified (login resolves by email
  alone); the *disclosure* is not.
  **Fix:** return a message that does not distinguish "taken here" from "taken elsewhere" (« Cet email ne peut pas être
  utilisé pour un nouveau compte. »), or reveal the collision only when the existing account is in the caller's own
  clinic.
- **Challenge verification:** Lines 96–103 read exactly as described, including the comment explaining that the check
  is cross-clinic on purpose. The same verbatim message is returned from `LocalClinicProvisioning.cs:83`, so a fix
  should cover both wordings.

### Finding 26
- **Severity:** Minor
- **Category:** Breaking Change
- **Verdict:** Confirmed
- **File:** `web/lib/api/auth.ts`
- **Line:** 8
- **Anchor:** `AuthModeDto.mode`
- **Raised by:** **orchestrator cross-boundary trace** (server DTO ↔ TypeScript type)
- **Comment:** The client type declares `mode: 'local' | 'cloud'` (lowercase), but the server sends **`"Local"` /
  `"Cloud"`** — `AuthController.GetMode:57-60` returns `LocalAuthConfig.LocalMode`, and those constants are
  `"Local"`/`"Cloud"` (`LocalAuthConfig.cs:15-16`). The camelCase JSON policy renames *properties*, not string values.
  No current consumer compares it (both call sites destructure only `selfRegistrationEnabled`), so nothing is broken
  today — but this is a latent, silently-false comparison in a brand-new file: the next person to write
  `if (dto.mode === 'local')` gets `false` forever, and TypeScript will accept it because the declared type says that is
  the correct literal. Exactly the FE-string ↔ BE-value alignment trap, pointing the other way.
  **Fix:** declare `mode: 'Local' | 'Cloud'` to match the wire, or lowercase the value server-side. Do not leave the
  type and the wire disagreeing.
- **Challenge verification:** `web/lib/api/auth.ts:8` declares `mode: 'local' | 'cloud'`;
  `AuthController.GetMode` (54–61) returns `LocalAuthConfig.LocalMode`/`CloudMode`, which are `"Local"`/`"Cloud"`
  (`LocalAuthConfig.cs:15–16`). Both consumers verified to destructure `selfRegistrationEnabled` only
  (`app/join/page.tsx:37`, `user-management.tsx`), so the defect is latent, not live. Line re-anchored 9 → 8.

### Finding 27
- **Severity:** Suggestion
- **Category:** Security / Documentation
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** `api/ClinicManagement.Infrastructure/Persistence/ApplicationDbContext.cs`
- **Line:** 166
- **Anchor:** `ApplicationDbContext.OnModelCreating` — global query-filter block
- **Raised by:** Security
- **Comment:** This commit promotes the query filters from a fail-open backstop to a real isolation layer and enumerates
  the clinic-owned roots, but **`Notification`** — the SMS/WhatsApp reminder outbox — is left out and, unlike
  `User`/`Clinic`/`AuditEntries`, **no comment in this file explains why**. It carries `ClinicId`, `PatientId`, the
  recipient phone number and the rendered message body (patient name, appointment date). No reachable read is unscoped
  today (all four take a `clinicId`), so this is defence-in-depth rather than a live leak.
  **Fix:** record the real reason here, beside the `AuditEntries` note it structurally resembles.
- **Challenge note:** Severity lowered Minor → Suggestion, and one of the finding's supporting claims is **wrong**.
  (a) The exclusion *is* a recorded, reasoned decision — `TenantScopeFilterTests.UnfilteredByDesign` (lines 34–41)
  lists `Notification` with a stated reason, and the guard asserts that dictionary **equal to the model in both
  directions**, so the review's « the test derives over the filtered roots so it structurally cannot notice the
  omission » is false: a new unfiltered clinic-owned root fails that test. (b) The reviewer's own guess at the reason
  is the right one and the recorded reason is not: `Notification.ClinicId` is **`Guid?`** (Domain/Entities/
  Notification.cs:13) — the identical structural reason `AuditEntries` is exempt and *is* documented here — whereas
  the recorded « drained cross-clinic by the minutely dispatcher » does not hold, since `DocumentEmail` is filtered
  and its dispatcher declares `UseSystemWide` too. What is left is a documentation fix in this file, not a filter.

### Finding 28
- **Severity:** Minor
- **Category:** Security
- **Verdict:** Confirmed
- **File:** `deploy/docker-compose.hosted.yml`
- **Line:** 31
- **Anchor:** `services.api`
- **Raised by:** Security
- **Comment:** The API container runs as **root** — `api/Dockerfile` builds on `mcr.microsoft.com/dotnet/aspnet:8.0`
  (which does not switch users) and declares no `USER`, and this service declares no `user:`. The `web` service, by
  contrast, correctly drops to `nextjs` (uid 1001). This is the container that mounts `dataprotection_keys:/keys` — the
  cleartext key ring of Finding 10 — and holds the Postgres and MinIO root credentials in its environment, so any RCE or
  container escape is immediately root-with-the-keys.
  **Fix:** add `USER $APP_UID` to the final stage of `api/Dockerfile` or pin `user:` here, ensure `/keys` is owned by
  that uid, and consider `read_only: true` + `cap_drop: [ALL]` + `security_opt: [no-new-privileges:true]` on both
  application services.
- **Challenge verification:** `api/Dockerfile` has no `USER` in any of its four stages; the `api` service block
  (lines 31–86) declares no `user:`. The contrast is verified: `web/Dockerfile:42–55` creates and switches to
  `nextjs` (uid 1001).

### Finding 29
- **Severity:** Minor
- **Category:** Security
- **Verdict:** Confirmed
- **File:** `deploy/Caddyfile`
- **Line:** 50
- **Anchor:** page-response `handle { header { … } }` block
- **Raised by:** Security
- **Comment:** The new page-header block sets three headers and omits the two only it can supply in this topology.
  `SecurityHeadersMiddleware` covers only what Kestrel serves — behind this proxy, `/api/*` alone (the block's own
  comment says so) — so page responses get **no `Content-Security-Policy` at all**, meaning `Security:EnforceCsp`, the
  switch US-6 added, has no effect whatsoever on the HTML documents and scripts it is meant to constrain in
  `HostedMultiTenant`. An operator who flips it after "the page walk is clean" will believe the pages are protected when
  only JSON API responses are. (`Strict-Transport-Security` is missing here too — see Finding 9.)
  **Fix:** add both to this block, and note in `SecurityHeadersMiddleware` that the page-side CSP lives in the Caddyfile
  so the two do not silently diverge.
- **Challenge verification:** `deploy/Caddyfile:44–56` — the `handle { header { … } }` block sets exactly
  `X-Content-Type-Options`, `X-Frame-Options` and `Referrer-Policy`, and its own comment states that the API's
  middleware covers `/api/*` alone in this topology. Confirmed that `next.config.ts` emits no CSP either (the
  middleware's own docstring records this, and it is why nothing else fills the gap).

### Finding 30
- **Severity:** Suggestion
- **Category:** Security / Documentation
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** `api/ClinicManagement.API/Middleware/SecurityHeadersMiddleware.cs`
- **Line:** 46
- **Anchor:** `SecurityHeadersMiddleware.ContentSecurityPolicy`
- **Raised by:** Security
- **Comment:** The policy `Security:EnforceCsp` promotes to enforcing contains
  `script-src 'self' 'unsafe-inline' 'unsafe-eval'`, which permits inline `<script>`, `javascript:` handlers and `eval`.
  Against the class of attack CSP exists to mitigate — XSS in a product rendering free-text clinical notes, patient
  names and document content — that directive is close to no script policy at all, so turning the key on buys little
  while creating the impression the app is CSP-protected. `'unsafe-eval'` is rarely needed by a production Next build.
  **Fix (in scope for this feature):** state in the docstring that the enforcing policy constrains resource *origins*
  and not script injection, so an operator flipping the flag knows what they are and are not buying.
  **Fix (follow-up):** drop `'unsafe-eval'` and move to Next's nonce/hash support (`strict-dynamic`), leaving
  `'unsafe-inline'` on `style-src` only.
- **Challenge note:** Severity lowered Minor → Suggestion. The directive at line 46 is exactly as described, but the
  **policy string is pre-existing unchanged code** — what this feature added is `EnforceCspKey` (line 36) and the
  header-name ternary (line 91). The finding's own framing (« this feature is what advertises it as a control ») is
  fair, so it is not dismissed as pre-existing; but the in-scope actionable item is the docstring caveat, and
  rewriting the policy is a separate piece of work with its own page walk.

### Finding 31
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.API/Maintenance/ProvisionClinicCommand.cs`
- **Line:** 59
- **Anchor:** `ProvisionClinicCommand.RunAsync` (the profile gate at 59; the join-code print at 119)
- **Raised by:** Business Logic
- **Comment:** Two gaps against its siblings. **(a)** It prints `Join code: {provisioned.Clinic.Code}` unconditionally,
  but in `HostedMultiTenant` — the profile this verb exists for — `AllowsSelfRegistration` is false and
  `POST /api/auth/register` 404s, so the operator is handed a credential that leads nowhere, printed beside the one-time
  password as if it were an alternative. **(b)** Unlike `verify-schema`, `reconcile-money` and `reset-admin-password` it
  never calls `MaintenanceDatabase.HasConnectionString`, so with no `ConnectionStrings:DefaultConnection` it fails inside
  `AddInfrastructure`/DbContext resolution and reports an infrastructure exception instead of the operator sentence
  naming the env var — the defect `MaintenanceDatabase` was extracted to fix.
  **Fix:** suppress or label the join code when `!profile.AllowsSelfRegistration`; add the `HasConnectionString` gate.
- **Challenge verification:** Line 119 is `Console.WriteLine($"  Join code:          {provisioned.Clinic.Code}");`
  with no capability test anywhere around it, and lines 123–125 present the temp password as the way in without
  qualifying the code. The only gate in `RunAsync` is `profile.UsesLocalAccounts` (59–66); `MaintenanceDatabase` is
  not referenced in the file.

### Finding 32
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.API/Middleware/TenantScopeMiddleware.cs`
- **Line:** 32
- **Anchor:** `TenantScopeMiddleware`
- **Raised by:** Code Quality
- **Comment:** There is **no test anywhere** for `TenantScopeMiddleware` or `RequestAccount` (the name appears in
  `ClinicManagement.UnitTests` only inside a comment in `ClinicHubTenantScopeTests`). This is the single point on the
  request path that makes US-2's whole inversion work, and both of its load-bearing behaviours are unasserted: (a) the
  scope is set from the **DB-resolved** `User.ClinicId` and never from the JWT claim (amendment C3′ — the defect avoided
  is zero rows with no error), and (b) an unresolvable caller deliberately leaves the scope `Unset` so anonymous requests
  and a Cloud principal with no `User` row still work. `RequestAccount`'s "resolved once, cached even when null" contract
  — what stops the two middlewares double-querying and lets either run first — is also unpinned.
  **Fix:** add `TenantScopeMiddlewareTests` (the shape `AuthAttemptAccountTests` already uses): authenticated caller with
  a `User` row → `Kind == Clinic`; authenticated with no row → `Unset`; anonymous → `Unset` and no repository call; two
  `ResolveAsync` calls issue one `GetByAuth0SubAsync`.
- **Challenge verification:** Grep for `TenantScopeMiddleware|RequestAccount` across `api/ClinicManagement.UnitTests`
  returns exactly one hit — a prose mention inside `Hubs/ClinicHubTenantScopeTests.cs:10`. No test file exercises
  either type.

### Finding 33
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Application/Features/Clinics/Commands/CreateClinicCommand.cs`
- **Line:** 349
- **Anchor:** `CreateClinicCommandHandler.SeedDefaultProcedureTypesAsync` / `.SeedClinicCatalogsAsync`
- **Raised by:** Code Quality
- **Comment:** `LocalClinicProvisioning`'s docstring claims to be "the **single** definition" of creating a clinic and
  cites `fixes-dont-propagate` explicitly — but the extraction only covered the Local branch. The Cloud branch
  (lines 133–248) still holds its own copy of the clinic-code uniqueness loop, the `WorkingHoursSerializer.Normalize`
  block, the `Doctor` construct-and-link, and — byte-identically — `SeedDefaultProcedureTypesAsync` (349) and
  `SeedClinicCatalogsAsync` (360), both of which now also exist inline in `LocalClinicProvisioning.ProvisionAsync`
  (136–152). So "what it means to create a clinic" still has two answers, and the seldom-changed one is the copy the
  helper was written to eliminate.
  **Fix:** at minimum delete the two private helpers here and have the Cloud branch call the shared code.
- **Challenge verification:** All four duplicated pieces are present in the Cloud branch at the cited lines —
  `CodeExistsAsync` loop at 139, `WorkingHoursSerializer.Normalize` at 158, `doctor.LinkToUser` at 212, and the two
  private seed helpers at 349 and 360 — with their counterparts in `LocalClinicProvisioning.ProvisionAsync` at 87–90,
  102–106, 132 and 136–152. `LocalClinicProvisioning`'s docstring does claim to be "the **single** definition" and
  does cite `fixes-dont-propagate` by name.

### Finding 34
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Infrastructure/Repositories/NotificationRepository.cs`
- **Line:** 205
- **Anchor:** `NotificationRepository.GetOutboxDepthAsync`
- **Raised by:** Code Quality
- **Comment:** Three of this method's four counts are byte-identical duplicates of `GetClinicLogCountsAsync` twenty lines
  above — the same `scoped` expression, the same `Pending` count, the same `Blocked` count, the same
  `Failed && ScheduledFor >= failedSinceUtc` window. The repo's rule that the *dispatcher* predicate must be copied is
  explicit and justified (`Due` must match `GetDueForDispatchAsync`), but that argument does not extend to copying a
  sibling read in the same file: a change to what "blocked" or "failed recently" means now has to be made twice, in one
  class, with nothing holding them equal.
  **Fix:** extract the three shared counts into one private helper both methods call.
- **Challenge verification:** `GetClinicLogCountsAsync` (174–203) and `GetOutboxDepthAsync` (205–234) each open with
  `var scoped = _context.Notifications.Where(n => n.ClinicId == clinicId);` and then repeat the identical `Pending`,
  `Blocked` and `Failed && ScheduledFor >= failedSinceUtc` predicates. Only `Due` and `oldestDue` are unique to the
  second method, and only those are covered by the documented copy-the-dispatcher rule.

### Finding 35
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** `web/components/user-management.tsx`
- **Line:** 780
- **Anchor:** `UserManagement` — « Créer un compte » Dialog, Rôle field
- **Raised by:** Device & UX
- **Comment:** `<SelectTrigger id="create-user-role">` passes no `className`, so it keeps the primitive's base `w-fit`
  (`ui/select.tsx:42`) while the two `<Input>`s above it are `w-full`. At 320 px inside the bottom sheet the Rôle control
  renders as a ~110 px stub under a full-width label and above a full-width hint, in a 240 px column of otherwise
  full-width fields — and because it is shrink-to-fit it **changes width** when the admin picks « Administrateur »
  instead of « Secrétaire », reflowing the field mid-interaction. The same file states the convention twice already
  (`min-h-11 w-full` at line 484, `h-8 w-[150px]` at line 580).
  **Fix:** `className="w-full"` on the trigger.
- **Challenge verification:** Line 780 is `<SelectTrigger id="create-user-role">` with no `className`, and
  `ui/select.tsx:42` does carry `w-fit` in the trigger's base class list. The two sibling `<Input>`s (lines 755, 765)
  take the primitive's `w-full`.

### Finding 36
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** `web/app/join/page.tsx`
- **Line:** 35
- **Anchor:** `JoinClinicPage.checkUserStatus` — the `mode === "local"` probe branch
- **Raised by:** Device & UX
- **Comment:** `setIsChecking(false)` now runs only after `authApi.getMode()` settles, and `apiGet` attaches no
  `AbortSignal` or timeout. A *rejected* fetch is handled (the catch falls through to the form) but a **stalled** one is
  not: an API mid-restart, a flaky mobile connection, or a captive portal that completes the handshake and never answers
  leaves « Vérification du statut de votre clinique… » (line 115) on screen indefinitely — no retry, no error, no way
  forward — on the normal way into a LAN install, and exactly what a phone on a marginal signal produces. Before this
  change local mode rendered the form synchronously, so the regression is new. It also conflates "loading" with "failed
  to load", which § 13 requires to be distinct.
  **Fix:** give the probe `AbortSignal.timeout(~5000)` and treat a timeout exactly as the existing catch does.
- **Challenge verification:** Lines 35–47: the `mode === "local"` branch awaits `authApi.getMode()` and only then
  reaches `setIsChecking(false)` (line 46) — on both the success and the catch path, but there is no path at all for a
  request that never settles. Grep for `AbortSignal|signal:|timeout` in `web/lib/api/client.ts` returns **no matches**,
  so `apiGet` has no timeout of any kind. Line 115 is the « Vérification… » string.

### Finding 37
- **Severity:** Suggestion
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** `web/components/user-management.tsx`
- **Line:** 362
- **Anchor:** `UserManagement` — « Utilisateurs » CardTitle / « Créer un compte » trigger
- **Raised by:** Device & UX
- **Comment:** The new primary action is placed *inside* `CardTitle`'s `flex flex-wrap` row and pushed over with
  `ms-auto`, re-solving what `ui/card.tsx` already provides: `CardAction` together with `CardHeader`'s
  `has-data-[slot=card-action]:grid-cols-[1fr_auto]` exists for exactly this. Observable cost at 320 px (content box
  ~240 px): chip + « Utilisateurs » + count badge measure ~173 px, so the ~149 px button wraps to a second line where
  `ms-auto` right-aligns it alone and the « N en attente » badge wraps to a third — the action floats mid-header and the
  header runs to three lines.
  **Fix:** render `<CardAction><Button …/></CardAction>` as a sibling of `CardTitle` and drop `ms-auto`.
- **Challenge verification:** Lines 356–382: the `Button` (363–373, `className="ms-auto gap-2"`) sits inside
  `<CardTitle className="flex min-w-0 flex-wrap items-center gap-2.5 …">`, followed by the « N en attente » badge —
  so the wrap order is exactly as described.

### Finding 38
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — one cited call site is not per-request)
- **File:** `api/ClinicManagement.API/Controllers/AuthController.cs`
- **Line:** 38
- **Anchor:** `AuthController.Deployment`
- **Raised by:** Code Quality
- **Comment:** `DeploymentProfile.Resolve(IConfiguration)` is called **per request** from the controllers on the request
  path — here, `TrustController:63`, `UsersController:68`, `ConnectivityController:40` and `ClinicsController:364`/`388`
  — each re-reading the key, re-parsing the enum and allocating a new profile, while `AddInfrastructure` already
  registers the resolved profile as a singleton and `TtnIdentityProvider` (this same feature) injects it. Two answers to
  "which profile is this?" is the shape the profile type was created to remove.
  **Fix:** constructor-inject `DeploymentProfile` in the controllers, keeping `Resolve` for the composition root and the
  console verbs, which have no container yet.
- **Challenge note:** The precedent claim is **verified** — `Infrastructure/Extensions.cs:25` resolves the profile and
  line 30 is `services.AddSingleton(profile)`, and `TtnIdentityProvider`'s constructor takes `DeploymentProfile`
  directly. One cited site was **removed** from the finding: `SecurityHeadersMiddleware:66` reads it in the
  **constructor** of a convention-based middleware, which ASP.NET instantiates **once** at pipeline build, not per
  request — so it is not part of the per-request cost (and its `_hstsEnabled`/`_cspEnforced` fields are deliberately
  read once, as its own comment states). Five per-request controller sites remain.

### Finding 39
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.API/Middleware/SecurityHeadersMiddleware.cs`
- **Line:** 66
- **Anchor:** `SecurityHeadersMiddleware..ctor`
- **Raised by:** Code Quality
- **Comment:** `EnforceCspKey` was correctly introduced as a public const (line 36) and used at line 72, but
  `"Security:EnableHsts"` two lines above is still a bare string literal — so the file has one config key as a named
  constant and its sibling as a magic string, and `SecurityHeadersMiddlewareTests` hard-codes the HSTS key while using
  the const for the other.
  **Fix:** add `public const string EnableHstsKey = "Security:EnableHsts";` and use it in both places.
- **Challenge verification:** Line 36 declares `public const string EnforceCspKey = "Security:EnforceCsp";`, line 72
  uses it, and line 67 is a bare `configuration.GetValue("Security:EnableHsts", false)`.

### Finding 40
- **Severity:** Suggestion
- **Category:** Documentation
- **Verdict:** Confirmed
- **File:** `CLAUDE.md`
- **Line:** 511
- **Anchor:** the `multi-tenant-cloud` US-1 / Part A bullet
- **Raised by:** **orchestrator** (docs were excluded from the agents' diffs)
- **Comment:** The root guide says `Deployment:Profile` resolves to "a `DeploymentKind` plus **13** named capabilities",
  but `DeploymentProfile.cs` declares **14** bool capability properties (plus the `PermitsOsPush` method) — and line 584
  of the same file calls `SharesInstallWideTtnIdentity` "the 14th capability". Two numbers in one file, and this is the
  map every session reads first.
  **Fix:** update line 511 to 14, or drop the count and describe the criterion.
- **Challenge verification:** `CLAUDE.md:511` reads « a `DeploymentKind` plus **13** named capabilities ».
  `DeploymentProfile.cs` declares fourteen `public bool` capability properties (lines 84, 87, 90, 93, 96, 99, 102, 105,
  108, 111, 114, 117, 129, 148) plus the `PermitsOsPush(DevicePlatform)` method at 167. `CLAUDE.md:584` calls
  `SharesInstallWideTtnIdentity` the 14th.

### Finding 41
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.UnitTests/Common/TenantScopeFilterTests.cs`
- **Line:** 22
- **Anchor:** `TenantScopeFilterTests`
- **Raised by:** Code Quality
- **Comment:** The class docstring says "The one hand-written list here is the **three** clinic-owned tables that are
  deliberately not filtered", but `UnfilteredByDesign` (line 34) has **four** entries — `User`, `Clinic`, `AuditEntry`,
  `Notification`. In a repo where these docstrings are the primary design record and are cited from `CLAUDE.md`, a count
  disagreeing with its own list is the drift the test exists to prevent. (See also Finding 27 on `Notification`.)
  **Fix:** say "four", or drop the number and describe the criterion.
- **Challenge verification:** Line 22 says « the three clinic-owned tables that are deliberately *not* filtered »;
  `UnfilteredByDesign` (34–41) holds four keys. Note that `api/ClinicManagement.UnitTests/CLAUDE.md` already says
  « four », so the docstring is the odd one out.

### Finding 42
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.UnitTests/Common/Maintenance/SchemaVerificationServiceTests.cs`
- **Line:** 55
- **Anchor:** `SchemaVerificationServiceTests.CleanCounts`
- **Raised by:** Code Quality
- **Comment:** Adding one field to `DataMigrationCounts` forced a mechanical `, 0` edit into twelve positional
  constructor calls, and the record now takes fourteen positional `int?`s where nothing but position distinguishes
  `attributableButUnattributed` from `pushClinicMismatch`. The two tests **added** in this diff do it right
  (`CleanCounts with { ClinicsWithPartialTtnIdentity = 1 }`), which is proof the better form was available while those
  twelve lines were being edited anyway.
  **Fix:** migrate the remaining positional call sites to `CleanCounts with { … }`.
- **Challenge verification:** Counted: `CleanCounts` at line 55 plus **twelve** `new DataMigrationCounts(...)` calls
  (lines 363, 375, 385, 395, 411, 423, 439, 458, 474, 489, 632, 644), each fourteen positional arguments wide. The
  four `CleanCounts with { … }` usages (660, 670, 687, 697) are the shape the finding asks for, and two of them are
  the TTN cases this diff added.

---

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 1 |
| Major | 13 |
| Minor | 20 |
| Suggestion | 8 |
| **Total** | **42** |

### Severity changes made by the challenge

| Finding | Was | Now | Why |
|---|---|---|---|
| 14 (`MigrationLock` release in `finally`) | Major | Minor | Masking needs a **broken connection**; the ordinary `PostgresException` propagates intact |
| 16 (temp-password sheet dismissal) | Major | Minor | A documented recovery exists (`usersApi.resetPassword`); the misleading copy is the sharper half |
| 17 (`JoinUnavailable` centring) | Major | Minor | Fits at plain 320 px; fails only at 320 px **+ 200 % zoom**, and the sibling `/join` page has the same pre-existing wrapper |
| 27 (`Notification` unfiltered) | Minor | Suggestion | The exclusion **is** recorded and guarded in both directions; residue is documenting the real reason (nullable `ClinicId`) |
| 30 (`unsafe-eval` in the CSP) | Minor | Suggestion | The policy string is pre-existing unchanged code; the in-scope item is the docstring caveat |
| 38 (`DeploymentProfile.Resolve` per request) | Suggestion | Suggestion | Severity kept; one cited call site (`SecurityHeadersMiddleware`) removed — its ctor runs once, not per request |

### By part

| Part | Critical | Major | Minor | Suggestion |
|---|---|---|---|---|
| A (US-1, profile) | — | — | 1 | 2 |
| B (US-2, tenant scope) | — | — | 2 | 2 |
| C (US-3, provisioning) | — | 2 | 8 | 2 |
| D (US-4, TTN identity) | — | 4 | 1 | — |
| E (US-5, storage keys) | — | — | — | — |
| F (US-6, operations) | 1 | 6 | 8 | 1 |
| Docs | — | — | — | 1 |
| Cross-part / deploy | — | 1 | — | — |

**Part E drew no findings.** Its signature-enforced design (`Guid clinicId` required on both `UploadAsync` overloads,
composition in one `ClinicStorageKey`, reads verbatim) held up under both the security and breaking-change mandates.

### The pattern worth naming

Seven of the 14 Critical/Major findings share one shape: **an assumption that was true in `SelfHostedLan` and is
silently false in `HostedMultiTenant`.** `ClientIp`'s loopback trust (1), the JWT signing key's ephemeral home (2),
`IsHttps` behind a TLS-terminating proxy (9), DPAPI key-ring protection (10), an unbounded `/health` (11), and
`DocumentEmailJob`'s missing per-clinic bound (5) are all the same defect class — and it is the class the feature
existed to find. Three more (3, 6, 7) are the `fixes-dont-propagate` shape the repo already names as its dominant
defect: a correct rule (`Blocked`, the per-clinic bound, "never silently substitute an identity") wired to one call
site and not its sibling.

## Next

Findings are **challenged and final**. 1 Critical and 13 Major remain — run `/apply-review-fixes`.

⚠️ **Two things `/apply-review-fixes` must not do.**
1. **Do not revert the account-keyed auth limiter** (Finding 8). It is mandated by the story (Part F step 17 /
   DEV-14); the fix **adds** a bound or moves the account dimension onto failed attempts. Reverting to per-address
   re-opens the NAT lockout US-6 exists to close.
2. **Do not treat Findings 6 and 7 as independent.** 7 (a shipped `CloudBrowser` deployment loses e-invoicing with no
   in-product remedy) is the reason 6 (the queued row leaves the outbox permanently after ~5 attempts) is not merely
   cosmetic. A write path for `Clinic.Ttn*` — deliberately out of Part D's scope — is what closes both; a blocked
   state alone leaves 7 open.

Findings 1, 2, 9, 10, 11, 13 and 28 are all `deploy/`-facing or topology-facing, so several fixes land in the same two
files (`docker-compose.hosted.yml`, `Caddyfile`) — batch them, and re-read the operator gate in
`stories/story-1-full-hosted-multi-tenant.md` afterwards, since none of them can be verified without a real deploy.
