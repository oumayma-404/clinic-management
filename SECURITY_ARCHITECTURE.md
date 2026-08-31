# Security architecture — engineering reference

**Internal.** For engineers working on this codebase. Not for clients — the client-facing document
is `SECURITE-DOSSIER-PATIENT.md`.

**What this is.** How the security guarantees are *implemented* and, more importantly, **what holds
them** — because in this codebase almost every guarantee is enforced by a derived test, a startup
refusal or a schema check rather than by discipline. § 8 and § 9 are the sections to read before you
touch anything here; they are the ones that will save you.

**What this is not.** A posture snapshot with findings and severities — that is
`SECURITY_POSTURE_2026-08-16.md`. A vulnerability review — that is `SECURITY_REVIEW_2026-08.md`
(2026-08-07, predates the hardening; see its own staleness note). What is still **owed**, and by whom,
is `follow-up/security-remediation-outstanding.md`.

**Last updated 2026-08-31**, after a whole-codebase security and Tunisian data-protection audit and the
remediation batch that followed it. What that batch changed, and where to read it:

| | Section |
|---|---|
| Signing out revokes the session server-side; PBKDF2 at 210 000; one-time codes cannot be replayed | § 5.1 |
| A device grant no longer buys the whole API — scope-narrowed tokens, fail-closed | § 5.4 |
| A patient can refuse to be contacted, and the refusal is obeyed | § 5.5 |
| A hostname is resolved and re-checked at the socket, SMTP included | § 5A.5 |
| The clinic is inside the audit hash; the newest rows can no longer be deleted unseen; reads are recorded | § 7.1–7.3 |
| No catch-all hands the client an exception message | § 9.10 |
| An enum on the wire — found twice, one of them pre-existing and breaking the CSV import | § 9.11 |

⚠️ **The largest security item in this document is not in the code and never was**: the production
data sits in **London**, not Tunisia (§ 10, first row).

---

## 1. Threat model

### 1.1 Defended against

| # | Threat | Primary control |
|---|---|---|
| T1 | A clinic reads another clinic's records | EF global query filters, fail-closed on unset scope (§ 4) |
| T2 | A stolen password yields a practice's records | Mandatory admin TOTP + account-keyed rate limit (§ 5) |
| T3 | A stolen or replayed session outlives its revocation | `TokenVersion` + session families + per-request DB state check |
| T4 | A packet capture on the container network | TLS on every internal hop, startup-refused if absent (§ 6) |
| T5 | A stolen disk, snapshot or decommissioned volume | LUKS + certificate-encrypted key ring + individually encrypted secrets |
| T6 | A stolen off-site backup archive | `age` (dumps) / libsodium (WAL) encryption at source, keys custodied apart |
| T7 | Silent tampering with the activity record | HMAC-SHA256 chain keyed by a secret outside the database (§ 7) |
| T8 | Undetected exfiltration of a whole practice | Step-up auth + non-best-effort export ledger + 3-per-10-min limit |
| T9 | The vendor console reading clinical data | Closed field-name allow-list, build-failing reflection test |
| T10 | Silent egress to an undeclared third party | `DataResidencyAssurance` startup refusal |
| T11 | A tenant-supplied endpoint turned into a server-side request primitive | `OutboundEndpoint` at save time **plus** `PublicEgressGuard`'s connect callback (§ 5A.5) |
| T12 | A device credential exchanged for the whole API surface | Scope-narrowed tokens, refused by every endpoint that has not named the scope (§ 5.4) |
| T13 | The newest audit entries deleted to hide an action | Out-of-band sealed tips, `seal-audit-chain` (§ 7) |
| T14 | A patient contacted after refusing | Tri-state consent asked once, at the single enqueue gate (§ 5.5) |
| T15 | Server internals reaching a browser through an error body | `Result.Diagnostic` — logged, never serialised (§ 9.10) |

### 1.2 Explicitly **not** defended against — say so rather than implying otherwise

- **Root on the running host.** LUKS protects a disk at rest; a process that already has root reads
  the certificate it was given, by design. `deploy/README.md` states this in those words.
- **A malicious clinic administrator inside their own tenant.** They can read and export their own
  practice — that is the product. The audit chain makes it *attributable*, not impossible.
- **XSS via injected script.** The CSP carries `'unsafe-inline'` on `script-src`; see § 9.5.
- ~~**DNS rebinding on tenant-supplied integration URLs.**~~ **Closed 2026-08-31** — see § 5A.5. The
  `SocketsHttpHandler.ConnectCallback` this line called for now exists, and SMTP resolves before it
  dials. Kept struck through rather than deleted: the shape is worth recognising again.
- **Supply-chain compromise.** Dependency scanning exists now (`ci.yml` → `dependencies`), but the
  Windows client feed is still **unsigned** (`vpk pack` carries no `--signParams`) and the backup
  store has no object-lock, so a host with the S3 keys can delete the archives. Both are § 10 debt.
- **A compromised vendor console account with a valid second factor.** It can read every cabinet's
  *metadata* (never clinical data, § 9 T9) and grant entitlements. Every action is journalled.

---

## 2. Trust boundaries and deployment profiles

`Deployment:Profile` resolves to a `DeploymentKind` **plus 18 named capabilities**. Every mode branch
in the solution asks a capability — never `IsLocalMode`, never `Auth:Mode`. An unrecognised value
**fails startup loud** rather than falling back to Auth0 on a typo.

| Kind | Boundary shape | Identity |
|---|---|---|
| `SelfHostedLan` | Clinic's own Windows PC serving its LAN. Kestrel is the single browser-facing endpoint; YARP proxies non-`/api` to a loopback Next server | Local accounts |
| `HostedMultiTenant` | One backend, many practices, reached over the internet. Caddy is the edge | Local accounts |
| `CloudBrowser` | One hosted backend, browser-reached | Auth0 |

**The rule that matters:** a capability is decided by the **kind**, never by a setting an operator can
change. `RequiresAdminSecondFactor` is true on `HostedMultiTenant` and there is no key to turn it off
— because "we shipped MFA and an operator disabled it" is not a state we want to be reachable.

⚠️ `HostedMultiTenant` runs with `AUTH_MODE=local`. Anything that asks "is this Local?" meaning "is
this a clinic's own PC?" is already wrong there. That is the whole reason the profile abstraction
exists.

**Two extra listeners on the hosted kind.** The vendor console binds a **second Kestrel endpoint** on
an unpublished port (`127.0.0.1:9443`, reached over SSH forwarding). Both ports are bound in **one
`ConfigureKestrel` call** — an explicit endpoint overrides `ASPNETCORE_URLS` wholesale, so a bare
`ListenAnyIP(consolePort)` would unbind 5000 and take the entire product offline while the console
worked perfectly. `ConsolePortGate` then refuses **both directions**: console paths on the public
port, and anything but console paths on the console port.

---

