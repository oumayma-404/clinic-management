# Feature Review: clinic-self-signup

**Status:** INCOMPLETE
**Challenged:** No
**Date:** 2026-08-07
**Parent Branch:** main (merge-base with the working branch `feature/audit-sections-3-to-10`)
**Merge Base:** n/a for the diff — the feature is **uncommitted working-tree work**; the reviewed diff was
assembled with `git add -N <feature source paths>` + `git diff HEAD --unified=5`, then `git reset -q HEAD -- .`
(index restored, nothing committed). Branch tip at review time: `5f4ca28`.
**Files Reviewed:** 23 code files (+2,419 diff lines). Excluded from the reviewable diff: the six `CLAUDE.md`
docs, `20260807102000_AddClinicSignups.Designer.cs` (+3,798) and `ApplicationDbContextModelSnapshot.cs` (+76) —
all three read directly from the repo by the migration agent rather than fed as diff text — and
`features/mobile-native-shells/stories/story-1-full-clinic-on-a-phone.md`, which `progress.md` names as
unrelated pre-existing work.

**Review method:** six parallel agents rather than the default four. The default Agent 2 (ROP) was **repointed**
to this repo's actual idiom — MediatR handlers returning `Result<T>`, `ApiControllerBase` mapping to the canonical
`{ error }` body, the `when (ex is not ConflictException)` rule — because `Extensions.ROP` does not exist here.
A dedicated **Security** agent was added (the change is an anonymous, internet-facing auth surface with a CSPRNG
token, a password hash and an SMTP path), and **Agent 5 (Device & UX)** was mandatory: the diff ships two new
`.tsx` pages. The orchestrator additionally traced the request→command→entity→provisioning boundaries by hand
(`ClinicSignUpRequest` ⇄ `SignUpClinicCommand` ⇄ `DoctorPersonalInfoDto`, the DI registrations, the
`AuditSaveChangesInterceptor` reach, and `PurgeSpentAsync`'s interaction with the change tracker).

**Scope note:** `spec.md`'s *Out of Scope* section was given to every agent — password reset, billing/trials,
TTN certificate upload, per-clinic email branding, the `CreateClinicCommandHandler` seed duplication and any
change to `SelfHostedLan`/`CloudBrowser` behaviour are deliberately absent and were not raised.

---

## Findings

### Finding 1
- **Severity:** Critical
- **Category:** Security / Breaking Change
- **File:** api/ClinicManagement.Application/Features/Auth/Commands/SignUpClinicCommand.cs
- **Line:** 145
- **Anchor:** `SignUpClinicCommandHandler.Handle` — the `signup.Renew(...)` branch
- **Comment:** An unauthenticated caller can replace the credentials of somebody else's **live** pending signup.
  `GetByEmailAsync` finds the victim's unconsumed row and `Renew` overwrites `ClinicName`, `FullName`,
  **`PasswordHash`** and `TokenHash` with the attacker's values, then mails the new link to the victim's own
  address. Sequence: the victim signs up for `dr@cabinet.tn` (password P_v, token T1); the attacker POSTs
  `/api/auth/signup` for the same address with password P_a. T1 is now dead — the victim's own link answers « Ce
  lien n'est plus valable » — so the victim is pushed toward the second, byte-identical « Vérifiez votre adresse »
  mail sitting in the same inbox. Clicking it provisions the clinic with `PasswordHash = hash(P_a)` and the
  attacker signs in as its admin on a multi-tenant hosted backend. AC-6 requires « one row per address »; it does
  not require that an anonymous request be able to rewrite a pending row's credentials. Fix (preferred): when the
  existing row `IsUsable(nowUtc)`, do **not** `Renew` — re-send the *existing* token unchanged, or return the
  neutral acknowledgement without touching the row — and re-arm only once it has expired or been consumed. That
  keeps AC-6's single row and the resend behaviour while making the first submission for an address the one that
  owns it. (Flagged independently by the Security and Breaking-Change agents.)

### Finding 2
- **Severity:** Major
- **Category:** Security / Business Logic
- **File:** api/ClinicManagement.Application/Features/Auth/Commands/SignUpClinicCommand.cs
- **Line:** 119
- **Anchor:** `SignUpClinicCommandHandler.Handle` — the existing-user short circuit
- **Comment:** AC-3's « byte-identical » response does not hold, on two independent axes, so the endpoint **is**
  an account-enumeration oracle. (a) **Timing:** the already-an-account branch returns after two cheap indexed
  reads, while the free-address branch additionally runs `ILocalAuthService.HashPassword` (ASP.NET Identity v3
  PBKDF2, 100 000 iterations) *and* a synchronous SMTP round trip (up to the 20 s ceiling). The gap is hundreds
  of milliseconds to seconds — orders of magnitude above network jitter — so an attacker can walk a list of
  addresses and learn which are administrators of this hosted backend. (b) **Body:** during any mail outage the
  free branch returns « L'e-mail de vérification n'a pas pu être envoyé… » (line 159) while the taken branch
  returns the neutral sentence — a deterministic oracle needing no timing at all. Fix: compute `passwordHash`
  **before** the `GetByEmailAsync` check so both branches pay for PBKDF2; give the taken branch a real send of
  comparable cost (a « quelqu'un a tenté de créer un cabinet avec votre adresse » notice to the same address,
  which is also useful to the real owner); and return `Acknowledged()` on send failure too, surfacing the failure
  in the log and `EmailSendAttempts` rather than to the visitor. A fixed delay is not a fix — the SMTP leg's
  variance still shows through. (Flagged by three agents.)

### Finding 3
- **Severity:** Major
- **Category:** Security
- **File:** api/ClinicManagement.Application/Features/Auth/Commands/SignUpClinicCommand.cs
- **Line:** 215
- **Anchor:** `SignUpClinicCommandHandler.LooksLikeAnEmailAddress` / `ClinicSignup.NormalizeEmail`
- **Comment:** The submitted value is *validated* with `MailAddress.TryCreate` but the **raw string** — not the
  parsed `.Address` — is what is normalised, stored, uniquely indexed, compared against `Users`, and later becomes
  `User.Email`. `MailAddress` accepts the display-name form, so `Attaquant <dr@cabinet.tn>` parses fine and is
  stored as `attaquant <dr@cabinet.tn>`. Three consequences: (a) `_userRepository.GetByEmailAsync` never matches
  the real `dr@cabinet.tn` row, so both the AC-3 already-an-account guard and the AC-10 now-taken guard are
  bypassed; (b) `IX_ClinicSignups_Email` is unique per *variant*, so « one row per address » collapses and an
  attacker mints unlimited pending rows and unlimited verification mails aimed at one victim mailbox, since
  `mail.To.Add(new MailAddress(email))` re-parses and delivers to the real address every time; (c) if such a link
  is verified, `LocalClinicProvisioning` creates a `User` whose `Email` is that malformed string, which no login
  form can reproduce — the clinic is created and immediately unreachable. Fix: parse once and keep the canonical
  form — `MailAddress.TryCreate(value.Trim(), out var parsed)` then store `parsed.Address.ToLowerInvariant()` —
  and refuse anything where the parsed address does not round-trip the input (display names, extra angle brackets,
  comma-separated lists).

### Finding 4
- **Severity:** Major
- **Category:** Business Logic / Breaking Change
- **File:** api/ClinicManagement.Infrastructure/Persistence/Configurations/ClinicSignupConfiguration.cs
- **Line:** 31
- **Anchor:** `ClinicSignupConfiguration.Configure` — the `Email` property
- **Comment:** `ClinicSignup.Email` is `varchar(320)` while `User.Email` is `varchar(200)`
  (`UserConfiguration.cs:25`). Every other carried field matches its provisioning target exactly (ClinicName 200 =
  `Clinic.Name` 200, Address 500, City 100, Phone 50) — this is the one width that accepts a submission
  provisioning cannot store. An address of 201–320 characters passes signup, gets its verification email, and then
  fails at `VerifyClinicSignUpCommand` with PostgreSQL `22001`. The save that would have consumed the token is the
  one that threw, so the row stays usable and the link is **permanently unverifiable**, answering « La
  vérification n'a pas pu aboutir. Veuillez réessayer. » forever with no operator-visible reason. Fix: cap
  `ClinicSignup.Email` at 200 to match `User.Email`, **and** validate the length in
  `SignUpClinicCommandHandler.Validate` so the refusal happens before an email is sent.

### Finding 5
- **Severity:** Major
- **Category:** Security / Business Logic
- **File:** api/ClinicManagement.Application/Features/Auth/Commands/SignUpClinicCommand.cs
- **Line:** 186
- **Anchor:** `SignUpClinicCommandHandler.Validate`
- **Comment:** No stored string is length-checked before persistence, and `DoctorInfoJson` has no cap at all.
  `Validate` tests emptiness, the email shape and the password length only. Two failures follow. (a) An over-long
  `ClinicName`/`FullName`/`Phone`/`Address`/`City` makes the *insert* throw, which the catch-all at line 171
  renders as « L'inscription n'a pas pu aboutir. Veuillez réessayer. » — a retry-me message for a condition no
  retry can fix, naming no field. (b) `DoctorInfoJson` is serialised from an attacker-controlled
  `DoctorPersonalInfoDto` into a **`text`** column (`ClinicSignupConfiguration.cs:49`) with no bound anywhere; with
  Kestrel's default 30 MB body limit an anonymous caller stores ~30 MB per accepted request, at the request rate
  Finding 6 permits, in a table nothing cascades away. Its members also only meet `Doctor`'s column widths at
  *verification* time, repeating Finding 4's shape. Fix: validate maximum lengths in `Validate` (refusing in
  French) for every stored field including each `DoctorPersonalInfoDto` member, and cap the serialised
  `doctorInfoJson` — or map `DoctorInfoJson` to a bounded `varchar` and check it before the row is built.

