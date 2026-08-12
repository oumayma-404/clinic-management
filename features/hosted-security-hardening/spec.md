# Feature Specification: Hosted Security Hardening

**Status:** APPROVED
**Created:** 2026-08-11
**Approved:** 2026-08-12
**Feature:** Harden every layer behind the TLS edge on the hosted multi-tenant deployment — identity, transit, key custody, and evidence.

---

## Overview

The product is going to production as **`HostedMultiTenant`**: one hosted backend serving many Tunisian dental practices, reached by a browser, a Windows desktop shell, an Android shell and an iOS shell. Client-to-server traffic is already TLS with HSTS. **Everything behind that edge is not.**

Verified on 2026-08-11: the database connection carries no `sslmode`, the object-store connection has TLS explicitly off, the database and object-store volumes are unencrypted, the off-site backups are unencrypted, the Data Protection key ring is plaintext XML on a volume, clinic accounts have no second factor, a stolen session cookie renews itself indefinitely, the audit ledger can be rewritten silently, and the endpoint that exports an entire practice's medical records is recorded nowhere. Full evidence with `file:line` in **[`exploration.md`](./exploration.md)** — read it before starting any part.

This feature closes all of that. It **does not** encrypt PHI columns in the application (that decision, and why, is in *Out of Scope*).

It ships in **four parts, each starting in a fresh session.** The parts are grouped by what must ship *together*, not by even size.

---

## User Story

> **As** the vendor operating the hosted multi-tenant clinic platform,
> **I want** every layer behind the TLS edge hardened — identity, transit, key custody and evidence —
> **so that** a stolen credential, a stolen disk or a stolen backup does not yield a practice's medical records, and so that what happened to that data can be reconstructed afterwards.

### Acceptance criteria for the story as a whole

- **AC-1** — An attacker holding a clinic administrator's password alone cannot sign in, cannot export a practice's records, and cannot read a patient's file.
- **AC-2** — An attacker with a shell on the host, or a packet capture on the container network, reads no patient data in flight.
- **AC-3** — An attacker holding a copy of the database volume, the object-store volume, or an off-site backup archive reads no patient data at rest.
- **AC-4** — Every export of a practice's complete record is attributable to a person and a moment, and the record of it cannot be silently removed.
- **AC-5** — Every one of the above is enforced by something the build or `verify-schema` checks, not by a configuration key somebody remembered to set. *(`Security:EnforceCsp` has existed and been unset for the life of the deployment; that is the failure mode this criterion exists to prevent.)*
- **AC-6** — Nothing in the `SelfHostedLan` or `CloudBrowser` profiles regresses. A clinic's own offline Windows PC and the Auth0 deployment behave exactly as before, except where a change is explicitly stated as global.
- **AC-7** — No practice is ever locked out of its own records by a control introduced here. Every new gate has a stated recovery path, and every recovery path is reachable by somebody.

---

# Part 1 — Identity

> **Starting cold?** Read `exploration.md` § 0 and § 1. The whole TOTP mechanism already exists for the vendor console and is to be **mirrored, not re-invented** — § 1.1 gives the exact check order, refusal codes and entity shapes.

## What Part 1 delivers

A second factor on the accounts that can export a practice, detection of a replayed session, and a password floor that is stated once.

## Functional requirements

### FR-1.1 — A second factor, required for administrators

- **A clinic `admin` must present a time-based one-time code to sign in.** Doctor and secretary accounts may enrol one voluntarily; they are never forced.
- This is true on **`HostedMultiTenant` only**, decided by a new deployment capability `RequiresAdminSecondFactor` — by the deployment *kind*, never by a setting an operator can change. On a clinic's own offline PC an administrator locked out with no vendor to call is a worse outcome than the threat.
- **No session is issued before enrolment completes.** A correct password from an un-enrolled administrator is refused with `totp_enrolment_required`, exactly as the vendor console does. *If a pre-enrolment login issued a working token, the second factor would be decoration — any client that skipped the enrolment screen would have full access.*
- The refusal ladder mirrors the console's, in this order: unknown account → lockout → password → deactivated → not enrolled → code missing → code wrong. A **present-but-wrong code and a wrong password are indistinguishable**, with one exception in FR-1.9.

### FR-1.2 — Enrolment happens in the app, not out of band

- After a correct password, an administrator who is not enrolled is shown an enrolment step **on the login screen itself**, carrying their address and password across.
  *Not a separate route: a route loses the typed password, adds a seventh shell-less page to a set the codebase documents as exactly six, and lands behind the forced-password-change gate which short-circuits before anything after it.*