## 3. The request pipeline

Order is load-bearing. This is the actual chain from `Program.cs`:

```
UseSwagger / UseSwaggerUI                    (Development only)
UseOriginalPeerCapture                       ← captures the REAL TCP peer before headers are trusted
UseForwardedHeaders                          ← bounded to Security:TrustedProxies
UseCors("AllowAll")
SecurityHeadersMiddleware                    ← § 9.5
[trust-port gate]                            (SelfHostedLan only)
[console-port gate]                          ConsolePortGate — unconditional
UseAuthAttemptAccountCapture                 ← lifts the submitted email onto HttpContext.Items
UseRateLimiter                               ← partitions on that email; see § 5.3
ExceptionMiddleware
ClientVersionMiddleware                      ← 426 before auth, so a stale shell's LOGIN 426s not 401s
UseAuthentication                            ← DEFAULT (clinic) scheme only
AccountStateMiddleware                       ← DB role + IsActive → HttpContext.Items
PlatformAccountStateMiddleware               ← console paths; authenticates the console scheme ITSELF
UseAuthorization                             ← where the console scheme is normally authenticated
PlatformTenantScopeMiddleware                ← UseSystemWide("platform console")
TenantScopeMiddleware                        ← UseClinic(account.ClinicId) — DB-resolved
LocalAuthEnforcementMiddleware               ← TokenVersion + must_change_password
SubscriptionGateMiddleware                   ← 402, last before routing
MapControllers → /health → MapHub → /hangfire → MapReverseProxy
```

### Why four of these positions are non-negotiable

| Position | Break it and… |
|---|---|
| `UseOriginalPeerCapture` **before** `UseForwardedHeaders` | The two loopback-only gates (`setup`, `/hangfire`) become forgeable by `X-Forwarded-For: 127.0.0.1`. They are decided by the real TCP peer, never a header. |
| `ClientVersionMiddleware` **before** `UseAuthentication` | A stale shell's login returns 401, which the client reads as "signed out" — a login screen it can never get past. |
| `AccountStateMiddleware` **before** `UseAuthorization` | The DB-resolved role is published too late and the handler silently reverts to the JWT claim. This is exactly how the Auth0 demotion bug happened. |
| `SubscriptionGateMiddleware` **after** `LocalAuthEnforcementMiddleware` | 402 masks the 401 of a revoked token and the 403 `must_change_password` — a deactivated colleague is told the subscription lapsed, and a user owing a password change is routed to the billing screen instead of the one that unblocks them. |

Each of the last three is asserted **against `Program.cs`'s own source text** by a test
(`AccountStateEnforcementTests`, `SubscriptionGateMiddlewareTests`,
`PlatformAccountStateTests`), because the middleware is correct in isolation and only its *position*
is wrong — nothing else in the build can see that.

---

## 4. Tenant isolation

### 4.1 Mechanism

**36 `HasQueryFilter` registrations** in `ApplicationDbContext`, all the same shape:

```csharp
modelBuilder.Entity<Patient>().HasQueryFilter(p => IsSystemWide || p.ClinicId == ScopedClinicId);
```

Fed by a three-valued `ITenantScope`:

| State | `ScopedClinicId` | Result |
|---|---|---|
| `Unset` | `Guid.Empty` | **zero rows** — fail-closed |
| `Clinic(id)` | `id` | that clinic's rows |
| `SystemWide(reason)` | — | `IsSystemWide` short-circuits; every row |

⚠️ **The filters were fail-*open* and therefore inert for a whole release** before `multi-tenant-cloud`
US-2. A path that establishes no scope now reads nothing instead of everything.

### 4.2 Where the clinic id comes from

`TenantScopeMiddleware` → `RequestAccount.ResolveAsync` → **DB lookup of the token `sub`**. Never the
JWT claim. The Cloud claim is written by an Auth0 Action outside this repo; a stale token was harmless
under fail-open but would now mean zero rows with no error.

Handlers *additionally* re-verify each loaded aggregate's `ClinicId`. Two layers, deliberately.

### 4.3 The seven clinical children

`MedicalDocument`, `DentalRecord`, `PatientMedicalHistory`, `PatientFamilyHistory`, `PatientFile`,
`PatientFolder`, `ToothState` carry a **denormalised `ClinicId`**, not a filter through the `Patient`
navigation — that would put a correlated subquery on the hottest reads in the product.

The constructors take `clinicId` as a **required positional parameter right after `patientId`**, so a
new write path that forgets it is a **compile error**, not a silent leak. Every caller passes the
*patient's* own `ClinicId` — already tenant-checked one line above — never the caller's.

Denormalisation means the two can disagree and nothing in the model can forbid it, hence
`verify-schema`'s `clinical-child-clinic-matches-patient`, which catches both directions: a backfill
that covered nothing (rows at `Guid.Empty` — symptom is a patient record that reads as *empty*, not an
error) and a write path naming the wrong clinic (visible, to the wrong practice).

### 4.4 Blob tenancy

`IFileStorage.UploadAsync` **requires a `Guid clinicId` in its signature**, both overloads. The second
overload's path is *relative to the clinic*. An unprefixed key is not something a caller can write.

⚠️ The clinic is a **parameter**, deliberately not read off `ITenantScope` — the tempting version works
for every HTTP path and fails silently for the one that matters: an outbox job uploads under
`UseSystemWide`, where there is no clinic in scope at all.

Reading is asymmetrical **on purpose**: `DownloadAsync`/`DeleteAsync` take the stored key verbatim, so
pre-US-5 flat keys still resolve. There is no backfill.

---

## 5. Authentication and authorization

### 5.1 Credentials

| | |
|---|---|
| Password hash | PBKDF2-HMAC-SHA512 via `PasswordHasher<User>` (v3 format) at **210 000 iterations**, rehash-on-login |
| Password floor | 12 chars, enforced on **set**, served by the server so no client can drift |
| TOTP secret | Encrypted at rest; own Data Protection purpose (§ 6.2) |
| Recovery codes | 8, hex SHA-256, single-use, **spent even when the accompanying sign-in fails** |
| Refresh | Rotating; family accepts current + immediate predecessor (two tabs racing must both work) |
| Revocation | `User.TokenVersion`, checked per request — **and `POST /api/auth/logout` now bumps it**, so signing out ends the session server-side rather than only clearing a cookie |
| One-time codes | `ITotpReplayGuard` on **every** `VerifyCode` call site (RFC 6238 § 5.2); enrolment is the sole exemption and is named |
| JWT validation | issuer / audience / lifetime / key, `ClockSkew = TimeSpan.Zero`, distinct audiences for access vs refresh |

⚠️ **Signing out used to clear cookies only.** A captured refresh credential stayed valid for its
full 12 hours after the user pressed « Se déconnecter ». `EndSessionCommand` is anonymous by
necessity — the credential in the body *is* the authentication, and demanding a live access token
would refuse exactly the case that most needs revoking, a browser signing out after its 30-minute
token expired. It answers **204 for a live, expired, unknown or already-ended credential alike**, so
it is no oracle.

