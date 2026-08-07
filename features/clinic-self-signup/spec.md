# Feature Specification: Clinic self-signup (hosted backend)

**Status:** APPROVED
**Type:** Small
**Created:** 2026-08-07
**Scope:** Full
**Profile:** `HostedMultiTenant` only
**Feature:** A dental practice reaching the hosted backend creates its own clinic and admin account, verifies its
email address, and starts working — with no operator action of any kind.

## Overview

Today a hosted clinic exists only because an operator ran the `provision-clinic` console verb. This adds a public
door: a visitor submits the clinic + admin details, receives a verification link, and clicking it provisions the
clinic through the **existing** `LocalClinicProvisioning.ProvisionAsync`. Nothing real is created before
verification — a pending `ClinicSignup` row only, holding an already-hashed password and the SHA-256 of a token
that exists in plaintext nowhere but the email.

This does **not** reopen the door `multi-tenant-cloud` closed. That decision (`plan.md:89`) was about the
**6-character clinic code** — a LAN-scale gate and a shared password on the internet. `AllowsSelfRegistration`
stays `false`; staff onboarding remains admin-creates-account. The new gate is a fresh 32-byte secret delivered to
an address the signer-up controls, single-use and expiring.

## What Changes

- `POST /api/auth/signup` (anonymous) writes a pending `ClinicSignup` and emails a verification link.
- `POST /api/auth/signup/verify` (anonymous) consumes the token and provisions the clinic + admin.
- New `ClinicSignup` aggregate + hand-written migration.
- New `ITransactionalEmailSender` / `SmtpTransactionalEmailSender` — the first email path in the product not bound
  to a clinic.
- New `DeploymentProfile.AllowsPublicClinicSignup` capability; `GET /api/auth/mode` gains `publicSignupEnabled`.
- New `/signup` and `/signup/verifier` pages.

## Verified starting position

| Door | Why it does not serve this |
|---|---|
| `POST /api/auth/setup` | Loopback-only (`LocalRequest.IsLoopback`) **and** refuses once any user exists. Creates clinic #1 on the server machine, never clinic #2. |
| `POST /api/auth/register` | Joins an **existing** clinic by code; 404s here (`AllowsSelfRegistration` = false). |
| `provision-clinic` | The operator intervention being removed. |

`LocalClinicProvisioning.ProvisionAsync` is already the single definition of "clinic + first password-backed admin
+ seeded catalogs" and **deliberately holds no authorization opinion** (its own docstring). It needs no change —
this adds a third caller.

**No email verification exists anywhere** in the product (no `VerificationToken`, `EmailVerified`, `ResetToken`,
`forgot-password`). **No clinic-free email sender exists**: `IDocumentEmailSender.SendAsync` requires a
`ResolvedReminderSettings`, which is resolved **per clinic** — and a signup has no clinic. That is the real
coupling, not the `DocumentEmail` entity's non-empty `AttachmentStorageKey`.

**Two pieces already exist and must be reused, not rebuilt:** `SmtpConfig` (per-install `Notification:Smtp:*`,
already the documented fallback beneath each clinic's own settings) and `FrontendUrl` (already used by
`GoogleCalendarController` to build a browser-facing redirect). **This feature adds no new config key.**

## API Contract

### POST /api/auth/signup
Request: `{ clinicName, fullName, email, password, phone?, address?, city?, doctorInfo? }`
Response 202: `{ message: <neutral French sentence> }`
Errors: `404` (capability closed) · `400` French refusal for password policy or a malformed field · `429` limiter

### POST /api/auth/signup/verify
Request: `{ token }`
Response 200: `{ message }` — **no** access token, **no** cookie
Errors: `404` (capability closed) · `400` single French refusal shared by expired / unknown / malformed / now-taken

Both `[AllowAnonymous]` + `[EnableRateLimiting(RateLimiting.AnonymousAuthPolicy)]` on `AuthController`. Being a
`/api/auth/*` POST means the existing limiter applies with no new configuration — `AuthAttemptAccount` already
lifts `email` from the body pre-binding, so the partition is per submitted account with a looser per-address ceiling.

## Data / Schema Changes

New `ClinicSignup` aggregate. **No `ClinicId`** (no clinic exists yet), so it is outside the EF tenant filter by
construction and needs no `TenantScopeFilterTests` entry.