- The step shows **a QR code and the secret in readable form**, and takes a generated code to confirm. Nothing is bound until that code verifies.
- Because the phone showing the screen is often the phone holding the authenticator, the screen must offer **all three** of: a QR to scan, a tappable link that opens the authenticator directly, and a copy control — with the secret also displayed in short groups for hand-typing.
- On success, **eight single-use recovery codes are shown once**, and the flow **stops there** on a screen that must be acknowledged. It does not sign the user in. *A screen that navigates away is a screen that destroys the only copy of those codes that will ever exist.*
- The codes screen must offer copy, download and print. On a phone with no file manager, a screen full of codes and no way off it is the same as no codes.
- Acknowledgement is **explicit** — the user confirms they have saved them; the button alone is not enough.

### FR-1.3 — Voluntary enrolment has a home

- A per-account **« Sécurité »** surface, reachable by **every role**, where a doctor or secretary enrols, regenerates recovery codes, sees how many remain, and disables their factor.
  *Not « Mon profil », which is the practitioner's document identity and does not exist for a secretary. Not « Paramètres », which is clinic-wide and admin-shaped.*
- A doctor or secretary may **disable** their own factor by presenting a current code.
- An administrator **cannot** disable theirs. The screen says so in words; the control is not silently absent.
- Recovery codes may be **regenerated** by presenting a current code. Regenerating invalidates every previous code.
- When **two or fewer** codes remain, the user is told, wherever they can act on it.

### FR-1.4 — Losing a factor has a way back

Three paths, in order of who can use them:

1. **Recovery codes** — a code signs the user in once. A code is **spent even when the sign-in it accompanied fails**; a wrong password spends none. *(This rule already exists and is tested for the console; reuse it.)*
2. **A clinic administrator resets another user's factor** from the staff list, beside the password reset that is already there. This requires the step-up of FR-4.3 and **notifies the affected user**, because it is otherwise a way for a stolen admin session to strip a colleague's protection quietly.
3. **A vendor console verb, `reset-user-totp`**, for the case nobody in the practice can fix — the last remaining administrator. It re-issues a secret and invalidates the previous authenticator and every recovery code.

### FR-1.5 — Becoming an administrator revokes the session

- Any change that makes an account an administrator — an administrator changing a colleague's role, or the startup backfill that promotes a clinic's earliest user — **revokes that account's live sessions**.
  *Without this there is no login event for "forced at next login" to attach to: a user can become an administrator holding a token minted minutes earlier and simply carry on.*
- The enrolment requirement is additionally checked **per request**, not only at login, so a session that predates the requirement cannot outlive it.

### FR-1.6 — A replayed session is detected

- Each sign-in begins a **session family**. Refreshing rotates the credential; the family remembers the current one **and its immediate predecessor**, and accepts either.
  *The predecessor is what preserves today's deliberate behaviour that two tabs refreshing at once must both keep working. The rule is about ordering, not elapsed time.*
- Presenting a credential **older than the predecessor** means the credential was replayed. That family is ended and the user must sign in again on that device.
- **Only that family ends.** The user's other devices keep working, and the account is not globally revoked. *A phone resuming after hours offline, a retried request, and three tabs racing are all real shapes that look like a replay; each must cost one re-login on one device, not sign a practice out of everything.*
- The user is **told** — in the app and by e-mail — that a session was replayed and ended.
- Expired families are purged. Nothing that is still live is ever deleted.

### FR-1.7 — The session cookie is hardened

- The session cookie is restricted so it can only be set by, and returned to, this exact origin over HTTPS, and is not sent on cross-site requests.
- **The hardened form is only used where the connection is actually secure.** On a plain-HTTP connection — a clinic's own LAN install, a developer machine — the browser silently discards a cookie that claims to be host-locked, producing a login that appears to succeed and immediately bounces, forever, with no message.
- **Both** session cookies are treated the same way; they are already written and cleared together and must not diverge.
- ⚠️ **Two flows must be walked before this ships**, because a strict cross-site rule stops the session cookie being sent on a top-level navigation *into* the app: the **Google Calendar OAuth callback** (whose own state cookie is deliberately relaxed for exactly this reason) and the **e-mailed signup verification link**. If either breaks, the relaxed form stays and the reason is recorded here.
- This renames the cookie, so **every live session ends once on deploy**. The user sees a French explanation on the login screen — not a bare form, which is indistinguishable from a bug.