⚠️ **Recovery codes are plain SHA-256, not PBKDF2, and that is correct** — they are 128-bit random
values, so iterated hashing buys nothing against a search space that large and costs latency on a
login path. The reasoning is in the entity's own docstring; don't "fix" it.

### 5.2 Policies

Four, and **all 32 route controllers carry a class-level named policy**; no bare `[Authorize]` remains.

`Authenticated` (onboarding only) · `AnyClinicRole` · `AdminOrDoctor` · `AdminOnly`

The charter is **record yes, erase no**: the clinical record is `AnyClinicRole` to read and write,
`AdminOrDoctor` to delete from — four delete actions are the *only* gate, so removing one is a silent
widening. `ClinicalRecordAccessTests` states the charter as data and **fails on an unclassified new
action**.

⚠️ `AnyClinicRole` includes `admin` deliberately. `CreateClinicCommand` makes a clinic's creator an
admin and links the single dentist's `Doctor` record to that account, so in the common Tunisian
practice the owner-dentist's role *is* `admin` — a literal `{doctor, secretary}` policy would lock the
owner out of their own practice.

⚠️ The console policy **pins its scheme**; the four clinic policies deliberately do not. That asymmetry
is what makes a token on the wrong surface **401, not 403**.

### 5.3 Rate limiting

| Policy | Limit | Window | Partition |
|---|---|---|---|
| Auth | 30 | 300 s | **submitted account** |
| Auth address ceiling | 150 | 300 s | client address |
| General API | 600 | 60 s | client address |
| Archive export | **3** | **600 s** | user |
| CSP report | 60 | 60 s | address |

⚠️ It could **not** be a compound `account+address` key — that hands one attacker a fresh budget per
address. The named policy partitions on the account; the *global* limiter partitions the same request
on its address. Both apply.

⚠️ `UseAuthAttemptAccountCapture` buffers and rewinds the body, and **anything unreadable falls back to
the address** — non-JSON, truncated, >8 KB, `auth/refresh`. Nothing about it can refuse a request:
turning a JSON slip into a 500 at the limiter makes the login page unreachable.

### 5.4 Scope-narrowed tokens

`POST /api/backup/archive-grants/token` is **anonymous by necessity** — an unattended workstation
presenting its device secret has no session and nobody at the keyboard — and it used to return an
ordinary 30-minute **clinic-admin access token with the entire API surface**. A PC authorised only to
fetch a nightly archive could read every patient record, issue invoices and manage accounts.

It now mints a token carrying `clinic_scope`, and `ScopedTokenFilter` (a **global** MVC authorization
filter) refuses it on every action that has not named that scope.

⚠️ **The direction of the test is the whole design.** The question is « does this endpoint *name* the
scope? », never « does it forbid it? ». A controller action written next month is therefore out of
reach of a restricted token on the day it is written, with no decision required from its author. The
inverse — endpoints declaring what they refuse — is one forgotten attribute away from the hole it
replaces, and the forgotten endpoint is exactly the one an over-broad token finds.

⚠️ **Adding the claim removes surface and never grants any.** Every ordinary gate still applies to a
scoped token: the role policy, the subscription gate, the step-up confirmation, the grant re-check.

⚠️ **Four actions name it, not one** — `Backup.DownloadArchive`, `Backup.GetFileManifest`,
`Backup.ReportVaultCopy` and `PatientFiles.DownloadFile`. They were read off the shell's own request
URLs in `desktop/ClinicManagement.DesktopShell` rather than guessed; naming only the archive download
would have silently stopped the file mirror and the coffre report. The per-file download widens
nothing — the same token can pull the archive, which contains those files.

Held by `ScopedTokenCoverageTests`: the declared set must equal the reviewed set **in both
directions**, plus a probe controller proving an action that names nothing is refused.

### 5.5 Consent to be contacted

Recording a phone number used to enrol a patient into SMS/WhatsApp reminders with no way for the
patient **or the cabinet** to exempt anybody. `Patient.ReminderConsent` is tri-state —
`NotRecorded` / `Granted` / `Refused` — with the date and the person who took the answer, because a
consent nobody can date is not one a cabinet can defend to the INPDP.

⚠️ **`NotRecorded` still receives reminders, and that is a dated decision rather than an oversight.**
Every patient already on file predates the column; treating them as refusals would have muted every
reminder in every cabinet on the day it shipped, discovered through empty waiting rooms rather than
through a message. The number was given *for* being contacted about care. What the column adds is the
ability to say **no** and have it obeyed, which did not exist.

⚠️ **One gate, and consent is asked first.** `ReminderScheduler.ReachabilityOfAsync` answers consent
and deliverability together for both enqueue paths. The order matters: reporting « no valid phone » to
a patient who refused sends reception off to correct a number — and correcting the number is precisely
the action that must **not** put the message back in the queue.

`ReminderConsentCoverageTests` is the derived guard, in this repository's usual shape: `Refused` is
compared in exactly one production file, and the scheduler reads a patient in exactly one place. A
third enqueue path written against the repository would compile, pass every other test, and message
somebody who said no.

---

## 5A. Transit

HTTPS is a single encrypted channel, so request and response are covered by the same session. There
is no asymmetry to reason about — what differs is only *who issues the certificate* and *who has to
trust it*.

### 5A.1 Client ↔ server

| Topology | Edge | Certificate |
|---|---|---|
| `HostedMultiTenant` / `CloudBrowser` | Caddy | Let's Encrypt via ACME (`DOMAIN` + `ACME_EMAIL`), auto-renewed. HSTS `max-age=31536000; includeSubDomains` |
| `SelfHostedLan` | Kestrel, in-process | Self-minted CA + SAN server cert into `.local/` on first boot (`CertificateProvisioner`, idempotent) |
| Vendor console | Caddy, `tls internal` | Caddy's own local CA — `127.0.0.1` has no public name for ACME to issue against |

On `SelfHostedLan`, **HTTPS on 5001 is the only LAN-facing port**; plain HTTP binds loopback-only and
exists solely for the Next BFF hop on the same machine. The installer opens 5001 alone on the firewall.

⚠️ **A set-but-missing `Https:CertPath` fails startup** rather than dropping to HTTP. That downgrade was
a real Phase-4 gap; do not reintroduce a fallback here.

### 5A.2 Distributing the LAN CA — `/api/trust`

A self-signed certificate is only useful if the devices can trust it, and a phone cannot install a CA
it has to log in to fetch. `TrustController` (`api/trust`, `[AllowAnonymous]`, `ExposesTrustEndpoints`
only) serves the CA three ways from one page: the raw `ca.crt` for Android, an Apple
`.mobileconfig` profile for iOS/iPadOS, and a QR code so a phone reaches the page without typing an IP.

