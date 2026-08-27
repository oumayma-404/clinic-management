# Feature Specification: Abonnement du cabinet (essai gratuit puis abonnement payant)

**Status:** APPROVED
**Challenged:** Yes
**Created:** 2026-08-10
**Approved:** 2026-08-10
**Feature:** On the hosted deployment, a clinic gets 30 free days and must then be subscribed; an expired clinic keeps full read and export access but cannot record new work.

> **Companion feature:** [`features/platform-console/spec.md`](../platform-console/spec.md) is the vendor's back-office
> that replaces this spec's console verbs as the day-to-day unlocking tool. It **depends on** this one. This spec is
> deliverable and useful on its own; that one is not.

---

## Overview

Anybody who has the URL of the hosted deployment can create their own cabinet and use every part of the product,
for ever, free. That door — public self-signup behind an emailed single-use link — is deliberate and stays: the
easiest possible arrival is the point. What is missing is anything that turns an arrival into a customer.

This feature makes a clinic's right to **record new work** an explicit, dated entitlement. A new cabinet gets **30
free days** with no card and no friction. After that, somebody at the practice pays — by bank transfer, D17 or cash,
because no Tunisian payment rail can auto-charge a card monthly — and the vendor records that payment, which extends
the entitlement. A clinic past its date is **read-only**: the agenda, every patient file, every allergy, every
invoice and every CSV export still work exactly as before, and only writing is refused, with a French explanation
naming the date and how to pay.

Read-only rather than locked-out is the load-bearing product decision. A dentist with a patient in the chair must
never be unable to read that patient's allergies over an accounting matter, and a clinic must always be able to
leave with its own medical records. The commercial pressure comes from being unable to *work*, not from being
unable to *look*.

---

## User Stories

### US-1: A new cabinet starts on a free trial
As a **dentist creating a cabinet**, I want 30 free days without giving payment details, so that I can find out
whether the product suits my practice before paying anything.

**Acceptance Criteria:**
- **AC-1.1:** A cabinet created through public self-signup has an entitlement running to the end of its **30th
  clinic-local day**, where **the day of creation is day 1**. A cabinet created on 10 August ends on
  **8 September** and may work all of that day.
- **AC-1.2:** **No door may create a cabinet without an entitlement.** On the hosted deployment every door
  produces a **30-day trial** — public self-signup, the vendor's provisioning command, and any other. On the two
  deployment kinds that do not enforce subscriptions, the entitlement created is **open-ended** (no end date)
  rather than a trial, so a self-hosted install never carries an entitlement that silently expires behind an
  enforcement that will never read it.
- **AC-1.2a:** There are **two** places in the product where a cabinet is constructed — the shared provisioning
  helper used by self-hosted first run, the vendor's command and signup verification, and the Auth0 branch that
  builds its own. **Both** must produce an entitlement. Wiring only the shared helper is this repository's
  documented recurring defect shape, and here its symptom would be a whole deployment kind of cabinets that
  FR-13 declares impossible.
- **AC-1.3:** The signup form and the verification e-mail both state « 30 jours d'essai gratuit, sans carte
  bancaire » before the visitor submits anything.
- **AC-1.4:** During the trial every capability of the product is available. A trial is not a reduced product.
- **AC-1.5:** Changing the configured trial length later does **not** move the end date of any cabinet that already
  exists.

### US-2: The clinic can see where it stands and how to pay
As a **clinic owner**, I want to see how many days I have left and exactly how to pay, so that paying is never
blocked on not knowing what to do.

**Acceptance Criteria:**
- **AC-2.1:** An « Abonnement » screen shows the state (« Essai gratuit » / « Actif » / « Expiré » / « Suspendu »),
  the end date, the days remaining, the plan, the price, the payment instructions and the vendor's contact details.
- **AC-2.2:** The screen is reachable by **every** role, including a secretary — the person who meets a refused save
  is often not the person who pays, and she must be able to read why. It sits with the other clinic-administration
  destinations but **outside their admin-only grouping**.
  ⚠️ This is a **deliberate exception** to the product's existing rule that a secretary sees no clinic-wide money
  screen. The amounts here are what the practice owes its software vendor, not clinic revenue — none of it appears
  in la caisse or any patient's balance (FR-2) — and EC-10 depends on reception being able to open it. What stays
  admin-only is the **payment history** (AC-2.3), not the screen.
- **AC-2.3:** An administrator additionally sees the history of what the cabinet has paid: date, period covered,
  amount, method and reference. A corrected entry is shown struck through with its reason.
- **AC-2.4:** The price and payment instructions are set per deployment, not compiled in — changing a price is not
  a code change.
- **AC-2.5:** When the entitlement has no end date (a grandfathered or complimentary cabinet), the screen says so
  in words rather than showing a far-future date.

### US-3: The clinic is warned before it stops being able to work
As a **clinic owner**, I want to be told before my access changes, so that expiry is never a surprise discovered
mid-consultation.

**Acceptance Criteria:**
- **AC-3.1:** From **7 days** before the end date, a banner appears on every screen of the app stating the state
  and the date, with a link to the « Abonnement » screen.
- **AC-3.2:** While the entitlement is still valid the banner is dismissible, and it returns the next clinic day.
  Dismissal is **per browser** and lasts until that day turns over; it is deliberately **not** recorded on the
  server, so it needs no write and keeps working unchanged once the cabinet expires. Dismissing on the chairside
  tablet therefore leaves it showing at reception, which is right here: this is a recurring countdown aimed at
  whoever is looking, not a one-time announcement to be acknowledged once for the practice.