### FR-1.7a — Which forced gate wins

A new account can owe **both** a password change and an enrolment. The order is: **password change first, then enrolment.** The password is the credential being protected; enrolling a factor onto an account still holding a vendor-issued one-time password protects the wrong thing.

### FR-1.8 — The password floor is stated once and served

- The minimum password length rises from 8 to **12**, enforced when a password is **set**, never when one is checked. Existing passwords keep working.
- **The floor is served by the server** and read by every client that states it. It is currently duplicated in four places in the web app and once as prose in the console, and one of those — the wizard that serves both first-run setup *and* public signup — gates on the wrong condition, so raising the server alone would produce a signup form that accepts a password the server then refuses.
- Every **generated** password the product issues must clear the floor too. Six paths mint one today and none of them consults the policy.

### FR-1.9 — What a refusal says

- A wrong code and a wrong password are indistinguishable **in the code returned**, so the endpoint discloses nothing.
- But once the password is known correct, the sentence shown to the user must **name the way out** — a recovery code, and who can reset a factor. A receptionist whose phone clock has drifted must not be told to reset their password.
- The web app's login hop currently discards the machine-readable part of a refusal. It must relay it **for the second-factor refusals only**, so the "never disclose more" property of an ordinary bad-credentials answer is untouched.
- **Server clock drift is detectable.** A code is valid for roughly ninety seconds; past that, drift fails every account's login at once with the same sentence as a wrong password and no diagnostic anywhere. `verify-schema` reports the server's clock offset.

### FR-1.10 — Reachability

Enrolment, code verification, recovery-code redemption and the step-up must work on a cabinet **whose subscription has expired** and on an account that **owes a password change**. Otherwise an expired practice's administrator is forced to enrol, cannot, and therefore cannot reach the screen that would let them pay.

## Part 1 edge cases

| Case | Required behaviour |
|---|---|
| Enrolment abandoned after the secret is shown, before a code verifies | Nothing is bound. Returning to enrolment **reuses the same pending secret** so a previously-scanned entry still works. A pending secret expires; after that a fresh one is issued and the screen says the old entry is dead. |
| Recovery codes screen closed without saving | The codes are gone. The user can regenerate from « Sécurité » using their authenticator. If they have neither, FR-1.4 path 2 or 3. |
| A code is pasted with spaces, or with a leading zero | Accepted. Whitespace is stripped; a leading zero survives. |
| The same code is presented twice inside its ~90 s window | Refused. A verified code is single-use per account. |
| The QR image fails to render | The screen still enrols — the readable secret and the link are enough. A failed image is shown as a failure with a retry, never as an empty box. |
| An administrator is the only user in the practice and has lost phone and codes | Vendor verb. The refusal the user sees says who to contact. |
| A user has no smartphone at all | Out of scope for this feature; recorded in *Open Questions*. |
| Deploy day: everyone is signed out once | Explained on the login screen. |

## Part 1 gate

Backend unit suite green. `verify-schema` clean before and after the migration batch, diffed. `web`: `npm run check:responsive`, `npx tsc --noEmit`, `npm run build`, then an eye pass at 320 / 390 / 820 / 1180 / 1440 plus a landscape phone plus with the on-screen keyboard up. Walk the OAuth callback and the signup verification link under the new cookie rule.

---

# Part 2 — Transit

> **Starting cold?** Read `exploration.md` § 0 and § 2. § 2.1 explains why the Kestrel binding is shaped as it is — getting it wrong takes the whole product offline while the vendor console works perfectly.

## What Part 2 delivers

Every hop inside the perimeter encrypted, and a deployment that refuses to start if it is not.

## Functional requirements

### FR-2.1 — The database connection is encrypted and verified
The application connects to PostgreSQL over TLS and **verifies the server's identity**, against an internal certificate authority created for this deployment.

### FR-2.2 — The object-store connection is encrypted
Same authority.

### FR-2.3 — The server refuses cleartext
The database itself refuses unencrypted connections. Otherwise anything else on the container network still connects in the clear and the application's own setting is a courtesy.
⚠️ **The backup sidecar and the point-in-time-recovery process connect with their own credentials.** They must be brought across in the same change, or the nightly backup fails silently at 02:00.

### FR-2.4 — The application learns the real client address
The reverse proxy's forwarded headers are honoured, **bounded to the proxy's own address** using the trusted-proxy setting that already exists.
⚠️ **This changes three existing behaviours and each must be checked:** the per-address rate-limit ceiling and the per-account lockout tier stop treating every practice as one address (an improvement), and **two loopback-only gates become reachable by anyone who can forge a forwarded header if the bound is wrong**. Those gates must not be decided by an address a header can claim.