| Field | Notes |
|---|---|
| `Id` | `Guid` |
| `ClinicName`, `FullName` | trimmed as submitted |
| `Email` | lowercased + trimmed, matching `User.CreateLocalUser`'s normalisation |
| `PasswordHash` | PBKDF2 via `ILocalAuthService.HashPassword` **at signup**. Plaintext never stored. |
| `DoctorInfoJson` | nullable; the optional practitioner block |
| `TokenHash` | **SHA-256 of the raw token**, which exists only in the email |
| `ExpiresAtUtc` | signup instant + 24 h |
| `ConsumedAtUtc` | nullable; non-null ⇒ spent |
| `CreatedAtUtc`, `EmailSendAttempts` | |

Mutators: `Consume(nowUtc)`, `IsUsable(nowUtc)`. No lifecycle beyond that.

`DeploymentProfile.AllowsPublicClinicSignup` — a normal `bool` property: `SelfHostedLan` ✗, `HostedMultiTenant` ✓,
`CloudBrowser` ✗.

## Device Behaviour

- **Leading device:** phone. A dentist signing up is as likely to be on a phone as at a desk, and this is the
  product's first impression.
- **Narrow width (< 640):** `/signup` is a single-column stacked form, never a two-column grid; the optional
  practitioner block is a collapsed section rather than always-open fields. `/signup/verifier` is a single centred
  status panel at every width.
- **Touch:** every field and the submit button clear 44 px on a coarse pointer; `text-base md:text-sm` on inputs so
  iOS does not zoom on focus. Nothing is hover-revealed.

Floor inherited from `~/.claude/rules/frontend-web.md` and `DEVICE-CONTRACT.md` — not restated here.

## Acceptance Criteria

**Signup**
- **AC-1:** `POST /api/auth/signup` 404s in `SelfHostedLan` and `CloudBrowser` **without reaching the mediator**.
- **AC-2:** A valid submission writes exactly one `ClinicSignup` and **no** `Clinic`, `User`, `Doctor` or
  `ProcedureType` row.
- **AC-3:** The response is byte-identical whether the email is free, already an account, or already has a pending
  signup — no enumeration oracle.
- **AC-4:** The password is checked against `PasswordPolicy.MinLength` **before** the neutral response and refused
  in French. A length rule reveals nothing about the address, so it is not an oracle.
- **AC-5:** The stored `PasswordHash` never equals the submitted password; the raw token appears in no log, no
  response body and no database column.
- **AC-6:** A second signup for the same email re-sends against the **same** row rather than creating a second; an
  expired pending row is replaced.
- **AC-7:** Expired rows are purged opportunistically on the signup path — no new background job.

**Verification**
- **AC-8:** A correct, unexpired, unconsumed token provisions the clinic + admin through `ProvisionAsync` and marks
  the row consumed.
- **AC-9:** The same token used twice succeeds once and is refused the second time.
- **AC-10:** Expired, unknown, malformed, and "email became taken since signup" all return the **same** French
  refusal; the row is consumed in the taken-email case.
- **AC-11:** The lookup is by `TokenHash`, compared in constant time; a database dump yields no usable link.
- **AC-12:** Verification issues **no** session, sets no cookie and returns no access token. The clinic signs in at
  `/login` with the password it chose.
- **AC-13:** The created admin has `IsActive = true` and `MustChangePassword = false` — they chose the password.

**Catalogs**
- **AC-14:** A clinic created through this anonymous path has its CNAM, medication and dental-act catalogs actually
  seeded, asserted by **row counts**, not by the absence of an exception. (`TrySeedCatalogsAsync` is best-effort and
  swallows-and-logs, so success is not observable from the result alone.)

**Email**
- **AC-15:** With `Notification:Smtp:Server` unset the signup returns a French refusal naming the missing
  configuration — never a 202 over an email that will never arrive.
- **AC-16:** The link is built from `FrontendUrl`, so no host is compiled in.

**Frontend**
- **AC-17:** At 320 px `/signup` renders as one column with no horizontal scroll and every control reachable and
  ≥ 44 px on a coarse pointer; French throughout.
- **AC-18:** The page hides itself when `publicSignupEnabled` is false, but a **failed** capability probe falls
  through to the form — the `/join` precedent: refusing on a network hiccup is the worse error.
