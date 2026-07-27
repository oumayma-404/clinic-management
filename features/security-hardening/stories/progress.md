# Progress — Security Hardening (Audit Section 2)

**Story:** [story-1-security-hardening.md](./story-1-security-hardening.md) — one story, five ordered parts
**Branch:** `feature/security-hardening`

## Status

| Part | Status |
|---|---|
| P1 Installer filesystem posture | **done** — committed `43fe6d5` |
| P2 Backup output posture | **done** — committed |
| P3 Auth & session | **done** — US-4 and US-5 closed (client IP, rate limiter, per-source lockout, token revocation, R-4, short lifetime + silent renewal) |
| P4 Authorization | in progress — **P4.1–P4.4, P4.6, AC-9.3 done**; remaining: **P4.5** frontend gating only |
| P5 Hygiene | pending |

## Working tree note (start of session, 2026-07-27)

Untracked paths present at branch point that are **not part of this story** and are excluded from every commit here. Files are staged explicitly by path — never `git add -A`/`.`:

| Path | Note |
|---|---|
| `features/data-and-money-integrity/` | Contains `exploration.md` + `spec.md`. Not authored by this session — appears to be parallel work on audit § 1 (Data loss & money correctness). Left untouched. |
| `CODEBASE_AUDIT_2026-07.md` | The audit itself, untracked. The spec's "correct § 2's index count 12 → 14" step would be the first thing to commit it; flagged to the user rather than committed unilaterally. |

## Deviations

### DEV-1: Data Protection key-ring configuration extracted to a shared helper
**Date:** 2026-07-27
**Story:** 1 (Part 1)
**Category:** Technical
**Original Plan:** `plan.md` listed `DbCredentialProtector` and the console verbs, but did not name a file for the Data Protection configuration — implying the verb would configure its own.
**Actual Implementation:** New `Infrastructure/Security/LocalDataProtection.cs` owns the configuration (`SetApplicationName` + `PersistKeysToFileSystem` + `ProtectKeysWithDpapi`); `AddInfrastructure` now calls it instead of configuring inline, and the console verbs call it to build a standalone provider over the same key ring.
**Justification:** The plan's console verb forces a **second** configuration site to exist, because a verb runs outside the web host's DI container. Two definitions can drift, and the failure mode is severe and silent: the installer writes ciphertext the API cannot read, so an existing cluster becomes unreachable (the EC-4 unrecoverable path). `LEARNINGS.md` records this exact convention for the JWT signing key — "the **same** config path is used by both the issuer and the validator so they can never drift" — and implement-story Step 6.7 directs preferring the project rule over the plan's file list. Behaviour is unchanged; the moved block is verbatim.
**Impact:** One edited block in `Extensions.cs` (behaviour-neutral). Makes P2 and any future verb reuse the same key ring for free.
**Approved:** Yes — confirmed by the user before coding.

## Auto-Approved Deviations

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| Converted a **pre-existing** `{ }` comment in `clinic-server.iss` (`StartAndExportCa`, line 442 at HEAD) to `//` | Trivial | Comment-only, zero behaviour change. Inno's Pascal `{ }` comments do not nest, so the first `}` — the one closing `{app}` — ends the comment and leaves `\api), so the CA is at` to be parsed as code. This repo has hit that exact bug before. It sits in the file Part 1 ships its changes through, so if it does break ISCC it blocks the whole part; the fix is free. **Not compile-verified** — ISCC is not installed here (R-1). |
| Added `EnsureLocalModeConfig` to the installer, seeding a minimal `appsettings.Production.json` before the first verb call | Trivial | Internal to the `.iss`, no API or behaviour change. Forced by an ordering fact: `appsettings.json` ships `Auth:Mode=Cloud` and `Local` only arrives via the generated Production overlay, which `WriteProductionConfig` cannot write until the DB password exists — so a verb invoked from `EstablishDbCredentials` would refuse as "not Local mode". The alternative (passing `Auth__Mode=Local` as an env var through `cmd /C set … &&`) adds fragile nested shell quoting to a file that cannot be executed or parse-tested here. Pure Pascal was the lower-risk choice. |
| ACL policy implemented as `icacls` invocations rather than managed `System.Security.AccessControl` APIs | Trivial | The plan left the mechanism unspecified. Both projects target `net8.0` (not `net8.0-windows`), so managed ACL APIs would require a new `PackageReference` and arguably a TFM change — a dependency change needing approval. `icacls` is what `clinic-server.iss` already uses (with the same well-known-SID convention) and the codebase already has a `Process` shell-out precedent in `PgDumpBackupService`. Zero new dependencies; testable via an injected runner. |
| Well-known SIDs (`*S-1-5-18`, `*S-1-5-32-544`, `*S-1-5-32-545`, `*S-1-1-0`, `*S-1-5-20`) rather than account names | Trivial | Internal to the new class. Account names are localized — a French Windows has `BUILTIN\Utilisateurs`, not `Users` — so name-based ACL edits would silently no-op on the target machines. `clinic-server.iss` already uses the SID form for exactly this reason. |
| No programmatic "assert no Users/Everyone" verification pass | Trivial | Internal scope. `icacls` resolves SIDs to localized display names in its listing output, so a name-matching assertion would be fragile on non-English Windows. The fail-loud requirement (AC-1.4) is met by strict exit-code checking on each invocation; the resulting posture is printed by the verb for the installer log, and AC-1.1's listing check is an operator-checklist item (R-1). |