- **AC-3.3:** Once the entitlement has ended the banner is **not** dismissible.
- **AC-3.4:** A notification appears in the staff notification centre at **7, 3 and 1 day(s)** before the end date
  and again on the day it ends, deep-linking to « Abonnement ». That is **four distinct notifications** over the
  life of a warning, each one genuinely new so it badges the bell.
- **AC-3.5:** The warning is re-evaluated daily rather than fired once, and a threshold already crossed produces
  **no** second row — so the daily pass never yields a fifth notification, however many days the countdown runs.
  ⚠️ Four rows rather than one restated row is deliberate, and it is the opposite of what the product's existing
  stale-backup and expiring-stock alerts do. Those reuse a single row and reword it — but rewording **does not
  clear who has read it**, so once the owner has read « 7 jours », the « 3 jours », « 1 jour » and « expiré »
  restatements would stay read and never badge the bell again. One row would make AC-3.4's last three warnings
  invisible to exactly the person who is paying attention.
- **AC-3.6:** The notification never reaches a locked phone as a push banner. An accounting reminder is not
  time-critical to a person, and spending the operating system's notification permission on one risks losing the
  five categories that are.
- **AC-3.7:** Warnings are addressed to the whole practice, not only the administrator — the more likely the owner
  hears about it, the better.

### US-4: An expired cabinet keeps its records and loses only recording
As a **dentist whose subscription has lapsed**, I want to still open my patients' files, so that a payment matter
never becomes a clinical risk.

**Acceptance Criteria:**
- **AC-4.1:** With the entitlement ended, **every read** succeeds exactly as before: the agenda, the patient list,
  every patient tab, the odontogram, invoices, devis, la caisse, the dashboard, documents, files and PDFs.
- **AC-4.2:** **Every CSV export** succeeds. A clinic can always leave with its own data.
- **AC-4.3:** Printing or downloading a document the cabinet already holds succeeds.
- **AC-4.4:** Any attempt to **create, modify or delete** anything is refused with a French message naming the end
  date and pointing at « Abonnement ».
- **AC-4.5:** The refusal is machine-recognisable, so the app can raise the banner rather than showing a bare
  error, and never signs the user out.
- **AC-4.6:** A refused save leaves the form open with the typed input intact, so nothing the user typed is lost.
- **AC-4.7:** Signing in still works. Changing a password still works, including a password change the
  administrator has forced — otherwise an expired cabinet whose user must change their password can do neither.
- **AC-4.8:** Reading « Abonnement » still works. The one screen that says how to pay must be reachable by exactly
  the cabinets that have not paid.
- **AC-4.9:** Requests that look like writes but only compute or preview — a CNAM reimbursement estimate, a CSV
  import dry run, rendering a document for download — still succeed. The **AI assistant does not**: it can book
  and cancel appointments, so it is refused like any other way of recording work (FR-3).
- **AC-4.11:** Screens the user experiences as reading keep working even where they issue a write to do so —
  opening a patient's Files tab, signing in on a mobile shell, and clearing the notification bell. FR-3 names
  these explicitly; a refusal there would present as a broken product rather than as an unpaid subscription.
- **AC-4.10:** Nothing about the refusal is silent. There is no case where a save appears to succeed and does not.

### US-5: The vendor unlocks a cabinet that has paid
As the **vendor**, I want to record a received payment and extend a cabinet's entitlement, so that a clinic that
pays is working again within minutes.

**Acceptance Criteria:**
- **AC-5.1:** A command records a payment against one cabinet — identified by its id **or** by its administrator's
  e-mail address — with a duration in **whole months** (or an explicit end date), and optionally a plan, an
  amount, a method (virement / espèces / chèque / carte), a reference and a note.
- **AC-5.2:** The new end date is computed from **whichever is later**, the current end date or today, plus the
  duration. A cabinet that pays before expiring never loses the remainder.
- **AC-5.3:** Each payment is recorded as its own entry. Entries accumulate; nothing overwrites an earlier one.
- **AC-5.4:** The entitlement's end date is always **derived from the entries** — a fold over the non-cancelled
  ones in the order they were recorded — so correcting **any** of them corrects the date, not only the most
  recent (FR-2).
- **AC-5.5:** A mistaken entry is **cancelled with a written reason**, never edited or deleted; it stays visible,
  struck through, and the end date recomputes — possibly back into the past.
- **AC-5.6:** Every grant and every cancellation records who did it and when, and appears in the cabinet's activity
  journal.
- **AC-5.7:** Recording a grant is refused for a non-positive duration, and for a cabinet that does not exist, with
  a message naming which.
- **AC-5.8:** The cabinet's own app reflects a grant **without anybody signing out or restarting** — the banner and
  the « Abonnement » screen update on their own, within a stated delay (FR-15).
- **AC-5.9:** A read-only report lists cabinets by state — expiring within N days, and already expired — and can be
  run on a schedule as a safety net.

### US-6: Cabinets that already exist are not disturbed
As the **vendor**, I want the change to affect no existing cabinet, so that introducing subscriptions never locks a
current user out of their practice.

**Acceptance Criteria:**
- **AC-6.1:** Every cabinet existing before this feature is deployed receives an entitlement with **no end date**.
- **AC-6.2:** Each of those cabinets carries one entry recording that it was grandfathered and why, so its state is
  explained rather than mysterious.
- **AC-6.3:** No existing cabinet sees a banner, receives a warning notification, or has any request refused as a
  result of the deployment.