- **AC-19:** Submit is disabled in-flight; a failure leaves the form populated and shows a French `sonner` toast.

## Pitfalls

1. **The catalog-seed fear is unfounded — verified, do not "fix" it.** `ClinicCatalogSeeder` calls
   `IgnoreQueryFilters()` on **every** read (`ClinicCatalogSeeder.cs:50,62,74,86,138,165`), so it is structurally
   immune to `ITenantScope` rather than dependent on it. `ProvisionAsync`'s only other reads are `User` and
   `Clinic`, both deliberately unfiltered; everything else is an `Add`, which no query filter touches. So an
   anonymous request's `Unset` scope does **not** break the seed, and today's `/api/auth/setup` is not affected
   either. **Do not add a `UseClinic(...)` call to mimic `provision-clinic`** — that verb declares a scope because
   it has no HTTP context at all, not because the seed needs one. AC-14 proves the outcome regardless.
2. **The migration must be hand-written.** `dotnet ef` cannot scaffold here (Smart App Control, `0x800711C7`) —
   write the migration, its `.Designer.cs` **and** the model snapshot by hand, as `AddChequeDetailsToPayments` was.
   An uncommitted snapshot makes the next migration duplicate this one.
3. **The R-2 truth-table test goes red without a third category.** `DeploymentProfileTests`
   `Both_pre_existing_kinds_reproduce_the_old_IsLocalMode_truth_table` expects every bool to equal `wasLocal` or
   its negation; `(SelfHostedLan false, CloudBrowser false)` is neither. Add a `hostedOnlyCapabilities` set beside
   `invertedCapabilities`. That is *more* faithful to R-2 ("both shipped profiles behave exactly as before"), not a
   dodge of it. An `ExpectedMatrix` row is also required or `Every_capability_is_covered_by_the_matrix` fails.
4. **New anonymous actions** must be added to `ControllerAuthorizationCoverageTests.ExpectedAnonymous` as
   `Auth.SignUp` and `Auth.VerifySignUp`, or four tests go red.
5. **Command placement.** `RealtimeBroadcastBehavior` derives its key from the namespace, so a command under
   `Features/Clinics/Commands` would broadcast `clinics` — announcing a clinic that does not exist. Signup writes no
   clinic, so **`Features/Auth/Commands` is correct**.
6. **CSPRNG only** — `RandomNumberGenerator.GetBytes(32)`, never `new Random()`.
7. **`verify-schema`** gains a `clinic-signup-has-no-orphans` counter (no consumed row past retention; no pending
   row whose email now has an account). The new table's indexes and columns are diffed off the EF model for free —
   do **not** hand-list them.
8. **The sender reads `SmtpConfig`, not `ResolvedReminderSettings`** — install-level by necessity, and its
   docstring must say so, or somebody later "fixes" it into `ReminderSettingsProvider` and it stops working for the
   one caller that has no clinic. It is deliberately **not** an outbox queue: every existing queue keys on
   `ClinicId`, and a verification email the visitor is actively waiting for is not a background dispatch (AC-15).

## Out of Scope

- Billing, trials, clinic suspension or deletion (`Clinic` has no lifecycle field today).
- **Password reset — still absent from the product.** With self-signup, a hosted admin who forgets their password
  and has no colleague-admin has no recovery but `reset-admin-password` on the server: exactly the operator
  intervention this feature removes. The token machinery built here is what a reset flow would reuse. Flagged, not
  fixed.
- TTN certificate upload; any per-clinic email branding.
- The duplicated procedure-seed loop in `CreateClinicCommandHandler`'s Cloud branch (`LocalClinicProvisioning`'s own
  docstring flags it as finding 33). Pre-existing; do not fold it in.
- Any change to `SelfHostedLan` or `CloudBrowser` behaviour.

## Sizing Note

Marked **Small** as requested, and it is honestly at the ceiling: a new aggregate + hand-written migration + a
`verify-schema` counter + a new email transport + two endpoints + a capability + two pages + four guard-test
entries. Choosing SMTP over a new vendor removed the whole config-and-deploy-asset slice. If the implement step
runs long, the split is (A) backend signup + verification, (B) the SMTP sender, (C) the two pages.