## Learnings

- `LEARNINGS.md`'s note that `ClinicManagement.UnitTests` references **only** `Application` is **stale** — the `.csproj` references `Application`, `Infrastructure` **and** `API`. Testable logic can therefore live in its natural layer rather than being pushed into Application for testability. Worth correcting in `LEARNINGS.md` during `/capture-learnings`.
- The audit's § 2 index row (`5/4/3 = 12`) undercounts its own bullets (`5/5/4 = 14`). Recorded in the spec; the correction itself is an in-scope step.

## Session log

### 2026-07-27 — Part 1
- Cleared prerequisites: `plan.md` DRAFT → APPROVED; created `feature/security-hardening`; created `stories/`.
- Verified before coding: both projects target `net8.0`; `Microsoft.AspNetCore.DataProtection` 8.0.0 already referenced in Infrastructure; the Local key ring is already `ProtectKeysWithDpapi(protectToLocalMachine: true)`, so Data Protection ciphertext is machine-bound exactly as AC-3.1 words it; `ReminderSecretProtector` is the in-repo precedent for a purpose-scoped protector.
- Implemented P1.1–P1.9. New: `DirectoryAclHardener`, `LocalDataProtection`, `DbCredentialProtector` (Infrastructure/Security); `HardenPermissionsCommand`, `CredentialProtectionCommand` (API/Maintenance); three verb interceptions in `Program.cs`; `clinic-server.iss` rewired; `packaging/README.md` + `CLAUDE.md` updated.

#### Quality gates — Part 1

| Gate | Result |
|---|---|
| Backend build (`--no-incremental`, full recompile) | **0 errors**, 58 warnings — all pre-existing baseline (`CS8618` on existing Domain entities/value objects, `CS8981` on lowercase migrations). The only warning in a file this part touched is `Program.cs(269)` `CS0618` (Hangfire `UsePostgreSqlStorage` obsolete), pre-existing and ~200 lines from the edit. **0 new warnings.** |
| Test project build | 0 errors, 0 warnings |
| New tests | **33/33 passing** (`DirectoryAclHardenerTests` 11, `DbCredentialProtectorTests` 17, `LocalDataProtectionTests` 5) |
| Full unit suite | 818 passed / **8 pre-existing failures** — see below |
| `.iss` parse on target runtime | **Not possible here** — ISCC is not installed (R-1). Verified instead by inspection: UTF-8 **BOM present** (so ISCC reads the French strings reliably); `begin`/`end` balance checks out (37/38 raw, less one `BEGIN` and two `END`s inside a SQL string literal = 36/36); no `{ }` comment contains a brace constant. |

**Pre-existing test failures (8), proven not mine.** Stashed `Extensions.cs` + `Program.cs`, rebuilt and re-ran the full suite: **identical** 8 failed / 818 passed / 826 total, same test names. Then restored.