- **AC-6.4:** It is verifiable after deployment that **no** cabinet exists without an entitlement, and that the
  number of grandfathered entries equals the number of cabinets that existed — as **named checks in the product's
  existing schema-verification command**, which is where every other backfill in this product is asserted and the
  only gate a schema change has anywhere here. A backfill is the one class of change no test can see: it can
  cover zero rows and every suite still passes.

### US-7: Only the hosted deployment is affected
As the **owner of a self-hosted install**, I want none of this to apply to me, so that software running on my own
PC never refuses to work over a subscription.

**Acceptance Criteria:**
- **AC-7.1:** On a self-hosted (own-PC / LAN) install, nothing is enforced, no banner appears, no warning is
  produced and the « Abonnement » screen is absent. Its cabinets carry an **open-ended** entitlement (AC-1.2)
  that nothing reads — present so that FR-13 holds everywhere, never able to expire.
- **AC-7.2:** The same is true of the Auth0-based browser deployment.
- **AC-7.3:** Whether enforcement applies is decided by the deployment's **kind**. No configuration setting can
  turn it on or off within a kind — otherwise a mistyped setting would either lock a practice out or give the
  product away.

---

## Functional Requirements

### FR-1: What an entitlement is
- One entitlement per cabinet, carrying a **plan**, an **end date** and a **suspension flag**.
- The end date is an **inclusive calendar day in the clinic's own timezone** (Tunisia, UTC+1). A cabinet whose end
  date is today may work all of today.
- **Days remaining is the end date minus the clinic's today, in days.** It is **0 on the last day the cabinet can
  still work**, and negative days are never shown — past the end date the state is `Expiré` and the count is not
  the thing being said. The four warning thresholds of AC-3.4 are stated against this same figure: 7, 3, 1 and 0.
  ⚠️ Inclusive-or-not is not a presentation detail here. It decides whether « il vous reste 1 jour » lands on the
  final working day or the day after it, and whether « 30 jours d'essai gratuit » delivers 30 days or 31.
- **No end date means no expiry.** This is a real state (grandfathered, complimentary), not a placeholder date.
- The end date is **derived** from the cabinet's non-cancelled payment entries, never accumulated independently.
- Derived state, computed and never stored: `Essai` (only trial entries so far, still valid) · `Actif` (a paid or
  open-ended entitlement, still valid) · `Expiré` (past the end date) · `Suspendu`.
- One rule computes that state, and it is the same rule the refusal, the banner, the warning job, the report and
  the vendor's console all read. There is exactly one answer to « may this cabinet write? ».

### FR-2: Payment entries
- Each entry records: **a duration**, its kind (essai / payé / antériorité / offert), an optional amount, an
  optional method, an optional reference, an optional note, who recorded it, and when.
- Amount, method and reference are optional because a trial and a grandfathered period have none.
- An entry may be **cancelled** with a mandatory reason, recording who and when. It is never removed.
- **An entry stores a duration, not a fixed window, and the end date is a fold over the non-cancelled entries in
  the order they were recorded** — each applying AC-5.2's later-of-current-end-or-today rule in turn. The period
  an entry « covers » is **derived for display** and recomputes with everything else.
  ⚠️ **This is the difference between AC-5.4 being true and being true only sometimes.** With absolute stored
  windows, cancelling anything but the *latest* entry changes no date: a cabinet with a mis-keyed 12-month grant
  followed by a correct one keeps all 24 months after the wrong entry is cancelled, because the later window's
  end is still the maximum. Folding durations removes exactly the cancelled entry's days wherever it sits.
- A duration is a **whole number of months**, clamped to the last day of the target month — 31 January + 1 month
  ends 28 (or 29) February, never spilling into March. An explicit end date is the alternative form.
- Amounts are money in the product's existing convention (Tunisian dinars to the millime).
- **These amounts are the vendor's revenue and never the clinic's.** They must not appear in la caisse, l'extrait de
  caisse, « Créances », the dashboard's Argent section, or any patient's balance.

### FR-3: Refusing a write
- Applies only where the deployment's kind requires a subscription (FR-11).
- Applies to requests that **record new work for the practice**. Reads are never refused.
- Refusal carries HTTP **402**, the canonical `{ error }` body used everywhere in this product, and a
  machine-readable code so the app can react without matching French prose.
- The French message names the end date and directs the reader to « Abonnement ».
- A cabinet with **no** entitlement at all is refused (a state FR-13 makes impossible), with a **different** code
  from the ordinary expiry so it reads as a fault to fix rather than as « you have not paid ».
- A **suspended** cabinet is refused identically, with its own message naming suspension rather than a date.

⚠️ **The gate cannot be « refuse POST, PUT and DELETE ».** That rule is wrong in both directions in this codebase,
and each direction has a real instance:
- **A GET can write.** The Google OAuth **callback** persists a refresh token onto the cabinet. It needs no
  exemption in practice — the request that *starts* the flow is refused, so the callback is unreachable on an
  expired cabinet — but the rule that produces that answer must be « what does this do », not « what verb is it ».
- **A POST can be a read.** Three are pure computation and are allowed (below).
- **And a POST can be a write wearing a read's clothes.** The **AI chat** looks like a compute-only request and is
  the one thing in this list that must be **refused**: its action set includes booking and cancelling
  appointments, so exempting it would be a booking bypass on an expired cabinet — the single defect this whole
  feature exists to prevent. It is named here so that nobody reaches the opposite conclusion from its shape.

**Always allowed regardless of entitlement.** This set is **explicit and fixed** — adding to it or removing from
it is a deliberate act, never a side effect — and it is stated as *what*, so that a route rename cannot silently
change it:

