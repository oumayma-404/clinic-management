# Feature Specification: Console éditeur (vendor back-office — usage et abonnements)

**Status:** APPROVED
**Challenged:** Yes
**Created:** 2026-08-10
**Approved:** 2026-08-10
**Feature:** A private back-office where the vendor sees how much each clinic actually uses the product, and unlocks a clinic for the period it paid for — without ever being able to read a patient record.

> **Depends on:** [`features/clinic-subscription/spec.md`](../clinic-subscription/spec.md). That feature defines the
> entitlement this one grants and the trial it extends. This feature must not be started before it, and adds no new
> commercial rule of its own — it is a *surface* over rules that already exist.

---

## Overview

Once cabinets are on trials and subscriptions, the vendor needs two things every week: to know **who is actually
using the product** (a cabinet that has recorded nothing in three weeks will not renew, and a cabinet recording
every day will), and to **unlock a cabinet the moment its transfer lands**. Today both are console commands run
against the container, which works and does not scale past a handful of clinics — and does not answer « qui utilise
vraiment » at all.

This feature is a private web console listing every cabinet with its subscription state beside its real activity,
where a payment is recorded in a few clicks. Two properties define it, and both are deliberate constraints rather
than limitations.

**It cannot read a clinical record.** Everything it needs — patients created, appointments booked, saves in the last
7 and 30 days, days active, last sign-in, and the cabinet's own monthly takings — is a **number**. So it reads counts
and totals and nothing else: no patient name, no appointment, no note, no diagnosis, no per-patient balance. This is
enforced structurally, not by discipline — one narrow read surface with a declared, closed shape, held by a check
that fails the build (AC-7.2) — which is what lets the vendor tell a clinic « nous ne pouvons pas voir vos
patients » as a fact rather than a promise, and what limits the damage if the operator account is ever compromised.
⚠️ Note what that sentence must **not** be stretched into: the cabinet's monthly collected total is visible, and a
promise phrased as « nous ne voyons rien » would be broader than the truth (AC-7.4).

**It is not on the public internet.** It answers on its own port, which is never published; the vendor reaches it
over an SSH tunnel or a VPN. Its accounts are separate from clinic accounts, its tokens are not interchangeable with
clinic tokens, and it requires a second factor. It is the one surface in the product that can see across every
practice, and it is protected accordingly.

---

## User Stories

### US-1: The vendor signs in to the console
As the **vendor**, I want a private sign-in that a leaked clinic password cannot reach, so that the one account able
to see every practice is protected properly.

**Acceptance Criteria:**
- **AC-1.1:** The console has its own accounts, entirely separate from clinic user accounts. No clinic account —
  including an administrator's — can sign in to it, and no console account can sign in to a clinic.
- **AC-1.2:** Sign-in requires an e-mail address, a password, **and** a time-based one-time code.
- **AC-1.3:** The second factor's enrolment secret is produced by the **bootstrap command** (AC-8.1) and shown to
  the operator there — **never handed out in response to a password**. A password-only sign-in on a not-yet-enrolled
  account is told enrolment is required and is given **nothing else**: no secret, no recovery codes, no session.
- **AC-1.3a:** Enrolment is completed on a separate action that carries the password **and a valid generated
  code**; only then is the factor bound and the recovery codes shown **once**, with an explicit instruction to
  store them. Whoever holds only the password can therefore neither enrol their own authenticator nor sign in.
- **AC-1.3b:** A recovery code is redeemed on its own action, is single-use, and is consumed whether or not the
  sign-in it was presented for completes.
- **AC-1.4:** A session obtained from the console cannot be used against any clinic route, and a clinic session
  cannot be used against any console route — refused as unauthenticated, not merely unauthorised.
- **AC-1.5:** Repeated failed attempts lock the account temporarily, as clinic accounts already do — **and** the
  console's sign-in is bounded by the product's existing anonymous-authentication rate limits, per submitted
  account and per address, exactly as the clinic's sign-in is. The private address is a third layer, never the
  only one.
- **AC-1.6:** A console account can be deactivated, and deactivation takes effect on the next request rather than
  when its session would have expired.
- **AC-1.7:** The console is unreachable from the deployment's public address. Requests for console paths on the
  public address are refused as if the paths did not exist, and requests for clinic paths on the console's own
  address are refused likewise. **This covers the console's pages, not only its endpoints** — a console screen
  served by the deployment's public front door would defeat the requirement however the API behind it is bound.
- **AC-1.8:** The console can be switched off entirely for a deployment, and when off nothing about it is reachable.

### US-2: The vendor sees every cabinet, and how much each one is used
As the **vendor**, I want one list showing every cabinet's subscription state next to its real activity, so that I
know who to invoice, who to help and who is about to churn.