⚠️ The page's links are **absolute** (`/api/trust/ca.crt`), because a relative `href="ca.crt"` on a page
served at `/api/trust` resolves against `/api/` and 404s.

This is one of the four anonymous LAN routes on `ControllerAuthorizationCoverageTests`' reviewed list —
adding or removing one fails the build until it is reviewed.

### 5A.3 Server-internal hops (hosted kinds only)

Every hop behind the edge is TLS against a deployment-private CA with a **ten-year** lifetime — nobody
outside these containers evaluates it, so a short lifetime buys almost nothing and adds a failure mode
where an expiry plus a fail-loud startup turns any restart into a crash loop. `verify-schema` reports
days remaining.

- API ↔ PostgreSQL: `SSL Mode=VerifyFull;Root Certificate=/certs/ca.crt`
- API ↔ MinIO: `MinIO__UseSSL` + `MinIO__RootCertificate`
- `backup` and `pitr` sidecars: `PGSSLMODE=verify-full` + `PGSSLROOTCERT` — **they connect with their own
  credentials and must be brought across in the same change**, or the nightly dump fails silently at 02:00
- PostgreSQL itself runs `ssl=on` with a baked `pg_hba.conf` offering **`hostssl` only**, so the server
  refuses cleartext and the application's setting is not merely a courtesy

`TransportAssurance` refuses startup if any of this is unsatisfied, gated on the deployment **kind** and
reporting **every** problem rather than the first. See § 10 for the one active reduction.

### 5A.4 Certificate validation in the clients — and why there is no pinning

**No client bypasses validation.** Android `onReceivedSslError` calls `handler.cancel()` (and reports —
see § 9.9); iOS uses `.performDefaultHandling`; there is no
`ServerCertificateCustomValidationCallback` in the desktop shell and no `NSAllowsArbitraryLoads` on iOS.
`proceed()` appears nowhere in the project.

**Android additionally trusts user-installed CAs** (`network_security_config.xml`). That is what makes
the `SelfHostedLan` topology reachable at all, since its certificate is self-signed per install.
Cleartext stays refused.

⚠️ **There is no certificate pinning anywhere, and that is a decision — not an omission.** Recording it
here because an undocumented absence reads as an oversight to the next reviewer:

1. **The hosted certificate is Let's Encrypt, rotating every ~90 days.** A pin shipped through three app
   stores cannot be rotated on that cadence; the first renewal after a slow review cycle takes every
   mobile client offline simultaneously, and the fix requires a store release. Pinning would convert a
   routine renewal into an outage generator.
2. **The LAN certificate is minted per install**, so there is no stable key to pin — each clinic's server
   generates its own CA on first boot. A pin would have to be per-deployment, i.e. configuration the
   shell learns at runtime, which is exactly the trust decision the OS trust store already makes.
3. **What pinning defends against is already covered differently.** The threat is a CA mis-issuance for
   our domain; the LAN topology's answer is that the CA *is* the clinic's own, and the hosted topology's
   answer is HSTS plus the fact that a mis-issued certificate still cannot present a valid session.

Revisit only if the hosted deployment moves to a long-lived certificate under our own control **and** a
rotation path that does not require a store release exists. Do not add a pin to one client alone — three
clients disagreeing about which certificates are acceptable is worse than none pinning.

### 5A.5 Outbound egress — the half that used to be owed

A clinic admin types the SMS gateway URL, the WhatsApp Graph base and the SMTP host into a settings
screen, and a background job inside the API container then dials them. `OutboundEndpoint` refuses the
obvious forms at save time — `localhost`, a single-label container name, an IP literal in a private
range, and anything not `https` where the deployment forbids it.

⚠️ **`IPAddress.TryParse` returns false for every hostname, so hostnames passed unconditionally.** On
`HostedMultiTenant` public signup makes anyone an admin of their own clinic, and that admin could
point `smtpHost` at `127.0.0.1.nip.io` — or at a name they control answering `10.0.0.5` — and have the
container dial its own compose network on their behalf. The HTTP channels were covered only by the
accident that `https` is forced; **SMTP had nothing at all**, since it dials a bare host on a bare
port.

`PublicEgressGuard` closes it where the address is finally known:

- a `SocketsHttpHandler.ConnectCallback` on the **default** handler of the shared `IHttpClientFactory`
  — attached there rather than per named client, because every one of those integrations dials an
  admin-editable URL and a per-client opt-in is one forgotten registration away from the hole
- an explicit resolve-then-check in `SmtpDocumentEmailSender`, which has no connect callback to hang
  it on

⚠️ **Save-time validation structurally cannot close this, however careful it is.** DNS is mutable: a
name resolving publicly when the admin presses « Enregistrer » can resolve to `169.254.169.254` an
hour later when the job dials it. The check has to happen at the socket.

⚠️ **Every resolved address is checked, not just the first** — a name can answer with one public and
one private record, and connecting to whichever the resolver ordered first is nobody's decision. The
socket then connects **by address**, so no second lookup can disagree with the one that was checked.

⚠️ **The refusal names the host and adds nothing else.** Reporting the resolved address back to the
tenant who chose the name would make every refusal a DNS-driven scanner of the operator's private
network. Asserted as an exact string, so a later author cannot append a helpful « (10.0.3.7) ».

⚠️ On `SelfHostedLan` the guard is **absent rather than lenient** — a relay on the practice's own LAN
is the normal arrangement there and there is no tenant boundary to defend.
`SmtpTransactionalEmailSender` is deliberately unguarded too: it reads the **operator's** own
configuration, where an internal relay is intended.

---

## 6. Cryptography inventory

### 6.1 Key hierarchy

```
Key-ring protecting certificate (PKCS#12)        ← operator-supplied, HostedMultiTenant: REQUIRED
   └── Data Protection key ring                  ← DB rows (Render) or a durable volume
         ├── ClinicManagement.User.TotpSecret.v1
         ├── ClinicManagement.PlatformConsole.TotpSecret.v1
         ├── ClinicManagement.Reminders.<...>       (SMS / WhatsApp / SMTP credentials)
         ├── ClinicManagement.Clinic.GoogleRefreshToken.v1
         └── ClinicManagement.DbCredentials.<...>   (SelfHostedLan only)

Audit chain key (Audit:ChainKey, ≥32 bytes)      ← DELIBERATELY NOT the ring — see § 7
Backup key (age keypair)                          ← private half off-host
PITR key (WALG_LIBSODIUM_KEY)
LUKS volume keyfile                               ← host boot volume, 0400 root:root
```

`SelfHostedLan` uses machine-scoped **DPAPI** instead of a certificate. Development tolerates an
unprotected ring (`TolerateUnprotectedKeyRing`) — every deployed environment refuses.

### 6.2 Purpose separation is enforced by the framework, not by convention

