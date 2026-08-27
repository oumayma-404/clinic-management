# password-recovery-gaps — what shipped, and why it is that way

An audit of « what happens when somebody forgets their password » found three working paths (Auth0 in
`CloudBrowser`; an admin resetting a colleague; `reset-admin-password` on the server) and four gaps. All four are
closed. The four are worth reading as one decision, because each ✗ was a *different* kind of missing.

## A mailbox may replace a password, and that is safe only because TOTP still gates the sign-in

`RequestPasswordResetCommand` + `CompletePasswordResetCommand` (`Features/Auth/Commands/`) are the first
self-service way back into a local account. The recovery codes never were one: `RedeemRecoveryCodeCommand`
verifies the **password** before it will spend a code, deliberately, so a stranger cannot burn an account's codes
by guessing — which left the most ordinary failure in the product with no path its owner could take alone.

⚠️ **The completion step does not touch the second factor, and must never start.** Controlling the mailbox is
enough to replace a password *precisely because* the six-digit code still gates the sign-in that follows. Clearing
the factor here — or opening a replacement window as `RedeemRecoveryCodeCommand` does after proving two things —
would convert read access to one inbox into full account takeover. `CompletePasswordResetCommandHandlerTests`
pins it.

⚠️ **It issues no session either.** A reset is not an authentication; the BFF-less client call writes no cookies.

⚠️ **Three things fall out of `User.SetPassword` and none is incidental**: `TokenVersion` is bumped (every session
opened with the forgotten password dies — right, if the reason it was forgotten is that somebody else changed it),
the lockout is cleared (a person who locked themselves out guessing does not then wait fifteen minutes), and it is
the one choke point every password path funnels through, so none of it was re-implemented.

## The row carries no `ClinicId`, and that is load-bearing rather than tidy

`PasswordResetRequest` has no clinic column. Both endpoints are anonymous, so **no tenant scope is ever
established**, and an `Unset` scope reads zero rows *with no error* — indistinguishable from « no such request »,
on the one path whose job is telling those two apart. Omitting the column puts the table outside the EF query
filter by construction and outside `TenantScopeFilterTests`' clinic-owned set, which is derived from the presence
of that very property. `ClinicSignup` is outside it for the same mechanical reason. There is an assertion
(`The_Entity_Carries_No_ClinicId`) so adding the column for neatness fails loudly instead of silently.

## Token lifetime is one hour, against the signup link's twenty-four

A signup link creates something that does not exist yet; this one replaces the credential of an account already
holding patient records. An hour covers a mail queue and a walk to another room without leaving a live key in an
inbox overnight. The 2-minute per-account resend cooldown is the signup path's, reused via a derived
`LastIssuedAtUtc()` so no column carries it.

⚠️ **The token rides in the URL *fragment***, never the query string — a fragment is never sent to a server, so
the live credential stays out of the proxy access log and every intermediate hop, all of which outlive the hour by
a long way. Asserted, because `?token=` would be a silent permanent leak.

## Every ineligible branch answers identically, and the timing does not

Unknown address, deactivated account, pending activation, Auth0-backed account: all four answer the same neutral
French sentence as a real request. What is **not** claimed is indistinguishability by stopwatch — a live account
writes a row and waits on SMTP; an unknown one returns immediately. Closing that would mean faking a send or
deferring the real one to a queue, and every queue in this product is clinic-keyed. The bound on probing is the
`AnonymousAuthPolicy` limiter, and the handler says so in as many words so nobody later reads the identical
sentences as a guarantee they are not.

## `AllowsPasswordResetByEmail` is a capability, and `SelfHostedLan` is the interesting ✗

True for `HostedMultiTenant` alone. `CloudBrowser` does not own its identities (a local token would replace a
`PasswordHash` its login path never reads). `SelfHostedLan` *does* own them but is a surgery PC with no SMTP
credentials and frequently no internet — so the capability would be **present-and-broken**, a « Mot de passe
oublié ? » link that always answers « impossible d'envoyer l'e-mail ». There the ways back stay an
administrator's reset and `reset-admin-password` on the machine in the room, and the login screen **names them**.

The capability 404s the route before the mediator; `ITransactionalEmailSender.IsConfigured` is the separate,
runtime, French refusal. Conflating the two would either 404 a misconfigured install (« the feature does not exist
here », false) or advertise a link on a LAN install that can never send.

## The login screen's line is present *before* the first refusal

`ClinicAuthRefusals.InvalidCredentials` stays « Identifiants invalides. » — its vagueness is deliberate
anti-enumeration, so the guidance cannot live in the error. `ForgotPasswordLine` renders a link where the
capability exists, a sentence naming the administrator where it does not, and **nothing at all** while the probe is
unresolved: the two branches say opposite things about who can help, and flashing the wrong one is worse than a
moment of nothing.