**Acceptance Criteria:**
- **AC-2.1:** The list shows, per cabinet: name, city, creation date, plan, state (« Essai » / « Actif » /
  « Expiré » / « Suspendu »), end date, days remaining, number of staff accounts, number of patients, appointments
  in the last 30 days, **saves in the last 7 and last 30 days**, days on which anything was saved in the last 30,
  the last sign-in, and the amount the cabinet itself collected this month (« encaissé par le cabinet » — the
  practice's own turnover, distinct from AC-2.7's « encaissé auprès des cabinets », which is the vendor's).
- **AC-2.2:** « Saves » counts only actions taken by **people at the cabinet**. Two exclusions, and both are
  silent failures otherwise: background work — scheduled backups, reminder dispatch, expiry passes — and **the
  vendor's own console writes**, or granting a dormant cabinet a subscription makes it read as active the next
  morning, which is exactly the signal the list exists to give.
- **AC-2.3:** The list can be filtered to: « en essai », « expire sous N jours », « expiré », « suspendu », and
  « dormant » (nothing saved in 30 days).
- **AC-2.4:** The list can be sorted by end date, by activity and by creation date, and is paged.
- **AC-2.4a:** Because the list can be **filtered and sorted on activity**, every activity figure must exist for
  every cabinet **before** a page is cut — a figure computed over the page that was already selected would filter
  and sort a window rather than the portfolio. The figures are therefore read from **per-cabinet counters
  maintained by a scheduled pass** (FR-3), never derived per request from the mutation ledger.
- **AC-2.5:** A free-text search matches a cabinet's name, city or administrator's e-mail address.
- **AC-2.6:** Every figure shown is a count, a date or a total. **No patient, appointment, document, note or
  per-patient amount appears anywhere on the screen.**
- **AC-2.7:** A summary above the list gives: cabinets in trial, active, expiring within a fortnight, expired,
  suspended, and the total the **vendor** recorded as collected from cabinets this month. This is the vendor's
  revenue and is never a sum of AC-2.1's per-cabinet figure; the two are labelled so they cannot be read as the
  same quantity.
- **AC-2.8:** Activity figures are as fresh as the last counter pass, and the screen **says when that was**. A
  stale figure presented as live is how a cabinet that started working yesterday gets a churn call today.

### US-3: The vendor opens one cabinet
As the **vendor**, I want one cabinet's detail with its activity over time and everything it has paid, so that I can
prepare a renewal conversation or answer « what did we pay last year? ».

**Acceptance Criteria:**
- **AC-3.1:** The detail shows the same figures as the list, plus a **six-month trend** of activity.
- **AC-3.2:** It shows the cabinet's full payment history: period, kind, amount, method, reference, note, who
  recorded it and when — including cancelled entries, struck through with their reason.
- **AC-3.3:** It shows the administrator's name and e-mail address and the number of staff accounts, so the vendor
  knows who to contact.
- **AC-3.4:** It shows **no** clinical or per-patient information of any kind.
- **AC-3.5:** Opening a cabinet's detail is itself recorded — who looked at which cabinet, and when. **Listing
  cabinets is not**: one list read touches every cabinet, so recording it would write a row per cabinet per page
  load, several times a day, and drown the readings anyone will ever want.

### US-4: The vendor records a payment and unlocks the cabinet
As the **vendor**, I want to record a received payment and extend the cabinet in one action, so that a clinic that
has paid is working again within minutes of my seeing the transfer.

**Acceptance Criteria:**
- **AC-4.1:** From a cabinet's detail, the vendor records: a duration (or an explicit end date), a plan, an amount,
  a method (virement / espèces / chèque / carte), a reference and an optional note.
- **AC-4.2:** The new end date follows the same rule as everywhere else — from whichever is later, the current end
  date or today. The console introduces no second arithmetic.
- **AC-4.3:** On success the console shows the cabinet's new state and end date immediately.
- **AC-4.4:** The clinic's own app reflects the change without anybody signing out or reloading, **within the delay
  the companion feature states** (its FR-15). ⚠️ **The mechanism is the companion's re-read, not a broadcast from
  here.** That feature settled this deliberately: the app re-reads its subscription on an interval while a warning
  or expiry is in force, on window focus, and immediately on any 402 — because the two moments that change the
  state cannot push one (a grant by the vendor's *command* runs in another process with no realtime notifier and no
  caller clinic to address, and an entitlement lapsing at midnight has no actor at all). The console must not
  re-answer a question already answered there.
- **AC-4.4a:** The console **may** additionally notify the affected cabinet directly — it grants in-process, so
  unlike the command it can — but only as an optimisation on top of the re-read, never as what AC-4.4 depends on.
  If it does, the audience is the **target** cabinet named explicitly: the product's live-refresh signal derives
  its audience from the acting user's own clinic, which a console account does not have, so a console write relying
  on that default reaches nobody — silently, and invisibly in testing, since the vendor and the clinic are never
  the same browser.
- **AC-4.5:** The action is refused, with a message naming the reason, for a non-positive duration or a cabinet that
  no longer exists.
- **AC-4.6:** Recording the same payment twice by double-clicking produces **one** entry.
- **AC-4.7:** Every grant records which console account made it, and appears in that cabinet's own activity journal.
- **AC-4.8:** The vendor may also record a complimentary period (no amount) — for a pilot, a goodwill extension or a
  partner — and the entry says which it is.

### US-5: The vendor corrects a mistake
As the **vendor**, I want to cancel an entry I recorded wrongly, so that a mis-keyed amount or a payment credited to
the wrong practice can be put right without rewriting history.

**Acceptance Criteria:**
- **AC-5.1:** Any payment entry can be cancelled with a **mandatory written reason**.
- **AC-5.2:** The entry is never edited and never deleted: it stays listed, struck through, with its reason, its
  canceller and the moment.
- **AC-5.3:** The end date recomputes, and may move back into the past — at which point the cabinet becomes
  read-only again, which the confirmation says out loud before the vendor commits.
- **AC-5.4:** The confirmation names the cabinet and the amount being cancelled, so with several tabs open the
  vendor cannot cancel the wrong one.

### US-6: The vendor suspends a cabinet for abuse
As the **vendor**, I want to suspend a cabinet independently of its payment state, so that fraud or abuse can be
stopped without pretending it is a billing matter.

**Acceptance Criteria:**
- **AC-6.1:** Suspension requires a written reason and is recorded with its author and moment.
- **AC-6.2:** A suspended cabinet is read-only exactly as an expired one is, and is told it is suspended — never
  told it has expired.
- **AC-6.3:** Suspension is visually distinct from expiry throughout the console; it is not a payment state.
- **AC-6.4:** Unsuspending restores whatever entitlement the cabinet had. Suspension never consumes paid days.
- **AC-6.5:** Suspending is confirmed with the cabinet named, and warns that the practice will be unable to record
  new work.

### US-7: A clinic's records stay private from the vendor
As a **clinic owner**, I want the vendor to be structurally unable to read my patients' records, so that hosting my
practice with them does not mean handing over my patients' medical information.

**Acceptance Criteria:**
- **AC-7.1:** No console screen, endpoint or export exposes a patient, an appointment, a clinical note, a document,
  a file, a diagnosis or a per-patient amount.
- **AC-7.2:** This is enforced by the system's own structure and **checked automatically**, so a future change that
  would expose such data fails before it ships rather than being caught in review. Concretely: the console reads
  through **one narrow surface whose returned shape is a closed, declared set** (FR-3), and a **derived check** —
  the pattern this product already uses to hold the tenant filters, the clinical-record policies and the realtime
  keys — fails the build if a console read returns anything outside it. It is derived rather than a hand-kept list,
  because a list is a second place to remember and the first thing added to it silently would be the one that
  matters.
- **AC-7.2a:** ⚠️ **The tenant filter is not the mechanism, and assuming it is would be the defect.** The
  product's per-clinic query filter is fed by the caller's own clinic; a console account has none, so a console
  request either reads **nothing at all** (every cabinet showing zeros, with no error — the failure mode the
  product has already been bitten by) or declares itself cross-cabinet, which switches the filter off entirely.
  The console does the latter and says so; the guarantee therefore has to be carried by AC-7.2's declared surface,
  not by the filter.
- **AC-7.3:** Every console read **of a cabinet's detail**, and every console write, is recorded — who, which
  cabinet, which action, when — so « who looked at what » is answerable. See AC-3.5 for why list reads are not.
- **AC-7.4:** The clinic can be told this in plain language, and it is true rather than aspirational. ⚠️ What is
  said must include what the vendor **does** see: counts, dates, subscription state, and the cabinet's **own
  monthly collected total** (AC-2.1). That figure is not per-patient and not clinical, but it is the practice's
  turnover, and a promise phrased as « nous ne voyons pas vos données » would be broader than the truth.

### US-8: The vendor can get in the first time, and get back in after losing the second factor
As the **vendor**, I want to create the first console account and recover a locked-out one, so that the console can
never become permanently unreachable.

**Acceptance Criteria:**
- **AC-8.1:** **Every** console account is created by a command run with direct access to the deployment, never
  through a web page — the first and each later one. There is no self-registration and no screen that mints an
  account able to see every practice. The command prints the account's enrolment secret (AC-1.3) and its one-time
  password.
- **AC-8.2:** A console account whose second factor is lost is recovered by a recovery code, or failing that by the
  same kind of command, which re-issues an enrolment secret and invalidates the old one.
- **AC-8.3:** The companion feature's **four** subscription commands — grant, cancel, suspend/unsuspend and
  report — keep working, so a broken or unreachable console never blocks unlocking a paying clinic, nor
  correcting a mis-keyed grant.
- **AC-8.4:** More than one console account may exist, and each acts under its own identity in every record.
- **AC-8.5:** **Deactivating** a console account is also a command (AC-1.6 is what makes it take effect on the next
  request). There is no console screen that lists, creates or deactivates console accounts.
- **AC-8.6:** A **signed-in** console account can change **its own** password in the console. It is the action
  needed fastest after a suspected leak, it grants nothing that account does not already have, and requiring
  deployment access for it means in practice it is never done.

---

## Functional Requirements

### FR-1: Console identity
- Console accounts are a separate population from clinic users: their own credentials, their own activation state,
  their own lockout, their own sessions.
- A console session and a clinic session are **not interchangeable in either direction** (AC-1.4).
- A second factor is **required**, not optional, and cannot be disabled per account.
- The second factor's secret is stored encrypted at rest; recovery codes are stored so that they cannot be read back,
  only checked. ⚠️ The at-rest protection this rides on is the deployment's key ring, which on the hosted profile is
  already required to be durably stored — **losing it now costs sign-in itself**, not only the reminder credentials
  it protected before, so the volume behind it becomes load-bearing for reaching the console at all (see
  Dependencies).
- The enrolment secret is issued **out of band, by the bootstrap command**, and confirmed by a generated code before
  it is bound (AC-1.3 / AC-1.3a). No authenticated-by-password-alone response ever carries it.
- Deactivation and session revocation take effect on the next request, not at natural session expiry.
- The sign-in endpoint belongs to the deployment's **anonymous-authentication surface** for rate-limiting purposes —
  the per-account and per-address bounds apply to it as they do to the clinic's sign-in (AC-1.5). A route prefix the
  limiter does not recognise gets the loose general API ceiling, which is not a decision anyone would take
  deliberately for this surface.

### FR-2: Exposure
- The console answers on **its own network address**, separate from the one clinics use.
- That address serves **only** console paths and refuses everything else; the clinic-facing address refuses console
  paths. Neither is a matter of configuration hygiene — each address serves what it is for and nothing else.
- ⚠️ **The refusal is two-way, and the precedent in this product is one-way.** The existing private-address pattern
  (the LAN device-trust page) refuses everything *but* its own paths on its own port, while deliberately leaving
  those paths reachable on the normal front door too. The console needs the opposite as well: its paths must be
  absent from the public address. The mechanism carries over; its direction does not.
- ⚠️ **Binding a port is not scoping a surface.** The web host answers every route it has mapped on every port it
  binds, so a second listener alone publishes everything twice. The refusal is what does the work.
- **The console's pages are served on that address too, not only its endpoints.** On the hosted deployment the
  public front door proxies everything outside the API to the clinic web application, so console screens shipped in
  that application would be publicly reachable by default and a proxy rule would be the only thing hiding them.
  Instead, the console is served by **its own listener on the private address**, with no route to it from the
  public front door at all. It is a separate deployment unit; the clinic-facing bundle contains none of it.
- The console's address is **not published** by the deployment. Reaching it requires a tunnel or a private network.
- A misconfiguration that would make the two addresses collide **prevents the deployment from starting**, with a
  message naming the problem, rather than silently making the whole product unreachable.
- The console can be switched off, and when off it is absent rather than present-and-refusing.
- The console exists only on the hosted multi-tenant deployment.

### FR-3: What the console may read
- Exactly one cross-cabinet read exists, and it returns **only**: identity and address of the cabinet, its creation
  date, its subscription state and dates, its plan, counts (staff accounts, patients, appointments over a window,
  saves over a window, active days over a window), its last sign-in, and its collected total over a window.
- It returns **no** free text authored in a clinic, **no** name of any patient, and **no** amount attributable to a
  patient.
- The returned shape is a **closed, declared set of scalars**, and a derived check fails the build if a console read
  returns anything outside it (AC-7.2). This is the guarantee's whole enforcement — the per-clinic query filter
  cannot be it (AC-7.2a).
- **Activity figures are read from per-cabinet counters, not computed per request.** A scheduled pass derives each
  cabinet's counts from the product's existing record of mutations — restricted to actions taken by people at that
  cabinet, excluding background work **and** console writes (AC-2.2) — and stores one row per cabinet per day. Three
  things fall out of that, and each is a requirement in its own right:
  - the list can filter and sort on activity **and still be paged**, which a per-request derivation cannot offer
    (AC-2.4a);
  - the read stays bounded by the number of cabinets rather than by the busiest practice's entire history (EC-11);
  - the counters **survive** any retention policy later applied to the mutation record, so the console's own history
    is not silently truncated by a decision taken elsewhere (see Non-Functional Hints).
- The console's own read surface touches the counter table, the cabinet, its administrator and the subscription
  ledger. Nothing else.

### FR-4: What the console may write
- Exactly three write actions: **record a payment period**, **cancel a payment period**, **suspend / unsuspend**.
- All three operate on the entitlement defined by the companion feature and use its rules unchanged. The console
  introduces no commercial rule and no second arithmetic.
- Nothing else in any cabinet can be created, changed or deleted from the console. It cannot edit a clinic's
  settings, its users, its catalogue or any record.
- Each write is attributed to the acting console account, and appears in the affected cabinet's own activity
  journal — distinguishably from a clinic user, and **excluded from that cabinet's activity figures** (AC-2.2).
- A write must be safe to submit twice (AC-4.6).
- ⚠️ **The console's routes are outside the companion feature's write-refusal.** That feature refuses every write
  under the API for a cabinet whose entitlement has lapsed, over an allow-list it states is explicit and fixed. A
  console caller has no cabinet of its own, and the cabinets it writes to are precisely the lapsed ones — so
  without an explicit exemption the endpoint that unlocks a practice is blocked by the state it exists to clear.
  This must be named on **both** sides: here, and on the companion's allow-list.
- Concurrency is **not** modelled as a conflict. The end date is derived from an append-only ledger, so two grants
  landing together produce two entries and the wrong one is cancelled (EC-6) — there is nothing half-applied for a
  version check to protect. Duplicate submission of the *same* action is handled by idempotency, which is a
  different question.

### FR-5: Accountability of the console itself
- Every console **read of a cabinet's detail**, and every console **write**, is recorded: which account, which
  cabinet, which action, when. Listing cabinets is not recorded (AC-3.5).
- Reads are recorded nowhere else in this product; here they are, because this is the only surface that can look
  across practices.
- ⚠️ **This is its own ledger, not the clinic-facing activity journal.** That journal is written by the mechanism
  that observes saves, and its vocabulary is the three shapes a save takes — a read performs no save and has no
  place in it. Folding console access into it would also mean deciding, by accident, what a clinic's administrator
  sees on their own « Journal d'activité » screen.
- **Who may read the console ledger: console accounts.** Surfacing « the vendor opened your cabinet on 3 August »
  to the cabinet itself is a defensible and possibly attractive extension of US-7's promise, but it is a second
  read surface and a separate decision — deliberately **out of scope** here rather than arrived at silently.
- Console records are append-only and have no editing or deletion path.

### FR-6: Live effect on the clinic
- Recording, cancelling, suspending or unsuspending takes effect for that clinic **immediately** — the entitlement
  is changed the moment the console write commits, with nothing cached that could keep a paid cabinet locked out.
- The clinic's open sessions learn about it through the **companion feature's re-read** (its FR-15), within the
  delay that feature states. This spec introduces no second mechanism and states no shorter delay.
- ⚠️ **A direct notification is an optimisation, not the contract** (AC-4.4a). Where the console does send one, it
  addresses the **target** cabinet explicitly: the product's live-refresh signal addresses the acting user's *own*
  clinic and derives what changed from where the code lives, and neither default is right here — a console account
  has no clinic, so the signal reaches nobody, and a new signal name nothing subscribes to fails the check that
  holds the server's signals and the client's subscriptions equal.
- Only the affected cabinet is notified. Nothing about one cabinet's subscription may reach another cabinet.

### FR-7: The console's own presentation
- French throughout, like the rest of the product.
- The console is its **own** application area: it does not present the clinic navigation, the clinic search, the
  assistant, the notification bell or any clinic-scoped chrome. It cannot be navigated into from the clinic app.
- Every destructive or state-changing confirmation names the cabinet it affects and states the consequence
  (AC-5.4, AC-6.5).
- Amounts are shown in the product's existing Tunisian dinar format; dates in the clinic's own day, so an end date
  reads the same in the console as it does to the clinic.

### FR-8: Bootstrap and recovery
- One command creates a console account and one deactivates it; there is no web path to either (AC-8.1, AC-8.5).
  The creating command is also where the second factor's enrolment secret is issued (AC-1.3).
- A signed-in console account may change **its own** password in the console, and nothing else about any account
  (AC-8.6).
- The companion feature's grant and report commands remain the fallback whenever the console is unavailable
  (AC-8.3).

---

## API Endpoints

All console endpoints are served on the console's own address only (FR-2) and require a console session with a
verified second factor.

### Sign in
```
POST /api/platform/auth/login
{ "email": "…", "password": "…", "totpCode": "123456" }

Response 200: { "token": "…", "expiresAt": "…" }
Response 401: { "error": "Identifiants invalides." }
Response 401: { "error": "Code de vérification requis.", "code": "totp_required" }
Response 403: { "error": "Ce compte doit d'abord enrôler son second facteur.", "code": "totp_enrolment_required" }
Response 429: { "error": "Trop de tentatives. Réessayez dans quelques minutes." }
```
⚠️ **The 403 carries nothing else** — no secret, no recovery codes, no session. The enrolment secret comes from
the bootstrap command (AC-1.3); returning it here would hand the second factor to whoever has the password, which
is the one thing it exists to prevent.

### Complete enrolment (once per account, after the command has issued the secret)
```
POST /api/platform/auth/totp/enrol
{ "email": "…", "password": "…", "totpCode": "123456" }        // code generated from the command's secret

Response 200: { "recoveryCodes": ["…"], … }                     // shown once, never retrievable again
Response 401: { "error": "Identifiants invalides." }
Response 400: { "error": "Code de vérification invalide." }     // nothing is bound
Response 409: { "error": "Le second facteur est déjà enrôlé pour ce compte." }
```

### Sign in with a recovery code
```
POST /api/platform/auth/recovery
{ "email": "…", "password": "…", "recoveryCode": "…" }

Response 200: { "token": "…", "expiresAt": "…", "recoveryCodesRemaining": 4 }
Response 401: { "error": "Identifiants invalides." }
```
The code is single-use and consumed on presentation (AC-1.3b).

### Change one's own password
```
POST /api/platform/auth/password        (console session required)
{ "currentPassword": "…", "newPassword": "…" }

Response 200: { }
Response 400: { "error": "…" }          // policy failure, in French
```
The only account action reachable over the web (AC-8.6). Creating and deactivating console accounts are commands.

### List cabinets
```
GET /api/platform/clinics?state=&expiringWithin=&dormant=&q=&sort=&page=&pageSize=

Response 200:
{
  "items": [
    {
      "clinicId": "…", "name": "…", "city": "…" | null, "createdAt": "…",
      "plan": "Cabinet", "state": "Trial" | "Active" | "Expired" | "Suspended",
      "endsOn": "2026-09-08" | null, "daysRemaining": 29 | null,   // same definition as the companion feature:
                                                                   // endsOn inclusive, daysRemaining 0 on the last working day
      "users": 3, "patients": 412, "appointments30d": 96,
      "writes7d": 41, "writes30d": 188, "activeDays30d": 22,
      "lastLoginAt": "…" | null, "lastWriteAt": "…" | null,
      "clinicCollectedThisMonthDt": 14320.000        // the CABINET's own turnover — see AC-2.1 / AC-7.4
    }
  ],
  "totalCount": 37,
  "countersAsOf": "2026-08-10T03:00:00Z"             // how fresh every activity figure above is (AC-2.8)
}
```
⚠️ Every activity figure (`patients`, `appointments30d`, `writes7d`, `writes30d`, `activeDays30d`,
`clinicCollectedThisMonthDt`) is read from the per-cabinet counters (FR-3), which is what lets `sort=activity` and
`dormant=true` apply to the portfolio rather than to the page already selected.

### Summary
```
GET /api/platform/summary

Response 200:
{ "inTrial": 6, "active": 24, "expiringWithin14Days": 3, "expired": 4, "suspended": 0,
  "vendorCollectedThisMonthDt": 3480.000 }        // the VENDOR's revenue — never a sum of the cabinets' own
```

### One cabinet
```
GET /api/platform/clinics/{clinicId}

Response 200:
{
  "clinic":  { … the list row … },
  "admin":   { "fullName": "…", "email": "…" },
  "trend":   [ { "month": "2026-03", "writes": 210, "appointments": 88 }, … ],   // 6 months
  "periods": [ … the payment ledger, incl. cancelled entries … ]
}
Response 404: unknown cabinet
```

### Record a payment period
```
POST /api/platform/clinics/{clinicId}/subscription
{ "months": 12 | null, "throughDay": "2027-08-09" | null,
  "plan": "Cabinet", "kind": "Paid" | "Complimentary",
  "amountDt": 2900.000 | null, "method": "Transfer" | null,
  "reference": "VIR-2026-08-114" | null, "note": null | "…",
  "idempotencyKey": "…" }

Response 200: { "state": "Active", "endsOn": "2027-08-09", "periodId": "…" }
Response 400: { "error": "La durée doit être positive." }
Response 404: unknown cabinet
```
⚠️ **No conflict response, deliberately.** The end date is derived from an append-only ledger, so two simultaneous
grants are two entries and not a corrupted state — the surplus one is cancelled (EC-6). `idempotencyKey` covers the
double-click, which is the only case a caller can actually act on.

### Cancel a payment period
```
POST /api/platform/clinics/{clinicId}/subscription/{periodId}/cancel
{ "reason": "Paiement crédité au mauvais cabinet." }

Response 200: { "state": "Expired", "endsOn": "2026-09-09" }
Response 400: { "error": "Le motif est obligatoire." }
```

### Suspend / unsuspend
```
POST /api/platform/clinics/{clinicId}/suspend      { "reason": "…" }
POST /api/platform/clinics/{clinicId}/unsuspend    { }

Response 200: { "state": "Suspended" | "Active", "endsOn": "…" | null }
Response 400: { "error": "Le motif est obligatoire." }
```

---

## Device & Interface Behaviour

**Leading device: the desk machine.** This is the vendor's own tool, used deliberately while going through
transfers, and reached over a tunnel — which in practice means a laptop. It is nevertheless held to the same floor
as the rest of the product, because the one thing genuinely done on a phone is *unlocking a cabinet that has just
paid* while away from the desk.

| Surface | Phone (< 640) | Tablet portrait (640–1023) | Desktop |
|---|---|---|---|
| **Cabinet list** (14 columns) | Card list — title = cabinet name, then state + end date, then activity (« 41 enreg. / 7 j »), then patients and staff. Filters in a sheet; the active filter is a removable chip. | ⚠️ Still a **card list**, not a table: 14 columns at this width, even with no rail, leaves nothing legible. | Table, sortable, paged. |
| **Summary** | Two-column grid of six figures, wrapping. Each figure links to the list filtered to what it counted. | Three columns. | One row. |
| **Cabinet detail** | Single column: state → activity → trend → payment history (card list) → actions. | Single column at a readable measure. | Two columns; trend and history side by side. |
| **Six-month trend** | Scrolls inside its own container; never widens the page. Its values are also given as text, so the chart is not the only way to read them. | Same. | Inline. |
| **Record-a-payment form** | Full-screen sheet, sized to the dynamic viewport, with the primary action pinned and still visible with the keyboard open. Dismissible by a visible control and by Escape; confirms before discarding typed input. | Same sheet. | Dialog. |
| **Confirmations** (cancel, suspend) | Bottom sheet naming the cabinet and the consequence. | Same. | Dialog. |

- **Touch paths:** row actions live in an explicit menu on every width. Nothing is revealed on hover only, and no
  affordance in this console is hover-reachable.
- **Named exceptions:** none. The list is a card list below its hinge rather than a horizontally scrolling table,
  which is the *same* capability presented differently.
- **Sign-in on a phone** must work fully, including entering the one-time code and, if it comes to it, a recovery
  code — it is the likeliest phone use. Enrolment (AC-1.3a) is a desk action and needs no phone layout.
- **Counter freshness** (AC-2.8) is stated once per screen, beside the figures it qualifies, on every width — not
  in a tooltip and not on desktop only.

---

## Scope

### In Scope
- Separate console accounts with mandatory second factor, lockout, deactivation and revocation.
- Serving the console on its own unpublished address, refusing anything else there and refusing it elsewhere.
- A paged, filterable, searchable cabinet list with subscription state beside real activity.
- A summary of the whole portfolio.
- One cabinet's detail: activity trend, payment ledger, administrator contact.
- Recording a payment period, cancelling one, suspending and unsuspending.
- Recording every console detail read and write, in the console's own append-only ledger.
- Per-cabinet activity counters maintained by a scheduled pass, and the freshness of those counters on screen.
- Commands to create and deactivate a console account and to recover a locked-out one; changing one's own console
  password from the console.
- French throughout; full device behaviour as specified.

### Out of Scope
- **Reading any clinical or per-patient data.** Not a limitation to be lifted later — it is the feature's premise
  (US-7).
- **Signing in as a clinic user (impersonation) for support.** Would defeat US-7 entirely; if support ever needs
  more than counts it gets its own spec, its own consent story and its own per-use record.
- **Online payment.** Payments are recorded, never taken. Same reasoning as the companion feature.
- **Issuing the vendor's own fiscal invoices to clinics.** That happens in the vendor's accounting.
- **Editing anything else about a cabinet** — its settings, its users, its catalogue, its records.
- **Deleting a cabinet or purging its data.** Deliberately absent: the companion feature keeps data indefinitely,
  and an irreversible delete of medical records does not belong behind a console button.
- **Per-clinic feature flags or plan-based gating.** Plans are price-only.
- **Notifying the clinic from the console** (e-mails, messages). The clinic is warned in-app by the companion
  feature; outreach is done by phone or WhatsApp by a person.
- **Usage analytics beyond the stated counts** — funnels, per-screen tracking, session recording.
- **Multiple vendor organisations / reseller hierarchies.**
- **A console screen that manages console accounts** — listing, creating, deactivating or resetting them. Those are
  commands (AC-8.1, AC-8.5); the only account action on the web is changing one's own password (AC-8.6).
- **Showing the clinic who from the vendor looked at its cabinet.** A defensible extension of US-7's promise, and
  deliberately a separate decision: it is a second read surface with its own audience, not a by-product of FR-5.

---

## Edge Cases

### EC-1: A leaked clinic administrator password
- **Scenario:** A clinic admin's credentials are compromised.
- **Expected:** They grant nothing here. A clinic session is refused by the console as unauthenticated, and the
  console's address is not reachable publicly in any case.

### EC-2: A leaked console password
- **Scenario:** A console password is compromised without the second factor.
- **Expected:** Sign-in fails. Reaching the console at all still requires the private network. And even a full
  compromise cannot read a patient record (US-7), which bounds the damage to business metrics and the ability to
  grant free subscriptions — both recoverable and both recorded.
- **Including an account that has never signed in:** the password alone yields no enrolment secret and no recovery
  codes, so the attacker cannot enrol their own authenticator and get in ahead of the legitimate holder
  (AC-1.3/AC-1.3a). This is the case the naive first-sign-in enrolment flow gets wrong, and it is the account most
  likely to be attacked — the freshly bootstrapped one.

### EC-3: The second factor is lost
- **Scenario:** The vendor's phone is replaced and the authenticator is gone.
- **Expected:** A recovery code gets them in; failing that, the bootstrap command resets the account. And in the
  meantime the companion feature's grant command still unlocks paying clinics, so no customer waits on it.

### EC-4: The console and clinic addresses are misconfigured to collide
- **Scenario:** The console is configured on the port the clinic app already uses.
- **Expected:** The deployment **refuses to start** with a message naming both settings. Starting would make either
  the console or the whole product unreachable, silently.

### EC-5: Double-click on « Enregistrer le paiement »
- **Scenario:** The vendor clicks twice on a slow connection.
- **Expected:** One entry. The control is disabled in flight, and a repeated submission of the same action is
  recognised as the same action.

### EC-6: Two console accounts grant the same cabinet at once
- **Scenario:** Two people record the same transfer simultaneously.
- **Expected:** **Both succeed, and that is correct.** The end date is derived from an append-only ledger, so two
  grants are two entries; there is no half-applied state to protect against. The surplus entry is cancelled with a
  reason (US-5) and the end date recomputes. Neither caller is shown a conflict they cannot act on. What is
  prevented is the *same* action landing twice — a double-click — which idempotency handles (EC-5).

### EC-7: A cancellation puts a working cabinet back into read-only
- **Scenario:** The vendor cancels a grant recorded three weeks ago; the cabinet has been working since.
- **Expected:** The confirmation states, before committing, that the cabinet will become read-only and from which
  date. After committing, the cabinet's own banner appears without anybody signing out.

### EC-8: A cabinet with no activity at all
- **Scenario:** A cabinet signed up, verified, and never returned.
- **Expected:** It appears with zeros, a « dormant » marker and its last sign-in — **not** omitted and not shown as
  a blank row. A zero here is the most useful figure in the list.

### EC-9: A cluster of same-day trials with no activity
- **Scenario:** Someone creates eight cabinets in a day to farm trials.
- **Expected:** Visible as such — same-day creations with near-zero saves — so the vendor can act. The console makes
  it observable; it does not block signup, which stays open.

### EC-10: The activity measure is polluted by machine work — or by the vendor
- **Scenario:** Scheduled backups and reminder dispatch run hourly for every cabinet. Separately, the vendor
  records a payment against a cabinet that has recorded nothing in six weeks.
- **Expected:** Both are excluded from every activity figure. A dormant cabinet must never read as active because
  the software touched it, **nor because the vendor did** — the second is the more misleading of the two, since it
  lands on exactly the cabinet the « dormant » filter is meant to surface, and on the day it was surfaced.

### EC-11: A very large portfolio
- **Scenario:** Several hundred cabinets, one of which has been recording for four years.
- **Expected:** The list stays paged and responsive, and the summary stays one bounded read. Neither grows into
  per-cabinet work performed on every page load, **and neither grows with any one cabinet's history** — reading
  the mutation record live would make the console's cost a function of the busiest practice's past rather than of
  the number of practices. The counters of FR-3 are what make this true rather than hoped for.

### EC-12: The console is opened while the clinic-facing app is down
- **Scenario:** The clinic app or its database is unhealthy.
- **Expected:** The console reports that it cannot read rather than showing an empty portfolio — « zéro cabinet » and
  « je n'ai pas pu lire » must never look the same.
- **The same distinction applies one level down:** a console read that returns rows *because* it declared itself
  cross-cabinet, and one that returns nothing because it failed to, are indistinguishable on screen — every figure
  simply reads zero. « Aucune activité » is a real and useful answer (EC-8), so the console must not be able to
  produce it by accident: an unscoped read is a fault, not an empty portfolio.

### EC-15: The counter pass has not run
- **Scenario:** The scheduled pass fails or has not run since the deployment.
- **Expected:** The screen says how stale the figures are (AC-2.8), and a portfolio whose counters were never
  written is reported as such — not as a portfolio of dormant cabinets, which would send the vendor to phone every
  customer they have.

### EC-13: A cabinet is deleted or disappears between list and detail
- **Scenario:** The vendor opens a detail from a stale list.
- **Expected:** A clear French « ce cabinet n'existe plus » state with a way back to the list — not an error page.

### EC-14: A grant for a cabinet with no end date
- **Scenario:** The vendor records a payment against a grandfathered, never-expiring cabinet.
- **Expected:** Allowed and recorded (it is a real payment), and the cabinet still has no end date. The console says
  so explicitly rather than displaying a computed date.

---

## Non-Functional Hints

- **Security:** this is the highest-privilege surface in the product. Three independent layers are expected to hold
  — the private address, the separate token population, and the second factor — such that no single one failing
  exposes it. Nothing here may become a way to reach clinical data, and that must be enforced by structure and
  checked automatically rather than reviewed by eye.
- **Privacy:** the counts-only guarantee is a statement the vendor can make to customers, so it must be true by
  construction. Console reads are recorded because « who looked » is a question this surface will be asked.
- **Performance:** the list and the summary must each be a bounded read, not a per-cabinet loop; they are consulted
  several times a day. Bounded by the **number of cabinets**, and specifically not by any cabinet's accumulated
  history — which is what the counters of FR-3 buy, and what deriving the figures per request would cost.
- **Accessibility:** every figure that is also a link has an accessible name saying what it filters to; state is
  conveyed by text and shape, **never by colour alone** (« Expiré » and « Suspendu » must be distinguishable in
  greyscale, and they are different things); the trend's values are available as text as well as as a chart; the
  one-time-code field is reachable and labelled, and pasting a code works.
- **Scalability:** activity figures originate in records that already accumulate, but they are **stored as
  counters** rather than recomputed (FR-3) — so a retention policy later applied to the underlying record trims
  what can be recomputed, not the console's own history. That was the trade-off this hint warned about, and
  storing the counters is how it is made deliberately rather than discovered.

---

## Dependencies

- **`features/clinic-subscription/`** — hard dependency. Defines the entitlement, the ledger, the arithmetic and the
  read-only behaviour this console operates on; must ship first.
- **The hosted multi-tenant deployment** (`features/multi-tenant-cloud/`) — the only deployment this exists on, and
  the source of the capability that decides it.
- **The product's record of mutations** — the source the activity counters are derived from (FR-3), read by the
  scheduled pass and never by a console request.
- **The at-rest encryption used for per-clinic secrets** — protects the second-factor secrets, which makes its
  durable storage load-bearing for *sign-in*, not only for reminders. ⚠️ On the hosted deployment that key ring is
  already required to be configured and durably mounted; losing it previously made reminder channels report
  « non configuré », and now additionally makes **every console account unable to sign in**, recoverable only by
  the bootstrap command.
- **The private-address serving pattern already used for the LAN device-trust page** — the same shape: one address
  serving one bounded set of paths. ⚠️ **Its direction is not the same**: that gate is one-way by design (its paths
  stay reachable on the front door too), while the console needs both directions (FR-2). And it gates *endpoints*;
  the console additionally has **pages**, which on the hosted deployment are served by a different process than the
  API — so the pattern covers half the surface and the other half is new work.
- **The companion feature's subscription re-read** (its FR-15) — how a clinic actually learns of a console write
  (AC-4.4). Not a dependency this feature may replace: that spec chose a re-read over a broadcast because the
  vendor's *command* and a midnight lapse can push nothing, and a console-only push would leave those two cases
  uncovered.
- **The product's live-refresh signal** — optional here (AC-4.4a), and if used, addressed to the *target* cabinet.
  Its default audience (the caller's own clinic) and its default naming (derived from where the code lives) are
  both wrong for a console write; the first reaches nobody and the second fails the contract check that holds the
  server's signals and the client's subscriptions equal.
- **The deployment's write-refusal on lapsed entitlements** (companion FR-3) — the console's routes must be on its
  allow-list, or the endpoint that unlocks a practice is refused by the lapse it exists to clear (FR-4).
- **A time-based one-time-password capability**, which the product does not have today; this feature introduces it.

## Open Questions

> None of the below blocks planning or implementation. The first two are operational (who holds an account, how the
> private address is reached) and belong in the operator runbook; the last two are threshold values the spec already
> states a default for.

- [ ] How many console accounts should exist initially, and who holds them?
- [ ] How will the console be reached in practice — SSH tunnel, VPN, or a bastion? This decides the operator
      runbook, not the software.
- [ ] Should the six-month trend be per-month saves only, or saves plus appointments plus collected? The spec
      assumes saves and appointments.
- [ ] Is a « dormant » threshold of 30 days with no saves the right churn signal, or should it be 14?
- [ ] How often should the activity-counter pass run — nightly, or more than once a day? The spec assumes **daily**
      and requires the freshness to be on screen either way (AC-2.8), so this is a value, not a structure.

## Screenshots

None. No browser exploration was performed — see Deviations.

---

## Deviations from `/define-feature`

- **No parallel exploration agents** and **no browser exploration** — same reasons recorded in the companion spec:
  the relevant areas were explored directly against the source in the session that produced these decisions, and
  this repository has no browser tooling.
- **Questions were asked in batches** rather than strictly one at a time, the decisions having been settled in the
  preceding design session.
- **This spec is the second half of a deliberate split** from `features/clinic-subscription/`, on the skill's own
  signals: a different user (the vendor rather than clinic staff), an independent workflow, and a materially
  different security posture.