### FR-2.5 — Misconfiguration fails at startup, loudly
On `HostedMultiTenant`, the application refuses to start if the database connection is not verified-TLS, or if the object-store connection is not TLS.
- The check is on the deployment **kind**, never on whether a certificate file happens to be present. *A guard that switches itself off when its subject is missing is not a guard.*
- The check and the configuration that satisfies it **ship in the same commit**, or the deployment stops booting the moment the code lands.

### FR-2.6 — Internal certificates do not become an outage
- The internal authority and its certificates are **long-lived (ten years)**. Nobody outside these containers evaluates them, so a short lifetime buys almost nothing and adds a failure mode where an expiry plus a fail-loud startup turns any restart into a crash loop.
- **`verify-schema` reports days remaining**, so the warning appears on the tool already run before and after every schema change.

### FR-2.7 — What must not change
- `SelfHostedLan` serves its own front door through Kestrel and a reverse proxy in-process, with a self-signed certificate. Nothing in Part 2 may alter that path.
- The public port and the vendor console port must continue to be bound **in a single call**, and the console port gate must keep refusing in both directions.

## Part 2 edge cases

| Case | Required behaviour |
|---|---|
| Certificate absent or unreadable at startup | Refuse to start, naming the file and the setting. Never fall back to cleartext. |
| The trusted-proxy setting is empty or wrong | Forwarded headers are ignored entirely, and this is stated in the log at startup. Never trust an unbounded header. |
| A sidecar cannot negotiate TLS | The backup run **fails loudly**. It must not skip and report success. |
| Clock skew makes a certificate not-yet-valid | Same as absent: refuse and say which. |

## Part 2 gate

Bring the stack up from cold and confirm every hop negotiates TLS. Confirm the backup sidecar and PITR still run. Confirm a deliberately-wrong setting refuses to start. `verify-schema` clean.

---

# Part 3 — Custody

> **Starting cold?** Read `exploration.md` § 0 and § 3. ⚠️ § 3.1 records that the two operator documents currently **contradict each other** about how to back up the key ring; Part 3 resolves that, and the resolution is a semantic change, not a wording fix.

## What Part 3 delivers

Nothing readable from a stolen disk or a stolen backup, and a written answer to "where are the keys".

## Functional requirements

### FR-3.1 — The key ring is protected
The Data Protection key ring is encrypted by a certificate supplied to the deployment, rather than sitting in plaintext on a volume.
⚠️ **This ring now protects both populations' second factors.** Losing it locks out every clinic user *and* every console account, on top of the reminder credentials it already protects. It is the single most valuable object in the deployment.

### FR-3.2 — Rotation does not destroy data
When the protecting certificate is replaced, previous certificates remain available for decryption. A rotation that silently makes existing ciphertext unreadable is data loss.
The number of generations retained is stated in the operator guide.

### FR-3.3 — A failure to decrypt refuses, never degrades
If a protected secret cannot be decrypted, the operation that needed it **fails and says so**, naming the recovery verb. It never falls through to a weaker path. *For a second factor specifically, "could not decrypt" must never become "sign in without one".*

### FR-3.4 — The remaining plaintext secret is protected
The Google Calendar refresh token is stored encrypted. It is the last credential in the database held in the clear.

### FR-3.5 — The data volume is encrypted at rest
The volume holding the database and the object store is encrypted, unlocked at boot by a keyfile on the host's own boot volume.
- This protects a **stolen, snapshotted, or decommissioned disk** — the realistic threat. It does **not** protect against someone who already has root on the running host, and the operator guide says so in those words rather than implying more.
- The server must continue to reboot unattended.

### FR-3.6 — Backups leave encrypted
Both the nightly off-site copy and the continuous point-in-time-recovery stream are encrypted before they leave the host.

### FR-3.7 — A backup nobody can restore is not a backup
- **Each run verifies what it just uploaded** — decrypt it and confirm it parses. A failure fails the backup run, following the precedent the product already sets for its own in-app backups.
- A **manual restore drill** is documented with a stated cadence and a stated pass condition.

### FR-3.8 — Key custody is written down
The operator guide states, for each of the key ring's protecting certificate, the backup encryption key and the volume keyfile: where it lives, who holds a copy, where the copy is kept, and how to use it in a disaster. **This is a deliverable, not a note.**