Each protector has its **own purpose string**, which feeds the key derivation. A reminder credential
is not decryptable by the code that reads a second factor. When you add a protector, give it a new
purpose — changing an existing one invalidates all its ciphertext.

### 6.3 The rule that must never be relaxed

**A failed unprotect returns false and the operation refuses.** It never throws, never returns the
input, never falls back to a legacy plaintext column, and never degrades. For a second factor
specifically, *"could not decrypt" must never become "sign in without one."* Each protector's
docstring names its recovery verb; keep that pattern.

### 6.4 What is **not** encrypted

**Patient/PHI columns.** Rejected in writing with reasons: it breaks accent-insensitive DB free-text
search (without which a patient on page seven reads as "aucun résultat"), duplicate detection, and
name-ordered paging. Revisit only under a compliance requirement, as its own feature. Do not
half-introduce it on one column.

**Hashes, which are not credentials.** `User.PasswordHash`, `PlatformAccount.PasswordHash`,
`ClinicSignup.PasswordHash` and `ClinicArchiveGrant.SecretHash` are deliberately plaintext columns:
they are irreversible by construction, and encrypting them would put sign-in — or, for the last one,
the unattended archive copy — **behind the key ring**, so a lost ring would lock every account out of
a deployment instead of costing only the second factor. That inversion is avoided everywhere else in
this product. They are also compared on every use, and ciphertext is not equality-searchable.
`SecretProtectionCoverageTests` requires every credential-shaped column to be either ciphertext or a
**named decision**, in both directions.

---

## 7. Audit chain

`AuditSaveChangesInterceptor` writes **one row per mutated aggregate root** (actor, clinic, entity,
action, compact changed-field summary). Not `CreatedBy`/`ModifiedBy` on `Entity<TId>` — those are a
write-path obligation on 38 entities, any writer that forgets produces an unattributed row
indistinguishable from a legitimate one, and they answer nothing about a delete.

`AuditChain.Hash` = **HMAC-SHA256** over a canonical field encoding of the entry plus its
predecessor's hash, verified with `CryptographicOperations.FixedTimeEquals`. Minimum key 32 bytes.
Chains are **per clinic**, serialised by `pg_advisory_xact_lock`.

### 7.1 Scheme v2 — the clinic is inside the hash

`ClinicId` and `UserEmail` were **not in the hashed set** while every read of the journal filters on
`ClinicId`. So `UPDATE "AuditEntries" SET "ClinicId" = NULL` removed a row from a cabinet's journal
permanently **and the chain still verified as intact**. Rows written from now on carry a `v2:` prefix
and cover both fields.

⚠️ **The scheme is read off the stored value, never guessed.** « Try v2, else v1 » would let a nulled
`ClinicId` fall through to the v1 form and verify clean — reintroducing the defect inside its own fix.

⚠️ `AuditChainEntry`'s two new fields have **no defaults**, deliberately: a default hid the
`SchemaVerificationReader` bug where the SQL projection had not been widened, which would have
reported *every* chained row as altered — on the one check whose whole value is being believed when it
says so.

Historical rows stay on v1 and are migrated by `rehash-audit-chain --apply` (§ 11), which **refuses
any chain that does not already verify**: rehashing a tampered row would launder it into a valid v2
hash and destroy the only evidence.

### 7.2 The sealed tip — the break the arithmetic cannot see

Every check above compares an entry against its **neighbour**. Deleting the newest *k* rows removes no
neighbour: the shortened chain is internally perfect, and the next append re-links from whatever tip
it finds. « Supprime la dernière heure » was the cheapest possible attack on this ledger and it left
nothing behind.

Nothing inside the database can close that — whoever can delete the rows can delete the record of
them. `seal-audit-chain --apply` records each chain's tip **outside** the database, beside the chain
key and for the chain key's own reason; `verify-schema` then compares and reports `TipTruncated`.

⚠️ **Sealing is an operator action, never automatic.** A seal the application rewrote on every append
would be rewritten by the process an attacker already controls, one moment after the truncation, and
would report clean for ever. A seal means « a person confirmed this chain was intact at this length »,
which is only true if a person did it.

⚠️ **A chain that does not verify is not sealed**, and the walk runs *with* the existing seal — so a
chain already truncated since the last sealing is refused rather than quietly re-sealed at its new,
shorter length.

⚠️ **The seal records the tip's hash, not merely its sequence.** Rebuilding a forged chain to the same
length is the obvious way round a length-only check, and every internal test passes on the rebuilt
chain.

⚠️ **An unreadable seal file throws** rather than reading as « nothing sealed » — the fallback would
silently restore the exact blindness this closes. **Back the file up**; losing it corrupts nothing and
re-opens the blind spot.

⚠️ Growing past the seal is normal. Without that case, `verify-schema` would report every healthy
deployment as tampered the day after it was sealed, which is how a check gets switched off.

### 7.3 Reads are recorded, not just writes

« Qui a sorti la radiographie de ce patient ? » had no answer: the ledger recorded mutations only.
`AuditAction.Read` and `PatientRecordAccessLedger` now record a file or document being pulled, keyed
on the **patient's** id rather than the file's, so the journal is answerable per patient.

⚠️ **Thumbnails do not audit, and the reason is in the exemption.** The tile grid used to paint by
fetching the original through the audited download, so scrolling one file list wrote a row per visible
tile — burying the real accesses in noise and pulling full-size radiographs to draw 40 px squares.
Tiles now fetch the downscaled stand-in only, whose endpoint is a **named exemption** in
`PatientFileAccessCoverageTests` that holds *only* while preview serves a stand-in rather than the
original.

⚠️ Consequence to expect: **audit write volume rises materially** on a busy clinic.

### 7.4 Properties to preserve

Five now, and the last two arrived on 2026-08-31.

1. **Audit writes stay best-effort.** A failed audit write must never roll back the clinical or
   financial operation it describes. When one fails, a **declared gap** is recorded — so a later walk
   distinguishes "a gap we know about" from "a break nobody declared".
2. **A restore legitimately breaks a chain**, and records a declared boundary rather than leaving
   something that reads as tampering.
3. **The chain key is NOT the Data Protection ring.** FR-3.9 makes the ring the thing a restore may
   fail to read — if the chain were keyed on it, the ledger would become unverifiable at exactly the
   moment somebody wanted to check it.
4. **A hash's scheme is read, never guessed** (§ 7.1) — « try the new form, else the old » is how a
   nulled field falls through to a form that does not cover it and verifies clean.
5. **The sealed tip lives outside the database** (§ 7.2), and a chain that does not verify is never
   re-sealed.

Read surface is `GET /api/audit` (`AdminOnly`, paged). **There is no write endpoint** — a ledger with
one is a ledger somebody can correct.

---

## 8. How each guarantee is held

This is the codebase's actual security design. Four mechanisms, in descending order of how much you
should trust them:

| Mechanism | Fails when | Examples |
|---|---|---|
| **Compile error** | The code cannot be written | `clinicId` positional param on clinical children; `IFileStorage.UploadAsync` requiring a clinic id |
| **Derived reflection test** | A *new* thing forgets | `TenantScopeFilterTests` (every clinic-owned table is filtered or a named decision) · `SystemWideCallerCoverageTests` (every no-HTTP-context reader declares a scope) · `PlatformReadShapeTests` (closed field-name set, **both directions**) · `ControllerAuthorizationCoverageTests` · `SubscriptionExemptionCoverageTests` · `ClinicalRecordAccessTests` · **`ScopedTokenCoverageTests`** (which actions accept a restricted token) · **`ReminderConsentWireShapeTests`** (no DTO serialises an enum as a number) |
| **Derived source scan** | A *new* call site forgets | **`TotpReplayCoverageTests`** (every `VerifyCode` spends the code) · **`ReminderConsentCoverageTests`** (one definition of a refusal; one patient read in the scheduler) · **`ExceptionLeakCoverageTests`** (no catch-all hands the client `ex.Message`) · `LogTemplateCoverageTests` (no patient identifier in a log template) · `ClinicalRecordAuditCoverageTests` |
| **Startup refusal** | Configuration is wrong, at boot, loudly | `TransportAssurance` · `DataResidencyAssurance` · key-ring certificate absent · empty connection string · unrecognised `Deployment:Profile` |
| **`verify-schema`** | The database disagrees with the model | `clinical-child-clinic-matches-patient` · `cheque-details-only-on-cheques` · every backfill row count · key-ring protection checks |

⚠️ **A source scan must be brace-matched, not line-based, and must know its own exceptions.** Both
lessons were paid for during the 2026-08-31 batch. `ExceptionLeakCoverageTests`' first pass scanned by
line, rewrote **71 of 78** sites and reported success — the failing form is routinely wrapped across
three lines. Its second pass then blanked three `catch (Exception ex) when
(SubscriptionRefusals.IsDomainRefusal(ex))` blocks, turning « la durée doit être positive » into « Une
erreur est survenue »; only an unrelated handler test caught it. A generic catch narrowed by a
**domain** predicate is not a catch-all, while an `is not X` exclusion still is.

⚠️ **Ask the question the wire asks, not the one the type asks.** `ReminderConsentWireShapeTests`
tests « does this property reach the client as a number? », not « is this property an enum » — a
per-property `[JsonConverter(typeof(JsonStringEnumConverter))]` makes an enum serialise as its name,
and the cruder question would flag code that is already correct.

**The lesson this codebase learned the hard way, twice:** a *listed* expectation rots. The realtime
key table stayed green for a whole period while five keys were broadcast with nothing listening;
`verify-schema` reads the EF model rather than a hand-maintained list for the same reason. **Prefer
derived over listed, always.**

When you add a control, ask which of the four rows above holds it. If the answer is "a reviewer will
notice," it is not held.

---

## 9. Silent failure modes

Every one of these is real and happened here. None turned a test red on its own.

### 9.1 A SignalR hub method reading a filtered entity

HTTP middleware does not run per hub invocation, so `TenantScopeMiddleware` never fires and the
invocation lands in `Unset` — **zero rows, no error**. `ClinicHub` is safe *only* because it reads
`User`, which is unfiltered. A hub method touching a filtered entity must set the scope itself.
`ClinicHubTenantScopeTests` fails if a filtered repository is injected, because the broken version
*succeeds* with an empty result.

### 9.2 A new background job that forgets `UseSystemWide`

Reads nothing and **logs a clean run**. Reminders and document emails simply stop.
`SystemWideCallerCoverageTests` derives its candidate set by reflection so a new job fails on the day
it is written. ⚠️ Its console-verb branch matched **zero types** for a long time — the filter was
`IsAbstract: false` and every verb is a `static class`, which is abstract *and* sealed in metadata.
Check your reflection predicates against a known-present type.

### 9.3 A middleware whose subject comes from a pinned auth scheme

`PlatformAccountStateMiddleware` was **inert in production for the entire life of the console feature**
— six parts. It read `context.User`, which for a console token is never populated there:
`UseAuthentication` authenticates only the *default* scheme, a console token fails it by design, and
the console scheme is authenticated inside `AuthorizationMiddleware` because the policy pins it —
*after* this middleware. Every check passed through. Proven over the wire: sign in, run
`platform-account --deactivate`, call the API again → **HTTP 200, whole portfolio**.

⚠️ **Its tests passed throughout because they set `context.User` by hand** — the one thing production
does not do. If a middleware's subject is established by a pinned scheme, unit-testing it through
`DefaultHttpContext.User` asserts the very arrangement that is broken. Install a stub
`IAuthenticationService` instead.

### 9.4 A controller parameter that never got added

All four subscription-state filters were dead from Part 2 until a user reported it: the controller
bound five query parameters and **not `state`**, so model binding had nowhere to put it and the list
narrowed nothing. Every layer behind the hop was correct, and the handler test asserted the handler
forwards every filter *it is given* — which it did. A dropped filter fails **silently**: the list
still answers, with more rows than were asked for.

The fix is a **derived** controller test: every settable property of the query must have a parameter
to arrive on.

### 9.5 CSP that looks enforcing and is not

- Two `Content-Security-Policy` headers make the browser enforce the **intersection**, not either
  policy. `SecurityHeadersMiddleware` therefore refuses to overwrite one an upstream component set.
- The policy is stated in **three** places (this middleware, `deploy/Caddyfile`'s two sites,
  `console/next.config.ts`) and held **byte-identical** by a build-failing test. Change one, change
  all three.
- ⚠️ **Enforcing ≠ XSS protection.** `script-src 'self' 'unsafe-inline'` permits inline `<script>` and
  `javascript:` handlers. Turning `Security:EnforceCsp` on constrains resource **origins**. Getting
  real script protection means Next nonces + `strict-dynamic`, which is its own change with its own
  page walk — do not smuggle it in behind the flag.
- `Security:EnforceCsp` is read **once at construction**, not per request: a mid-session config reload
  must not change the header a page's assets are already loading under.

### 9.6 A migration that reports success having done nothing

- EF's differ emits `AddColumn<uint>("xmin")` for the concurrency token, which PostgreSQL **rejects**.
  Two migrations hit this. `AddConcurrencyToken` has a deliberately empty `Up()` and exists for its
  model snapshot only.
- EF scaffolds `DropColumn` **before** a backfill that reads that column — it cannot know. Statement
  order in a hand-edited migration is the design, not tidying.
- A backfill covering **zero rows** is invisible to every layer except `verify-schema`. Every backfill
  gets a row-count check there.

### 9.7 Configuration that is present and inert

- `DataProtection:KeyRingPath` set to a path with **no durable volume behind it** produces exactly the
  symptom of it being unset — every clinic's credentials undecryptable after the first redeploy — and
  **no code can detect it.** That half is stated beside the volume in the compose file.