| Allowed | Why |
|---|---|
| Signing in, refreshing a session, **changing a password** | AC-4.7. Refusing these strands an account in both directions. |
| Reading the subscription state and its history | AC-4.8. The one screen that says how to pay. |
| Deployment-metadata reads, the health check | Not clinic work; the health check is anonymous. |
| The three **compute-only** requests of AC-4.9 | A CNAM reimbursement estimate, a CSV-import **dry run**, and rendering a document for immediate download. None persists anything. |
| **Marking an in-app notification read**, singly or all | Otherwise the bell badge is permanently lit and **the very expiry notification of AC-3.4 can never be dismissed** — the product nagging about a payment while refusing to let anyone acknowledge it. |
| **Registering and deregistering a device's push token** | Fired at every sign-in and sign-out of the mobile shells. Refusing it breaks AC-4.7's « signing in still works » on the chairside device specifically. |
| **Creating a patient's default file folders** | Fired on first visit to a patient's Files tab. Refusing it makes a **read** fail, contradicting AC-4.1. |
| **The signed-in user's own dashboard layout preference** | Personal interface state, not the practice's record. |
| **Running a backup on demand** | The same argument as AC-4.2's exports — a clinic can always get its own data out — and FR-8 already keeps the *scheduled* backup running, so refusing the manual one would be inconsistent with the automatic one beside it. |
| **Activating or deactivating a colleague's account** | Offboarding and security. Refusing it would mean a departed employee keeps their access until the practice pays, which is a worse outcome than the unpaid invoice. |
| **Every write made by the vendor's console** (`features/platform-console/`) — recording a period, cancelling one, suspending, unsuspending | The whole of it. These are the actions that **clear** a lapse, performed against precisely the cabinets that have one, so refusing them makes unlocking a paying practice impossible by the very state it is being paid to end. |

⚠️ Three of the last four are the load-bearing ones, and they share a shape worth naming: **each is a write issued
by a screen the user experiences as reading.** A gate written from the list of things a dentist *does* will miss
all three, and each failure presents as « an expired cabinet cannot open a patient / cannot sign in on the tablet /
cannot clear its notifications » — none of which reads as a subscription matter to the person meeting it.

⚠️ **The console row fails differently, and worse.** It is not a write disguised as a read — it is a write by
somebody who is not the cabinet at all. A console account has no clinic of its own, so a gate that resolves « the
caller's entitlement » finds none and refuses under the *missing-entitlement* code (FR-3's fault case), which reads
as a defect rather than as a lapse and points nobody at the real cause. The refusal then lands on the one endpoint
whose purpose is to end the refusal. The console spec states the same exemption from its side
(`features/platform-console/` FR-4), deliberately: it is the kind of coupling that is discovered on a real expired
cabinet, at the moment a customer has just paid.

### FR-4: Trial creation
- Creating a cabinet and creating its entitlement are **one indivisible operation**. A cabinet cannot come into
  existence without one, at **either** of the two places a cabinet is constructed (AC-1.2a).
- Where the deployment enforces subscriptions, the entitlement is a **trial**; where it does not, it is
  **open-ended**. Both satisfy FR-13; neither leaves a cabinet unaccounted for.
- The trial length is read from configuration **only at the moment of creation**; the stored end date is
  authoritative from then on.
- Default trial length: **30 days**, counted with the creation day as day 1 (AC-1.1).

### FR-5: Warnings
- Evaluated **once a day**, per cabinet, on the hosted deployment only.
- Produces a staff notification the first time the cabinet crosses each of the **four thresholds** — 7 days, 3
  days, 1 day, and the end date itself — and withdraws any outstanding warning once the entitlement is extended.
- Re-evaluation must not create a second notification for a threshold already crossed. Deduplication is therefore
  per **(cabinet, threshold)**, not per cabinet: the daily pass is idempotent within a threshold while still
  producing a genuinely new, unread notification when the next one is reached.
- An extension that moves the end date **beyond** the warning window withdraws the outstanding warnings and
  **re-arms** the thresholds, so a cabinet that renews and later approaches expiry again is warned again.
- In-app only — never an operating-system push banner (AC-3.6).
- Not dependent on internet reachability: the warning is in-app, so it must work regardless.

### FR-6: Granting and reporting (vendor)
- Four commands, run by the vendor with direct access to the deployment: **grant** (records an FR-2 entry and
  extends per AC-5.2), **cancel** (voids an entry with a mandatory reason, AC-5.5), **suspend / unsuspend**
  (FR-7) and **report** (list cabinets by state).
- All four exist here rather than waiting for the companion console, because this spec ships a cancellable ledger
  and a suspension flag: without the verbs behind them, a mis-keyed grant would be uncorrectable (EC-4) and a
  suspension unreachable, on a feature that is meant to be deliverable on its own.
- The report exits with distinguishable outcomes for « nothing to do », « could not run » and « found cabinets
  expiring or expired », so it can be scheduled.
- Neither command is reachable over HTTP. Granting oneself a subscription must have no web-facing path.
- Both remain available after the vendor's console exists, as the recovery path for when the console is
  unreachable or its account is locked out.

### FR-7: Suspension
- A cabinet may be suspended with a mandatory written reason, and unsuspended.
- Suspension is for abuse or fraud. **Non-payment is not suspension** — non-payment is simply the absence of a
  grant, and it expresses itself as expiry.
- A suspended cabinet is read-only exactly as an expired one is, and its « Abonnement » screen says it is
  suspended, not expired.