| Failing test | Note |
|---|---|
| `DoctorCachetTests` (×4) | ⚠️ **Relevant to Part 5** — P5.2 extracts `FileContentValidation` out of `UpdateDoctorProfileCommand`, i.e. exactly this code. These need triaging *before* that extraction, or a broken baseline gets attributed to the refactor. |
| `DocumentTypeAndFilenameTests.Create_With_Supported_Type_Passes_The_Type_Guard` | Unrelated to this feature. |
| `ReminderSchedulerTests` (×3) | Pure-Moq tests, unrelated. Not time-bombs (dates are `UtcNow`-relative). |

**Test bug found and fixed during the run:** `Harden_uses_well_known_sids_not_localized_account_names` initially failed because the temp directory lives under `C:\Users\…`, so the target path matched the "no account names" filter. Fixed by scoping the assertion to the ACL arguments (skipping the path element) — the production code was correct; the assertion was measuring the wrong thing.

**Note on the killed process:** a `ClinicManagement.API` (PID 41568) was holding its `bin` DLLs, so the first build failed with `MSB3021`/`MSB3027` file locks (not compile errors). Stopped it and rebuilt clean. The dev API will need restarting.

### 2026-07-27 — Part 2 (backup output posture)

Implemented P2.1–P2.4. `PgDumpBackupService` now hardens the timestamped folder **before** the dump, via P1's `DirectoryAclHardener` — this is the whole of P2's dependency on P1, and it is real rather than nominal.