⚠️ **Both new routes are in `middleware.ts`'s `PUBLIC_ROUTES`, and omitting them was a self-cancelling bug** —
found by the browser walk, not by any check. Somebody who has forgotten their password has no session by
definition, so gating « mot de passe oublié » on one sends them to the screen they just failed at, and the emailed
link lands on `/login?returnTo=…` instead of the form, spending nothing and explaining nothing. `tsc`, the build
and all 20 responsive checks passed with the bug in place.

## The vendor gets the sibling of the second-factor reset, not a combined « réinitialiser l'accès »

`ResetClinicUserPasswordFromConsoleCommand` mirrors `ResetClinicUserSecondFactorFromConsoleCommand`: mandatory
motif on the `PlatformAccessEntry` row, ledger staged **before** the single save, cabinet in the URL and person in
the body (so the console gains no roster read), and the affected person told in-app *and* by e-mail that the
**vendor** did it.

⚠️ **Two calls, never one button.** A caller who has lost both credentials is rare; one who has lost one is the
ordinary case, and a combined control would reset the credential they still hold. Keeping them apart is also what
stops a single telephone call defeating both proofs, and it puts two rows in the journal instead of one ambiguous
one.

⚠️ **The temporary password is returned once and never mailed.** The mailbox is either unreachable — the reason
this path exists — or in somebody else's hands, the reason the notice exists; mailing the credential would make
that notice the delivery mechanism for the takeover it is meant to reveal. Asserted
(`The_Notification_Email_Never_Carries_The_Temporary_Password`), and kept out of every log line too: a log is read
by more people, and kept longer, than the screen that shows it once.

## `platform-account --reset-password`, and the inference it broke

A console account whose password was forgotten previously had one remedy: deactivate it and create another —
discarding that account's enrolled authenticator, its recovery codes and its journal identity to fix a forgotten
string. The verb now has a fourth mutually-exclusive switch.

⚠️ **`Report` used to infer the operation from which fields of `PlatformAccountProvisioned` were null**, which was
sound while three operations existed and stopped being sound the moment a fourth returned « a temporary password
and no secret »: a password reset would have printed « Console account created » and enrolment instructions for a
secret it never minted. The operation is passed in now.

## Two things shared rather than copied, both because the second call site arrived

- **`EmailAddressInput`** — the display-name defence (`Attaquant <dr@cabinet.tn>` parses, and stored verbatim
  matches no `User` row, is unique per variant, and yields an account no login form can reproduce). It was private
  to `SignUpClinicCommand`, correct while that was the only anonymous door taking an address from the internet.
- **`PasswordResetNotice`** / **`EmailGreeting`** — the in-app row is written by `NotificationGenerator` and the
  e-mail by whichever command did the reset, so the wording would have existed twice per actor.
  `SecondFactorResetNotice` is the precedent and states the reasoning in full. There is deliberately **no
  `SelfService` member** on `PasswordResetBy`: somebody who chose their own password gets a different message
  entirely, and « quelqu'un a réinitialisé votre mot de passe » would be an alarm about the reader's own action.

## Adjacent gap closed in the same pass

`ResetUserPasswordCommand` — an **administrator** resetting a colleague — had no notification at all, while
`ResetUserTotpCommand` beside it always had one. So an admin (or a stolen admin session) could replace a
colleague's credential with nothing reaching that colleague: their sessions simply end, which reads as an ordinary
timeout. It now sends the same notice with `PasswordResetBy.ClinicAdministrator`.

## Verification

- `dotnet test -c Release` (out-of-repo `BaseOutputPath`, per the SAC note): **3538 passed, 0 failed**, including
  ~60 new cases.
- Five **derived guards** went red on arrival and each was answered with a reviewed entry rather than an
  exemption: `DeploymentProfileTests` (matrix + hosted-only set), `ControllerAuthorizationCoverageTests` (two new
  anonymous endpoints), `SubscriptionExemptionCoverageTests` (three entries — the two auth routes are covered by
  `AuthController`'s class-level attribute), `SecretProtectionCoverageTests` (the token hash is a named plaintext
  decision).
- `verify-schema` before → 8 drifts including the three new indexes « MISSING in the database »; after applying
  the migration → **5**, the three resolved (`(TokenHash): present (unique)`, `(UserId): present (unique)`,
  `(ConsumedAtUtc, ExpiresAtUtc): present`) and the 5 pre-existing ones (audit chain, overlapping appointments,
  messaging month, key ring, superseded secrets) untouched.
- ⚠️ The scaffold emitted `xmin` inside `CreateTable`; removed by hand. `CREATE TABLE … (xmin xid)` is refused
  outright by PostgreSQL, so the migration would have been unappliable on an empty database. `AddClinicSignups`
  has the same shape and the same omission.
- `web/`: `tsc --noEmit` clean, `check:responsive` 20/20, production build clean. `console/`: `tsc` clean, build
  clean.
- Eye pass: 320 / 390 px on both new pages and the login line, both capability branches (probe intercepted to
  reach the gated one), document `scrollWidth == clientWidth == 320` on both. `/patients` still 307s to `/login`,
  which is the control proving the middleware fix did not widen the gate.