### Finding 6
- **Severity:** Major
- **Category:** Security
- **File:** api/ClinicManagement.API/Controllers/AuthController.cs
- **Line:** 91
- **Anchor:** `AuthController.SignUp`
- **Comment:** Nothing bounds this anonymous endpoint per *recipient* or in aggregate, so the deployment is usable
  as a mail cannon and as a write amplifier. `RateLimiting.AnonymousAuthPolicy` partitions on
  `account:{submitted email}|{ip}` — a value the **attacker chooses** — so a fresh 30-permit budget is minted for
  every made-up address; the only real bound is the global `authip:{ip}` ceiling of 150 per 5 min, i.e. ~1 800
  attacker-addressed emails **per hour per source IP**, each sent from the deployment's own envelope sender and
  each writing a `ClinicSignup` row. Targeted variant: 30 verification mails per 5 min to one victim address
  (`EmailSendAttempts` is incremented but never enforced as a cap), unbounded when combined with Finding 3. This
  costs the deployment its sender reputation and hands an unauthenticated caller an unbounded INSERT primitive
  against a table with no owner and no cascade. Note the per-account partition is *right* for `login` (a practice
  behind one NAT address) and *wrong* here, where the account is not yet an account. Fix: a dedicated limiter
  policy for the signup path keyed on the resolved client address (never on the submitted email) with a low permit
  count; a per-recipient cooldown enforced against `ClinicSignup.EmailSendAttempts`/`CreatedAtUtc` before any send;
  and a global cap on live pending rows above which the endpoint refuses with the same neutral sentence.