### FR-8: Background work for an expired cabinet
- **SMS and WhatsApp reminders stop**, and **operating-system push stops**. Queued reminders are **parked with a
  stated reason rather than discarded**, and resume automatically once the cabinet is extended.
  ⚠️ **Parking is two halves and shipping one of them sends every parked reminder within a minute.** The outbox
  already has the right mechanism — a non-terminal parked status that survives the purge and carries a reason —
  but the pass that returns rows to the queue asks only whether the *channel* can send: is there a sender, is the
  channel enabled for this clinic, are its credentials present. A row parked for expiry passes all three, so it
  would be un-parked and dispatched on the next tick, on a cabinet that has not paid. Parking for expiry
  therefore requires **a machine-readable reason on the row** (today there is only a French sentence, which the
  review cannot interrogate) **and** a matching clinic-state term in the review. The **push queue has the
  identical shape and the identical gap**, and needs both halves too.
- Parking must not be replaced by « filter expired cabinets out of the dispatch scan ». That recreates the
  starvation defect the parked status was invented to fix: unsendable rows accumulate at the front of an
  oldest-first, capped scan and consume every tick for ever.
- **Scheduled backups keep running.** An unbacked-up medical record is a liability regardless of who has paid, and
  it is the one consequence a clinic cannot undo by paying late.
- The daily approaching-expiry stock alert keeps running (it is in-app and free to run).
- Nothing in this requirement changes what the clinic can *read*.

### FR-9: Grandfathering existing cabinets
- On deployment, every existing cabinet receives an open-ended entitlement plus one explanatory entry (US-6).
- Verifiable afterwards by counting, through the schema-verification command (AC-6.4): cabinets without an
  entitlement must be **zero**, and grandfathered entries must equal the pre-deployment cabinet count.
- Run before and after the deployment and diff, as that command's existing workflow prescribes. It never mutates.

### FR-10: Plans
- A plan is a **label and a price, and gates nothing**. Every plan gets every capability.
- Plan names, prices and payment instructions come from per-deployment configuration.
- The tiers are the ones the public Tarifs page sells: **Cabinet**, **Clinique**, **Sur-mesure**. A price carries a
  **monthly and an annual figure**, because the page already publishes both (120 → 100 DT/mois facturé
  annuellement; 290 → 242). `3 900 DT` is **not a plan** — it is the one-off self-hosted licence, sold « sans
  abonnement » and already outside this feature by US-7.