### FR-3.9 — A restore knows which keys it needs
A database backup carries a marker identifying the key-ring generation in force when it was taken, checked on restore. A mismatch is refused with an explanation.
*Without this, restoring a dump against the wrong ring produces a practice whose second factors and integration credentials are all silently undecryptable, discovered when nobody can sign in.*

### FR-3.10 — Secrets reach the process as files, not environment
Credentials are supplied as files rather than environment variables, which are visible to anything that can inspect the container.
*Ranked last of Part 3's items and may be dropped if the part is running long — say so rather than half-doing it.*

### FR-3.11 — The contradictory instruction is resolved
One statement, in one voice, about what is backed up together and what is kept apart, reflecting FR-3.1: once the ring is encrypted, the thing that must travel separately is the **certificate**, not the ring.

## Part 3 edge cases

| Case | Required behaviour |
|---|---|
| The protecting certificate is missing at startup | Refuse to start. |
| A backup is restored against a mismatched key ring | Refuse, naming both generations. |
| The backup encryption key is lost | Backups are unrecoverable. FR-3.8 exists to prevent this; the guide states it plainly. |
| The host reboots unattended | The volume unlocks and the platform returns with no human present. |
| Part 3 is deployed before Part 1 has enrolled anyone | No interaction. The reverse order is the dangerous one — see *Deploy order*. |

## Part 3 gate

Reboot the host cold and confirm the platform returns unattended. Take a backup, verify it decrypts and parses, and complete one manual restore drill end to end. Confirm a mismatched key ring is refused. Confirm the four encrypted columns still round-trip after the ring is re-protected.

---

# Part 4 — Evidence & surface

> **Starting cold?** Read `exploration.md` § 0 and § 4. § 4.1 gives the precedent for "a read that must be recorded" and why it is not best-effort; § 4.2 lists what the archive contains and confirms nothing records it today; § 4.3 names the one thing that breaks an enforcing policy immediately.

## What Part 4 delivers

A tamper-evident record of what happened, an attributable record of what left, and a browser surface that is actually enforcing.

## Functional requirements

### FR-4.1 — The audit ledger is tamper-evident
- Each entry carries a value derived from itself and its predecessor, keyed by a secret **the database does not hold**, so an entry cannot be altered or removed without breaking the sequence.
- The chain is **per clinic**, and appends within a clinic are serialised.
- ⚠️ **Audit writes remain best-effort.** A failed audit write must still never roll back the clinical or financial operation it describes — that is an existing, deliberate guarantee. When a write fails, a **declared gap** is recorded instead, so a later walk can tell "a gap we know about" from "a break nobody declared".
- `verify-schema` walks each chain and reports breaks and gaps separately. A break is drift; a declared gap is reported without being drift.
- **A restore legitimately breaks a chain.** The restore records a declared boundary rather than leaving something that reads as tampering.

### FR-4.2 — Exporting a practice's records is recorded
- A download of the full-cabinet archive writes an attributable entry — who, which practice, when.
- **The entry is not best-effort.** If it cannot be written, the download does not happen. *The operation is the thing being recorded; an unrecorded export succeeding makes the guarantee false.*
- The entry states whether the archive was **delivered**, not merely requested.
- The archive endpoint gets its own tight rate limit. It currently falls to the general limit, which permits six hundred full-practice exports a minute.

### FR-4.3 — Exporting and restoring require the password again
- The full-cabinet **download** and the full-cabinet **restore** both require the user to re-enter their password immediately beforehand.
- **Failures do not count toward the login lockout**, so a mistyped password at the export card cannot lock a practice's only administrator out of the product mid-day. They are bounded on their own counter.
- The confirmation is single-use per action.
- Per-list CSV exports are **not** gated — they are already role-restricted and are a daily action; daily friction is what gets a control routed around.

### FR-4.4 — Patient data leaves the logs
- No log line records a patient's name or other identifying detail. Where a name is genuinely the diagnostic handle, an identifier replaces it.
- This covers the calendar-sync path, the document-generation path, **the AI service's raw payload**, and **document file names**, which are composed from a patient's name.
- Logs are written to a durable location with a stated retention, rather than a container layer that vanishes on restart.
⚠️ Making logs durable **persists what was previously ephemeral**, so FR-4.4's first clause must land in the same change, not after it.