- New `Infrastructure/Security/BackupProtectionPolicy` — decides whether a destination *can* be protected. Only a local fixed disk qualifies; a USB stick (often FAT32, no ACLs at all) or a network share (the far end's ACL to enforce) gets a French warning instead of a false promise. `Unknown` resolves as unprotectable — warn rather than claim an unverified protection. A UNC path is `Network` by definition (no `DriveInfo` exists for it).
- Refusing the backup on an unprotectable medium was rejected deliberately: an operator who cannot back up to a USB drive stops backing up.
- The harden call sits **inside** the existing try, so an ACL failure on a fixed disk reuses the partial-folder cleanup already there — AC-14.4 comes for free rather than as a second cleanup path.
- `BackupResultDto.Warning` → `BackupNowCommand` (returns the DTO directly, so no mapping change) → `backup.ts` → `backup-settings.tsx`: a distinct amber panel **plus** a longer-lived `toast.warning` instead of the success toast. A warning only in the server log is a warning nobody reads (AC-14.3).
- `DirectoryAclHardener` registered as a singleton (stateless) in `Extensions.cs`.

**Constructor change handled in lock-step:** `PgDumpBackupService` gained a third ctor parameter, and `PgDumpBackupServiceTests` constructs it directly — updated in the same step, so the test project never stopped compiling.

#### Quality gates — Part 2

| Gate | Result |
|---|---|
| Backend build | 0 errors. First attempt failed with `CS0246` (missing `using ClinicManagement.Infrastructure.Security` — `PgDumpBackupService` lives in `Services`); fixed. |
| Backend tests | **19/19** passing for `PgDumpBackupServiceTests` + `BackupProtectionPolicyTests` (3 pre-existing backup tests still green) |
| Frontend typecheck | `npx tsc --noEmit` → **0 errors** |
| Frontend lint | **Cannot run** — ESLint is not installed (`npm run lint` → "'eslint' is not recognized"), and `next.config.ts` sets `eslint.ignoreDuringBuilds: true`. Per the documented fallback, the gate is typecheck + a production build. Not treated as a failure. |
| Frontend production build | **Succeeded** — all routes compiled |

### 2026-07-27 — Part 3, step 0 (the client-IP chain)

Done **before** the rate limiter, deliberately: building the limiter first means building it against a constant, and it would look correct.

- New `Infrastructure/ClientIp` (root namespace, like `LocalRequest`/`CorsOrigins`, so UnitTests can exercise it). Honours `X-Forwarded-For` **only when the immediate peer is loopback** — our own front door/BFF — so a LAN client's own header is never trusted.
- New `web/lib/auth/forwarded-for.ts`; **both** BFF routes that call the API server-side (`local-login`, `change-password`) now pass the inbound header through. `change-password` was not in the plan's file list but has the identical shape — fixing only one would have left a latent repeat.
- `LocalRequest.IsLoopback` now **fails closed** on a null peer (was `true`).

#### DEV-2: `UseForwardedHeaders` rejected in favour of a separate resolver
**Date:** 2026-07-27 · **Story:** 1 (Part 3) · **Category:** Technical
**Original Plan:** "API `ForwardedHeaders` with `KnownProxies` restricted to loopback".
**Actual Implementation:** No `UseForwardedHeaders` anywhere. A dedicated `ClientIp.Resolve(HttpContext)` reads the header directly, under the same loopback-peer trust rule.
**Justification:** `UseForwardedHeaders` **overwrites** `HttpContext.Connection.RemoteIpAddress`, and that is exactly what `LocalRequest.IsLoopback` reads to gate the first-run `setup` endpoint and the Hangfire dashboard. The plan's own third rule — "`IsLoopback` keeps reading the raw peer address, never the forwarded one" — is not satisfiable *while* using the middleware; the two requirements are in tension. Resolving separately honours both: the limiter gets the real client, and the loopback gates stay a property of the actual TCP peer, so a future topology change (a real reverse proxy, a widened `KnownProxies`) cannot silently make them spoofable. Costs a small amount of `X-Forwarded-For` parsing, which is fully unit-tested (13 cases incl. port/bracket forms and garbage).
**Impact:** None on the plan's outcome; strictly closer to its stated intent. `ClientIp` has **no consumer until P3.1** — the limiter and the tracker are its callers.
**Approved:** Auto (trivial-scope alternative satisfying the same ACs more completely; logged rather than blocking).

#### DEV-3: `IsLoopback` fail-closed reverses an existing pinned test
**Date:** 2026-07-27 · **Story:** 1 (Part 3) · **Category:** Technical
**Original Plan:** "also fix its `null ⇒ true` fail-open default".
**Actual Implementation:** Done, and `LocalRequestTests.Null_remote_ip_is_loopback` — which asserted `true` — was rewritten as `Null_remote_ip_is_NOT_loopback`.
**Justification:** Plan-mandated, and the LEARNINGS pitfall "default security gates to deny on missing/ambiguous input" says the same. Verified safe for production: Kestrel over TCP always populates the peer, so a null only occurs in-process/in tests. A null in production would mean an unexpected hosting topology — precisely where assuming "must be local" is wrong. Risk considered: if the peer *were* ever null on a real clinic PC, first-run setup would become unreachable. It cannot be, for TCP.
**Impact:** One existing test intentionally inverted. Full suite re-run confirms nothing else depended on the permissive behaviour.
**Approved:** Yes — explicitly in the approved plan.

#### Quality gates — Part 3 step 0

| Gate | Result |
|---|---|
| Backend build | 0 errors, 58 warnings (unchanged pre-existing baseline; none in changed files) |
| New/changed tests | **25/25** (`ClientIpTests` 20, `LocalRequestTests` 5) |
| Full suite | **854 passed / 8 failed** — the same 8 pre-existing failures as the Part 1 baseline, so the fail-closed change regressed nothing (818 → 854 is this feature's 36 new tests) |
| Frontend typecheck | deferred to the P3.1 commit — the BFF change is two header lines, and P3.1 touches the same files |

### 2026-07-27 — Part 3, steps 1–2 (rate limiter + per-source lockout)

US-4 is now closed. `ClientIp` has its consumers.

- `API/Startup/RateLimiting` — two limiters of deliberately different shape: per-client-address sliding window on the anonymous auth endpoints (the brute-force surface, `[EnableRateLimiting]` on `login`/`setup`/`register`), and a generous per-user global limiter that exists to bound a runaway loop, not to shape traffic. 429 renders the canonical `{ error }` body with `Retry-After` and a French message that states the delay.
- **Exemptions are as load-bearing as the limits.** `/api/connectivity` (polled every 15 s *per tab* — a 429 there reads as "offline" and disables AI + Google Calendar), the OAuth callback, `/hub/*`, `/hangfire`, and **everything outside `/api`**. That last one matters: in Local mode Kestrel is the front door for *all* traffic, so a global limiter would throttle the proxied Next pages and their `_next` chunks — one page load fires dozens.
- `ILoginAttemptTracker` (Application) + `LoginAttemptTracker` (Infrastructure, `IMemoryCache` + `IHttpContextAccessor` + `ClientIp`), keyed `(userId, source)`, sliding 15-min window, 5 attempts. Sliding on purpose: a source that keeps hammering stays locked rather than getting a fresh allowance.
- `User.MaxFailedLoginAttempts` **5 → 50**. It was the account-only lockout that made the DoS possible; it is now the durable cross-source backstop at a level one source cannot reach alone, and what survives the restart that clears the in-memory counters.
- Both lockout tiers return the **same** message, so the caller cannot learn which brake stopped them — otherwise the per-source design becomes an oracle for "is this account locked elsewhere".

**Two frontend gaps found while wiring AC-4.5.** `handleResponse` already reads `.error` first, so the French 429 body surfaces automatically — but (a) the BFF flattened **every** API failure to 401, so a rate-limit refusal was indistinguishable from a wrong password; it now passes 429 through with its `Retry-After`; and (b) a 429 whose body is missing or unparseable would have shown "HTTP 429: Too Many Requests" to a clinic, so `client.ts` has a French safety net.

**Constructor change handled in lock-step:** `LoginCommandHandler` gained a fourth parameter; `LoginCommandHandlerTests`' single `Handler()` factory was updated in the same step.

#### Quality gates — Part 3 steps 1–2

| Gate | Result |
|---|---|
| Backend build | 0 errors, 0 new warnings |
| New tests | **37/37** (`LoginAttemptTrackerTests` 10, `RateLimitingTests` 20, `LoginCommandHandlerTests` +4 per-source cases) |
| Full suite | **888 passed / 8 failed** — the same 8 pre-existing. Raising `MaxFailedLoginAttempts` 5 → 50 regressed nothing, as predicted: existing tests loop *to* the constant rather than hard-coding 5. |
| Frontend typecheck | 0 errors |
| Frontend production build | Compiled successfully, 27/27 static pages |

**Not yet verified end-to-end.** The limiter's real behaviour under load (EC-6: a whole clinic behind one NAT address at 08:00) needs a running stack; the unit tests pin the policy shape and the exemptions, not the thresholds in practice. Defaults are deliberately loose (auth 30 per 5 min, API 600 per min) and config-tunable per AC-4.6.

### 2026-07-27 — Part 3, step 3 (token revocation)

AC-5.1, AC-5.2, AC-5.11 and AC-5.15 closed. **Deliberately sequenced before the lifetime change**: revocation is independent of the renewal flow, so it ships now while `Auth:Local:TokenLifetimeMinutes` stays at its current 12h. Shortening the lifetime to ~30 min without renewal in place would log users out mid-session — a broken intermediate state, which R-1's mitigation forbids.

- `User.TokenVersion` (+ migration `20260727174753_AddUserTokenVersion`, one column, reversible) stamped into every token as `token_version` and compared per request.
- Bumped by `SetPassword` — the single choke point all four password paths funnel through (voluntary change, admin reset, offline CLI recovery, first set) — and by `Deactivate`.
- **Not** bumped by `UpgradePasswordHash`. That is plan risk **R-7** and it now has a dedicated test: the method runs *during* a successful login, so bumping there would invalidate the token that same login is about to issue, and every sign-in whose stored hash needed upgrading would appear to fail.
- `LocalAuthEnforcementMiddleware` compares the version on the account it **already loads**, so revocation costs no extra query. A token with a missing or unparseable claim is rejected — that is what retires every pre-upgrade token (AC-5.15) and is the one-time clinic-wide re-login.
- New `LocalAuthClaims` holds the claim name, shared by issuer and validator so they cannot drift on a spelling — same reason `LocalAuthConfig` owns the signing key for both sides.

#### Quality gates — Part 3 step 3

| Gate | Result |
|---|---|
| Backend build | 0 errors (two fixed on the way: a missing `using System.Globalization` in each of `LocalAuthService` and the middleware) |
| Migration | `AddUserTokenVersion` — single `integer` column, default 0, `Down` drops it. The `dotnet ef` "host was aborted" output is normal for a `WebApplication` startup host, not a failure. |
| New tests | **8/8** (`UserTokenVersionTests`) |
| Full suite | **896 passed / 8 failed** — the same 8 pre-existing |

**The default of 0 does not weaken the check.** Existing rows get `TokenVersion = 0` and newly issued tokens carry `token_version=0`, which matches — but a *pre-upgrade* token carries no claim at all, and absence is rejected regardless. Presence, not value, is what retires the old tokens.

> **Suite flake discovered — read a single test run with care.** One full-suite run reported **18** failures, adding 9 `GenericDocumentRenderTests` and 1 `LiaisonRenderContentTests`. Those pass in isolation, and three consecutive full runs then reported a stable 8/896/904. The PDF-render tests share process-wide QuestPDF/`Bs1FontResolver` state and are order- or timing-sensitive. Not caused by this change and not in scope to fix, but anyone comparing failure counts across runs needs to know the baseline is not always 8. Worth a follow-up.

### 2026-07-27 — Part 3, R-4 (one token-acquisition site)

Plan risk **R-4 closed**, and done *before* the renewal work rather than alongside it: renewal logic can only live in one place if acquisition does. Eight sites → **one**.

| Site | Was | Now |
|---|---|---|
| `client.ts` | private helper | the single **exported** `getAccessToken()` |
| `invoices`, `billing`, `clinics`, `doctors`, `patient-files`, `treatment-plans` | a private copy each | import the shared helper |
| `medical-documents` | an inline IIFE mid-function | imports the shared helper |
| `use-auth-token` hook | direct `fetch` | shared helper (+ an unmount guard it lacked) |
| `clinic-hub` (SignalR) | direct `fetch` | shared helper |

Net **−77 lines**. The six identical copies were removed by a scripted brace-match rather than by hand, so differing formatting between them could not cause a partial edit; the diff was reviewed afterwards.

Why this mattered enough to do first: while tokens live 12 hours, eight copies are merely duplication. Once the lifetime drops to ~30 minutes, **any copy that bypasses the helper keeps using an expired token and fails silently** — surfacing to the user as a random unexplained error, in whichever screen happened to use that module.

Two follow-through notes left in the code for P3.4–P3.6:
- `use-auth-token` still caches the token in component state, which is safe only while tokens are long-lived. A `NOTE` in the file says so and points at the shared client instead.
- `clinic-hub` relies on SignalR calling `accessTokenFactory` on every (re)connect, so a reconnect picks up a fresh token for free (AC-5.8).

| Gate | Result |
|---|---|
| Single acquisition site | `grep -rn "bff/auth/token"` over `lib/`, `components/`, `app/` → **one hit**, in `client.ts` |
| Frontend typecheck | 0 errors |
| Frontend production build | Compiled successfully, 27/27 static pages |

### 2026-07-27 — Part 3, steps 4–6 (short-lived tokens + silent renewal). **Part 3 complete.**

US-5 closed. Two token kinds, and the cookie no longer carries a working API credential.

| | Access token | Refresh token (the cookie) |
|---|---|---|
| Lifetime | **~30 min** (`Auth:Local:AccessTokenLifetimeMinutes`) | 12h — unchanged, so the felt session length is the same |
| Audience | the API audience | **`…-refresh`** — the API's bearer validation rejects it outright (AC-5.5) |
| Held by | browser memory only | HttpOnly cookie, never reaches JS |
| Claims | `sub`, `clinic_id`, `role`, `token_version` | `sub`, `role`, `email`, `name`, `token_version` — **no `clinic_id`**, so it is useful for nothing but being exchanged |

- `POST /api/auth/refresh` — anonymous by necessity (the caller has no access token; that is the point), rate-limited like the other auth endpoints, and **re-checks live account state**: version, `IsActive`. A session revoked since the cookie was issued cannot mint itself a new access token (AC-5.6) — without that check the refresh token would *be* the long-lived unrevocable credential this feature removes.
- Refresh token passed in the **body**, not a bearer header: the authentication layer requires the access-token audience, so a refresh token in `Authorization` would be rejected before the endpoint ran.
- A pending forced password change is deliberately **not** a refusal — the change-password screen needs a working access token to submit, and the enforcement middleware already restricts such a token to that one endpoint.
- All rejections share one message: expired, revoked and forged must be indistinguishable.
- `ValidateRefreshToken` requires the refresh audience, so an **access** token replayed at the refresh endpoint cannot mint an endless supply. Uses `JsonWebTokenHandler` (what the runtime's JwtBearer uses) per the LEARNINGS note that the legacy handler misreads its own `iss` on .NET 8.
- `/bff/auth/token` now **exchanges** instead of echoing the cookie — which is what previously made the HttpOnly flag worthless, since the browser held the same 12h credential the cookie did. It distinguishes 401 (session gone → stop, sign in) from **503** (API unreachable → retry, do not log the user out over a blip — spec EC-10).
- `handleRequest` renews **once** on a 401 and retries (AC-5.7). Exactly once is deliberate: a genuine 401 must surface promptly rather than spin. Skipped when the caller passed its own token — that caller owns its lifecycle.
- The two multipart variants build their headers **inside** the callback so the retry rebuilds them with the renewed token. Uploads are precisely where a stale token bites: they are user-initiated after a period of reading, so they are the likeliest request to be the first past expiry. This is why R-4 had to land first.

`ControllerAuthorizationCoverageTests` gained `Auth.Refresh` — the guard did its job, forcing the new anonymous endpoint through a conscious review rather than letting it appear silently.

#### Quality gates — Part 3 steps 4–6

| Gate | Result |
|---|---|
| Backend build | 0 errors. Three fixed on the way: a missing `Microsoft.IdentityModel.JsonWebTokens` type, then an **ambiguity** that import caused with `JwtRegisteredClaimNames` (resolved by fully qualifying rather than importing), then a duplicate `[AllowAnonymous]` where the new action's attributes landed under `setup`'s. |
| Backend tests | **897 passed / 8 failed** — the same 8 pre-existing |
| Frontend typecheck | 0 errors |
| Frontend production build | Compiled successfully, 27/27 static pages |

**Test-harness gap fixed in lock-step:** three login success-path tests began failing because the handler now calls `GenerateRefreshToken` and the mock returned `null`, which the handler's catch-all turned into a failure. Fixed with a default setup in a new test constructor, plus a test asserting the two tokens are issued and are **different** values — if they were the same, the cookie would carry a working API bearer and the separation would be cosmetic.

**Not verified end-to-end.** Renewal, the 401-retry and hub reconnection are unit- and build-verified only; AC-5.4 (never bounced), AC-5.8 (hub survives expiry) and EC-8 (a form open past expiry submits fine) need a running stack with a shortened lifetime. `use-auth-token` still caches in component state — flagged in the file; prefer the shared client, which now renews.

### 2026-07-27 — Part 4, partial (authorization)

**Done:** P4.1, P4.2, P4.3, and **6 of the 10** P4.4 catalog tenant checks.

- **P4.2** `AdminOnly` on `PUT /api/clinics/doctors`, all four procedure-type writes, and `PUT /api/patients/recalls/settings` — the last finally matching its own doc comment, which claimed "Admin-editable" while enforcing nothing.
- **P4.3** Own-or-admin in `SetDoctorWorkingHoursCommand`, copied from the sibling `UpdateDoctorProfileCommand` that already got it right. Now resolves the user (not just the clinic) so it can compare `doctor.UserId`; cross-clinic still reads as "not found".
- **P4.1** The clinic billing gate extended to matricule fiscal, TVA applicable/rate and timbre fiscal, following the desired-vs-current pattern the TTN check already used. Per-**field** rather than closing the endpoint, for a concrete reason: the settings form submits the whole card, so a secretary correcting the phone re-sends matricule fiscal and TVA at their existing values — comparing against the stored value means only an *actual* edit is refused (EC-11). Refused before the logo upload, so an unauthorized caller never writes to storage.
- **P4.4** `Update`/`Deactivate` × DentalActs, CnamNomenclature, Medications — 6 commands now resolve the clinic from the **DB** and fold the check into the existing null guard, so a cross-clinic row reads as "not found".

Applied by script rather than by hand, and the script writes only on *full* success — the first run patched 2 and warned on 4 without modifying them, because those spell the guard `is null` rather than `== null`. Widening the pattern and re-running got the rest. That fail-safe is why the tree never held a partly-patched file.

**Constructor cascade handled in lock-step:** the 4 new resolver parameters broke `CnamNomenclatureCrudTests` and `MedicationCrudTests`, which construct the handlers directly. Both harnesses already mocked `ICurrentClinicResolver` for the `Create` handlers, so the fix was inserting the existing mock — and those tests passing now also demonstrates the same-clinic happy path still works.

**Follow-up in the same session — P4.4 is now complete, all 10 commands.** The four remaining ones were finished:

- The three `Confirm*` commands filter the returned set by `ClinicId` instead of guarding a single row — there is no id to check, so the set filter is the equivalent. This was the worst of the ten: with the filter inactive, one call confirmed *every* clinic's provisional rows.
- `UpdateCnamLetterValueCommand` guards by id like the others (it reads via `GetLetterValueByIdAsync`, not the shared `GetByIdAsync`, which is why the earlier script skipped it).

Cascade again, in three more test files. `CnamVlcTests` had **no** resolver mock at all, so it needed one plus a fixed `ClinicId` — and the row under test had to be moved into that clinic, otherwise the new guard correctly refuses it and the test would fail for the right reason. `CnamNomenclatureCrudTests` and `MedicationCrudTests` only needed their existing mock threaded into the `Confirm*` constructions.

Two tooling notes, both cost a cycle: a `python - <<'PY'` heredoc **hung for 7 minutes** because python is not installed here (the Store shim blocks on stdin) — use node, or write the script to the scratchpad. And a multi-line JS anchor silently failed to match because the files are **CRLF**; single-line `split`/`join` worked, and the fix went in via the edit tool.

### AC-9.3 — the tests that actually pin finding 10

New `Features/Catalogs/CatalogTenantIsolationTests` (11 cases) covering all 10 commands.

**Why they are meaningful where the CRUD tests are not.** A mocked repository applies no query filter at all, so having it return a row owned by *another* clinic is exactly what "the EF filter is inactive" looks like from the handler's side. The existing CRUD tests pass a row from the caller's own clinic, so they would pass with or without the guard.

Each case asserts three things: the operation fails, it reads as **"introuvable"** rather than "forbidden" (no existence disclosure), and **nothing is saved**. The `Confirm*` cases assert the caller's row *is* confirmed while the other clinic's is **not** — a set-level check, since there is no id to guard. One case covers the fail-open resolver itself: with no clinic resolvable the handler refuses outright and never reaches the row.

**Mutation-verified, not merely green.** Removing the `ClinicId` comparison from `DeactivateDentalActCommand` makes `DeactivateDentalAct_Refuses_Another_Clinics_Row` **fail**; restoring it makes it pass. Without that check these tests could have been decorative.

> **Caught a false negative in my own verification.** The first mutation attempt used `dotnet test --no-build` and reported 11/11 passing *with the guard removed* — it was running stale DLLs. Re-running without `--no-build` produced the expected failure. **Do not use `--no-build` when the production code has just changed**, or a test run will happily confirm the previous build's behaviour.

| Gate | Result |
|---|---|
| Full suite | **908 passed / 8 failed** — same 8 pre-existing |

### P4.6 — the role-policy guard

New `Api/AdminSurfaceCoverageTests` (5 cases). Scoped as a **rule** rather than a whole-API allow-list, because the finding was not "one endpoint was wrong" — it was that **three of the four** catalogs were correctly gated and the fourth was missed. A per-endpoint fix does not stop that recurring, so the guard states: *every mutating action on a catalog controller must be admin-gated.* A new write on any of them fails the build until its policy is decided.

It also asserts the inverse — catalog **reads** must **not** be admin-gated. Over-gating would be its own defect: a secretary booking an appointment needs to read the procedure catalog.

Deliberately narrower than a 200-entry policy matrix: a rule that is exactly right for a well-defined set of controllers is worth more than an allow-list nobody maintains. A fifth case pins that the named actions still exist, so a rename cannot silently empty the guard.

**Mutation-verified:** removing the gate from `DELETE /api/procedure-types/{id}` makes the guard fail *and name the offending endpoint*; restoring it passes.

| Gate | Result |
|---|---|
| Full suite | **913 passed / 8 failed** — same 8 pre-existing |

**Still open in P4** (do not assume US-6–US-9 are closed):

| Item | Note |
|---|---|
| P4.5 frontend gating | The newly admin-only controls still render for non-admins, who will now get a 403 |

| Gate | Result |
|---|---|
| Backend build | 0 errors |
| Full suite | **897 passed / 8 failed**, twice — same 8 pre-existing |

**Testing note.** The ordering assertion (AC-14.2) is proved indirectly but soundly: the dummy `pg_dump` is a real file (so the existence check passes) but not a real executable, so the dump throws. The hardening is still recorded — which is only possible if it ran *first*. If it ran after the dump it would never be recorded at all.