### Finding 7
- **Severity:** Major
- **Category:** Breaking Change / Code Quality
- **File:** api/ClinicManagement.Domain/Entities/ClinicSignup.cs
- **Line:** 21
- **Anchor:** `class ClinicSignup : AggregateRoot<Guid>`
- **Comment:** Deriving from `AggregateRoot<Guid>` opts this table into `AuditSaveChangesInterceptor`, which audits
  every aggregate root not in its two-item exclusion list (`AuditEntry`, `Notification`). Three consequences the
  change does not account for. (a) An **unauthenticated** endpoint now writes rows into the append-only audit
  ledger — one per insert, one per re-arm, one per purged row — with no rate-limit relationship to a real user and
  no purge on `AuditEntries`: unbounded growth from the public internet. (b) The delete summary renders
  identifying properties verbatim, so **purging a signup permanently records the abandoned visitor's name and
  email** in a table that outlives the 30-day retention this feature advertises — the purge is sold as forgetting
  them and does the opposite. (c) `ResolveClinicId` finds no `ClinicId` on the entity and falls back to the request
  scope, which is `Unset` for an anonymous caller, so every row has `ClinicId = null` and is invisible to
  `GET /api/audit` (clinic-filtered) — write-only ballast. Fix: add `nameof(ClinicSignup)` to
  `AuditSaveChangesInterceptor.ExcludedEntityTypes` with the reason (it is explicitly the « costs noise rather than
  silence » list, and `Notification` is excluded for the same machine-noise argument), or derive from
  `Entity<Guid>` if nothing needs root semantics. (Flagged by two agents and by the orchestrator's own trace.)

### Finding 8
- **Severity:** Major
- **Category:** Breaking Change / Security
- **File:** api/ClinicManagement.Infrastructure/Repositories/ClinicSignupRepository.cs
- **Line:** 47
- **Anchor:** `ClinicSignupRepository.PurgeSpentAsync`
- **Comment:** The opportunistic purge stages tracked deletes (`RemoveRange`) that ride the caller's
  `SaveChangesAsync`, and it is both **fatal on contention** and **unbounded**. Fatal: `ClinicSignup` derives from
  `AggregateRoot<Guid>` so its `Version` is mapped to `xmin` as a concurrency token (confirmed in the snapshot:
  `IsConcurrencyToken().HasColumnType("xid").HasColumnName("xmin")`), and EF raises
  `DbUpdateConcurrencyException` for any staged DELETE affecting 0 rows regardless. Two signup POSTs in the same
  tick load the same expired rows and stage the same deletes; the loser's save throws, `UnitOfWork` translates it
  to `ConflictException`, and the handler's catch filter (`when (ex is not ConflictException)`) deliberately lets
  it through — so a perfectly valid anonymous signup returns **HTTP 409** with « quelqu'un d'autre a modifié cet
  enregistrement », writes nothing and sends no email. This fires precisely when the endpoint is busiest (a burst
  of tokens expiring 24 h after a launch). Unbounded: `ToListAsync()` materialises **every** spent row into the
  request's change tracker, before any check that could refuse the request — so the rows Finding 6 lets an attacker
  create become, 24 h later, the load every subsequent anonymous POST pays. Fix: make the purge non-fatal and
  bounded — `ExecuteDeleteAsync` outside the caller's save (or its own transaction), or
  `.OrderBy(s => s.ExpiresAtUtc).Take(batchSize)` plus a catch around the purge's contribution — and run it *after*
  the request has been accepted rather than before validation.

### Finding 9
- **Severity:** Major
- **Category:** Error Handling / Business Logic
- **File:** api/ClinicManagement.Application/Features/Auth/Commands/VerifyClinicSignUpCommand.cs
- **Line:** 157
- **Anchor:** `VerifyClinicSignUpCommandHandler.Handle`
- **Comment:** `Result<ClinicSignUpVerificationDto>.FailureFrom(provisioned)` forwards `LocalClinicProvisioning`'s
  own French messages verbatim to an **anonymous** caller — including « Un compte existe déjà avec cet email. »
  (`LocalClinicProvisioning.cs:110`), which is a direct answer to the question AC-3 and AC-10 exist to refuse, and
  which breaks the class's own stated single-refusal contract 45 lines after the `existingUser` branch takes care
  not to give it. It is also inconsistent in the other direction: that branch *spends* the row, while this path
  leaves `signup.Consume` staged-but-never-committed, so the same condition leaves a live link. The two reads are
  not atomic, so the branch is genuinely reachable — that race is exactly why provisioning re-checks. Fix:
  `_logger.LogWarning("Clinic self-signup provisioning refused for signup {SignupId}: {Reason}", signup.Id,
  provisioned.Error);` then `return Refused();` — keep the internal reason in the log, not in the response, and
  commit the consume.

### Finding 10
- **Severity:** Major
- **Category:** Code Quality / Error Handling
- **File:** api/ClinicManagement.Infrastructure/Services/SmtpTransactionalEmailSender.cs
- **Line:** 64
- **Anchor:** `SmtpTransactionalEmailSender.SendAsync`
- **Comment:** `SmtpClient.Timeout` governs the synchronous `Send` path only — it is documented as having no
  effect on asynchronous sends — so the bound the comment promises (« an unreachable mail host cannot hold the
  visitor's request open ») is not enforced. The only bound in play is the request-abort token, which fires when
  the browser disconnects, so a blackholed SMTP host holds the request, its scoped `DbContext` and the visitor's
  spinner for the OS TCP timeout. This matters more here than in the inherited `SmtpDocumentEmailSender` pattern:
  there the caller is a background outbox job, here the visitor is waiting inline on an anonymous endpoint — which
  is the whole premise of not making this an outbox. Fix: `using var cts =
  CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); cts.CancelAfter(SendTimeout); await
  client.SendMailAsync(mail, cts.Token);`, keeping the rethrow filter on `cancellationToken.IsCancellationRequested`
  only so a *timeout* cancel falls through to the `Failed` classification while a caller abort still rethrows.
  (Flagged by three agents.)

### Finding 11
- **Severity:** Major
- **Category:** Error Handling / Business Logic
- **File:** api/ClinicManagement.Infrastructure/Services/PublicAppUrlProvider.cs
- **Line:** 26
- **Anchor:** `PublicAppUrlProvider.BaseUrl`
- **Comment:** An unset or blank `FrontendUrl` is silently swallowed into `http://localhost:3000`. On
  `HostedMultiTenant` — the only profile where this feature is reachable — every verification email then carries a
  link resolving to the recipient's own machine: the visitor sees a 202 and « un lien vient de lui être envoyé »,
  the operator sees a clean log, and the clinic is never created. The handler right next door refuses *before
  writing anything* when SMTP is unconfigured (`_emailSender.IsConfigured`); the equally fatal missing-link-host
  case has no equivalent gate, and the interface's stated justification compares against the wrong alternative (a
  link with no host) rather than against the third option — refusing, the shape AC-15 already uses. Fix: add an
  `IsConfigured`-style member checked alongside `_emailSender.IsConfigured`, or fail startup loud where
  `AllowsPublicClinicSignup` is true. At minimum log at Error once on first resolution (this is a singleton, so
  cache the resolved value). (Flagged by three agents.)

### Finding 12
- **Severity:** Major
- **Category:** Business Logic
- **File:** api/ClinicManagement.Application/Features/Auth/Commands/VerifyClinicSignUpCommand.cs
- **Line:** 155
- **Anchor:** `VerifyClinicSignUpCommandHandler.Handle`
- **Comment:** **AC-14 has no assertion anywhere, and neither do AC-2/3/6/9/10/13.** The feature ships with zero
  tests for `SignUpClinicCommandHandler`, `VerifyClinicSignUpCommandHandler`, `ClinicSignup` or
  `ClinicSignupRepository` — the only test edits are the three build-required guard-list entries. AC-14 is
  specifically the criterion that *cannot* be satisfied by inspection: `TrySeedCatalogsAsync` swallows-and-logs, so
  `CatalogsSeeded == false` is only a `LogWarning` here and a clinic created through the anonymous path with no
  CNAM/medication/dental-act catalogue is indistinguishable from a healthy one; the spec says so in as many words
  (« asserted by **row counts**, not by the absence of an exception »). The same gap covers the behaviours this
  diff's own comments assert but nothing verifies: the re-arm (AC-6), single use (AC-9), the four failures sharing
  one refusal (AC-10), and `IsActive = true` / `MustChangePassword = false` (AC-13). `progress.md` defers these to
  `/test-small-feature`; this finding records that the deferral is load-bearing, not cosmetic.

### Finding 13
- **Severity:** Major
- **Category:** Device & UX
- **File:** web/app/signup/verifier/page.tsx
- **Line:** 49
- **Anchor:** `VerifierContent` — the `verify()` catch
- **Comment:** Every thrown value collapses into `{ kind: "refused" }`, rendering the destructive « Lien non
  valable » panel whose only action is « Recommencer l'inscription ». At any width, but the case that actually
  happens is a phone on a marginal mobile signal opening the emailed link: `client.ts` raises
  `ApiError(code: Network)` / a `deadline()` abort, and the visitor is told their still-valid single-use link is
  dead and pushed to re-run signup — the connection loss is rendered as a business refusal, which
  `frontend-web.md` § 13 and `lib/errors.ts`' `isNetworkError` exist to keep apart. The page also cannot retry:
  `attempted.current` is already `true`, so only a full reload re-fires. Fix: branch on `isNetworkError(err)` into
  a third state (« Connexion interrompue » + a « Réessayer » button that resets `attempted.current = false` and
  re-runs `verify()`), keeping « Lien non valable » for a real 4xx.

### Finding 14
- **Severity:** Major
- **Category:** Device & UX
- **File:** web/app/signup/verifier/page.tsx
- **Line:** 124
- **Anchor:** `Panel` — the `<CardTitle>`
- **Comment:** The success title is `` `${state.clinicName} est créé` `` (line 91) — the only dynamic title in the
  diff — rendered at `text-2xl` (24 px) with no `break-words`. At 320 px the card's content box is 240 px
  (320 − `p-4` − `px-6`), so a clinic name typed without spaces at signup (« CabinetDentaireBenSalah », which the
  form accepts) is one unbreakable ~330 px run: it overflows the card, and since no ancestor clips, the **document
  gains body-level horizontal scroll** — the exact defect `join-unavailable.tsx` documents and guards against with
  `break-words` on its *static* title. At 200 % zoom an ordinary two-word name does it too. Fix:
  `className="text-2xl break-words text-accent-foreground"` on `Panel`'s `CardTitle`.

### Finding 15
- **Severity:** Major
- **Category:** Device & UX
- **File:** web/app/signup/page.tsx
- **Line:** 84
- **Anchor:** `SignUpPage.handleSubmit` — the client-side validation branches
- **Comment:** Both local refusals (`password.length < 8` at L84, the practitioner-name rule at L91) `setError(...)`
  and return with **no toast and no scroll**. `FormErrorBanner` renders at L216, i.e. above « Nom du cabinet ». At
  320/390 px the form is seven stacked fields plus the disclosure — roughly 900 px — so the submit button the user
  just tapped is a screen and a half below the banner: nothing within the viewport changes, the button simply
  re-enables, and the page reads as « the button does nothing ». `noValidate` on the form also makes the `required`
  / `minLength` attributes inert, so these two branches are the only client feedback there is. Fix: raise the same
  message through `showErrorToast(null, message)` in both branches, or `scrollIntoView({ block: "center" })` the
  banner after setting it.

### Finding 16
- **Severity:** Major
- **Category:** Device & UX
- **File:** web/app/signup/page.tsx
- **Line:** 303
- **Anchor:** `SignUpPage` — the Gouvernorat `<SelectTrigger>` (and L379, Spécialité)
- **Comment:** Neither `SelectTrigger` is given a width, so both keep the primitive's base `w-fit`
  (`components/ui/select.tsx:42`) while every sibling `Input` is `w-full`. Two consequences at 320 px: once a value
  is picked the Gouvernorat trigger collapses to its text — « Sfax » is a ~100 px control in a 240 px field column,
  so the tap area to change it is a quarter the width of the Adresse field directly beneath it; and inside the
  practitioner box the available width is only 208 px (320 − `p-4` − `px-6` − `px-4`) while the trigger is
  `whitespace-nowrap` and « Choisir une spécialité » plus `px-3` + `gap-2` + the 16 px chevron measures wider than
  that at 16 px type — `fit-content` floors at `min-content`, so the trigger pushes past the bordered box's right
  edge and, with nothing clipping up the tree, out of the document. Every other in-form `SelectTrigger` in the repo
  passes an explicit width. Fix: `className="w-full"` on both.

### Finding 17
- **Severity:** Major
- **Category:** Device & UX
- **File:** web/app/signup/page.tsx
- **Line:** 123
- **Anchor:** `SignUpPage.handleSubmit` — the catch
- **Comment:** `toast.error(message)` is raised directly instead of `showErrorToast(err, "…")` from
  `lib/errors.ts`, which that module documents as the only place an error toast may be raised. Two device
  consequences, both on a phone: the toast takes the global 4 s success duration instead of 8 s, and with
  `visibleToasts: 3` a full French sentence about a failed signup can be pushed off screen before it is read; and
  a transport failure loses the network-only « Réessayer » action — precisely the failure a visitor signing up on
  mobile data hits. Fix: `showErrorToast(err, { fallback: "L'inscription n'a pas pu aboutir. Veuillez réessayer.",
  onRetry: … })` and keep `setError(message)` for the banner.

### Finding 18
- **Severity:** Minor
- **Category:** Business Logic / Error Handling
- **File:** api/ClinicManagement.Application/Features/Auth/Commands/VerifyClinicSignUpCommand.cs
- **Line:** 123
- **Anchor:** `VerifyClinicSignUpCommandHandler.Handle`
- **Comment:** Two simultaneous verifications of the same token do not produce the shared refusal. `ClinicSignup`'s
  `Version` is a real `xmin` concurrency token, so both requests read the row as usable, both stage `Consume`, and
  the loser's save raises `DbUpdateConcurrencyException` → `ConflictException` — which this handler's catch filter
  deliberately re-throws, so the visitor gets a **409** carrying `ErrorMessages.Conflict` (« quelqu'un d'autre a
  modifié cet enregistrement ») on a page whose clinic was in fact created successfully by the sibling request. The
  `attempted` ref on `/signup/verifier` covers the StrictMode double-mount but not two tabs, a retry, or a
  double-tapped link. Fix: catch `ConflictException` specifically here and `return Refused()` — the correct
  user-facing statement is « ce lien a déjà été utilisé ».

### Finding 19
- **Severity:** Minor
- **Category:** Business Logic
- **File:** api/ClinicManagement.Infrastructure/Persistence/SchemaVerificationReader.cs
- **Line:** 516
- **Anchor:** `SchemaVerificationReader.ReadDataMigrationCountsAsync` — `clinic-signup-has-no-orphans`
- **Comment:** The new counter asks a different question from the code it verifies. Its first half is
  `EXISTS (SELECT 1 FROM "Users" u WHERE LOWER(u."Email") = s."Email")`, while the application's own « does this
  address already have an account? » is `UserRepository.GetByEmailAsync`, which requires
  `u."PasswordHash" IS NOT NULL` (`UserRepository.cs:86`) — the same filter the partial unique index on
  `Users.Email` uses. A password-less row (a Cloud/Auth0 identity, or a legacy row) therefore makes the counter
  report a stuck signup that both the signup and verification paths consider perfectly live: drift reported where
  there is none, on the one gate a schema change has. Fix: add `AND u."PasswordHash" IS NOT NULL` to the predicate.

### Finding 20
- **Severity:** Minor
- **Category:** Business Logic / Error Handling
- **File:** api/ClinicManagement.Application/Features/Auth/Commands/SignUpClinicCommand.cs
- **Line:** 114
- **Anchor:** `SignUpClinicCommandHandler.Handle` — the purge / re-read ordering
- **Comment:** `PurgeSpentAsync` runs **before** the caller's own row is looked up (L114 then L134), and its
  predicate (`ConsumedAtUtc == null && ExpiresAtUtc <= nowUtc`) matches exactly the row AC-6's « an expired pending
  row is replaced » is about. `RemoveRange` marks that entity `Deleted`; the subsequent `GetByEmailAsync` returns
  the *same tracked instance* by identity resolution — still `Deleted` — and `Renew()` + `DbSet.Update()` happens to
  flip it back to `Modified`. It works, but only by that accident, and nothing in either file says so. Two plausible
  edits break it silently: switching to the `ExecuteDelete` the docstring names as the rejected alternative, or
  moving the purge after the lookup — both leave a staged `DELETE` and a staged `INSERT` for the same `Email`,
  which the unique index refuses depending on how EF orders the batch, while the visitor is told an email is on its
  way. Fix: purge *after* the row is resolved and exclude the caller's own address, or state the Deleted→Modified
  transition explicitly and pin it with a test. (Flagged by two agents.)

### Finding 21
- **Severity:** Minor
- **Category:** Error Handling
- **File:** api/ClinicManagement.Application/Features/Auth/Commands/SignUpClinicCommand.cs
- **Line:** 171
- **Anchor:** `SignUpClinicCommandHandler.Handle` — the catch-all
- **Comment:** Two simultaneous signups for one address are an *expected* condition here — the unique `Email` index
  is explicitly described as « an invariant the database holds, not a race the handler hopes to win » — but the
  resulting `DbUpdateException` (23505) is not translated by `UnitOfWork` (which handles only
  `DbUpdateConcurrencyException` and 23P01) and so falls into this catch-all as « L'inscription n'a pas pu
  aboutir. ». The loser gets an opaque failure *and* a response distinguishable from the neutral acknowledgement
  everyone else receives — another AC-3 leak. Fix: add an arm above the catch-all —
  `catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { _logger.LogInformation(…); return Acknowledged(); }`
  — the winner's email really was sent, so the neutral sentence stays true.

### Finding 22
- **Severity:** Minor
- **Category:** Error Handling
- **File:** api/ClinicManagement.Application/Features/Auth/Commands/SignUpClinicCommand.cs
- **Line:** 145
- **Anchor:** `SignUpClinicCommandHandler.Handle` — the re-arm / commit ordering
- **Comment:** `Renew` overwrites `TokenHash` and the row is committed (L154) *before* the send is known to
  succeed, so a transient SMTP failure leaves the visitor strictly worse off than before the request: the link
  already in their inbox no longer verifies, no new one arrived, and the refusal tells them to « réessayer dans
  quelques instants » without mentioning that the earlier link is now void. Fix: re-arm only when the existing row
  is no longer usable (`!signup.IsUsable(nowUtc)`) and reuse the live one otherwise — which is also Finding 1's fix
  — or state it in the refusal so the message matches the state that was committed.

### Finding 23
- **Severity:** Minor
- **Category:** Error Handling
- **File:** api/ClinicManagement.API/Controllers/AuthController.cs
- **Line:** 114
- **Anchor:** `AuthController.SignUp`
- **Comment:** `HandleFailure(result)` renders every signup refusal as **400**, including the two that are not the
  caller's fault: « le serveur d'envoi d'e-mails (Notification:Smtp) n'est pas configuré » and « L'e-mail de
  vérification n'a pas pu être envoyé … réessayer ». A 400 tells every client and proxy the request was malformed
  and is not retryable, contradicting the message's own instruction. Fix: tag those two failures with a `Result`
  `Code` (e.g. `email_unavailable`) in the handler and map it here to `503` via `HandleFailure(result, 503)`,
  leaving the validation refusals at 400.

### Finding 24
- **Severity:** Minor
- **Category:** Code Quality / Error Handling
- **File:** api/ClinicManagement.Application/Features/Auth/Commands/SignUpClinicCommand.cs
- **Line:** 161
- **Anchor:** `SignUpClinicCommandHandler.Handle` — the send-failure log
- **Comment:** The log records `sent.Outcome` but drops `sent.Error` — the field
  `TransactionalEmailResult` exists to carry (« the operator-facing reason for a failure »), which is therefore
  read by nothing in the solution and is effectively dead. The SMTP server's own message (bad mailbox, auth
  rejected, relay denied) is the only thing that tells an operator why signups are failing. Fix:
  `_logger.LogWarning("Clinic signup verification email could not be sent for signup {SignupId}: {Outcome}
  {Reason}", signup.Id, sent.Outcome, sent.Error);`. Keep `sent.Error` out of the returned `Result` message — it
  can contain the SMTP host and response. (Flagged by three agents.)