### FR-4.5 — The content policy is enforced
- The policy is enforcing rather than reporting, and the setting that controls it is **set in the deployment configuration** — it has existed and been unset for the life of the product.
- The weakest directive is removed.
- Violations are **reported somewhere a person will see**. ⚠️ Report bodies carry the page address, and this app's addresses contain patient identifiers — so reports are subject to FR-4.4, and the receiving endpoint is bounded.
- ⚠️ **A third-party analytics script is removed** from the web app. It loads from an external origin, which breaks an enforcing policy before any other work, and it sends page views from a medical-records application to a third party on a self-hosted deployment that gets nothing back from it.
- The remaining browser-protection headers are added, and the **vendor console site gets a policy** — it has none.
- The page policy and the API policy must not drift. Something checks they agree.

### FR-4.6 — The redirect that does nothing
The HTTPS redirect currently registered has no port configured and silently does nothing. It is either configured or removed. A security control that is present and inert is worse than an absent one, because it reads as present.

## Part 4 edge cases

| Case | Required behaviour |
|---|---|
| The chain is broken | `verify-schema` reports it, naming the first broken entry. Nothing refuses to serve — an audit break is an alarm, not an outage. |
| An audit write fails | The operation still commits. A declared gap is recorded. |
| The archive download aborts at 90 % | Recorded as not delivered. |
| The archive ledger row cannot be written | The download is refused with a French explanation. |
| A user re-enters the wrong password three times at the step-up | Refused on its own counter, session untouched, and the screen says the session is still fine. |
| A shell user who signs in by biometrics does not remember their password | They cannot export. Recorded in *Open Questions*. |
| The report endpoint is flooded | Bounded; excess is dropped, not stored. |

## Part 4 gate

`verify-schema` clean, and confirm the chain walk turns red on a hand-edited entry. Confirm the archive is refused when the ledger cannot be written. Walk the whole app under the enforcing policy at 320 / 390 / 820 / 1180 / 1440 and confirm zero violations. Confirm no patient name appears in a log file after a full day of use.

---

## Scope

### In scope
- The four parts above, on the `HostedMultiTenant` profile.
- Changes to shared code that are **explicitly global**: the password floor, the session-cookie rules, the audit chain, patient data leaving the logs, and — for `CloudBrowser` only, because the compose files share their infrastructure — Part 2's **transit** configuration and its startup check (see Stated Assumption 11).
- Operator documentation for everything an operator must do or hold.
- The guard tests and `verify-schema` checks that make each guarantee checkable.

### Out of scope
- **Encrypting patient data in the application.** Rejected with reasons: it breaks database-side patient search (accent-insensitive free-text matching, without which a patient on page seven reads as "no results"), duplicate detection, and ordering a paged list by name. Revisit only if a compliance rule requires it, and then as its own feature.
- **Moving the database or object store to managed services.** A hosting and cost decision.
- **Certificate pinning in the mobile and desktop shells.** Pinning a ninety-day public certificate across three app stores is an outage generator.
- **A second factor for users with no smartphone**, and **hardware keys / WebAuthn**.
- **Per-practitioner data scoping.** This feature is about authentication and evidence, not about narrowing what a role can see.
- **Fixing the restore defect at `ClinicArchiveRestorer.cs:79`** — a leftover probe discards staged inserts before the save, so a restore reports success and persists nothing. It is unrelated, uncommitted, and should be fixed on its own.

---

## Device & Interface Behaviour

Governed by `.claude/rules/frontend-web.md`. Every new surface is usable at **320 px**, at a **380 px viewport height**, and at **200 % zoom**, with **44 px targets on a coarse pointer**.

| Surface | Required behaviour |
|---|---|
| **Login, with a code field** | ⚠️ The login card is today a centred box with no scrolling — the documented vertical-clipping trap. Adding a code field and a recovery link is what makes it fire on a landscape phone. It moves to the pattern the two existing full-screen gates already use, so the top stays reachable. The submit must remain reachable with the numeric keypad open. |
| **The code field** | Numeric keypad, one-time-code autofill, **a leading zero survives**, pasted spaces are stripped. A plain field, not six boxes — segmented fields break paste and password-manager fill. |
| **Enrolment step** | QR on a fixed light plate at a stated minimum size **regardless of theme** — the app is theme-aware and a dark card makes a QR unscannable. Plus the tappable link, the copy control, and the secret in short groups. A failed QR still enrols. |
| **Recovery codes** | Copy, download and print. Explicit acknowledgement before dismissal. The live region announces a short summary, **not all eight codes read aloud** — reception is often a shared desk. |
| **Step-up dialog** | A sheet below `md:`. Focus lands on the password field; cancel returns focus to the control that opened it; `Escape` closes it. |
| **Archive on a phone** | The archive is a multi-gigabyte file. Where it cannot work, say so in French — « Téléchargez l'archive depuis un ordinateur » — never fail silently and never leave a spinner running. |
| **Everywhere** | Refusals `role="alert"`; outcomes `role="status"` — the login error banner has **no role at all** today. Every state stated in **words as well as colour**. The three empty kinds kept apart: a failed read is never rendered as empty. Motion behind `motion-safe:`. |
| **Buttons** | An explicit minimum height on every new button — the large size in this codebase is 40 px, under the floor. Stacked text links grow their own box rather than using an overlay helper, which would steal a neighbour's taps. |

