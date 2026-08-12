# Implementation Plan: Hosted Security Hardening

**Status:** APPROVED
**Challenged:** Yes (2026-08-12 — 10 issues, all applied; see *Challenge outcomes* at the end)
**Created:** 2026-08-12
**Approved:** 2026-08-12
**Spec:** [features/hosted-security-hardening/spec.md](./spec.md) (APPROVED 2026-08-12)
**Context:** [features/hosted-security-hardening/exploration.md](./exploration.md) — every `file:line` fact this plan rests on

---

## Overview

One user story, delivered in **five ordered parts** (a Part 0 unblocker plus the spec's four). This is the spec's own single story — *harden every layer behind the TLS edge on `HostedMultiTenant`* — and it is planned as one story at the user's explicit direction, against the sizing heuristic. To keep it implementable, each part is a **self-contained vertical increment with its own commit, its own gate run and its own revert procedure**, and the part boundary is the natural split point if the work is ever divided across sessions. Within a part, steps are grouped into numbered increments (1.1, 1.2, …) that are individually landable in that order.

**Architectural decisions taken during planning** (each answered a real fork; the rejected options are recorded in the risk register or inline):

| Decision | Choice | Why |
|---|---|---|
| Clinic second factor storage | `ProtectedTotpSecret` / `TotpEnrolledAt` **on `User`**, plus a new `UserRecoveryCode` child mirroring `PlatformRecoveryCode` | The spec says mirror, not re-invent. `PlatformAccount` already records why two populations keep two copies rather than a shared base: the numbers are a policy decision per population — and here the FK shapes differ too (`User` is keyed by `string`, `PlatformAccount` by `Guid`). |
| Replay detection (FR-1.6) | New **`SessionFamily`** row per sign-in holding `CurrentCredentialHash` + `PreviousCredentialHash` | FR-1.6 verbatim. Preserves today's deliberate two-tabs-both-work behaviour, which a sequence counter cannot (two tabs racing both mint `seq+1`). |
| Step-up placement | Endpoint + dialog in **Part 1**; the archive call sites in Part 4 | FR-1.4 path 2 needs it in Part 1, and the spec's own API table lists it there. Without it, Part 1 ships a one-click way for a stolen admin session to strip a colleague's factor. |
| Internal CA (Part 2) | A **one-shot `certs` compose service** ordered before `postgres` via `service_completed_successfully` | `CertificateProvisioner` is pre-`Build()` and Windows-service-shaped (exploration § 2.4). Part 2's gate is "bring the stack up from cold", which a hand-run script cannot satisfy, and a console verb runs in a container that starts after the database it needs certificates to reach. |
| Backup encryption (Part 3) | **`age`** for the sidecar, **`WALG_LIBSODIUM_KEY`** for PITR | Two paths, two tools. `age` runs *inside* the backup run, so FR-3.7's "decrypt it and confirm it parses" is a real step in the same script rather than a round trip to the remote. An rclone crypt remote would put the encryption in a gitignored config file, invisible and unverifiable. |
| Audit chain key + serialisation (Part 4) | `Audit:ChainKey` from config, **fail-loud**; appends serialised on `pg_advisory_xact_lock(chainKey)` **inside one explicit transaction on the audit context**, plus a unique `(ClinicId, Sequence)` index | Deliberately **not** the Data Protection ring: Part 3 re-protects that ring and FR-3.9 makes it the thing a restore may fail to read, so chain verification must not depend on it. **Lock and index are both needed, and neither replaces the other**: the lock is what stops *ordinary* concurrency producing declared gaps (the index alone would put a retry-on-conflict path inside a swallow-and-log interceptor — a gap generator under normal load), and the index is what makes a missed or mis-scoped lock impossible to hide. ⚠️ The lock is per **chain**, and null-`ClinicId` rows are their own deployment-wide chain — see Part 4 step 4. |
| FR-3.10 (file-based secrets) | **Kept in full** | Every `${VAR}` in the hosted compose file moves to a Docker secret with `*_FILE` indirection. This is the largest single item in Part 3 by file count — see R-9. |
| Step-up alternative (OQ-2) | Password **or** a current TOTP code, both spending the same dedicated counter | Keeps AC-7 true for a shell user who signs in by biometrics. Not gated on a client header — this codebase refuses to make a gate's strength depend on one. |
| Log retention (OQ-5) | **30 days** on the durable volume |
| Restore-drill cadence (OQ-6) | **Quarterly, plus after any schema-batch deploy** | Pairs with the existing before/after `verify-schema` diff workflow. |
| Delivery | One branch, **one commit per part**, **one PR at the end** | Each part keeps its own revert procedure as a pickable commit, which the spec requires. |

**Patterns followed throughout:** the derived-guard house style (`exploration.md` § 5.1 — criterion in the docstring, candidate set by reflection or a `SolutionSources` scan, `Assert.NotEmpty`, exceptions as a name→reason map asserted equal in both directions, an executed red-proof); `verify-schema` checks that call the **real** production code rather than re-expressing it in SQL; French refusals with their code in the same file; every mode branch asking a named `DeploymentProfile` capability, never `IsLocalMode`.

---

## Files to Modify/Create

### Part 0 — the restore unblocker

| File | Change |
|---|---|
| `api/ClinicManagement.Application/Features/Backup/Archive/ClinicArchiveRestorer.cs` | **Read only — already correct.** Verify one `ForgetRestoredRows()`, *after* `SaveChangesAsync` (`:77-85`). The pre-save probe `exploration.md` § 4.2 describes has been reverted; deleting the surviving call would restore the quadratic-restore defect |
| `api/ClinicManagement.UnitTests/Features/Backup/ClinicArchiveRestorerTests.cs` | New/extended: a restore reporting *N* restored actually persists *N* rows |
| `features/hosted-security-hardening/exploration.md` | § 4.2's "LIVE DEFECT" note corrected — it is fixed |

### Part 1 — Identity

**Create**

| File | Purpose |
|---|---|
| `api/ClinicManagement.Domain/Entities/UserRecoveryCode.cs` | Single-use recovery code, child of `User` — `PlatformRecoveryCode`'s shape and alphabet |
| `api/ClinicManagement.Domain/Entities/SessionFamily.cs` | One sign-in's credential lineage: current + immediate predecessor hash, expiry, ended-at + reason |
| `api/ClinicManagement.Domain/Repositories/ISessionFamilyRepository.cs` | Lookup by family id, purge of expired families |
| `api/ClinicManagement.Infrastructure/Persistence/Configurations/UserRecoveryCodeConfiguration.cs` | EF config |
| `api/ClinicManagement.Infrastructure/Persistence/Configurations/SessionFamilyConfiguration.cs` | EF config + `(UserId, ExpiresAtUtc)` and unique `CurrentCredentialHash` indexes |
| `api/ClinicManagement.Infrastructure/Persistence/ApplicationDbContext.cs` *(modify)* | `SessionFamily` added to `SkipsConcurrencyToken`, with the set's required argument (see increment 1.5) |
| `api/ClinicManagement.Infrastructure/Repositories/SessionFamilyRepository.cs` | Implementation |
| `api/ClinicManagement.Infrastructure/Security/UserSecretProtector.cs` | `IUserSecretProtector` — `PlatformSecretProtector`'s twin with **its own purpose string**, so a clinic TOTP ciphertext and a console one are not interchangeable |
| `api/ClinicManagement.Application/Common/Interfaces/IUserSecretProtector.cs` | The `bool TryUnprotect` seam (never a nullable a caller can `??` past) |
| `api/ClinicManagement.Application/Features/Auth/ClinicAuthRefusals.cs` | The clinic-side refusal vocabulary + French sentences in one file, `PlatformAuthRefusals`' shape |
| `api/ClinicManagement.Application/Features/Auth/Commands/EnrolTotpCommand.cs` | Anonymous enrolment carrying the password; returns the recovery codes once, **no session** |
| `api/ClinicManagement.Application/Features/Auth/Commands/RedeemRecoveryCodeCommand.cs` | Sign in with a recovery code; the code is spent even when the sign-in then fails |
| `api/ClinicManagement.Application/Features/Auth/Commands/DisableTotpCommand.cs` | Non-admin disables their own factor by presenting a current code |
| `api/ClinicManagement.Application/Features/Auth/Commands/RegenerateRecoveryCodesCommand.cs` | Presenting a current code invalidates every previous code |
| `api/ClinicManagement.Application/Features/Auth/Commands/StepUpCommand.cs` | Confirms password **or** a current TOTP code; mints a short-lived single-use confirmation |
| `api/ClinicManagement.Application/Features/Auth/Queries/GetTotpStateQuery.cs` | « Sécurité »'s read: enrolled?, forced?, codes remaining |
| `api/ClinicManagement.Application/Features/Auth/TotpEnrolmentUri.cs` | Builds the `otpauth://` URI labelled with the practice name + the user's address (Stated Assumption 4) |
| `api/ClinicManagement.Application/Features/Users/Commands/ResetUserTotpCommand.cs` | An admin resets a colleague's factor; step-up required; notifies the affected user |
| `api/ClinicManagement.Application/Common/StepUpConfirmations.cs` | The dedicated failure counter + single-use-per-action confirmation store. **`AddSingleton` over `IMemoryCache` with an absolute expiry** — a scoped registration makes every step-up fail silently (see increment 1.4) |
| `api/ClinicManagement.API/Maintenance/ResetUserTotpCommand.cs` | The `reset-user-totp --email <address>` console verb |
| `api/ClinicManagement.UnitTests/Features/Auth/ClinicTotpAuthTests.cs` | The refusal ladder, the recovery-code spend rule, "no session before enrolment" |
| `api/ClinicManagement.UnitTests/Features/Auth/SessionFamilyTests.cs` | Predecessor accepted, older refused, only that family ends |
| `api/ClinicManagement.UnitTests/Common/SecondFactorCoverageTests.cs` | Derived guard: no session reaches an administrator without a verified factor |
| `api/ClinicManagement.UnitTests/Common/PasswordFloorSingleSourceTests.cs` | Derived guard: every client statement of the minimum reads the served value |
| `web/components/security/totp-enrolment-step.tsx` | QR on a fixed light plate + tappable link + copy control + grouped secret |
| `web/components/security/recovery-codes-panel.tsx` | Copy / download / print + explicit acknowledgement |
| `web/components/security/step-up-dialog.tsx` | Sheet below `md:`; focus lands on the field; `Escape` closes |
| `web/app/securite/page.tsx` | The per-account « Sécurité » surface, reachable by every role |
| `web/lib/api/security.ts` | Client for the TOTP + step-up endpoints |
| `web/lib/api/client.ts`, `web/lib/auth/session.tsx` *(modify)* | **`onSecondFactorRequired`** — the fourth refusal hook, on `onMustChangePassword`'s shape, routing a per-request `totp_enrolment_required` to the login screen's enrol mode (step 28) |

**Modify**

| File | Change |
|---|---|
| `api/ClinicManagement.Domain/Entities/User.cs` | `ProtectedTotpSecret`, `TotpEnrolledAt`, `IsTotpEnrolled`, `UnusedRecoveryCodeCount`, `IssueTotpSecret`, `CompleteTotpEnrolment`, `ConsumeRecoveryCode`, `DisableTotp`; **`PromoteToAdmin()` bumps `TokenVersion`** (FR-1.5 — it is the one mutator on this class that does not) |
| `api/ClinicManagement.Application/Common/PasswordPolicy.cs` | `MinLength` 8 → **12**, enforced on set only |
| `api/ClinicManagement.Application/Features/Auth/Commands/LoginCommand.cs` | The eight-step refusal ladder with **codes** (it returns bare prose today); optional `Code`; per-request enrolment requirement |
| `api/ClinicManagement.Application/Features/Auth/Commands/RefreshTokenCommand.cs` | Family lookup, rotation, replay detection, family end + notification |
| `api/ClinicManagement.Application/Features/Auth/Queries/GetAuthModeQuery.cs` *(or the controller's inline read)* | Publishes `passwordMinLength` + `requiresSecondFactor` |
| `api/ClinicManagement.Application/Features/Platform/…` + `PlatformReadShape.cs` | The same floor served on a **`/api/platform`** read, because `ConsolePortGate` makes `/api/auth/mode` unreachable from `console/`; `passwordMinLength` added to `AllowedLeafNames` |
| `api/ClinicManagement.Application/Features/Users/Commands/ChangeUserRoleCommand.cs` | Promotion to admin revokes the session (already via `ChangeRole`; assert it) |
| `api/ClinicManagement.Infrastructure/Auth/LocalAuthService.cs` | `GenerateTemporaryPassword` length reads `PasswordPolicy.MinLength` rather than coinciding with it; refresh token carries `family_id` |
| `api/ClinicManagement.Infrastructure/Deployment/DeploymentProfile.cs` | 18th capability **`RequiresAdminSecondFactor`** (`HostedMultiTenant` ✓ only) — the five edits of § 5.2 |
| `api/ClinicManagement.API/Controllers/AuthController.cs` | `totp/enrol`, `recovery`, `totp` GET/POST, `step-up`; status mapping from the refusal **code** |
| `api/ClinicManagement.API/Controllers/UsersController.cs` | `POST {id}/totp/reset` |
| `api/ClinicManagement.API/Program.cs` | Dispatch branch for `reset-user-totp`; the admin backfill revokes sessions |
| `api/ClinicManagement.Application/Common/Maintenance/SchemaVerificationService.cs` | `admins-without-a-factor-holding-a-live-session`, `session-families-have-no-orphans`, `server-clock-drift` (Info; its own text states that a host-wide drift is invisible to it) |
| `api/ClinicManagement.UnitTests/Infrastructure/Deployment/DeploymentProfileTests.cs` | Matrix row + `hostedOnlyCapabilities` entry |
| `api/ClinicManagement.UnitTests/Api/SubscriptionExemptionCoverageTests.cs` | The four new exempt writes (FR-1.10) |
| `web/app/login/page.tsx` | Four modes (`login` / `enrol` / `recovery` / `codes`), carrying the address and password across; moves to the two existing full-screen gates' scroll pattern |
| `web/app/bff/auth/local-login/route.ts` | Relays `code` **for the second-factor refusals only** |
| `web/lib/auth/session-cookie.ts` | `__Host-` prefix + `SameSite` hardening, applied only where the connection is secure; both cookies together. **Exports `sessionCookieNames(secure)`** — the name is conditional, so this becomes the single authority for reading as well as writing |
| `web/lib/auth/local-auth.ts` | Cookie constants become the unprefixed *base* names the resolver composes from |
| `web/middleware.ts` | Reads through the resolver; `/securite` is not public |
| `web/app/bff/auth/session/route.ts`, `web/app/bff/auth/token/route.ts` | Read/re-set through the resolver — a reader left on a bare constant is the silent-bounce failure |
| `web/components/change-password-form.tsx`, `web/components/join-wizard.tsx`, `web/components/setup-wizard.tsx` | Read the **served** floor; `setup-wizard`'s password rule stops being gated on `isLocalMode` (it is gated on the wrong condition today — see FR-1.8) |
| `web/lib/nav.ts`, `web/lib/zones.ts` | The « Sécurité » entry, every role |
| `console/app/login/sign-in-form.tsx` | The floor as prose → served value |

### Part 2 — Transit

**Create**

| File | Purpose |
|---|---|
| `deploy/certs/Dockerfile`, `deploy/certs/issue.sh` | The one-shot internal CA + two SAN leaves, idempotent |
| `api/ClinicManagement.API/Startup/TransportAssurance.cs` | Fail-loud startup check on the deployment **kind** — `!SelfHostsFrontDoor`, i.e. **both hosted kinds**, because `extends` puts the configuration on both (see Part 2 step 8) |
| `api/ClinicManagement.API/Middleware/OriginalPeer.cs` | Captures `Connection.RemoteIpAddress` **before** `UseForwardedHeaders`, so the two loopback gates cannot be decided by a forgeable header |
| `api/ClinicManagement.UnitTests/Deploy/TransportConfigurationTests.cs` | Derived guard parsing `deploy/docker-compose.hosted.yml` |

**Modify**

| File | Change |
|---|---|
| `deploy/docker-compose.hosted.yml`, `deploy/docker-compose.prod.yml` | `certs` service + `internal_certs` volume; `sslmode=verify-full` + `Root Certificate=`; `MinIO__UseSSL: "true"`; sidecar + PITR credentials brought across in the same change |
| `deploy/postgres/Dockerfile` | `ssl=on`, cert paths, `pg_hba.conf` with `hostssl` only |
| `deploy/backup/backup.sh`, `deploy/postgres/pitr-backup.sh` | TLS-verified connections; a sidecar that cannot negotiate **fails the run** |
| `api/ClinicManagement.Infrastructure/Extensions.cs` | MinIO client with TLS + the internal root |
| `api/ClinicManagement.API/Program.cs` | `UseForwardedHeaders` bounded by `Security:TrustedProxies`; ignored **entirely** and stated in the log when the setting is empty or wrong; `TransportAssurance` before the host runs |
| `api/ClinicManagement.Infrastructure/LocalRequest.cs` | Reads the original peer, not the substituted address |
| `api/ClinicManagement.Application/Common/Maintenance/SchemaVerificationService.cs` | `internal-certificate-days-remaining` |
| `deploy/.env.hosted.example`, `deploy/README.md` | The new variables and the cold-start order |

### Part 3 — Custody

**Create**

| File | Purpose |
|---|---|
| `api/ClinicManagement.API/Startup/KeyRingGenerationMarker.cs` | Writes the ring's active key id to a shared volume at startup, so the sidecar can stamp a dump (FR-3.9) without the ring ever being mounted into it |
| `deploy/KEY-CUSTODY.md` | FR-3.8's deliverable: for each of the three keys — where it lives, who holds a copy, where that copy is kept, how to use it in a disaster |
| `deploy/RESTORE-DRILL.md` | FR-3.7's documented drill, cadence and pass condition |
| `api/ClinicManagement.UnitTests/Common/SecretProtectionCoverageTests.cs` | Derived guard: every credential-shaped property is protected or a named decision |

**Modify**

| File | Change |
|---|---|
| `api/ClinicManagement.Infrastructure/Security/LocalDataProtection.cs` | `ProtectKeysWithCertificate` + `UnprotectKeysWithAnyCertificate` (retained generations). ⚠️ This protects **new** keys only — the ciphertext migration below is what makes FR-3.1 true |
| `api/ClinicManagement.API/Maintenance/ReprotectSecretsCommand.cs` | **New**: the `reprotect-secrets` verb — re-`Protect`s all six protected column families under the ring's new active key, idempotent, naming any row it cannot decrypt |
| `api/ClinicManagement.API/Program.cs` | Dispatch branch for `reprotect-secrets`; extend the verb-reachability guard |
| `api/ClinicManagement.Domain/Entities/Clinic.cs` | `GoogleRefreshTokenProtected` beside the plaintext column during the backfill window |
| `api/ClinicManagement.Infrastructure/Services/GoogleCalendarSyncService.cs` | Reads the protected value; an undecryptable token **refuses and names the recovery verb** (FR-3.3), never falls back |
| `deploy/docker-compose.hosted.yml` / `.prod.yml` | `secrets:` block + `*_FILE` for **every** secret (FR-3.10); the key-ring protecting certificate; the `keyring_marker` volume |
| `api/ClinicManagement.API/Startup/InstallConfiguration.cs` | A `*_FILE` configuration layer, applied by the host **and all console verbs** |
| `deploy/backup/Dockerfile`, `deploy/backup/backup.sh` | `age`; encrypt before rclone; **verify by decrypting and `pg_restore --list`**; stamp the key-ring generation |
| `deploy/postgres/pitr-entrypoint.sh` | `WALG_LIBSODIUM_KEY` |
| `deploy/README.md` | FR-3.11 — one statement, in one voice: once the ring is encrypted, what travels separately is the **certificate** |
| `api/ClinicManagement.Application/Common/Maintenance/SchemaVerificationService.cs` | `google-token-protected` |

### Part 4 — Evidence & surface

**Create**

| File | Purpose |
|---|---|
| `api/ClinicManagement.Domain/Services/AuditChain.cs` | Pure: `Hash(previous, entry, key)` and the walk — the one arithmetic `verify-schema` re-runs |
| `api/ClinicManagement.Application/Features/Backup/ArchiveAccessLedger.cs` | The non-best-effort record of a full-cabinet export |
| `api/ClinicManagement.API/Controllers/CspReportController.cs` | Anonymous, bounded, **addresses stripped to their route pattern** |
| `api/ClinicManagement.UnitTests/Common/LogTemplateCoverageTests.cs` | Derived guard: no log template names a patient |
| `api/ClinicManagement.UnitTests/Common/ContentSecurityPolicyAgreementTests.cs` | Derived guard: the middleware, `deploy/Caddyfile` and the console site agree |

**Modify**

| File | Change |
|---|---|
| `api/ClinicManagement.Domain/Entities/AuditEntry.cs` | `Sequence`, `PreviousHash`, `EntryHash`, `IsDeclaredGap`, **`ChainKey`** (`ClinicId ?? Guid.Empty` — `ClinicId` stays nullable) |
| `api/ClinicManagement.Infrastructure/Persistence/AuditSaveChangesInterceptor.cs` | Per-clinic advisory lock, chain append, **declared gap on a failed write** — still never rolling back the audited operation |
| `api/ClinicManagement.API/Controllers/BackupController.cs` | Archive: step-up, its own rate-limit policy, the ledger row, delivered-vs-requested |
| `api/ClinicManagement.API/Startup/RateLimiting.cs` | The archive policy + the CSP-report bound |
| `api/ClinicManagement.API/Middleware/SecurityHeadersMiddleware.cs` | `'unsafe-eval'` dropped, `report-to`, `Permissions-Policy`, COOP/CORP |
| `deploy/Caddyfile` | The same policy byte-for-byte; the console site gains one |
| `console/next.config.ts` | `headers()` |
| `web/app/layout.tsx`, `web/package.json` | `@vercel/analytics` removed |
| `api/ClinicManagement.Infrastructure/Services/PdfGenerationService.cs`, `GoogleCalendarSyncService.cs`, `HuggingFaceAIService.cs`, `SmtpDocumentEmailSender.cs` | The eleven PHI templates + the raw model payload + the file name → identifiers |
| `api/ClinicManagement.API/appsettings.json`, `deploy/docker-compose.hosted.yml` | Durable log volume, `retainedFileCountLimit: 30`, `Security__EnforceCsp: "true"`, `Audit__ChainKey` |
| `api/ClinicManagement.API/Program.cs` | FR-4.6 — `UseHttpsRedirection` configured or removed |
| `api/ClinicManagement.Application/Common/Maintenance/SchemaVerificationService.cs` | `audit-chain-intact` + `audit-declared-gaps` (reported apart) |

---

## Implementation Stories

### US-1: Every layer behind the TLS edge is hardened, and each guarantee is checkable

**Goal:** A stolen credential, a stolen disk or a stolen backup does not yield a practice's medical records, and what happened to that data can be reconstructed afterwards — on `HostedMultiTenant`, with `SelfHostedLan` and `CloudBrowser` unchanged except where a change is explicitly global.
**Blocked by:** None
**Layers:** DB · Domain · Application · API · Infrastructure · Deploy · UI · Docs

This story is delivered in five ordered parts. **Each part is one commit and must pass its own gate before the next begins.**

---

#### Part 0 — The restore actually restores *(unblocks Part 4's verification)*

**Why it is here:** Part 4 puts a step-up and a non-best-effort ledger row in front of `POST /api/backup/archive/restore`, and its gate is "confirm the archive is refused when the ledger cannot be written". Verifying that against an operation that persists nothing proves nothing. The spec puts this fix out of scope *as a feature*; it is included here as a prerequisite commit, unchanged in intent.

⚠️ **The code defect is already gone — do not "fix" it again.** `exploration.md` § 4.2 records a
`store.ForgetRestoredRows(); // RED PROOF — revert` call running **before** `SaveChangesAsync`; that line no longer
exists. `ClinicArchiveRestorer.cs:77-85` now reads `SaveChangesAsync` followed by a **single**
`ForgetRestoredRows()` *after* it, carrying the comment that explains why (EF re-scans every tracked entry on each
later save, so a full-cabinet restore across thirty tables would otherwise be quadratic — the `IUnitOfWork.StopTracking`
reasoning). **That surviving call must stay.** Deleting "the one before the save" now deletes the guard.

**Steps:**
1. Confirm by reading `ClinicArchiveRestorer.cs:77-85` that there is exactly **one** `ForgetRestoredRows()` and it is
   **after** `SaveChangesAsync`. If a second one has reappeared before the save, delete that one only.
2. Add the test that is still genuinely missing: a restore reporting *N* restored rows leaves *N* rows persisted after
   the save. Prove it red by re-introducing the pre-save call in a throwaway edit, then revert that edit.
3. Correct `exploration.md` § 4.2, which still documents the defect as live — a carried-context file that describes a
   fixed defect sends the next session to delete working code.

**Validation:**
- [ ] Exactly one `ForgetRestoredRows()`, after the save
- [ ] The new test proven red against a re-introduced pre-save call, green against the real code
- [ ] `exploration.md` § 4.2 corrected
- [ ] `dotnet test` green (build to a path outside the repo: `-p:BaseOutputPath=<temp>`, and never with `--no-build`)

---

#### Part 1 — Identity

**Increment 1.1 — the capability and the served password floor**

1. Add the 18th capability **`RequiresAdminSecondFactor`** to `DeploymentProfile.cs` via § 5.2's five edits, ✓ for `HostedMultiTenant` alone, with an XML doc stating why each ✗ is its own decision (on a clinic's own PC, a locked-out administrator with no vendor to call is worse than the threat). Add the `ExpectedMatrix` row **and** the `hostedOnlyCapabilities` entry, or `Both_pre_existing_kinds_reproduce_the_old_IsLocalMode_truth_table` fails.
2. Raise `PasswordPolicy.MinLength` to **12**. Confirm every one of the five enforcement sites is on the *set* path only (`CreateClinicCommand`, `JoinClinicCommand`, `SignUpClinicCommand`, `ChangePasswordCommand`, `ChangePlatformPasswordCommand`) — never on a check.
3. Make `LocalAuthService.GenerateTemporaryPassword` derive its length from `PasswordPolicy.MinLength` (it is coincidentally 12 today; a raised floor must not silently outrun it).
4. Publish `passwordMinLength` and `requiresSecondFactor` on `GET /api/auth/mode`, mirroring how `trialDays` is already served.
5. Replace the five client literals with the served value: `change-password-form.tsx` (the `MIN_PASSWORD_LENGTH` const), `join-wizard.tsx` (×2) and `setup-wizard.tsx` (×2). **`setup-wizard`'s rule moves out of the `if (isLocalMode)` branch** — it serves both first-run setup *and* public signup, and `mode` is fetched asynchronously, so before it resolves the signup flow validates step 2 against the Cloud branch and never checks the password at all. Gate on "this step collects a password", not on the deployment.
6. Serve the floor to the **console** on a `/api/platform` read, and replace its prose statement with that value.
   ⚠️ **The console cannot call `GET /api/auth/mode`, so the obvious version of this step 404s.** `console/lib/api/client.ts:11` points at `http://api:5443/api` — the console listener — and `ConsolePortGate` refuses **both** directions on it: anything not under **`/api/platform`** is 404, matched with `StartsWithSegments` (`ConsolePortGate.cs:41, 69-70`). Publish `passwordMinLength` on the platform surface the console already reaches (a field on the existing platform auth/meta response, or one small `/api/platform/meta` read), add its name to **`PlatformReadShape.AllowedLeafNames`** (the closed set is asserted equal in both directions, so an unlisted name fails the build), and read it there. One authority for the number, and the port separation stays intact.
7. Write `PasswordFloorSingleSourceTests`: scan `web/` and `console/` sources for a hard-coded password-length minimum and assert the set is empty, proven red by re-adding one.
   ⚠️ **It cannot use `SolutionSources.Root`, which never sees either directory.** That helper walks up to **`ClinicManagement.sln`, which lives in `api/`** (`SolutionSources.cs:18-31`), so `CsFiles(Root())` enumerates the backend only and the guard would pass while checking nothing. Locate the two roots the way `RealtimeResourceResolverTests.ClinicHubPath` does — walk up from `[CallerFilePath]` looking for the **relative path** (`web/`, `console/`) and **throw** when it is absent, never skip.
   ⚠️ **Two more house-style requirements the "derived, no exemption list" wording skips.** (a) `Assert.NotEmpty` on the **scanned-file count** before asserting the violation set is empty — "found nothing" must not read as "nothing was wrong", the lesson `SystemWideCallerCoverageTests`' console-verb branch cost. (b) Anchor the pattern on **password-length identifiers** (`MIN_PASSWORD_LENGTH`, `minLength`, and a length comparison within a few lines of `password`/`motDePasse`), not on bare numeric literals, which match unrelated numbers across `web/` — a too-loose check is noisy and gets deleted, a too-tight one is silent. Carry any genuine exception as a `Dictionary<file, reason>` asserted **equal in both directions**, so a stale exemption fails too.

**Increment 1.2 — the factor itself, and the login screen that enrols it**

8. Add to `User`: `ProtectedTotpSecret`, `TotpEnrolledAt`, `IsTotpEnrolled`, `UnusedRecoveryCodeCount`, `IssueTotpSecret` (clears the previous enrolment and every code, bumps `TokenVersion`), `CompleteTotpEnrolment`, `ConsumeRecoveryCode`, `DisableTotp`. Create `UserRecoveryCode` as `PlatformRecoveryCode`'s twin — same 32-symbol alphabet with no `0/O/1/I`, length 20, 8 per enrolment, hex SHA-256 of a normalised code, `Consume()` throwing on a second call.
9. Add `IUserSecretProtector` / `UserSecretProtector` with **its own purpose string** (`ClinicManagement.User.TotpSecret.v1`), registered `AddSingleton` inside `AddInfrastructure` so the console verb can resolve it. `TryUnprotect` returns a bool, sets the out parameter to empty first, catches everything, logs a French sentence naming the recovery verb, and never yields the input.
10. Write the migration: the two `User` columns, `UserRecoveryCodes`, `SessionFamilies` (1.7 needs it and one migration is cheaper than two). **Delete the scaffolded `xmin` column line from every `CreateTable`** — PostgreSQL rejects it. Every `AddColumn`/`CreateIndex` first, any backfill last. Commit the model snapshot with the migration.
11. Add `ClinicAuthRefusals` — the codes and their French sentences in one file, `PlatformAuthRefusals`' shape, with a reflection-derived `AllCodes`. Codes: `invalid_credentials`, `totp_required`, `totp_enrolment_required`, `totp_invalid`, `totp_already_enrolled`, `account_disabled`, `too_many_attempts`, `password_policy`.
12. Rewrite `LoginCommandHandler`'s ladder in `PlatformLoginCommand`'s order: unknown → lockout → password → deactivated → **not enrolled** (`totp_enrolment_required`, 403, carrying nothing else) → blank code (`totp_required`, 401) → wrong code (`RecordFailedLogin` + save → `invalid_credentials`) → rehash → success. Attach the code to the `Result`; the **controller** maps code → status, as `PlatformAuthController.StatusFor` does. Preserve the existing inactive/pending distinction, which is richer than the console's. Enrolment is required **only** where `RequiresAdminSecondFactor` and the role is `admin`; a doctor or secretary who has voluntarily enrolled is still asked for a code.
13. Add `EnrolTotpCommand` (anonymous, carries the password, mints nothing until the code verifies, returns the eight codes once, **issues no session**) and `RedeemRecoveryCodeCommand` (password verified first so a wrong password burns nothing; `ConsumeRecoveryCode`; **its own `SaveChangesAsync` before** the `IsActive` check, so a code is spent even when the sign-in then fails).
14. Add `TotpEnrolmentUri` producing `otpauth://totp/{practice}:{email}?secret=…&issuer={practice}`, and return **both** the URI and the readable secret in the enrolment response body — an image tag cannot carry a credential before a session exists.
15. Wire `AuthController`: `POST totp/enrol`, `POST recovery`, both `[AllowAnonymous]` and inside the anonymous-auth rate limit. Add them to `ControllerAuthorizationCoverageTests`' reviewed `ExpectedAnonymous` set (it is asserted equal in both directions, so the build fails until they are reviewed on).
16. Relay the machine-readable part in `web/app/bff/auth/local-login/route.ts` — **for the second-factor codes only**, leaving an ordinary bad-credentials answer flattened exactly as it is now.
17. Rework `web/app/login/page.tsx` into four modes (`login` / `enrol` / `recovery` / `codes`), carrying the address and password across, transitioning on the refusal's **code** and never on a French sentence — `console/app/login/sign-in-form.tsx` is the working reference. Move the card to the scroll pattern the two existing full-screen gates share (`session-lock-gate.tsx` / `client-version-gate.tsx`): `fixed inset-0 h-dvh items-start overflow-y-auto` with `my-auto` on the child. `items-center` inside a scroller clips the top on a landscape phone.
18. Build `totp-enrolment-step.tsx` (QR on a **fixed light plate at a stated minimum size regardless of theme**; a tappable `otpauth:` link; a copy control; the secret in short groups; a failed QR render shown as a failure with a retry, never an empty box) and `recovery-codes-panel.tsx` (copy, download **through `lib/download.ts`**, print; explicit acknowledgement; the live region announces a summary, **not eight codes read aloud**).
19. The code field: `type="text"` + `inputMode="numeric"` + `autoComplete="one-time-code"`, one field not six boxes, whitespace stripped, leading zero preserved. `min-h-11` written explicitly on every new button — `size="lg"` is 40 px.

**Increment 1.3 — « Sécurité », reachable by every role**

20. Add `GetTotpStateQuery`, `DisableTotpCommand` (a current code required; **refused for an admin where `RequiresAdminSecondFactor` is on**, and the screen says so in words rather than hiding the control), `RegenerateRecoveryCodesCommand` (a current code; invalidates every previous code).
    ⚠️ **The admin refusal is gated on the capability, not on the role alone.** An unconditional refusal would leave a `SelfHostedLan` or `CloudBrowser` administrator who enrolled voluntarily permanently unable to disable a factor their deployment never required — a control with no way out, on the two profiles the capability's own XML doc says must not have one (FR-1.1: a locked-out administrator with no vendor to call is worse than the threat). `GetTotpStateQuery` carries the same flag so the screen's wording follows it.
21. Add `GET/POST /api/auth/totp` and the `/securite` page, in `nav.ts` and `zones.ts` for **every** role — not « Mon profil » (the practitioner's document identity, which does not exist for a secretary) and not « Paramètres » (clinic-wide, admin-shaped). Warn at **two or fewer** codes remaining, wherever the user can act on it. No nudge and no prompt anywhere else.

**Increment 1.4 — step-up, and the ways back**

22. Add `StepUpCommand` + `POST /api/auth/step-up`: accepts the password **or** a current TOTP code, spends **its own** counter (never the login lockout), and mints a short-lived confirmation that is **single-use per action**. Three wrong attempts refuse on that counter with the session untouched, and the screen says the session is still fine.
    ⚠️ **`StepUpConfirmations` is a `AddSingleton` over `IMemoryCache`, and the lifetime is load-bearing.** The confirmation is minted by one request and consumed by another, so a scoped or transient registration builds a fresh store per request, the confirmation is never found, and **every** export refuses with a French « mot de passe incorrect » that is not incorrect — silent, and indistinguishable from the feature working. `IMemoryCache` is already registered in `AddInfrastructure`, and an absolute expiry is what makes both the short-lived confirmation and the failure counter expire without a sweep (the OAuth `state` cache's precedent). Registered in `AddInfrastructure` so the interface can stay in Application.
    ⚠️ **Stated residual:** the store is **instance-local**. The hosted deployment runs a single `api` service with no replicas today, so this is correct — but `MigrationLock` exists precisely because two containers *can* come up together, and a second replica would break step-up (mint on A, consume on B). Record it beside the registration and in `deploy/README.md`: scaling the API past one instance requires moving this store to a shared one first.
23. Add `step-up-dialog.tsx` — a sheet below `md:`, focus on the field, cancel returns focus to the opener, `Escape` closes.
24. Add `ResetUserTotpCommand` + `POST /api/users/{id}/totp/reset` (`AdminOnly`, step-up required, **notifies the affected user** in-app and by e-mail), and the staff-list control beside the existing password reset.
25. Add the `reset-user-totp --email <address>` console verb: `InstallConfiguration.BuildForConsoleVerb()` → `MaintenanceDatabase.HasConnectionString` → `AddInfrastructure` **only** → `RunAs(CommandName)` → `UseSystemWide(reason)`. Re-issues a secret and invalidates the previous authenticator and every recovery code. **Add its dispatch branch in `Program.cs`** — a verb with no branch boots the web host and reads as "the command did nothing" — and extend the reachability guard so it covers this verb rather than the five `Subscription*` types alone.
26. Mark enrolment, code verification, recovery redemption and step-up `[AllowsWithoutSubscription("…")]` **and** exempt from the forced-password-change gate (FR-1.10). Update `SubscriptionExemptionCoverageTests`' reviewed set. FR-1.7a: the password change wins — enrolment is checked *after* it.

**Increment 1.5 — a session that cannot be replayed, and a cookie that cannot be moved**

27. Add `SessionFamily` + its repository. `LocalAuthService` puts `family_id` in the refresh token; `RefreshTokenCommandHandler` accepts a credential matching the family's **current or immediate predecessor**, rotates (previous ← current, current ← new), and on anything older **ends that family alone** — the user's other devices keep working and the account is not globally revoked. Notify in-app and by e-mail. Purge expired families; never delete a live one.
    ⚠️ **`SessionFamily` must be added to `ApplicationDbContext.SkipsConcurrencyToken`, or FR-1.6 breaks on arrival.** That loop maps `Entity<T>.Version` onto `xmin` and calls `IsConcurrencyToken()` on **every** entity deriving from `Entity<>` (`ApplicationDbContext.cs:293-320`; today only `UserDashboardPreference` opts out) — so two tabs refreshing at once both UPDATE the family, the loser raises `DbUpdateConcurrencyException`, `UnitOfWork` translates it to `ConflictException`, and `/api/auth/refresh` answers **409** to exactly the case FR-1.6 exists to preserve. The opt-out carries that set's required argument: **a lost rotation loses no information a user typed**, which is the only test for membership. Its cost is one credential generation of slack in replay detection — the overwritten rotation leaves the *winner's* predecessor recorded, so a genuinely replayed credential from the losing generation is accepted once more before it falls behind. That is strictly within FR-1.6's own stated tolerance ("the rule is about ordering, not elapsed time") and is the trade FR-1.6 already makes by keeping a predecessor at all. Pin it with a `SessionFamilyTests` case that races two rotations over one family and asserts **both** succeed.
28. Make the enrolment requirement a **per-request** check in `LocalAuthEnforcementMiddleware`, so a session predating the requirement cannot outlive it. Make `User.PromoteToAdmin()` bump `TokenVersion` and confirm the startup admin backfill goes through it.
    ⚠️ **The client half is not optional: a refusal with no destination is an app that looks usable and is dead.** Add a fourth hook to `web/lib/api/client.ts` on `onMustChangePassword`'s exact shape — `onSecondFactorRequired`, firing on `code: "totp_enrolment_required"` — which `LocalSessionProvider` consumes to navigate to the login screen's **enrol** mode carrying the address, guarded against a redirect loop the way the existing one is (`session.tsx:176-181`). Two consequences to write down: the **enrol mode must be reachable while holding a session the API refuses** (so the exempt set of step 26 covers the enrolment endpoints for an authenticated-but-unenrolled caller too), and this becomes the **second** place `client.ts` replaces a server-sent message — stated with its reason, since the module's own docs currently say there is exactly one. Without it, every call from such a session surfaces a generic toast and no screen offers the way out.
29. Harden the cookies in `lib/auth/session-cookie.ts` — the single writer — to `__Host-` + `SameSite`, **only when `isSecure(request)`**, both cookies together. Add the French explanation on the login screen for the one-time sign-out on deploy.
    ⚠️ **This is not a constant rename, and treating it as one reproduces the symptom FR-1.7 quotes.** `__Host-` *requires* `Secure`, so where the connection is plain HTTP the name must stay unprefixed — the cookie **name is a function of `isSecure`**, not a new literal. Export **`sessionCookieNames(secure)`** (or `cookieNamesFor(request)`) from `session-cookie.ts` — already the single writer, and already where `isSecure` lives — as the one authority for **writing and reading**, then convert **every** reader: `middleware.ts` (gates on cookie presence), `/bff/auth/session` (decodes it), `/bff/auth/token` (re-sets it on every exchange), and the constants in `local-auth.ts`. Renaming the constants alone makes a plain-HTTP install write one name and read the other, i.e. « a login that appears to succeed and immediately bounces, forever, with no message ». Sweep for readers before finalising — `local_session` / `local_must_change_password` by name, and `SESSION_COOKIE` / `MUST_CHANGE_COOKIE` by symbol.
30. **Walk the two flows before this is final**: the Google Calendar OAuth callback (whose own state cookie is deliberately relaxed for exactly this reason) and the e-mailed signup verification link. If either breaks under `SameSite=Strict`, keep `Lax` and record the reason in this plan and in the spec's FR-1.7.

**Increment 1.6 — the guards**

31. `SecondFactorCoverageTests`: derive from the login/refresh/enrolment paths that no session-issuing path reaches an administrator without a verified factor where the capability is on. Proven red by removing one check.
32. `verify-schema`: **`admins-without-a-factor-holding-a-live-session`**, `session-families-have-no-orphans`, **`server-clock-drift`**.
    ⚠️ **The first check was `every-admin-has-a-factor-or-is-unenrolled`, which is a tautology** — every administrator satisfies one branch or the other, so it can never go red. That is the unfalsifiability that got `clinic-activity-day-unique-per-clinic-day` replaced (`platform-console` DEV-4). What is genuinely falsifiable is the state the per-request check of step 28 exists to make impossible: on a deployment where `RequiresAdminSecondFactor` is on, **count administrators with no verified factor that still hold a live `SessionFamily`**. Zero is the claim; a non-zero count is real drift and names the accounts.
    ⚠️ **`server-clock-offset` cannot detect the failure it was written for, and the check must say so.** The API and PostgreSQL run in containers on **one host** and read **one clock**, so an app↔DB comparison is ~0 by construction while the host drifting from real time — the case that fails every account's login at once with the same sentence as a wrong password — moves both sides together and is invisible. Report the offset as **Info** with its own text stating that limitation explicitly, and name the real control (**NTP on the host**) in `deploy/README.md` beside it. A check that reads green while its subject is broken is worse than no check, which is why the limitation goes in the check's own output rather than only here.

**Part 1 validation:**
- [ ] Backend unit suite green (`dotnet test -c Release`, `BaseOutputPath` outside the repo)
- [ ] `verify-schema` clean **before and after** the migration, outputs diffed
- [ ] `npm run check:responsive` · `npx tsc --noEmit` · `npm run build` clean in `web/` **and** `console/`
- [ ] Eye pass at 320 / 390 / 820 / 1180 / 1440, plus landscape phone, plus with the on-screen keyboard up — submit reachable in every case
- [ ] An administrator with a correct password and no factor cannot obtain a token
- [ ] A recovery code is spent by a failed sign-in; a wrong password spends none
- [ ] Two tabs refreshing simultaneously both keep working; a third-generation-old credential ends that family only
- [ ] OAuth callback and signup verification walked under the new cookie rule; outcome recorded

---

#### Part 2 — Transit

**Blocked by:** Part 1 (shares the branch; no code dependency). **The check and the configuration that satisfies it ship in the same commit** — landing the check alone stops the deployment booting.

**Steps:**
1. Add `deploy/certs/` — an alpine + openssl one-shot that mints a **ten-year** internal CA and two SAN leaves (`postgres`, `minio`) into a named `internal_certs` volume, idempotent (an existing loadable set is reused, `CertificateProvisioner`'s rule). Ten years because nobody outside these containers evaluates them, and a short lifetime plus a fail-loud startup turns any restart into a crash loop.
2. Wire it into both compose files with `depends_on: { certs: { condition: service_completed_successfully } }` on `postgres`, `minio`, `api`, `backup` and `pitr`. ⚠️ `extends` does not carry `depends_on`, so restate it in `docker-compose.hosted.yml` — the file already documents this trap.
3. Configure PostgreSQL: `ssl=on` with the leaf, and a `pg_hba.conf` that offers **`hostssl` only** (FR-2.3) — otherwise anything else on the container network still connects in the clear and the application's own setting is a courtesy.
4. Point the API's connection string at `sslmode=verify-full` with `Root Certificate=` the internal CA. Set `MinIO__UseSSL: "true"` and give the Minio client the same root.
5. **Bring the sidecars across in the same change** (FR-2.3's ⚠️): `backup.sh` and `pitr-backup.sh` connect with `sslmode=verify-full`; a sidecar that cannot negotiate **fails the run loudly** and never skips-and-reports-success.
6. Add `OriginalPeer` middleware capturing `Connection.RemoteIpAddress` **before** `UseForwardedHeaders`, and point `LocalRequest.IsLoopback` at it. This is FR-2.4's ⚠️: honouring forwarded headers makes the `/hangfire` and `setup` gates decidable by a header if the trusted-proxy bound is wrong, and those gates must not be decided by an address a header can claim.
7. Register `UseForwardedHeaders` bounded by the existing `Security:TrustedProxies`. An empty or unparseable setting **ignores forwarded headers entirely and says so in the startup log** — never an unbounded header.
8. Add `TransportAssurance`: refuse to start unless the database connection is verified-TLS and the object-store connection is TLS. Gate on the **kind** — `!SelfHostsFrontDoor`, i.e. **both hosted kinds** — never on whether a certificate file happens to be present, since a guard that switches itself off when its subject is missing is not a guard. Absent, unreadable or not-yet-valid certificates all refuse and name the file and the setting.
   ⚠️ **Why both hosted kinds and not `HostedMultiTenant` alone: the configuration reaches both.** `docker-compose.hosted.yml` `extends` `docker-compose.prod.yml`'s infrastructure, and `deploy/postgres/Dockerfile` is shared — so an `ssl=on` + `hostssl`-only server and a `certs` service land on **`CloudBrowser`** too. A check gated one kind narrower than its own configuration means a CloudBrowser deployment whose connection string was missed fails at the *first query* instead of at startup: transit failing open, LEARNINGS `:97`. **Transit is therefore a fifth global change**, alongside the password floor, the cookies, the audit chain and the logging — recorded here and in the spec's Stated Assumption 11 rather than left as an unstated side effect of `extends`. `SelfHostedLan` is untouched: it serves its own in-process front door and reaches PostgreSQL on the same machine.
9. Add `verify-schema`'s `internal-certificate-days-remaining` (Info, with the count).
10. Add `TransportConfigurationTests` — parse `deploy/docker-compose.hosted.yml` (the `RealtimeResourceResolverTests` / `CnamClosedSetContractTests` precedent for parsing a non-C# file via `[CallerFilePath]`) and assert verified-TLS, object-store TLS and — reserved for Part 4 — the enforcing-CSP setting are present. **This is the guard that would have caught `Security:EnforceCsp` being unset for the life of the deployment.**
11. Confirm Part 2 changed nothing about `SelfHostedLan`'s in-process front door, and that the public and console ports are still bound in **one** `ConfigureKestrel` call with the two-way `ConsolePortGate` intact.

**Part 2 validation:**
- [ ] Stack up from cold: every hop negotiates TLS (`\conninfo` in psql; MinIO over HTTPS)
- [ ] A cleartext connection attempt to PostgreSQL from another container is **refused by the server**
- [ ] The backup sidecar and the PITR stream both still run
- [ ] A deliberately-wrong `sslmode` refuses to start, naming the setting
- [ ] `Security:TrustedProxies` emptied ⇒ forwarded headers ignored, stated in the log, `/hangfire` still refuses a LAN caller
- [ ] `verify-schema` clean; certificate days reported
- [ ] `SelfHostedLan` boots and serves its own front door unchanged
- [ ] `CloudBrowser` (`docker-compose.prod.yml`) brought up from cold with the same transit configuration and `TransportAssurance` active — it receives Part 2's changes through `extends` and must not be left with TLS and no gate

---

#### Part 3 — Custody

**Blocked by:** Part 1 — the ring now protects clinic second factors, and Part 3 must **re-wrap** the existing keys, never mint a new ring.

**Steps:**
1. **Increment 3.1 — the ring, and the migration of what it already protects.** ⚠️ **`ProtectKeysWithCertificate` does not re-wrap an existing key**, and the plan previously assumed it did. Data Protection encrypts key XML **only when it writes it**, so the key already on the `dataprotection_keys` volume stays plaintext for the rest of its life *and* remains in the ring as a decryptor long after — FR-3.1 would read satisfied while a stolen volume still yields a readable master key, and `UnprotectKeysWithAnyCertificate` is a *decryption* fallback for keys encrypted under an older certificate, not a re-wrap of plaintext ones. Verified: `Infrastructure/Security/LocalDataProtection.cs:104-108` is the only at-rest branch and nothing rewrites persisted keys.
   Therefore, in this order:
   1. Change `AddConfiguredDataProtection` to `ProtectKeysWithCertificate(cert)` + `UnprotectKeysWithAnyCertificate(previous…)` where the deployment supplies one, keeping the Windows DPAPI branch untouched. State the retained generation count in the operator guide (FR-3.2).
   2. Force a **new active key** so every subsequent write is protected.
   3. Add a **`reprotect-secrets` console verb** that decrypts every existing ciphertext and re-`Protect`s it under the new key — the three `ClinicReminderSettings` columns, `PlatformAccount.ProtectedTotpSecret`, **Part 1's `User.ProtectedTotpSecret`** and Part 3's `Clinic.GoogleRefreshTokenProtected` — idempotent, reporting a count per column family and refusing to touch a row it could not decrypt (that row is named, not skipped in silence). Console-verb wiring is `reset-user-totp`'s: `BuildForConsoleVerb()` → `HasConnectionString` → `AddInfrastructure` **only** → `RunAs` → `UseSystemWide`, **plus its dispatch branch in `Program.cs`** and the reachability guard.
   4. Only then **delete the superseded plaintext key files**, and confirm every column family still round-trips.
   ⚠️ **The order is the whole safety argument**: deleting a plaintext key before its ciphertext has been re-protected is exactly R-2's data loss, arrived at from the other direction. **Re-mint is still forbidden** — a ring with no decryptor for the old keys kills every factor Part 1 enrolled.
   Add `secrets-protected-under-current-ring` to `verify-schema`: for each protected column family, the count of rows whose ciphertext does **not** resolve under the ring's active key. It is the only figure that says step 3 finished, and dropping the old key files (step 4) is gated on it reading zero on the live deployment.
2. FR-3.3: audit every `TryUnprotect` caller so a failure **refuses and names the recovery verb**, never degrades. For a second factor specifically, "could not decrypt" must never become "sign in without one" — `PlatformLoginCommand.VerifyTotp` is the model and Part 1's clinic equivalent must match it.
3. Encrypt `Clinic.GoogleRefreshToken`: add `GoogleRefreshTokenProtected`, backfill from the plaintext column in the startup backfill (it needs the DP ring, so it cannot be raw SQL in a migration), read the protected value everywhere, and add `verify-schema`'s `google-token-protected` counting rows still holding plaintext. **Dropping the old column is deliberately deferred** until that check has read zero on the live deployment — recorded as a follow-up rather than done blind.
4. Encrypt the data volume with LUKS, unlocked at boot by a keyfile on the host's own boot volume. Document, in these words, that this protects a **stolen, snapshotted or decommissioned disk** and does **not** protect against someone who already has root on the running host. The server must still reboot unattended.
5. Add `age` to the backup sidecar image. `backup.sh` encrypts the dump and the MinIO tar **before** rclone touches them. FR-3.7: each run then **decrypts what it just uploaded and confirms it parses** (`pg_restore --list` non-empty) — a failure fails the run, following the precedent the in-app backup already sets.
6. Set `WALG_LIBSODIUM_KEY` for the PITR stream.
7. FR-3.9: `KeyRingGenerationMarker` writes the ring's active key id to a `keyring_marker` volume at API startup; the sidecar reads it (read-only) and stamps it beside each dump; the restore procedure compares and **refuses a mismatch, naming both generations**. The ring itself is never mounted into the sidecar — that is what § 3.1 forbids.
8. FR-3.10 **in full**: a `secrets:` block in both compose files and `*_FILE` indirection for every secret. Add a `*_FILE` configuration layer to `InstallConfiguration` and confirm it is applied by the host **and all console verbs** — a verb reading one layer fewer resolves a different connection string from the app it is maintaining.
9. FR-3.11: resolve the contradiction. `deploy/README.md:55-56` says back the ring up *alongside* `postgres_data`; the compose file and `.env.hosted.example` say **separately**. One statement, in one voice, reflecting FR-3.1: once the ring is encrypted, the thing that must travel separately is the **certificate**.
10. Write `deploy/KEY-CUSTODY.md` (FR-3.8 — a deliverable, not a note) covering the key-ring protecting certificate, the backup encryption key and the volume keyfile: where each lives, who holds a copy, where the copy is kept, how to use it in a disaster. Write `deploy/RESTORE-DRILL.md` with the quarterly-plus-after-a-schema-batch cadence and a stated pass condition.
11. `SecretProtectionCoverageTests`: reflect over every credential-shaped property and assert each is protected or a named decision, asserted equal in both directions.

**Part 3 validation:**
- [ ] Reboot the host cold; the platform returns **unattended**
- [ ] Take a backup: it decrypts and parses, and a deliberately-corrupted upload **fails the run**
- [ ] One manual restore drill completed end to end and recorded
- [ ] A mismatched key-ring generation is refused, naming both
- [ ] All four pre-existing encrypted columns **and** Part 1's TOTP secrets round-trip after the ring is re-protected
- [ ] `reprotect-secrets` run; `verify-schema`'s `secrets-protected-under-current-ring` reads **zero** for every column family; only then are the superseded plaintext key files deleted, and the round-trip re-verified afterwards
- [ ] Confirmed no plaintext `<key>` element remains in the key-ring volume
- [ ] Every secret reaches the process as a file; no secret remains in `environment:`
- [ ] `verify-schema` clean

---

#### Part 4 — Evidence & surface

**Blocked by:** Part 0 (the restore must work for the restore step-up to be verifiable) and Part 1 (step-up).

**Steps:**
1. Add `AuditChain` in Domain — pure `Hash(previousHash, entry, key)` plus the walk. One arithmetic, called by both the interceptor and `verify-schema`, never re-expressed in SQL (the `subscription-cover-kind-matches-ledger` precedent).
2. Add `Sequence` (per clinic), `PreviousHash`, `EntryHash` and `IsDeclaredGap` to `AuditEntry`; migration with the `xmin` line removed and any backfill last. Existing rows get a **declared boundary** at the chain's start rather than a fabricated history.
3. `Audit:ChainKey` — required where the profile enforces, **startup throws** without it (`LocalDataProtection`'s precedent). Deliberately not the Data Protection ring: Part 3 re-wraps that ring, and chain verification must stay independently checkable.
4. In `AuditSaveChangesInterceptor.FlushAsync`, **open an explicit transaction on the audit context** and inside it, in this order: `pg_advisory_xact_lock(chainKey)` → read the chain's last `Sequence` + `EntryHash` → assign sequences and hashes → insert → commit. **Audit writes stay best-effort** — a failure must still never roll back the operation it describes — but a failure now records a **declared gap** instead of nothing, so a later walk tells "a gap we know about" from "a break nobody declared".
   ⚠️ **The explicit transaction is load-bearing, not tidiness.** `FlushAsync` today is `AddRangeAsync` + `SaveChangesAsync` with **no** transaction (`AuditSaveChangesInterceptor.cs:400-427`), so an `xact` lock taken as a separate statement is released at the end of its *own* implicit transaction and serialises **nothing** — two concurrent saves in one clinic then read the same predecessor and compute the same `PreviousHash`. This is `MigrationLock`'s documented lesson (`pg_advisory_lock`, never the `xact` variant, because it releases at the first commit) arriving from the other side: here `xact` is right *provided* the transaction spans the whole append.
   ⚠️ **`AuditEntry.ClinicId` is nullable, and those rows get their own chain.** A job or console verb mutates rows with no clinic derivable from them — which is why `AuditEntries` is the one clinic-owned table deliberately unfiltered — so "the chain is per clinic" leaves every background and every vendor write outside any chain, i.e. removable without breaking anything. Null-clinic rows therefore form **one deployment-wide chain**, addressed through a new non-nullable **`ChainKey`** column (`ClinicId ?? Guid.Empty`) that keys both the advisory lock and the uniqueness index — `ClinicId` itself is untouched, since a `NULL` cannot participate in a unique index and a sentinel written into it would turn "unattributed" into "belongs to a clinic that does not exist" (see Migration 3). `verify-schema` walks it as its **own scope** and names it as such.
   ⚠️ **The unique `(ChainKey, Sequence)` index stays** — it is the real backstop, and the decision table's claim that it was rejected *in favour of* the lock is corrected there: the lock is what stops ordinary concurrency producing declared gaps, the index is what makes a missed lock impossible to hide.
5. Make the archive restore record a **declared boundary**: a restore legitimately breaks a chain and must not leave something that reads as tampering.
6. Add `verify-schema`'s `audit-chain-intact` (walks each clinic's chain **plus the null-clinic deployment-wide chain, reported as its own scope**, and names the first broken entry — **a break is drift**) and `audit-declared-gaps` (**reported without being drift**). Both call the real `AuditChain`, never SQL.
7. Add `ArchiveAccessLedger` and record every full-cabinet download — who, which practice, when, and whether it was **delivered** rather than merely requested. **Not best-effort**: if the entry cannot be written the download does not happen, and the refusal is a French sentence (`PlatformAccessLedger`'s stated reasoning — the operation *is* what is being recorded). Notify administrators (Stated Assumption 9).
8. Give the archive endpoint its **own tight rate-limit policy**. It currently falls to the global limiter — 600 full-practice exports a minute.
9. Apply the Part 1 step-up to `GET /api/backup/archive` **and** `POST /api/backup/archive/restore`. Per-list CSV exports stay ungated: they are already role-restricted and are a daily action, and daily friction is what gets a control routed around. On a phone, where a multi-gigabyte download cannot work, say so in French — « Téléchargez l'archive depuis un ordinateur » — never a silent failure and never a spinner left running.
10. FR-4.4: replace the patient name in all eleven templates (three in `PdfGenerationService`, eight in `GoogleCalendarSyncService`) with an identifier; stop `HuggingFaceAIService:161` logging a raw payload; stop `SmtpDocumentEmailSender:85` logging a name-composed `{FileName}`. Extend `ReminderPhone.Mask`'s precedent rather than inventing a second masker.
11. **In the same commit**, make logs durable: a mounted volume, `retainedFileCountLimit: 30`. Durability persists what was previously ephemeral, so the scrub cannot land after it.
12. Add `LogTemplateCoverageTests` — scan every Serilog template for a patient-identifying property name, derived, asserted empty, proven red.
13. FR-4.5: set `Security__EnforceCsp: "true"` in the hosted compose file, drop **`'unsafe-eval'`** (the weakest directive) from the policy, and **remove `@vercel/analytics`** from `web/app/layout.tsx` and `package.json` — it loads from an external origin, which breaks an enforcing policy before any other work, and it sends page views from a medical-records application to a third party.
14. Add `report-to` + `POST /api/csp-report` — anonymous, its own rate-limit bound, excess **dropped not stored**, and the report's `document-uri` **stripped to its route pattern** before anything is recorded: this app's addresses contain patient identifiers, so reports are themselves subject to FR-4.4.
15. Add `Permissions-Policy` and COOP/CORP; give the console site a policy in `deploy/Caddyfile` (it has three headers and no CSP today) and `headers()` in `console/next.config.ts`.
16. Add `ContentSecurityPolicyAgreementTests`: parse `deploy/Caddyfile`'s page block and the console config, and assert byte-identity with `SecurityHeadersMiddleware.ContentSecurityPolicy`. The middleware's own docstring already says the two must change together; nothing enforced it.
17. FR-4.6: `UseHttpsRedirection()` is registered with no port configured and silently does nothing. **Configure it or remove it** — a control that is present and inert is worse than an absent one.

**Part 4 validation:**
- [ ] `verify-schema` clean, and the chain walk turns **red on a hand-edited entry** (executed, not assumed)
- [ ] A failed audit write leaves the operation committed and a declared gap recorded; the walk reports it apart from a break
- [ ] The archive is refused, in French, when the ledger row cannot be written
- [ ] An aborted download is recorded as **not delivered**
- [ ] Three wrong step-up attempts refuse on their own counter; the session is untouched and the screen says so
- [ ] Whole app walked under the **enforcing** policy at 320 / 390 / 820 / 1180 / 1440 with **zero violations**
- [ ] No patient name in any log file after a full day of use
- [ ] `npm run check:responsive` · `npx tsc --noEmit` · `npm run build` clean in `web/` and `console/`

---

## Testing Strategy

The backend unit suite is the **only** automated check the API has, and nothing in it touches a database — so a migration is verified by `verify-schema`, and every frontend claim is verified by the three commands plus an eye pass. `web/` has no test runner, no working ESLint and no CI; that is a fact to work with, not a gap to fill mid-feature.

### Unit tests (xUnit + Moq, mirroring the layer under test)

- `ClinicTotpAuthTests` — the eight-step refusal ladder in order; a present-and-wrong code is indistinguishable from a wrong password; a correct password from an un-enrolled admin yields `totp_enrolment_required` **and no token**; an undecryptable secret refuses rather than bypassing.
- `RecoveryCodeTests` — a code is spent when the sign-in it accompanied then fails; a wrong password spends none; a spent code is refused; regeneration invalidates every previous code.
- `SessionFamilyTests` — the predecessor is accepted (two tabs); an older credential ends **that family only** and leaves the user's other families live; an ended family refuses; expired families purge and live ones never do. Plus the two-rotation race: **both** succeed, which is what the `SkipsConcurrencyToken` entry buys and the one case that goes red if the entry is dropped.
- `StepUpTests` — password accepted; a current TOTP code accepted; the confirmation is single-use per action; failures spend the dedicated counter and never the login lockout.
- `AuditChainTests` — a tampered entry breaks the walk at the right row; a declared gap is reported apart from a break; a restore boundary does not read as tampering; **a null-clinic row lands on the deployment-wide chain and is walked**, not silently unchained.
- `StepUpTests` additionally pins that the confirmation store is **shared across requests** — mint on one scope, consume on another — which is the case a scoped registration fails and no behavioural test of a single request can see.
- `TransportAssuranceTests` — refuses on the kind, not on file presence; names the file and the setting; the other two profiles are untouched.
- `DeploymentProfileTests` — the matrix row for the 18th capability (the reflection test fails without it) and the hosted-only entry.

### Derived guard tests (house style: docstring criterion · reflected/scanned candidate set · `Assert.NotEmpty` · both-direction exception map · executed red-proof)

| Guard | Derives | Part |
|---|---|---|
| `DeploymentProfileTests` matrix | The new capability, by reflection over every capability | 1 |
| `SecondFactorCoverageTests` | No session reaches an administrator without a verified factor | 1 |
| `PasswordFloorSingleSourceTests` | Every client statement of the minimum reads the served value | 1 |
| `TransportConfigurationTests` | Parses the deployment configuration file: verified-TLS, object-store TLS, enforcing CSP | 2 |
| `SecretProtectionCoverageTests` | Every credential-shaped property is protected or a named decision | 3 |
| `PasswordFloorSingleSourceTests` | ⚠️ Locates `web/`/`console/` the `ClinicHubPath` way — **not** `SolutionSources.Root`, which stops at `api/` — asserts a non-zero scanned-file count, and anchors on password-length identifiers | 1 |
| `LogTemplateCoverageTests` | No log template names a patient | 4 |
| `ContentSecurityPolicyAgreementTests` | The page policy and the API policy are identical | 4 |

### `verify-schema` checks (run before and after each migration, and the outputs diffed)

`admins-without-a-factor-holding-a-live-session` · `session-families-have-no-orphans` · `server-clock-drift` (Info, and its text states what it cannot see) · `internal-certificate-days-remaining` · `google-token-protected` · **`secrets-protected-under-current-ring`** · `audit-chain-intact` (per clinic **and** the null-clinic deployment-wide chain) · `audit-declared-gaps`

### Manual / operator verification (per part, recorded in `progress.md`)

Cold-stack TLS walk · sidecar and PITR runs · a deliberately-wrong setting refusing to start · cold host reboot returning unattended · one full restore drill · the enforcing-policy page walk at five widths plus landscape and keyboard · a full day of use with no patient name in a log · the OAuth callback and the emailed verification link under the new cookie rule.

---

## Risk Register

| ID | Risk | Likelihood | Impact | Part | Mitigation |
|----|------|------------|--------|------|------------|
| R-1 | One user story spanning four security domains is too large to hold in one session | **High** | Med | All | Five commit-bounded parts with independent gates; split at a part boundary |
| R-2 | Part 3 re-mints the key ring instead of re-wrapping, killing every Part 1 factor | Low | **High** | 3 | Re-wrap is a stated requirement; round-trip test over all five protected column families before and after |
| R-3 | `SameSite=Strict` breaks the OAuth callback or the emailed verification link | **High** | Med | 1 | Walk both before finalising; `Lax` stays and the reason is recorded — the spec already allows this |
| R-4 | Enrolment locks an administrator out with no way back | Med | **High** | 1 | Three recovery paths shipped together; `reset-user-totp` exempt from the subscription and password-change gates |
| R-5 | Forwarded headers make the two loopback gates forgeable | Med | **High** | 2 | Gates read the **original peer** captured before substitution; empty/wrong trusted-proxy ⇒ headers ignored entirely |
| R-6 | The fail-loud transport check lands before the config that satisfies it | Med | **High** | 2 | Check and configuration in the **same commit**; the spec requires it |
| R-7 | The audit chain's advisory lock serialises more than intended and slows writes | Med | Med | 4 | Lock is per clinic and per transaction (`xact` variant), taken in the interceptor's own context; measure on a seeded clinic |
| R-8 | Enforcing CSP breaks a screen not covered by the walk | Med | Med | 4 | Report-only already ran for the deployment's life; `report-to` lands with the enforcement so the first violations are visible |
| R-9 | FR-3.10 in full touches every `${VAR}` and the config layering | **High** | Med | 3 | Land it as the **last** increment of Part 3, after the parts that must not be blocked by it |
| R-10 | Migration scaffolding fails or duplicates (running API holds `bin/`; `xmin` emitted into `CreateTable`) | **High** | Med | 1, 4 | `-p:BaseOutputPath=<temp>`, never `--no-build`, snapshot committed with each migration, `xmin` lines deleted by hand |
| R-11 | Smart App Control blocks freshly-built test assemblies (`0x800711C7`) | Med | Low | All | `dotnet test -c Release` with `BaseOutputPath` outside the repo — the known fix |
| R-12 | `SelfHostedLan` or `CloudBrowser` regresses through a "global" change | Med | **High** | 1, 2, 4 | **Five** changes are global — floor, cookies, chain, logging **and transit** (`extends` puts Part 2's compose/postgres changes on `CloudBrowser`, so `TransportAssurance` gates on `!SelfHostsFrontDoor`, not on one kind); every other branch asks a named capability; `DeploymentProfileCoverageTests` blocks a new `IsLocalMode` branch |
| R-13 | Host clock drift fails every account's login at once, with the same sentence as a wrong password | Low | **High** | 1 | ⚠️ **`verify-schema` cannot detect this** — app and database share the host's clock, so the reported offset is ~0 while both are wrong together (see increment 1.6). The real controls are **NTP on the host**, documented in `deploy/README.md`, and the refusal wording itself: once the password is known correct the sentence names the recovery paths (FR-1.9), so a drifted server is diagnosable from the screen even though no check fires |
| R-14 | The archive is already buffered twice in memory with no size cap; step-up and recording must not make it worse | Med | Med | 4 | Record and gate **before** the buffer is built; no change to the buffering (out of scope, stated) |

### R-1: One story spanning four security domains
- **Description:** Identity, transit, key custody and evidence in one story is far beyond the ~10–12-file session heuristic; a single session cannot hold it and a single PR is a very large review.
- **Likelihood:** High · **Impact:** Medium
- **Part:** All
- **Mitigation:** Parts are ordered, dependency-respecting and each is a *vertical* increment with its own commit, gate and revert procedure. Within a part, numbered increments are individually landable.
- **Contingency:** Split at a part boundary — the plan is written so that costs nothing but a second PR.

### R-2: The key ring is re-minted rather than re-wrapped
- **Description:** Part 3 protects the ring with a certificate. Minting a new ring instead of keeping the existing keys as decryptors makes every Part 1 factor, every clinic's reminder credentials and every console TOTP secret undecryptable at once. ⚠️ **The reverse error is now live too**: increment 3.1's step 4 deletes the superseded plaintext key files, which is the same data loss reached from the other direction if step 3 has not finished.
- **Likelihood:** Low · **Impact:** High
- **Part:** 3
- **Mitigation:** `UnprotectKeysWithAnyCertificate` with retained generations; the four-step order in increment 3.1, with the deletion **gated on `verify-schema`'s `secrets-protected-under-current-ring` reading zero on the live deployment**; a round-trip test over all six protected column families executed before and after. `reprotect-secrets` names any row it could not decrypt rather than skipping it.
- **Contingency:** Restore the ring from its separately-held backup and re-apply with the previous certificate in the unprotect list. Do not delete a key file until the check reads zero.

### R-5: Forwarded headers make a loopback gate forgeable
- **Description:** `UseForwardedHeaders` substitutes `RemoteIpAddress`. `LocalRequest.IsLoopback` gates `/hangfire` and first-run `setup`, and it already returns `true` on a null address — a gate that defaults to allow.
- **Likelihood:** Medium · **Impact:** High
- **Part:** 2
- **Mitigation:** Capture the original peer in middleware registered **before** `UseForwardedHeaders` and have `LocalRequest` read that, so the two gates remain a property of the real TCP peer. An empty or unparseable `Security:TrustedProxies` ignores forwarded headers entirely and says so at startup.
- **Contingency:** Bind `/hangfire` off the public listener entirely, as the console port already is.

### R-9: FR-3.10 in full
- **Description:** Moving every secret to `*_FILE` touches both compose files, `InstallConfiguration`'s layering and every console verb's configuration build. A verb reading one layer fewer resolves a different connection string from the app it maintains.
- **Likelihood:** High (of overrun, not of failure) · **Impact:** Medium
- **Part:** 3
- **Mitigation:** Land it as the final increment of Part 3, after the ring, the volume, the backups and the documentation. Assert the `*_FILE` layer is applied by `AddInstallLayers()` — the one path the host and all verbs share.
- **Contingency:** Reduce to the three secrets Part 3 introduces (the protecting certificate, the backup key, the chain key) and record the rest as a follow-up.

---

## Breaking Changes

### 1. Every live session ends once, on deploy
- **What breaks:** FR-1.7 renames the session cookies (`__Host-` prefix). Every signed-in user is signed out exactly once.
- **Who:** Every user on every profile — this change is global.
- **Handling:** A French explanation on the login screen. A bare form is indistinguishable from a bug.

### 2. A clinic administrator cannot sign in without enrolling a second factor
- **What breaks:** On `HostedMultiTenant` only, a correct password from an un-enrolled administrator is refused with `totp_enrolment_required` and no session.
- **Who:** Every hosted clinic administrator, at their next sign-in.
- **Handling:** The refusal opens the enrolment step on the login screen itself, carrying the address and password. Three recovery paths ship in the same part.

### 3. The minimum password length rises from 8 to 12
- **What breaks:** New and changed passwords under 12 characters are refused. **Existing passwords keep working** — the floor is enforced on set, never on check. Global.
- **Who:** Anyone setting a password after deploy, on all three profiles.
- **Handling:** The floor is served, so every client states the same number as the server that enforces it.

### 4. `@vercel/analytics` is removed
- **What breaks:** No page-view telemetry from the web app.
- **Who:** The vendor. Nothing user-facing.
- **Handling:** Stated. It breaks an enforcing CSP before any other work and sends page views from a medical-records application to a third party.

### 5. The archive download and restore require a fresh password (or TOTP code)
- **What breaks:** Two admin-only operations gain a confirmation step.
- **Who:** Clinic administrators exporting or restoring a full cabinet.
- **Handling:** Per-list CSV exports are deliberately untouched. Failures spend their own counter, so a mistyped password at the export card cannot lock a practice's only administrator out mid-day.

### 6. Cleartext connections to PostgreSQL are refused
- **What breaks:** Any container connecting without TLS. The two sidecars are brought across in the same change.
- **Who:** **Both hosted deployments' operators.** `docker-compose.hosted.yml` `extends` `docker-compose.prod.yml` and `deploy/postgres/Dockerfile` is shared, so this reaches `CloudBrowser` as well — transit is the fifth global change (Part 2 step 8, spec Stated Assumption 11). `SelfHostedLan` is untouched.
- **Handling:** Same commit, plus a cold-start gate that runs on both hosted kinds so neither gets TLS with no check behind it.

---

## Migrations

Three EF migrations plus two data backfills and one host-level change. Every one is additive; nothing is altered, narrowed or dropped. **Scaffolding rules apply to all three** (`exploration.md` § 5.4): `-p:BaseOutputPath=<temp>` because a running dev API holds `api/**/bin`; never `--no-build`; commit the model snapshot with the migration; delete the `xmin = table.Column<uint>(…)` line EF emits into every `CreateTable`, which PostgreSQL rejects outright.

### Migration 1 — `AddUserSecondFactorAndSessionFamilies` (Part 1)
- **What:** `Users.ProtectedTotpSecret`, `Users.TotpEnrolledAt`; the `UserRecoveryCodes` table (FK cascade to `Users`); the `SessionFamilies` table (`(UserId, ExpiresAtUtc)` index, unique `CurrentCredentialHash`).
- **When:** Before Part 1 is deployed. `verify-schema` clean before and after, diffed.
- **Rollback:** Drop the two tables and the two columns. Reverting Part 1 signs everyone out a **second** time (the cookie rename reverses) — stated in Part 1's revert procedure.
- **Steps:** every `CreateTable`/`AddColumn`/`CreateIndex`; no backfill.

### Migration 2 — `AddProtectedGoogleToken` (Part 3)
- **What:** `Clinics.GoogleRefreshTokenProtected`, nullable, beside the existing plaintext column.
- **When:** With Part 3. The **backfill is a startup pass, not SQL** — it needs the Data Protection ring.
- **Rollback:** The plaintext column is still present and still populated, so reverting is dropping the new column.
- **Steps:** 1. Add the column. 2. Startup backfill protects any row whose protected value is null and whose plaintext is not. 3. `verify-schema`'s `google-token-protected` counts the remainder. 4. Dropping the plaintext column is a **follow-up**, once that check reads zero on the live deployment.

### Migration 3 — `AddAuditChain` (Part 4)
- **What:** `AuditEntries.Sequence`, `.PreviousHash`, `.EntryHash`, `.IsDeclaredGap`, **`.ChainKey`** (non-nullable `uuid` = `ClinicId ?? Guid.Empty`); a unique **`(ChainKey, Sequence)`** index.
- ⚠️ **`ChainKey` is its own column and `ClinicId` is left exactly as it is.** A unique `(ClinicId, Sequence)` index cannot cover the null-clinic rows at all — PostgreSQL treats each `NULL` as distinct — and writing a `Guid.Empty` sentinel *into* `ClinicId` would break the nullable semantics `GetAuditEntriesQuery` and the deliberate absence of a query filter both rest on, turning "unattributed" into "belongs to a clinic that does not exist". The derived column keeps the chain total and the ledger's own reading unchanged.
- **When:** With Part 4.
- **Rollback:** Drop the four columns and the index. Re-applying after the chain is populated leaves a permanent declared boundary — stated in Part 4's revert procedure.
- **Steps:** 1. All DDL. 2. The backfill, **last**, writes a declared boundary at each clinic's chain start rather than fabricating hashes for history that predates the key.

### Host change — LUKS on the data volume (Part 3)
- **What:** The volume holding the database and the object store is encrypted, unlocked at boot by a keyfile on the host's own boot volume.
- **When:** During a scheduled window; it requires moving the data.
- **Rollback:** Documented in `deploy/KEY-CUSTODY.md`. Requires the keyfile.
- **Steps:** documented end-to-end in the operator guide, including the unattended-reboot verification that is Part 3's gate.

---

## Deploy order and rollback

- **Part 0 → Part 1 → Part 2 → Part 3 → Part 4.**
- **Part 1 before Part 3 is mandatory**, not preferred: Part 3 re-protects the ring and Part 1's factors live on it. Part 3 must **re-wrap**.
- **Part 2 before Part 3** is preferred, not required.
- **Part 0 before Part 4** is required: Part 4's gate verifies a refusal on the restore path.
- **Known revert asymmetries:** reverting Part 1 signs everyone out a second time; reverting Part 4 after the chain is populated leaves a permanent boundary when re-applied; reverting Part 3's file-based secrets after the environment values are deleted is a hard startup failure.

## Deferred, with the remedy chosen

| Item | Why deferred | Remedy |
|---|---|---|
| Dropping `Clinic.GoogleRefreshToken` (plaintext) | The backfill must be proven complete on the live deployment first | Follow-up, gated on `google-token-protected` reading zero |
| A no-smartphone administrator (Open Question 1) | Named unanswerable by this feature in the spec | Recorded; needs a product decision on a printed-codes-only mode |
| The vendor's identity check before `reset-user-totp` (Open Question 3) | A written procedure, not code | Belongs in `deploy/README.md`'s operator runbook |
| The archive's double in-memory buffering and missing size cap | Out of scope, stated; Part 4 must not make it worse | Its own feature |
| Dropping the superseded plaintext key-ring files | Gated on `secrets-protected-under-current-ring` reading zero on the live deployment | Increment 3.1 step 4, after `reprotect-secrets` |

---

## Challenge outcomes (2026-08-12)

Ten issues, every one verified against source before being raised; all ten applied. The four that changed what gets
built, rather than how it is described:

| # | Finding | Where it landed |
|---|---|---|
| 1 | **`ProtectKeysWithCertificate` does not re-wrap an existing key** — Data Protection encrypts key XML only when it writes it, so FR-3.1 would have read satisfied while the plaintext master key stayed on the volume, and R-2's `UnprotectKeysWithAnyCertificate` "retained generations" mitigation describes a decryption fallback, not a re-wrap | Increment 3.1's four-step order + the new **`reprotect-secrets`** verb + `secrets-protected-under-current-ring`; R-2 rewritten |
| 2 | **`SessionFamily` would inherit the `xmin` concurrency token**, so two tabs refreshing at once 409 — breaking the exact behaviour FR-1.6 exists to preserve (`ApplicationDbContext.cs:293-320`) | `SkipsConcurrencyToken` entry with the set's required argument, + a racing test |
| 3 | **The audit chain had no rule for null-`ClinicId` rows** (every job and console write) and the advisory lock as described **serialised nothing** — `FlushAsync` has no explicit transaction, so an `xact` lock releases immediately | Part 4 step 4: one explicit transaction, a derived **`ChainKey`** column, a deployment-wide null-clinic chain; Migration 3 and the decision table corrected |
| 4 | **Part 0 was stale** — the defect it deletes is already reverted, and following it literally would have deleted the surviving quadratic-restore guard | Part 0 rewritten as verify-and-test; `exploration.md` § 4.2 flagged for correction |

The other six: the console cannot reach `/api/auth/mode` through `ConsolePortGate` (floor moved to a `/api/platform`
read); the `__Host-` cookie **name** is conditional, so it needs a resolver rather than renamed constants; the step-up
store's DI lifetime was unstated and the wrong one fails silently; the per-request enrolment refusal had no client
destination (`onSecondFactorRequired`); **transit is a fifth global change** because `extends` puts it on
`CloudBrowser`, so `TransportAssurance` gates on `!SelfHostsFrontDoor` (spec Stated Assumption 11 amended); and three
guard/check defects — a tautological `verify-schema` check, `SolutionSources.Root` never reaching `web/`/`console/`,
and an unconditional admin factor-disable refusal.