- Seat limits and per-module gating are **out of scope** (see Scope). The entitlement answers one question.
- ⚠️ **The Tarifs page nevertheless advertises differences this feature does not enforce**: a practitioner cap
  (« 1 praticien + 1 secrétaire » on Cabinet, « jusqu'à 5 praticiens, puis 45 DT par praticien » on Clinique) and a
  module split (Cabinet excludes rappels SMS/WhatsApp and El Fatoora; Clinique adds relances, salle d'attente,
  laboratoire, stock, agenda Google and l'assistant). Those are **enforced commercially by the vendor, not by the
  software** — the plan recorded on the entitlement is the label the vendor sells against and honours by hand.
  This is a deliberate, stated divergence, not an oversight: making it real is the follow-on feature named in Scope.

### FR-11: Deployment scope
- Enforcement, warnings, the « Abonnement » screen and the vendor commands exist **only** on the hosted
  multi-tenant deployment, decided by the deployment's kind and by nothing an operator can set.
- The two other deployment kinds behave exactly as they do today, byte for byte.

### FR-12: Accountability
- Every change to an entitlement — trial creation, grant, cancellation, suspension — appears in the cabinet's
  activity journal with an actor and a moment, through the mechanism that already records every other mutation.
- A grant recorded by the vendor is attributed to the vendor, distinguishably from a clinic user.
- ⚠️ **The entitlement and its ledger entries must be aggregate roots, or the journal records nothing at all.**
  The interceptor that writes the journal only sees entities of that kind. The obvious template for a per-clinic
  settings record in this product is deliberately **not** one — copying its shape would satisfy every other
  requirement here while making this one silently false, with no error anywhere. An accountability requirement
  that fails silently is the worst possible shape for one, which is why it is pinned here rather than left to the
  data model.
- ⚠️ **Attribution granularity is honest about its limit.** A grant made by a vendor **command** is attributed to
  that command, which is already distinguishable from any clinic user. It is not attributable to a **named human**
  at the vendor, because a command carries no person's identity. Per-person attribution arrives with the companion
  console's own accounts (its AC-4.7), and this feature does not pretend to it.

### FR-13: No silent absence
- It must be impossible for a cabinet to exist without an entitlement: guaranteed at **both** creation sites
  (FR-4, AC-1.2a), covered for history (FR-9), refused if it somehow happens (FR-3), and checkable after any
  schema change (AC-6.4).
- The check is a **derived count over every cabinet**, never a hand-maintained list of the doors known to create
  one — a new door is exactly the case it exists to catch.

### FR-14: Data retention
- **Nothing is ever deleted automatically**, however long a cabinet stays expired. Its records remain intact,
  readable and exportable indefinitely.
- Any deletion is a deliberate, written, out-of-band act. There is no retention timer in this feature.

### FR-15: How the app learns the state changed
- The banner and the « Abonnement » screen are a function of a **re-read of the subscription**, not of a pushed
  event. The app re-reads it: on a **periodic interval** while a warning or an expiry is in force, on **window
  focus**, and **immediately on any 402**. Between those, a state up to one interval old is acceptable and the
  spec says so rather than implying instantaneity.
- ⚠️ **This is a re-read rather than a broadcast because neither moment that changes the state can push one.**
  A grant made by the vendor's command runs in a **separate process** from the API that holds the open
  connections, and its container does not even resolve the realtime notifier; the pipeline behaviour that
  broadcasts for ordinary commands derives the clinic from the **caller's token**, which a vendor command has
  none of. And an entitlement ending at midnight (EC-1) has **no actor at all** — no request, no command, no job
  writes anything — so there is nothing to broadcast *from*. A re-read is the only mechanism that covers both,
  and it works identically whoever granted.
- A push, where one is genuinely available, is a **later optimisation and never the mechanism the behaviour
  depends on**: the companion console grants in-process and could notify that one cabinet directly. Adding it
  must not let the re-read be removed.
- The re-read must not be so frequent that it is a load concern, nor so rare that « working again within
  minutes » (US-5) is false. It is bounded per client, not per cabinet.

---

## API Endpoints

### Read the cabinet's subscription
```
GET /api/subscription
Authorization: Bearer <token>            (any clinic role)

Response 200:
{
  "state":            "Trial" | "Active" | "Expired" | "Suspended",
  "plan":             "Cabinet" | "Clinique" | "SurMesure",
  "endsOn":           "2026-09-08" | null,     // inclusive; null = no expiry
  "daysRemaining":    29 | null,               // endsOn − clinic today; 0 on the last working day
  "allowsWrites":     true,
  "shouldWarn":       false,
  "suspensionReason": null | "…",
  "priceMonthlyDt":   290.000 | null,          // null on Sur-mesure (« sur devis »)
  "priceAnnualDt":    2900.000 | null,
  "paymentInstructions": "…",                  // operator-configured, French
  "contactEmail":     "…",
  "contactPhone":     "…"
}

Response 404: this deployment does not use subscriptions
```
Reachable even when the entitlement has expired (AC-4.8).

**Whether the deployment uses subscriptions at all is published as a flag on the existing deployment-metadata
read**, alongside the one that already says whether public signup is open — optional on the wire and read strictly
as *true*, following that flag's own convention, because the web and API containers are deployed separately and a
rolling deploy can serve a new page against an older API. The client mounts the banner and the navigation entry
from that flag. ⚠️ **The 404 stays** as the server-side guarantee, but it must not be what the client infers
absence from: a network failure and a genuine 404 are indistinguishable to a probe, and EC-13 requires a failed
read to be retryable rather than read as « aucun abonnement ».

### Read what the cabinet has paid
```
GET /api/subscription/history?page=1&pageSize=25
Authorization: Bearer <token>            (admin only)

Response 200:
{
  "items": [
    {
      "id":          "…",
      "kind":        "Trial" | "Paid" | "Grandfathered" | "Complimentary",
      "fromDay":     "2026-08-10",
      "throughDay":  "2027-08-09" | null,
      "amount":      2900.000 | null,
      "method":      "Transfer" | "Cash" | "Cheque" | "Card" | null,
      "reference":   "VIR-2026-08-114" | null,
      "note":        null | "…",
      "recordedAt":  "2026-08-10T09:14:00Z",
      "recordedBy":  "…",
      "isCancelled": false,
      "cancelledAt": null,
      "cancelReason": null
    }
  ],
  "totalCount": 3
}
```

### The refusal, on every write route
```
Any POST / PUT / PATCH / DELETE under /api, when the entitlement has ended

Response 402:
{
  "error": "Votre abonnement a expiré le 09/09/2026. Vous pouvez toujours consulter et exporter
            vos données. Rendez-vous dans « Abonnement » pour le renouveler.",
  "code":  "subscription_required"
}

Response 402 (suspended):
{
  "error": "Votre accès est suspendu. Contactez-nous pour rétablir votre abonnement.",
  "code":  "subscription_suspended"
}

Response 402 (no entitlement on record — a fault, not a lapse):
{
  "error": "L'abonnement de ce cabinet est introuvable. Contactez-nous, nous le rétablissons.",
  "code":  "subscription_missing"
}
```

---

## Device & Interface Behaviour

**Leading device: the tablet at the chair.** The banner is the one element that appears on **every** screen of the
app, so it competes for space with the agenda and the patient file on the device those are used on. The
« Abonnement » screen itself is most likely read at the desk, but the refusal is met chairside.

| Surface | Phone (< 640) | Tablet portrait (640–1023) | Desktop |
|---|---|---|---|
| **Subscription banner** | One line, wrapping to at most two. State + date + « Renouveler » as the only control. The dismiss control is a 44 px target on a coarse pointer and is **absent** once expired (AC-3.3). Must not consume more than ~15 % of a 380 px-tall landscape viewport, or it eats the agenda. | Same single row, date and days-remaining both shown. | State, date, days remaining, plan, and « Renouveler ». |
| **« Abonnement » screen** | Single column: state card → price → payment instructions → contact. Payment instructions are the reason the screen exists, so they are never behind a disclosure. | Single column at a readable measure; do not stretch to two just because the width allows it. | Two columns: state + plan left, instructions + contact right. |
| **Payment history table** (admin) | Card list — title = period covered, then amount, then method + reference, then state. A cancelled entry is marked in **words** (« Annulé ») as well as struck through. | Card list (six columns is past a comfortable table at this width alongside the rail). | Table. |
| **Refusal feedback** | A French toast carrying the server's own sentence, and the form **stays open with its input intact** (AC-4.6). The banner is already on screen, so the toast does not repeat the whole explanation. | Same. | Same. |

- **Touch paths:** the banner's only affordances are the link and the dismiss control, both real controls at 44 px
  on a coarse pointer — nothing here is revealed on hover.
- **Live update:** the banner and the « Abonnement » screen must reflect a grant without a manual reload (AC-5.8),
  through the re-read of FR-15 — an interval while a warning or expiry is in force, on window focus, and on any
  402. Not a pushed event; FR-15 says why one is not available.
- **Named exceptions:** none. Every surface in this feature works at 320 px.

---

## Scope

### In Scope
- A dated entitlement per cabinet, derived from a cancellable payment ledger.
- A 30-day trial created with every cabinet, through every creation door.
- Read-only degradation on expiry, with reads and exports untouched.
- A banner, an « Abonnement » screen and a payment history for administrators.
- In-app warnings at 7/3/1 days and on expiry.
- Vendor commands to grant, cancel, suspend and report.
- Grandfathering every existing cabinet, and a post-deployment check that proves it.
- Reminders and push pausing on expiry; backups continuing.
- Hosted deployment only.

### Out of Scope
- **Online payment of any kind.** No card, no gateway, no recurring mandate — no Tunisian rail supports automatic
  recurring billing (Stripe does not onboard Tunisian merchants; Konnect and Flouci are one-shot; Paymee's accounts
  are suspended). Payment is a transfer, D17 or cash, recorded by the vendor.
- **A web back-office for the vendor.** Deliberately a separate feature —
  [`features/platform-console/spec.md`](../platform-console/spec.md).
- **Seat limits and per-module gating.** Plans are price-only (FR-10). The practitioner cap and the module split
  the Tarifs page advertises are honoured commercially by the vendor until a follow-on feature makes them
  enforceable; FR-10 records that divergence rather than hiding it.
- **Proration, refunds, credit notes and invoices issued by the vendor to the clinic.** The vendor's own fiscal
  invoicing happens in the vendor's accounting, not in this product.
- **A grace period.** Expiry takes effect on its date; the 7-day warning is the grace.
- **Automatic deletion of an expired cabinet's data.** Never (FR-14).
- **Self-service payment declaration by the clinic** (« j'ai viré 290 DT, réf. X », awaiting confirmation). A
  reasonable follow-on, deliberately not here: a queue nobody drains leaves a cabinet read-only while believing it
  has paid.
- **Changes to public self-signup itself**, beyond stating the trial (AC-1.3).
- **Multi-factor authentication for clinic users.** Belongs to the companion feature's operator accounts.

---

## Edge Cases

### EC-1: The entitlement ends while somebody is working
- **Scenario:** A dentist has a fiche de soins open; midnight passes and the entitlement ends.
- **Expected:** Reads on that screen keep working. The save is refused with the 402 message, the fiche stays open
  with everything typed still there, and the banner appears without a reload — carried by the re-read of FR-15,
  which the refused save itself triggers. Nothing pushes this: midnight has no actor to broadcast from.

### EC-2: A user must change their password **and** the cabinet is expired
- **Scenario:** An administrator resets a colleague's password; the cabinet expires before the colleague signs in.
- **Expected:** Signing in and changing the password both work (AC-4.7). Were they refused, the account would be
  unusable in both directions with no way out.

### EC-3: A cabinet pays 10 days before expiring
- **Scenario:** End date is 20 September; a 12-month payment is recorded on 10 September.
- **Expected:** The new end date is 20 September **+ 12 months**, not 10 September + 12 months. Paying early never
  costs days. Month arithmetic clamps to the end of the target month (FR-2), so a grant landing on a 31st never
  spills into the following month.

### EC-4: A grant is recorded for the wrong cabinet
- **Scenario:** The vendor grants 12 months to the wrong practice.
- **Expected:** The entry is cancelled with a reason; it remains visible struck through; the end date recomputes and
  may return to the past, at which point that cabinet is read-only again. Nothing is edited and nothing is deleted.

### EC-5: Two grants at the same moment
- **Scenario:** The vendor records a grant from the console while a scheduled command records one too.
- **Expected:** **Both land, and both are kept.** They are two entries in an append-only ledger, and the end date is
  a fold over the non-cancelled ones (AC-5.4) — so there is no half-applied state for a conflict check to protect,
  and no caller is shown a conflict it could not act on anyway. If the second grant was not wanted, it is
  **cancelled with a reason** (AC-5.5) and the date recomputes; nothing is edited and nothing is lost.
- ⚠️ What *is* prevented is the **same** action landing twice — a double-click, a retried request — which is
  idempotency, a different question from concurrency (`features/platform-console/` EC-5/EC-6 states the console
  side of both). Reporting a conflict here would promise an outcome this ledger cannot produce.

### EC-6: A cabinet has no entitlement record
- **Scenario:** A schema change or a defect leaves a cabinet without one.
- **Expected:** Writes are refused (never allowed by default), with the **distinct** `subscription_missing` code so
  it is diagnosable as a fault, and the post-deployment check reports it.

### EC-7: A reminder was queued before expiry, for an appointment after it
- **Scenario:** A patient has an appointment next Tuesday; the cabinet expires on Sunday.
- **Expected:** The reminder is parked with a stated reason rather than sent or deleted. If the cabinet is extended
  before Tuesday, it is sent. If not, it never goes out, and the parked row explains why to whoever looks.

### EC-8: The date changes over a Tunisian midnight
- **Scenario:** It is 00:30 in Tunis on the day after the end date; the server's clock is UTC.
- **Expected:** Expiry follows the **clinic's** day. A cabinet is not expired an hour early, and is not still valid
  an hour late.

### EC-9: An expired cabinet exports everything and leaves
- **Scenario:** A practice decides not to renew and wants its data.
- **Expected:** Every CSV export on all nine lists works, and the patient records, invoices and documents remain
  readable. Nothing about leaving requires paying first.

### EC-10: A secretary meets a refusal
- **Scenario:** Reception tries to book an appointment for a cabinet that expired yesterday.
- **Expected:** The French refusal, the banner, and a « Abonnement » screen she is allowed to open so she can tell
  the owner what happened — not a rights error, which would send her to the wrong person.

### EC-11: A suspended cabinet reads its own state
- **Scenario:** A cabinet is suspended for abuse.
- **Expected:** « Abonnement » says suspended, not expired, and names no internal detail beyond what the vendor
  chose to say. Reads and exports still work.

### EC-12: The trial length is changed after cabinets exist
- **Scenario:** The configured trial goes from 30 days to 14.
- **Expected:** Cabinets already created keep their original end date (AC-1.5). Only cabinets created afterwards
  get 14 days.

### EC-13: The subscription read itself fails
- **Scenario:** The « Abonnement » screen cannot load its data (network drop).
- **Expected:** A retryable French « Réessayer » state — **not** an empty screen and not « aucun abonnement », which
  would read as a fault the clinic cannot act on.

---

## Non-Functional Hints

- **Performance:** the entitlement check sits on every write request, so it must not add a perceptible cost. Reads
  must not pay for it at all. It must not be cached in a way that leaves a paid cabinet locked out — a grant takes
  effect immediately.
- **Security:** granting a subscription must have no HTTP-reachable path in this feature. The refusal must not
  become a way to learn anything about another cabinet.
- **Accessibility:** the banner is a status region, not an interruption, and announces its state change without
  stealing focus; the expired banner is not a modal. State is conveyed by **text and an icon, never by colour
  alone** — « Expiré » must be legible in greyscale. Every control has a real accessible name, and the payment
  history distinguishes a cancelled entry in words as well as visually.
- **Scalability:** the daily warning pass and the report are per cabinet and grow with the number of cabinets; they
  must remain one bounded pass rather than unbounded work per cabinet.

---

## Dependencies

- **Public clinic self-signup** (`features/clinic-self-signup/`) — the door this feature makes commercially safe;
  the trial is created on the same path.
- **The hosted multi-tenant deployment** (`features/multi-tenant-cloud/`) — the only deployment this applies to,
  and the source of the capability that decides it.
- **The staff notification centre** — carries the warnings.
- **The reminder outbox and the push queue** — pause on expiry (FR-8).
- **The scheduled backup** — deliberately continues (FR-8).
- **The activity journal** — records every entitlement change (FR-12).
- **The Tarifs page** (`features/landing-website/mockups/03-tarifs.html`) — the prices shown to the public must
  match the configured ones. It sells **Cabinet 120 DT/mois** (100 en annuel), **Clinique 290** (242 en annuel) and
  **Sur-mesure sur devis**; `3 900 DT` on that page is the **self-hosted one-off licence**, not a plan. It also
  advertises a practitioner cap and a module split that FR-10 deliberately does not enforce — read that bullet
  before treating the page as a specification of behaviour.
- **The landing site's onboarding copy** (`features/landing-website/mockups/05-contact.html`) — its funnel ends in
  « **Essai accompagné — 2 semaines** ». That is a *guided engagement* — installation, reprise des données,
  formation de l'équipe — and **not** this feature's entitlement: a prospect who arrives that way has a cabinet
  created through the same door as anyone else and gets the same **30 days** (AC-1.1). The two numbers are
  nevertheless visible to the same reader, so the wording is worth aligning before go-live. Recorded here so the
  site is not mistaken for a specification of behaviour.
- **Companion:** `features/platform-console/spec.md` depends on this feature and must not be started before it.

---

## Open Questions

> None of the below blocks planning or implementation. Each is a **deploy-time value**, not a structural decision:
> the spec requires prices and payment instructions to be per-deployment configuration (AC-2.4, FR-10), so they are
> set when the deployment is configured and changing one is not a code change. They are kept here so they are not
> forgotten before go-live.

- [ ] Real prices — the tiers are **Cabinet / Clinique / Sur-mesure** (FR-10) and the Tarifs page's
      `120 / 290` DT per month, with `100 / 242` billed annually, are placeholders in both places. Confirm and set
      the figures at deploy time. Sur-mesure carries no configured price (« sur devis »).
- [ ] The exact French text of the payment instructions (bank, RIB, D17 details, what reference to put).
- [ ] ~~Whether an annual payment gets a stated discount~~ — **settled**: the Tarifs page already publishes one, so
      a plan carries a monthly *and* an annual price (FR-10). What remains is only the figures, above.

## Screenshots

None. No browser exploration was performed — see Deviations.

---

## Deviations from `/define-feature`

- **No parallel exploration agents.** The eight scopes the skill would have covered — similar features, the data
  model, API and error conventions, UI patterns, permissions, user flows, and the outbox/job/realtime integration
  patterns — were explored directly against the source earlier in the same session that produced this spec's design
  decisions, including the deployment-capability system, the middleware pipeline order, the clinic-creation doors,
  the notification and outbox shapes, the activity journal and the front-end error contract. Re-running them would
  have produced a second, weaker copy of findings already applied.
- **No browser exploration.** There is no browser tooling in this repository (`agent-browser` absent, no start
  script wired for it) — the same deviation `features/landing-website/design.md` records.
- **Questions were asked in batches rather than strictly one at a time**, because the design decisions had already
  been settled in the preceding session and the remaining gaps were independent of one another.
- **The feature was split in two** on the skill's own signals (distinct user groups, independent workflows). See the
  companion spec.