- `Security:EnforceCsp` existed and was unset for a whole release. That is the failure shape AC-5 of
  the hardening spec exists to prevent, and why the Render relaxation logs a warning **every boot**.
- An HTTPS redirect registered with no port configured silently does nothing. A security control that
  is present and inert is worse than an absent one, because it reads as present.

### 9.8 An actor prefix that nothing matches

`AuditActor.AsRestore()` **prepends** `restore|`, so it matched neither the `job|` test in
`AuditLabels.Actor` nor the console's counter exclusions — a vendor restoring a dead cabinet made it
the portfolio's most active practice the next morning, and the restore rendered as the named admin's
own email. If you add an actor decoration, grep every prefix consumer.

---

### 9.9 A refusal that refuses correctly and shows nothing

The Android shell originally left `onReceivedSslError` unoverridden, reasoning that the default
implementation cancels the load so the failure would surface as « Impossible de joindre ». **It does
not.** When the SSL handler cancels, `onReceivedError` is *not* raised for the main frame — so
`mainFrameFailed` stayed false, `onPageFinished` still fired, and the shell switched to an **empty
WebView**. A white rectangle: the one outcome AC-74 forbids.

The security property was never wrong — the certificate was refused throughout. What was wrong was that
**the user could not tell a refused certificate from a broken app**, and on the `SelfHostedLan` topology
that state is *expected* on every device that has not yet imported the clinic's CA. Found on physical
hardware, not by any test.

⚠️ Two documentation files asserted "deliberately not overridden" for a long time after the override
landed. Corrected 2026-08-17. The general lesson: **"the platform default is safe" is a claim about the
platform, and platform defaults interact.** Verify what the user actually sees, on a device.

### 9.10 An error body carrying the server's internals

About **80 handlers** ended their catch-all with `Result.Failure(ex.Message)`, and that string is
rendered verbatim as `{ error }`. Npgsql SQLSTATEs and table names, S3 endpoints, server file paths
and English framework text all reached an authenticated browser — and because nothing logged the
exception, reached **nowhere else**. The detail was simultaneously exposed where it must not go and
lost where it was needed.

The fix is a `Result.Diagnostic` that nothing serialises, logged once in
`ApiControllerBase.HandleFailure`. Placed at the boundary rather than at 78 catch sites: no handler
can forget it, and none had to grow an `ILogger` — which would have broken every test that constructs
them.

⚠️ **Typed catches were deliberately left alone.** `catch (ArgumentException ex)` and
`catch (InvalidOperationException ex)` here carry French domain text the handler threw itself. Blanking
those would replace every precise refusal with « Une erreur est survenue »: a worse product, and no
more secure.

### 9.11 An enum that serialises as a number

This API registers **no global `JsonStringEnumConverter`**, so a raw enum property on a DTO leaves as
`0`/`1`/`2`. Two failures follow, both silent:

- **Reading** — the client compares an integer against `"Refused"`, never matches, and renders the
  fallback over every stored value
- **Writing** — sending the name is rejected as a **400 by the model binder**, before any handler runs,
  so there is no French message and no log line either

Found twice on 2026-08-31. The consent field shipped that way (its own DTO's docstring, three
properties above, warns about exactly this for `Dentition`); the derived guard written for it then
found `PatientImportRowDto.Outcome` with the identical defect, **pre-existing** — which had made the
whole CSV import unusable: the importable-rows filter came back empty so « Importer » had nothing to
import, no duplicate could be ticked « créer quand même », and every row badged as Invalid.

⚠️ **No gate could see either.** `tsc` is happy (the client's type says `string` and the server never
disagreed), the unit suite constructs the enum directly and never serialises, `check:responsive` looks
at layout. **Only a real HTTP request showed it** — which is the general lesson: a contract between two
languages is not checked by either compiler.

Two shapes of fix, each for its reason: a **string** property plus a `*Rules.Parse` (`DentitionRules`,
`ReminderConsentRules`) where only the client reads it, and a **per-property `[JsonConverter]`** where
the server also reads it. Held by `ReminderConsentWireShapeTests`.

---

## 10. Known reductions and operational debt

Full detail with anchors in `SECURITY_POSTURE_2026-08-16.md` § 1 and § 3. Summary:

