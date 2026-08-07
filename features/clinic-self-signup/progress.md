# Progress: Clinic self-signup (hosted backend)

**Started:** 2026-08-07
**Type:** Small
**Branch:** feature/audit-sections-3-to-10 (user chose the current branch over a new one)

## Status
- [x] Implementation
- [x] Quality checks (backend build, tsc, check:responsive, next build)
- [ ] Tests (handled by /test-small-feature)

## Working tree note (start of session)
Unrelated, pre-existing and **excluded from this feature's commits**:
- `M features/mobile-native-shells/stories/story-1-full-clinic-on-a-phone.md`

(`M api/ClinicManagement.API/Controllers/TrustController.cs` was dirty at session start and is no longer —
reverted outside this work. Nothing here touched it.)

## Sizing note
The spec's own « Sizing Note » calls this « honestly at the ceiling » for Type Small and enumerates every
piece. The landed surface is 26 files. That is over the skill's ~10-file envelope, but it is the scope the
approved spec pins rather than a discovery, so it was built as specified rather than escalated.

## Files Changed

**Domain (2 new)**
- `Domain/Entities/ClinicSignup.cs` — the aggregate. No `ClinicId`, `HashToken`/`TokenHashMatches` live here so
  the write and read sides cannot hash differently.
- `Domain/Repositories/IClinicSignupRepository.cs`

**Application (3 new, 2 edited)**
- `Common/Interfaces/ITransactionalEmailSender.cs` — new (clinic-free email seam).
- `Common/Interfaces/IPublicAppUrlProvider.cs` — new (see DEV-1).
- `Features/Auth/Commands/SignUpClinicCommand.cs`, `.../VerifyClinicSignUpCommand.cs` — new.
- `Common/Interfaces/ISchemaVerificationReader.cs`, `Common/Maintenance/SchemaVerificationService.cs` — the
  `clinic-signup-has-no-orphans` counter (spec Pitfall 7).

**Infrastructure (5 new, 5 edited)**
- `Persistence/Configurations/ClinicSignupConfiguration.cs`, `Repositories/ClinicSignupRepository.cs`,
  `Services/SmtpTransactionalEmailSender.cs`, `Services/PublicAppUrlProvider.cs` — new.
- `Migrations/20260807102000_AddClinicSignups.cs` + `.Designer.cs` — new; `Migrations/ApplicationDbContextModelSnapshot.cs` — edited.
- `Deployment/DeploymentProfile.cs` (`AllowsPublicClinicSignup`), `Extensions.cs` (3 registrations),
  `Persistence/ApplicationDbContext.cs` (`DbSet`), `Persistence/SchemaVerificationReader.cs` (the count).

**API (1 new, 1 edited)**
- `Models/ClinicSignUpRequest.cs` — new.
- `Controllers/AuthController.cs` — `signup`, `signup/verify`, `publicSignupEnabled` on `mode`.

**Tests (3 edited — build-required only, see below)**
- `UnitTests/Infrastructure/Deployment/DeploymentProfileTests.cs`, `UnitTests/Api/ControllerAuthorizationCoverageTests.cs`,
  `UnitTests/Common/Maintenance/SchemaVerificationServiceTests.cs`.

**Web (2 new, 2 edited)**
- `app/signup/page.tsx`, `app/signup/verifier/page.tsx` — new.
- `lib/api/auth.ts` (`publicSignupEnabled`, `signUp`, `verifySignUp`), `middleware.ts` (public routes).