---

## API Endpoints

Shapes only; exact paths follow existing conventions. All French, all `{ error }` with a machine-readable code where a caller branches.

| Endpoint | Purpose | Notes |
|---|---|---|
| `POST /api/auth/login` *(modified)* | Accepts an optional code | Refuses with `totp_required` / `totp_enrolment_required` / `totp_invalid` |
| `POST /api/auth/totp/enrol` | Confirm a factor, return recovery codes once | Anonymous + rate-limited; carries the password. **No session issued.** |
| `POST /api/auth/recovery` | Sign in with a recovery code | Code spent even if the sign-in then fails |
| `GET/POST /api/auth/totp` *(« Sécurité »)* | Read state, enrol, disable, regenerate codes | Authenticated |
| `POST /api/users/{id}/totp/reset` | An admin resets a colleague's factor | Admin only; step-up; notifies the user |
| `GET /api/auth/mode` *(modified)* | Publishes the password floor and whether a factor is required | Mirrors how the trial length is already published |
| `POST /api/auth/step-up` | Confirm the password for a sensitive action | Own failure counter |
| `GET /api/backup/archive` *(modified)* | Recorded, rate-limited, step-up | |
| `POST /api/backup/archive/restore` *(modified)* | Step-up | |
| `POST /api/csp-report` | Receives policy violations | Anonymous, bounded, addresses stripped |

**Exempt from the subscription gate and from the forced-password-change gate:** enrolment, code verification, recovery redemption, step-up.

**Console verb:** `reset-user-totp --email <address>`, gated on a configured database connection (not a capability), dispatched from the startup switch — ⚠️ a verb with no dispatch branch **boots the web host instead** and reads to an operator as "the command did nothing".

---

## Checkable guarantees

Per AC-5, each guarantee is held by something derived rather than listed. Following the house style in `exploration.md` § 5.1: criterion in the docstring, candidate set by reflection or a source scan, `Assert.NotEmpty`, exceptions as a name→reason map asserted **equal in both directions**, and an executed red-proof.

| Guard | Derives | Part |
|---|---|---|
| Deployment matrix row | The new capability, by reflection over every capability | 1 |
| Second-factor coverage | No session reaches an administrator without a verified factor | 1 |
| Password-floor single source | Every client statement of the minimum reads the served value | 1 |
| Transport configuration | **Parses the deployment configuration file** and asserts verified-TLS, object-store TLS and the enforcing policy are set — *this is the guard that would have caught the policy setting being unset for the life of the product* | 2 |
| Secret-protection coverage | Every credential-shaped property is either protected or a named decision | 3 |
| Log-template coverage | No log template names a patient | 4 |
| Policy agreement | The page policy and the API policy are identical | 4 |

**New `verify-schema` checks:** every administrator has a factor or is unenrolled · session families have no orphans · the Google token is protected · each clinic's audit chain is intact · declared gaps are reported apart from breaks · internal certificate days-to-expiry · server clock offset.

---

## Deploy order and rollback

- **Part 1 before Part 3.** Part 3 re-protects the key ring, and second factors live on that ring. If Part 3 were to mint a **new** ring rather than re-wrapping the existing keys, every factor enrolled in Part 1 would die. Part 3 must **re-wrap**, and this is stated as a requirement, not an intention.
- **Part 2 before Part 3** is preferred but not required.
- **Every part states its revert procedure.** Known asymmetries: reverting Part 1 signs everyone out a second time; reverting Part 4 after the chain is populated leaves a permanent boundary when re-applied; reverting Part 3's file-based secrets after the environment values are deleted is a hard startup failure.

---

## Non-Functional Hints