### Finding 25
- **Severity:** Minor
- **Category:** Security
- **File:** api/ClinicManagement.Application/Features/Auth/Commands/SignUpClinicCommand.cs
- **Line:** 103
- **Anchor:** `SignUpClinicCommandHandler.Handle` — the SMTP-not-configured refusal
- **Comment:** The refusal returned to an unauthenticated internet caller names an internal configuration key
  (« le serveur d'envoi d'e-mails (Notification:Smtp) n'est pas configuré »). That tells a stranger the backend's
  config schema and that this deployment's mail transport is down — useful for fingerprinting and for timing an
  abuse window. AC-15 asks for a refusal the *operator* can act on, which is what the log is for. Fix: return a
  neutral French sentence (« L'inscription en ligne est momentanément indisponible. Réessayez plus tard. ») and log
  the missing key at Error.

### Finding 26
- **Severity:** Minor
- **Category:** Security
- **File:** web/app/signup/verifier/page.tsx
- **Line:** 24
- **Anchor:** `VerifierContent` — `searchParams.get("token")`
- **Comment:** The verification token travels in the **query string** of a page navigation, so the live single-use
  credential is written to the reverse proxy's access log (Caddy logs the full URI), to any intermediate log on the
  way, and to the browser's history and session-restore store — all of which outlive the 24 h window it is bounded
  by. `Referrer-Policy: strict-origin-when-cross-origin` protects cross-origin leaks, but same-origin navigations
  from this page still carry the full URL. Partly mitigated because the effect consumes the token on mount, but a
  failed request (offline, 429 — see Finding 13) leaves it live *and* logged. Fix: strip the token from the visible
  URL as soon as it is read (`history.replaceState` to `/signup/verifier`), and prefer the fragment (`#token=`)
  over the query string in `BuildVerificationLink` — a fragment never reaches the server or an access log.

### Finding 27
- **Severity:** Minor
- **Category:** Code Quality
- **File:** api/ClinicManagement.Domain/Entities/ClinicSignup.cs
- **Line:** 159
- **Anchor:** `ClinicSignup.TokenHashMatches`
- **Comment:** This comparison can never return false, so it is dead code and the constant-time guarantee its
  docstring states is not delivered. `VerifyClinicSignUpCommandHandler` reaches it only via `GetByTokenHashAsync`,
  whose predicate is `s.TokenHash == tokenHash` — the row returned already has an equal hash, and the
  timing-variable work (the indexed equality search in PostgreSQL) happened before `FixedTimeEquals` was reached.
  Either drop the method and its call (the lookup *is* the decision, and AC-11 is satisfied by the hash being what
  is stored and indexed), or, if constant-time matching is genuinely wanted, the lookup itself has to stop being an
  equality search. As written it costs a UTF-8 allocation per verification and documents a property the code does
  not have.

### Finding 28
- **Severity:** Minor
- **Category:** Code Quality
- **File:** api/ClinicManagement.Domain/Entities/ClinicSignup.cs
- **Line:** 164
- **Anchor:** `ClinicSignup.NormalizeEmail`
- **Comment:** This is the **third** copy of `email.Trim().ToLowerInvariant()` — the others are inline in
  `User.CreateLocalUser` (`User.cs:106`) and `UserRepository.GetByEmailAsync` (`UserRepository.cs:82`) — and the
  doc above `Email` says out loud that it exists to match `User.CreateLocalUser`'s normalisation. That is exactly
  the « a rule with more than one answer » shape this repo tracks (`fixes-dont-propagate`): the signup row's key,
  the account's stored email and the lookup deciding « already an account » must agree, and today only convention
  keeps them agreeing — which Finding 3 shows is already not enough. Fix: put the rule in one place (a static on
  `User`, or an `EmailNormalization` helper in Domain) and have all three call it.

### Finding 29
- **Severity:** Minor
- **Category:** Code Quality
- **File:** api/ClinicManagement.Application/Features/Auth/Commands/SignUpClinicCommand.cs
- **Line:** 200
- **Anchor:** `SignUpClinicCommandHandler.Validate` — the practitioner rule
- **Comment:** The practitioner rule is retyped here with the comment « Mirrors LocalClinicProvisioning.Validate »
  — a literal copy of that block, French message included. Validating at signup rather than at verification is
  right (refusing at verification is useless), but the *rule* should not have two bodies: the one in
  `LocalClinicProvisioning` is already the shared definition and the two will drift the first time either the
  fields or the wording change. Fix: expose it (e.g. `public static string?
  ValidatePractitioner(DoctorPersonalInfoDto? info)`) and call it from both.

### Finding 30
- **Severity:** Minor
- **Category:** Code Quality
- **File:** api/ClinicManagement.Infrastructure/Repositories/ClinicSignupRepository.cs
- **Line:** 36
- **Anchor:** `ClinicSignupRepository.UpdateAsync`
- **Comment:** `_context.ClinicSignups.Update(signup)` is unguarded, unlike the documented pattern in
  `ClinicRepository.UpdateAsync` / `PatientRepository` (attach only when
  `_context.Entry(x).State == EntityState.Detached`). `ClinicSignup` derives from `AggregateRoot<Guid>`, so its
  `Version` is mapped to `xmin` — the exact reason that guard exists. Both current call sites hand in a tracked
  instance, so today this only over-marks every property as modified, but a future detached caller would get
  `WHERE xmin = 0`, zero rows matched, and a spurious 409. Fix: copy the guarded form.

### Finding 31
- **Severity:** Minor
- **Category:** Code Quality
- **File:** api/ClinicManagement.Infrastructure/Services/SmtpTransactionalEmailSender.cs
- **Line:** 60
- **Anchor:** `SmtpTransactionalEmailSender.SendAsync`
- **Comment:** The whole SMTP transport body is a near-verbatim copy of `SmtpDocumentEmailSender.SendAsync` (client
  construction, `EnableSsl`/`DeliveryMethod`/`Timeout`, optional `NetworkCredential`, the from-address with and
  without display name, the `MailMessage`, the cancel/catch pair) — including Finding 10's timeout defect, which
  now has to be fixed twice. The genuinely different part is only *where the settings come from* (per-install
  `SmtpConfig` vs `ResolvedReminderSettings`), which is what justifies the separate interface; the transport does
  not need a second copy. Fix: extract the mechanics into one internal helper and have both senders resolve their
  settings and call it. Separately (L74), `SmtpConfig.FromAddress(_configuration)` is evaluated twice on adjacent
  lines with a null-forgiving `!` each time, and Host/Port/UseTls/Username/Password/FromName are re-read from
  `IConfiguration` mid-expression — hoist them into locals so the send uses one consistent snapshot and both `!`
  operators disappear.

### Finding 32
- **Severity:** Minor
- **Category:** Code Quality
- **File:** web/app/signup/page.tsx
- **Line:** 28
- **Anchor:** `withTimeout`
- **Comment:** A verbatim second copy of `web/app/join/page.tsx`'s `withTimeout` (the comment says so out loud),
  down to a differently-named constant for the same value (`CAPABILITY_PROBE_TIMEOUT_MS` here vs
  `CapabilityProbeTimeoutMs` there). Both also leave the `setTimeout` running after the promise settles. Fix: move
  it to `web/lib/` with one exported timeout constant, clear the timer in a `finally`, and have both pages import
  it.

### Finding 33
- **Severity:** Minor
- **Category:** Business Logic / Device & UX
- **File:** web/app/signup/page.tsx
- **Line:** 90
- **Anchor:** `SignUpPage.handleSubmit` — the practitioner block
- **Comment:** The practitioner block is keyed entirely on `specialty`: a visitor who opens « Je suis aussi le
  praticien », types their first and last name and does not touch the Spécialité select gets
  `doctorInfo: undefined` sent, no validation message, and no `Doctor` record — the section's own promise (« crée
  votre fiche praticien (cachet, n° d'ordre) ») silently does nothing. The server mirrors the same asymmetry
  (`SignUpClinicCommand.cs:198` refuses only a *nameless* practitioner; `LocalClinicProvisioning.cs:149` creates a
  `Doctor` only when `Specialty` is non-empty), so nothing downstream catches it and the fiche can then only be
  created after the fact through « Mon profil ». Fix: refuse the reverse case too — a first/last name with no
  specialty — rather than dropping the block.

### Finding 34
- **Severity:** Minor
- **Category:** Breaking Change
- **File:** web/lib/api/auth.ts
- **Line:** 39
- **Anchor:** `interface AuthModeDto`
- **Comment:** `publicSignupEnabled` is declared non-optional, but the field is new — in the hosted topology `web`
  and `api` are separate containers, so a rolling deploy (or a web build in front of an older API) legitimately
  receives a `mode` payload without it, and TypeScript then asserts a `boolean` where the value is `undefined`.
  The signup page happens to fail safe (`!undefined` → `signupClosed`, so the door reads as shut rather than open),
  but the type is a lie for any future consumer, and the failure is invisible because `apiGet` does no shape
  validation. Fix: declare it `publicSignupEnabled?: boolean` and read it as `publicSignupEnabled === true` at the
  call site.

### Finding 35
- **Severity:** Minor
- **Category:** Device & UX
- **File:** web/app/signup/page.tsx
- **Line:** 166
- **Anchor:** `SignUpPage` — the `if (sentTo)` confirmation panel
- **Comment:** The confirmation replaces the whole form and its only action is « Retour à la connexion ». Because
  the server's acknowledgement is deliberately neutral, a visitor who mistyped their address — much more likely on
  a phone keyboard at 320/390 px than at a desk — sees a success panel, never receives the mail, and has no way
  back: the browser Back button leaves the route entirely rather than restoring `sentTo = null`, and the typed
  values are gone. Fix: add a secondary « Modifier l'adresse » button doing `setSentTo(null)` — every field's state
  is still held, so the form comes back populated.

### Finding 36
- **Severity:** Minor
- **Category:** Device & UX
- **File:** web/app/signup/page.tsx
- **Line:** 407
- **Anchor:** `SignUpPage` — the « Vous avez déjà un compte ? » footer link
- **Comment:** `/signup` links out to `/login`, but nothing anywhere links **in**. On `HostedMultiTenant` the login
  screen's two footer links (`app/login/page.tsx:132-143`) offer « Rejoindre la clinique » — which lands on
  `JoinUnavailable` because `AllowsSelfRegistration` is false there — and « Configurer la clinique »; there is no
  « Créer votre cabinet ». At every width, a visitor arriving at the product's front door cannot reach the one page
  that exists for them without typing the URL. Fix: add a third footer link on the login page's local branch, gated
  on the same `authApi.getMode().publicSignupEnabled` this page already probes.

### Finding 37
- **Severity:** Suggestion
- **Category:** Code Quality
- **File:** api/ClinicManagement.Domain/Entities/ClinicSignup.cs
- **Line:** 56
- **Anchor:** `ClinicSignup.EmailSendAttempts`
- **Comment:** Written (`RecordEmailSendAttempt`, on both the create and renew paths), persisted, and read by
  nothing — no query, no DTO, no `verify-schema` counter, no log line. Its docstring says « for operator
  diagnosis », but there is no door an operator can reach it through: `GET /api/audit` is clinic-scoped and a
  signup has no clinic, and `GET /api/outbox` covers the three `ClinicId`-keyed queues only. Either surface it (the
  natural homes are the send-failure log line of Finding 24 and the `clinic-signup-has-no-orphans` reader, which
  already reads this table) or drop the property and its column. Note Finding 6 proposes making it load-bearing as
  a per-recipient cooldown, which would resolve this too.

### Finding 38
- **Severity:** Suggestion
- **Category:** Code Quality
- **File:** api/ClinicManagement.Application/Features/Auth/Commands/SignUpClinicCommand.cs
- **Line:** 114
- **Anchor:** `SignUpClinicCommandHandler.Handle` — the discarded purge count
- **Comment:** `PurgeSpentAsync` returns the number of rows staged for deletion and the sole caller discards it, so
  the interface's return value is dead and the only trim this table ever gets is invisible in the logs — precisely
  the failure mode `verify-schema`'s `clinic-signup-has-no-orphans` was added to detect *after the fact*. Fix: log
  it when non-zero (`_logger.LogInformation("Purged {Count} spent clinic signup(s).", purged)`), or drop the return
  type from the interface.

### Finding 39
- **Severity:** Suggestion
- **Category:** Business Logic
- **File:** web/app/signup/page.tsx
- **Line:** 109
- **Anchor:** `SignUpPage.handleSubmit` — `doctorInfo.phone`
- **Comment:** `doctorInfo.phone` is filled from the **clinic** phone field (`phone.trim() || undefined`), so the
  practitioner record is created with a number the user never entered for the practitioner. In the single-dentist
  case they usually coincide, but the value is persisted on `Doctor` and shown as their contact; a wrong-by-default
  field is harder to notice than an empty one. Fix: omit `phone` from `doctorInfo` (leave the practitioner's number
  to « Mon profil »), or add a phone input inside the practitioner section.

### Finding 40
- **Severity:** Suggestion
- **Category:** Device & UX
- **File:** web/app/signup/verifier/page.tsx
- **Line:** 111
- **Anchor:** `Panel`
- **Comment:** `Panel` — a 16 px round tinted chip + icon, a `text-2xl` title, a description and one full-width
  action — is hand-rolled here and then hand-rolled three more times in `web/app/signup/page.tsx` (the probing
  state L128, the closed-door card L142, the sent card L167). That is the shape `components/ui/empty-state.tsx`
  already owns (`icon` + `title` + `description` + `action` + `chipClassName`, with loading / refused / done
  documented as distinct kinds), and the outer `min-h-dvh` + `mx-auto my-auto max-w-md` wrapper is a fourth copy of
  `components/join-unavailable.tsx`'s. Consequence: the next device fix to this auth-card shell — Finding 14's
  `break-words` is one — has to be made in five places. Fix: render these states through `EmptyState` inside one
  shared auth-shell component, as `join-unavailable.tsx` is already shared between `/join`'s probe and
  `join-wizard`'s 404 backstop.

---

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 1 |
| Major | 16 |
| Minor | 19 |
| Suggestion | 4 |
| **Total** | 40 |

### By category

| Category | Count |
|----------|-------|
| Security | 8 |
| Business Logic | 8 |
| Code Quality | 10 |
| Error Handling | 6 |
| Breaking Change | 2 |
| Device & UX | 6 |

### Themes

1. **The neutral-response contract (AC-3/AC-10) does not hold in four independent ways** — timing (Finding 2),
   the send-failure body (2), the provisioning message leak (9), and the unique-violation path (21). The design
   intent is sound and heavily documented; the implementation leaks on every branch that was not the happy path.
2. **The pending row is a mutable, anonymous-writable credential store** — Findings 1, 3, 5, 6 and 22 all descend
   from « an unauthenticated request may create *and rewrite* a row holding a password hash », with no per-recipient
   bound and no field caps.
3. **`AggregateRoot<Guid>` had two unintended consequences** — the audit ledger (7) and the `xmin` concurrency
   token turning both the purge (8) and a double verification (18) into user-facing 409s.
4. **Three widths and one email-shape mismatch cross a layer boundary silently** — Findings 3 and 4 both produce a
   clinic that is created but unreachable, or a link that can never verify, with a « réessayer » message in front
   of it.
5. **The two new pages do not reuse the primitives the repo already has** — `EmptyState`, `showErrorToast`,
   `isNetworkError`, `withTimeout`, and `w-full` on `SelectTrigger` — which is both the device defects (14, 16) and
   the duplication (32, 40). `progress.md` states the visual walk at 320/390/820/1180/1440 px was **not performed**
   (no browser in the environment); Findings 14 and 16 are the kind that walk would have caught.

### Owed before merge (from `progress.md`, not findings)

- Regenerate/verify `20260807102000_AddClinicSignups` with `dotnet ef` in an unrestricted environment, and run
  `verify-schema` before and after the migration and diff (this is also where Finding 19's predicate should be
  re-checked).
- The visual device walk at 320/390/820/1180/1440 px plus a landscape phone and a keyboard pass.
- The `/test-small-feature` scenarios — see Finding 12.