**Docs (5 edited, per the repo's own « update the nearest CLAUDE.md » rule)**
- root `CLAUDE.md` (feature bullet + capability count 14→15), `api/ClinicManagement.API/CLAUDE.md`,
  `api/ClinicManagement.Application/CLAUDE.md`, `api/ClinicManagement.Infrastructure/CLAUDE.md`,
  `web/CLAUDE.md`, `web/lib/CLAUDE.md`.

## Quality checks

| Layer | Command | Result |
|---|---|---|
| Backend | `dotnet build ClinicManagement.UnitTests.csproj` (compiles Domain→Application→Infrastructure→API→Tests) | **0 errors**; **0 warnings in any changed file** (verified with a scoped `--no-incremental` grep). The only warnings left are the repo's pre-existing CS8618/CS8981 baseline. |
| Frontend | `npx tsc --noEmit` | **0 errors** |
| Frontend | `npm run check:responsive` | **15/15 passed** |
| Frontend | `npm run build` | green; `/signup` and `/signup/verifier` both emitted as static routes |
| Frontend | `npm run lint` | **not run** — `eslint` is named in the script but absent from `devDependencies` and `next.config.ts` sets `eslint.ignoreDuringBuilds`; the three commands above are the repo's stated gate. |

### Device pass — NOT performed visually, and stating that plainly
There is **no browser in this environment**, so the 320/390/820/1180/1440 px eye pass the frontend rule requires
was not done. What was done instead: the mechanical gate above, plus a re-read of both new pages against
`DEVICE-CONTRACT.md` § 1 / `.claude/rules/frontend-web.md`:

- **§ 1 / § 10** — no `grid` anywhere on either page; both are `space-y` stacks in a `max-w-md` card with a
  `p-4 sm:p-6` gutter, so there is nothing to collapse at 320 px and nothing to reflow at 820 px.
- **§ 2** — the `Button` primitive already carries `touch-target` (44 px on coarse, painted size unchanged), and
  its instances sit in `space-y-3`/`space-y-5` stacks, so the overlay cannot overhang a neighbour. The one
  hand-rolled control (the practitioner disclosure) is a real `<button>` with `py-3 coarse:py-4` and two lines of
  text — it **grows** rather than overlaying, which is the § 2 rule for a control adjacent to a field.
- **§ 3** — every field is the `Input`/`Select` primitive with no unprefixed `text-sm` passed in, so the
  `text-base md:text-sm` guard survives tailwind-merge; `globals.css` supplies the 44 px coarse floor.
- **§ 7** — `min-h-dvh`, never `h-screen` (gate-enforced).
- **§ 11 + the vertical trap** — `justify-start` + `mx-auto` on the horizontal axis, and **`my-auto` on the
  child** rather than `items-center` on the parent, so the top of the card stays reachable in a scrolling box on
  a landscape phone.
- **§ 13** — submit disabled in flight with an early `if (isSubmitting) return` against double-submit; success via
  a French `sonner` toast; failure via a toast **plus** `FormErrorBanner` with every field left populated; real
  `<Label htmlFor>` on every field; `aria-hidden` on every decorative icon and no icon-only control; `role="status"`
  on each inline async result; `aria-expanded`/`aria-controls` on the disclosure; French throughout.

**Owed before merge:** the visual walk at those five widths plus a landscape phone and a keyboard pass.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| The verification handler short-circuits with `Result<T>.FailureFrom(provisioned)` and hoists `provisioned.Value.Clinic` into a local, instead of `Failure(provisioned.Error)` + repeated `.Value` access. | Internal to one method, same behaviour, and it carries the failure's `Code` across rather than dropping it — which is what `FailureFrom` exists for. Removes the only two new nullable warnings. |
| `SmtpTransactionalEmailSender` treats a missing **`FromAddress`** as « not configured » alongside a missing host, where the spec's AC-15 names only `Notification:Smtp:Server`. | Same class of refusal at the same moment for the same reason: `MailMessage` cannot be constructed without an envelope sender, so without this the check would pass and the send would fail one step later with a worse message. `SmtpConfig.FromAddress` already falls back to the username when that is an address, so the common single-key setup is unaffected. |

## Significant Deviations

### DEV-1 — `IPublicAppUrlProvider` seam instead of reading `FrontendUrl` in the handler
- **Original plan (spec AC-16):** « The link is built from `FrontendUrl`, so no host is compiled in. »
- **Blocker found at build time:** `ClinicManagement.Application` does **not** reference
  `Microsoft.Extensions.Configuration.Abstractions` — its whole dependency set is Domain, MediatR,
  FluentValidation, ASP.NET Authorization/Http abstractions and EF Core. `IConfiguration` does not resolve there
  (`CS0234`), so the handler literally cannot read the key.
- **Actual implementation:** a one-property outbound interface in `Application/Common/Interfaces` implemented by
  `Infrastructure/Services/PublicAppUrlProvider`, which reads `FrontendUrl` — the same key
  `GoogleCalendarController`'s OAuth success redirect uses.
- **Justification:** the alternative was adding a configuration package reference to Application, i.e. a **new
  dependency on a layer that deliberately has none** — a larger change than the one it avoids, and this is
  precisely the shape that layer already uses for every environment-derived value (`IReminderSettingsProvider`,
  `IOsPushAvailability`, `IInternetProbe`).
- **Impact:** AC-16 is satisfied unchanged — the link still comes from `FrontendUrl`, still adds no config key,
  and no host is compiled in. Two new files; no API contract, persistence or behaviour change.
- **Approved:** logged rather than asked, because it changes nothing the spec pinned (the *source* of the value)
  and only how a layer boundary is crossed. Flagging it here for the review pass.

### DEV-2 — the migration is hand-written (as the spec's Pitfall 2 anticipated), for a different reason than it gave
- Pitfall 2 predicted `dotnet ef` would be blocked by Smart App Control (`0x800711C7`). The actual blocker on this
  machine is a **file lock**: the user's `ClinicManagement.API` (PID 10508) is running and holds
  `api/ClinicManagement.API/bin/Debug`, so the design-time build dies on `MSB3026` before SAC is ever reached.
  The API project itself compiles cleanly to a scratch `-o` directory, which is how that was established.
- Killing a server the user is actively using for LAN testing was not an acceptable cost, so all three files were
  hand-authored, exactly as Pitfall 2 instructs: the migration (one table, three indexes, no relationships —
  small enough to verify by eye), the model snapshot, and the `.Designer.cs`, the last **derived mechanically**
  from the updated snapshot (copy → add the `Migrations` using → add `[Migration(...)]` → rename the class →
  rename `BuildModel` to `BuildTargetModel`) rather than retyped.
- **Owed before merge:** regenerate or verify the migration with `dotnet ef` in an unrestricted environment, and
  run `verify-schema` before and after applying it and diff — which is also where the new
  `clinic-signup-has-no-orphans` counter first proves itself.

## Review round 1 — applied fixes (2026-08-07)

`/review-feature` raised 40 findings (1 Critical, 16 Major); `reviews/feature-review.md` holds them. 35 were
applied. What changed in ways this document previously described differently:

- **A resend no longer re-reads the submitted details.** `Renew` overwrote `PasswordHash`, so an anonymous second
  submission for an address somebody else was mid-signup on replaced their password — and the victim's own inbox
  then provisioned the clinic against it. `ClinicSignup.Reissue` rotates only the token, and only **after** the
  send succeeded (a failure leaves the link already in the inbox alive). A full `Renew` is now reachable only for
  an expired or consumed row. A 2-minute per-recipient cooldown, derived from `ExpiresAtUtc` so it needs no
  column, bounds mail aimed at one mailbox.
- **The verification link carries its token in the URL fragment**, not the query string — a fragment never reaches
  the server, so the live credential stays out of the reverse proxy's access log; the page erases it from the
  address bar on read.
- **A failed send returns the neutral 202**, not a refusal. The refusal was an enumeration oracle: during a mail
  outage a free address and an existing account answered differently with no timing needed. AC-15's loud refusal
  is for an *unconfigured* host, which is still checked before anything is written — now alongside `FrontendUrl`,
  whose absence was equally fatal and completely silent. That refusal is **503**, not 400, and names no
  configuration key.
- **`PurgeSpentAsync` deletes independently and bounded** (200/call) instead of staging `RemoveRange` on the
  caller's save, which could 409 a valid signup through the `xmin` token.
- **`ClinicSignup` is excluded from the audit ledger** — an anonymous endpoint was writing rows no clinic-scoped
  read can ever show, and a purge recorded the abandoned visitor's name and address permanently.
- The email is stored in its **canonical** parsed form (`Attaquant <dr@cabinet.tn>` used to be stored verbatim,
  bypassing both account checks), every stored field is length-checked before the insert, and
  `LocalClinicProvisioning.ValidatePractitioner` is now one shared body — it also refuses a name with no
  specialty, which used to be dropped in silence on `/setup` and `provision-clinic` too.

**Deferred, not fixed:** the SMTP-duration half of the timing oracle (needs a notice email to the taken address);
a signup-specific rate-limit policy keyed on the client address rather than the submitted account; extracting the
shared SMTP transport out of `SmtpDocumentEmailSender`; a « Créer votre cabinet » link on `/login`; and the test
scenarios below, which the review raised as its own Major finding.

**Gate after the fixes:** backend `dotnet build` 0 errors / 0 warnings in changed files; `npx tsc --noEmit` 0
errors; `npm run check:responsive` 15/15; `npm run build` green. ⚠️ The first three `npm run build` attempts failed
on three *different* untouched pages — `next dev --turbopack` was running and respawned once after being stopped.
The device eye pass is still owed.

## Deferred to /test-small-feature
The three test edits above are **build-required only** — a new record field and two guard-list entries. No
scenario was written. The scenarios this change enables, none of which exist yet:

- **AC-1** — `signup`/`signup/verify` 404 in `SelfHostedLan` and `CloudBrowser` *without reaching the mediator*.
- **AC-2 / AC-5** — a valid submission writes exactly one `ClinicSignup` and no `Clinic`/`User`/`Doctor`/
  `ProcedureType`; the stored hash never equals the password and the raw token appears in no column or response.
- **AC-3** — the 202 body is byte-identical across free / already-an-account / already-pending.
- **AC-4** — a short password is refused in French *before* the neutral response.
- **AC-6 / AC-7** — a second signup re-arms the same row (and invalidates the first token); an expired row is
  replaced; spent rows are purged on the signup path.
- **AC-8 … AC-11** — provisioning through `ProvisionAsync`; the same token twice; the four causes sharing one
  refusal *and* the row being consumed in the now-taken case; lookup by hash with the constant-time compare.
- **AC-12 / AC-13** — no session, no cookie, no token in the verification response; the admin is `IsActive` with
  `MustChangePassword = false`.
- **AC-14** — catalogs asserted by **row count**, not by the absence of an exception.
- **AC-15 / AC-16** — an unset SMTP host refuses in French rather than 202-ing; the link is built from `FrontendUrl`.
- `DeploymentProfileTests` — a positive assertion that `HostedMultiTenant` alone allows public signup (the matrix
  row covers it, but the *pairing* with `AllowsSelfRegistration = false` is the decision worth pinning).
- `SchemaVerificationServiceTests` — the drift and not-applicable cases for `clinic-signup-has-no-orphans`.