- **Availability outranks strictness on the recovery paths.** Every gate here has a way past for the legitimate user; where it does not, that is named as an open question rather than left implicit.
- The identity work touches every sign-in in the product. It must not add a perceptible delay to a normal login.
- The audit chain adds work to every write. Appends are serialised per clinic, so one busy practice must not slow another.
- The archive is already buffered twice in memory server-side, with no size cap on the download. Part 4 does not fix that, but must not make it worse.
- French throughout. No English string reaches a user.

---

## Dependencies

- **`Otp.NET`**, already a dependency. No new library for code generation.
- **A QR generator exists server-side** but is currently reachable only on a profile this feature does not target — and the web app has no QR library. Since enrolment happens before a session exists, the code must arrive **in the enrolment response body**; an image tag cannot carry a credential.
- An internal certificate authority for Part 2, created by the deployment rather than by an existing provisioner (the existing one runs before the application is built and is Windows-service-shaped).
- Off-site storage that supports client-side encryption, for Part 3.
- Host root access, for Part 3's volume encryption. **Confirmed available.**

---

## Stated Assumptions

Decided during requirements gathering rather than asked, because each follows from an established convention in this codebase. **Correct any of these at approval.**

1. Enrolment is a **mode of the login screen**, not a route — the vendor console's stated reasoning applies verbatim, and a route would land behind the forced-password-change gate that short-circuits before anything after it.
2. **No session token before enrolment completes.** Anything else makes the factor decoration.
3. The QR arrives **in the response body**, for the reason in *Dependencies*.
4. The authenticator entry is labelled with the **practice name and the user's address**, so a user working at two practices can tell them apart.
5. The password floor is **served, not duplicated** — the alternative is knowingly shipping the mismatch the integration review found.
6. **Promotion to administrator revokes the session**, and the enrolment requirement is checked per request.
7. Voluntary enrolment lives on a **per-account « Sécurité » surface reachable by every role**, and there is **no nudge or prompt** for it — an unrequested interruption mid-consultation is what this codebase already refuses elsewhere.
8. Recovery codes are **regenerable by presenting a current code**, and the user is warned at **two remaining**.
9. Security events **notify**: factor enrolled, factor reset by someone else, session replay detected, full archive exported. To the affected user, and to administrators for the export.
10. The hardened cookie form applies **only where the connection is secure**, so plain-HTTP LAN and development installs keep working.
11. `SelfHostedLan` and `CloudBrowser` get the **password floor, the cookie rules, the audit chain and the logging change** — these are global — and **nothing else**.
    ⚠️ **Corrected at challenge: transit is a fifth global change, for `CloudBrowser` only.** `docker-compose.hosted.yml` `extends` `docker-compose.prod.yml`'s infrastructure and `deploy/postgres/Dockerfile` is shared, so Part 2's internal CA, `ssl=on` and `hostssl`-only `pg_hba.conf` reach the `CloudBrowser` deployment whether or not they are aimed at it. FR-2.5's startup check therefore gates on `!SelfHostsFrontDoor` — **both hosted kinds** — because a check narrower than its own configuration lets transit fail open on the profile that received it. `SelfHostedLan` remains untouched.

---

## Open Questions

1. **A user with no smartphone.** Currently unanswerable by this feature. If a practice's owner has no device that runs an authenticator, they cannot be an administrator on the hosted deployment. Is that acceptable for the Tunisian market, or does it need a printed-codes-only mode?
2. **A shell user who signs in by biometrics and does not remember their password** cannot pass the export step-up. Is a second-factor code an acceptable alternative confirmation for them?
3. **What identity check does the vendor perform** before running `reset-user-totp`? The verb takes only an address. This is a written procedure, not code, but nothing exists today.
4. **Cross-site cookie strictness vs. the OAuth callback and the e-mailed verification link** — FR-1.7 requires walking both. If they break, the relaxed form stays; the walk decides.
5. **Retention for the now-durable logs** — a number is needed.
6. **Manual restore-drill cadence** — a number is needed.
7. Whether **file-based secrets (FR-3.10)** stay in Part 3 or are dropped.

---

## Related

- `exploration.md` — all verified codebase context with `file:line`, organised by part.
- `features/cloud-security-and-tenant-isolation/`, `features/security-hardening/`, `features/multi-tenant-cloud/`, `features/platform-console/`, `features/postgres-pitr/` — prior security work.
- `features/LEARNINGS.md` — in particular: reflection-based allow-list tests as regression nets; gate mode-invariant guards on the mode, not a capability; security and transport configuration must fail closed and loud.