| | Item | Status |
|---|---|---|
| Reduction | `Security:AllowUnverifiedInternalTls` — DB hop encrypted but **identity unverified**. Opt-in, non-default, warned every boot. Render has no mountable CA. | Active on Render only |
| 🔴 **Debt** | **The production data is in London.** OVH zone `os-uk2`, confirmed 2026-08-31 — not Tunisia. A transfer abroad needs prior INPDP authorisation under *loi organique 2004-63* art. 51–52, and the art. 90 exposure falls on the **cabinet**, not the vendor. Two sidecars ship a copy off-server independently of where the app runs | **Outstanding — the largest item in this table** |
| 🔴 Debt | Four credentials in git history (Google secret + refresh token, HuggingFace key, DB password) — **unrotated**. `appsettings.json` still carries the same `ClientId`, so the leaked secret is valid for the live configuration: burn the OAuth **client**, not just the secret | Outstanding |
| ✅ Closed | **Secret paths are gitignored** — `deploy/secrets/`, `clinic-keys/`, `*.pfx`, `deploy/backup-identity.txt` (the age private key that decrypts every backup), `**/appsettings.Production.json`, `.env`/`.env.*` with an `!.env*.example` negation | Done 2026-08-31 |
| ✅ Closed | **`appsettings.Development.json` no longer ships in the published image.** It was verified present on the live server, carrying a working console signing key, `minioadmin`/`minioadmin`, a DB password and a webhook key — inert only while `ASPNETCORE_ENVIRONMENT` is not `Development`, which is one variable away | Done 2026-08-31 |
| ✅ Closed | **No hardcoded LAN database password.** `docker-compose.selfhosted-lan.yml` shipped `clinic_password` in two places; `${LAN_POSTGRES_PASSWORD:?…}` now refuses to start rather than falling back to a value published in this repository | Done 2026-08-31 |
| ✅ Closed | **`Console:SigningKey` rejects a placeholder and a key identical to `Auth:Local:SigningKey`** — the check its own error message had promised since it was written, with nothing enforcing it | Done 2026-08-31 |
| ✅ Closed | **WAL segments cannot ship unencrypted.** `wal-g wal-push` uploads in clear when `WALG_LIBSODIUM_KEY` is absent and reports success; the sidecar refused that condition while the `archive_command` beside it did not. `wal-push-guard.sh` fails closed — the segment stays in `pg_wal/` and is retried, so it costs disk rather than data. **The key is set in production** (verified 2026-08-31; 0 failed archives) | Done 2026-08-31 |
| Debt | **No restore drill has ever been performed**; `key-ring-protection` and `secrets-protected-under-current-ring` never run against a live deployment | Outstanding |
| Debt | Key-ring certificate self-signed on a dev laptop; `KEY-CUSTODY.md` custody table is placeholders | Outstanding |
| Debt | Sidecar secrets still in `environment:` (shared with non-.NET containers; wal-g has no `_FILE` convention) | Deferred with a plan |
| Gap | No database-backed integration tests; no dynamic testing, no penetration test | Standing |
| Debt | **Dependency scanning now exists** (`ci.yml` → `dependencies`); its first run found **1 critical + 5 high** on NuGet and 8 high on npm | Job landed |
| ✅ Closed | **NuGet advisories all cleared** 2026-08-17 — incl. the critical `System.Text.Encodings.Web` 4.5.0 pulled in by an ASP.NET Core **2.2** package sitting in a net8 project. Verified: 0 vulnerable across all five projects, 0 build errors, **3 362 tests pass** | Done |
| ✅ Closed | **npm advisories all cleared** 2026-08-17 via `next` 15.5 → **16.3.1** on both apps plus a non-breaking `audit fix` (`sharp`, `postcss`, `lodash`, `nanoid`, Auth0 SDK). `sharp` was the real one — `next/image` passes uploaded patient files through libvips. Both report **0 vulnerabilities**; all three container images build | Done — eye pass still owed |
| ✅ Closed | **Node 20 was end-of-life** (2026-04-30) in both Dockerfiles and all three CI node jobs. Moved to **Node 22 LTS** | Done |
| ✅ Closed | **`console/Dockerfile` had never been buildable** — it copies a `public/` that was never tracked, and `deploy/docker-compose.hosted.yml` builds the console from that context. Invisible to `next build`, the typecheck, the responsive gate and CI (nothing builds images) | Done |
| Gap | `CloudBrowser` keeps a null authorization `FallbackPolicy` | Standing, mitigated |
| Debt | **Thumbnails are gone for every file predating the preview feature** (5 on production). A preview backfill restores them; the audit fix that caused it is § 7.3 | Outstanding |
| Debt | **`vpk pack` runs with no `--signParams`** and clients pull from an anonymous feed — unsigned code installed silently on every clinic PC | Outstanding |
| Debt | **No object-lock or immutability on the backup store**, while the server holds read-write S3 credentials and a `wal-g delete` path | Outstanding |
| Debt | **No MinIO server-side encryption** (needs a KMS key; SSE-S3 fails closed and would break every upload if enabled blindly) and **no disk-encryption requirement for on-premise installs** — NTFS ACLs are defeated by a removed disk, and a stolen clinic PC is the most likely real breach for a Tunisian cabinet | Deliberate, revisit |
| Debt | Rate-limit / TOTP-replay / step-up stores are **per-process** — correct on one instance, silently weaker on the first `--scale api=2` | Outstanding |
| Debt | CSP still carries `script-src 'unsafe-inline'` (§ 9.5); nonces + `strict-dynamic` need their own page walk | Outstanding |
| Debt | **No restore drill has ever been run**, and `KEY-CUSTODY.md` is still `_(name, role)_` on all five rows | Outstanding |
| Debt | **Nothing filed with the INPDP** — no declaration, no health-data authorisation, no transfer authorisation, no DPAs, no breach procedure. `site/src/pages/confidentialite.html` carries `[bracketed]` gaps and needs Tunisian counsel before publication | Outstanding |

---

## 11. Verification runbook

```bash
# Schema + security invariants. Run BEFORE and AFTER every migration batch, and diff.
# Exit 0 clean / 1 couldn't run / 2 drift found. Read-only.
docker exec clinic-api-prod dotnet ClinicManagement.API.dll verify-schema

# Money ledger reconciliation, same exit codes, same before/after-and-diff workflow.
docker exec clinic-api-prod dotnet ClinicManagement.API.dll reconcile-money

# Record each audit chain's tip OUTSIDE the database, so a later deletion of the newest
# entries becomes visible (§ 7.2). Dry run without --apply. Refuses any chain that does
# not currently verify. Exit 0 / 1 / 2 as above.
#   ⚠️ BACK THE SEAL FILE UP — losing it corrupts nothing and re-opens the blind spot.
docker exec clinic-api-prod dotnet ClinicManagement.API.dll seal-audit-chain          # dry run
docker exec clinic-api-prod dotnet ClinicManagement.API.dll seal-audit-chain --apply

# Move historical rows onto hash scheme v2 (§ 7.1). Dry run without --apply. Refuses any
# chain that does not already verify — rehashing a tampered row would launder it.
docker exec clinic-api-prod dotnet ClinicManagement.API.dll rehash-audit-chain

# Confirm no secret reaches a container as a literal environment variable.
docker exec clinic-api-prod env | grep -Ei 'password|apikey|token|secret' | grep -v '_FILE='
#   → must return nothing

# Backend suite (the ONLY automated check the backend has; nothing touches a database).
# Build outside the repo — Smart App Control refuses freshly-built in-repo assemblies.
dotnet test -c Release -p:BaseOutputPath=<temp>
```

Queue depth and dispatcher health: `GET /api/outbox` (`AdminOnly`) — **the age of the oldest waiting
row is the diagnosis, not the count**. `/hangfire` is loopback-only in every profile, so this endpoint
is the only window.

⚠️ **Restart the API before believing any end-to-end check.** A local build does not reach an
already-running process, and a stale one will answer confidently with the old behaviour.

⚠️ **A probe that produces zero is not a passing probe until the control also runs.** During the
2026-08-31 verification, « booking a patient who refused queued no reminders » was reported as a pass —
and the control (the same booking with consent *not* refused) also queued zero, because that clinic's
WhatsApp channel is enabled with no API URL and no access token, so nothing was sendable for anybody.
Always run the negative case.

Liveness: `GET /health` — anonymous, un-rate-limited, outside `/api`. Database failure is `Unhealthy`
(503); **storage failure is `Degraded` (200)**, because a clinic with no object storage still books,
records and collects, and pulling every instance out of rotation would turn a partial outage into a
total one.

---

## 12. If you are adding something security-relevant

1. **Which of § 8's four mechanisms holds it?** If the answer is "review", it is not held.
2. **Derived, never listed.** A hand-maintained expectation table rots green.
3. **Fail closed, and fail loud at boot** rather than at the first query.
4. **A guard that switches itself off when its subject is missing is not a guard.** Gate on the
   deployment *kind*, not on whether a file happens to be present.
5. **Never recover an outcome by matching French prose.** Branch on a `code`. The
   `Contains("déjà facturée")` substring match was deleted for exactly this reason: rewording a
   sentence silently changed behaviour.
6. **Ask what the broken version looks like.** If it looks like success — empty results, a clean log,
   a 200 — write the test that can tell the difference, and **prove it red** before you fix it.
