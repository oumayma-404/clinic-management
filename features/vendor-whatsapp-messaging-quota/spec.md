# Feature Specification: Forfait de rappels WhatsApp (vendor-purchased messaging quota)

**Status:** APPROVED
**Challenged:** Yes
**Created:** 2026-08-11
**Feature:** The vendor buys WhatsApp messaging capacity centrally and allocates each cabinet a monthly allowance, counted as it is spent and enforced when it runs out.

---

## Overview

A cabinet's WhatsApp appointment reminders currently work only if the practice sets up its own Meta account, pastes
its own credentials into a settings panel, and pays Meta directly. Most practices never get past that. This feature
makes the vendor the buyer: the cabinet connects its WhatsApp number through a guided flow, the vendor's Meta credit
line pays for what it sends, and each cabinet receives a **monthly allowance of reminder messages** — visible to the
practice, adjustable by the vendor, and enforced when it is exhausted.

Enforcement is deliberately gentle. A cabinet that runs out does not lose reminders on the spot: they are **held**, and
go out the moment the vendor grants more. Everything else about the practice — its agenda, its records, its SMS
reminders — is untouched. The practice is warned twice before it happens, sees what it has left at any time, and is
never asked to understand Meta.

⚠️ **What holding is for, stated honestly.** A reminder reaches the allowance check only when it is **due** — 24 h (or
6 h) before the visit — so a held reminder is always for an appointment about a day away, never weeks. Holding
therefore rescues one case: the vendor **topping the cabinet up in time**, which is what lets a practice that runs out
on Tuesday morning still have Wednesday's patients reminded. It is **not** a promise that the waiting reminders go out
next month: by the 1st their appointments have passed, and a reminder about a visit that already happened is not sent
(AC-4.5). The allowance itself does renew on the 1st; the reminders held against the old one are, in the ordinary case,
**not rescued by that** — they are reported to the practice so a secretary can telephone those patients instead
(AC-4.9). Saying otherwise would be true for a narrow month-boundary sliver and misleading for the common case.

For the vendor, the feature answers two questions that have no answer today: **what has this cabinet cost us this
month**, and **what did we sell them**. Both are needed because Meta's own per-cabinet cost reporting is unavailable
on the credit-line arrangement this feature depends on (see `exploration.md` § 6.2) — so the product's own count of
what it sent is the billing record.

---

## User Stories

### US-1: A cabinet connects WhatsApp without configuring anything
As a **clinic admin**, I want to switch WhatsApp reminders on by connecting my practice's number, so that I do not
have to create a Meta account, obtain credentials or understand message templates.

**Acceptance Criteria:**
- **AC-1.1:** The « Rappels » screen offers « Connecter WhatsApp » to an admin on a deployment where vendor messaging
  is available; the flow is Meta's own guided connection, and the only thing the admin supplies to us is the
  practice's phone number as part of it.
- **AC-1.2:** The one-time code Meta sends arrives on the practice's own handset. The screen says so **before** the
  flow starts, so the admin knows they need that phone in reach.
- **AC-1.3:** On successful connection the product submits the French reminder message template on the cabinet's
  behalf. The admin is never shown a template editor and never asked to write or approve message wording.
- **AC-1.4:** The connection state is one of exactly five, each stated in words: **non connecté** · **connecté, en
  attente de validation par Meta** · **prêt à envoyer** · **modèle refusé** · **connexion suspendue par Meta**.
  « Connecté » alone is never presented as « prêt à envoyer ».
- **AC-1.5:** While the template is under review, the screen says how long it may take (up to 24 h) and that
  reminders booked meanwhile are held and will go out on approval.
- **AC-1.6:** On a deployment where vendor messaging is not available, none of this appears — no card, no button, no
  message. It is **absent**, not present and refusing.
- **AC-1.7:** The manual WhatsApp credential fields (endpoint, phone number id, access token) are **not offered** on
  a deployment where vendor messaging is available.

### US-2: A cabinet sees what it has left
As **any member of clinic staff**, I want to see how many WhatsApp reminders I have left this month, so that I can
tell whether a patient will actually be reminded.

**Acceptance Criteria:**
- **AC-2.1:** A « Forfait de rappels WhatsApp » section on the « Rappels » screen shows, for the current Tunisian
  calendar month: the allowance, the number consumed, and the number remaining.
- **AC-2.2:** It is readable by **every** clinic role including a secretary — the person who meets a refused
  « Relancer » chairside is usually not the person who pays.
- **AC-2.3:** It shows the twelve preceding months, each with its allowance and what was consumed.
- **AC-2.4:** A month in which the cabinet sent nothing reads **« 0 rappel envoyé »** — a real, measured zero. Only a
  month with **no counting row at all** reads **« non mesuré »**, which is a statement about *us*, not about the
  practice, and should not normally occur (FR-1a). A month before the cabinet existed is not listed at all.
  ⚠️ This is the mirror of EC-12 and the two are easy to conflate: « 0 restant » must never stand in for « nous
  n'avons pas pu lire », and « non mesuré » must never stand in for « vous n'avez rien envoyé ». A quiet practice
  told every month that its own record is unreadable is the second error.
- **AC-2.5:** A failed read renders as a failure with a retry, never as an empty or zeroed table.
- **AC-2.6:** The section states that SMS reminders are not affected by this allowance.
- **AC-2.7:** When the allowance is exhausted the section says so plainly, names the date it resets, and gives the
  vendor's contact details. Those come from **operator configuration** — the same place the default standing
  allowance does (FR-3) — never from a per-clinic field, since they are the vendor's own details and identical for
  every cabinet. Where they are unconfigured the section renders **no contact route at all** rather than an empty
  link or a `mailto:` to nowhere; a dead control is worse than an absent one.

### US-3: A cabinet is warned before it runs out
As a **clinic admin or secretary**, I want to be told my reminders are about to stop, so that I can ask for more
before patients stop being reminded.

**Acceptance Criteria:**
- **AC-3.1:** An in-app notification is raised when consumption crosses **80 %**, **95 %** and **100 %** of the
  month's allowance, **at the moment it is crossed** (FR-6) rather than on the next daily pass. Crossing more than
  one threshold at once raises one row for **each**.
- **AC-3.2:** Each threshold produces **one genuinely new unread row**, so each badges the bell. A later threshold is
  never a rewording of an earlier row.
- **AC-3.3:** The rows are clinic-wide (no actor, no target user) and deep-link to the « Rappels » screen.
- **AC-3.4:** A notification is **never** delivered as an OS push. A quota notice is not time-critical to a person,
  and spending the device's single notification permission on one risks the categories that are.
- **AC-3.5:** Warning wording is derived from the **threshold, the allowance and the month** — never from the live
  consumed count — so a threshold that holds for several days restates nothing.
- **AC-3.6:** A grant that puts the cabinet back below a crossed threshold **withdraws** the rows for the thresholds
  it no longer meets, so the bell never asserts two different states of the same month.
- **AC-3.7:** At the start of a new month the previous month's warning rows are withdrawn and all three thresholds
  are re-armed.

### US-4: Reminders stop rather than overspend, and resume by themselves
As the **vendor**, I want a cabinet's reminders to stop when its allowance is gone, so that I never pay for messages
I did not allocate — and as a **practice**, I want them to resume without anyone doing anything.

**Acceptance Criteria:**
- **AC-4.1:** When a cabinet's allowance for the current Tunisian month is fully consumed, a WhatsApp reminder due
  for dispatch is **held**, not sent and not failed, and records a French reason naming the cause.
- **AC-4.2:** A held reminder is **released automatically within one dispatch cycle of the vendor granting more** —
  the case holding exists for. It is also re-evaluated when the Tunisian month rolls over, but that is a
  re-evaluation and **not a rescue**: by then the appointment has almost always passed, so the row **fails as
  obsolete** under AC-4.5 rather than being sent.
  ⚠️ **The rollover must not be presented to the practice as « they will go out on the 1st ».** A reminder is only
  checked against the allowance when it comes **due** (24 h or 6 h before the visit), so a held reminder is for an
  appointment about a day away; a month later it announces something that already happened. The one case the rollover
  genuinely rescues is the sliver where a reminder falls due in the last hours of a month for a visit in the first
  hours of the next — real, but not the common case and not what the wording may claim. What the practice is told
  instead is that **the allowance** renews on that date (AC-2.7) and that these specific patients were not reminded
  (AC-4.9).
- **AC-4.3:** A cabinet with **no allowance record at all** is held in the same way, under its own distinct reason
  and its own distinct sentence — « we cannot find your allowance » is the vendor's fault and must not read as
  « renew it ».
- **AC-4.4:** Held reminders are **never purged while they could still be sent**. However long a cabinet stays
  exhausted, nothing is deleted out from under it.
  ⚠️ **This is bounded, and it has to be stated or it is not.** A held row has no attempt counter, no expiry, and is
  excluded from the purge by construction, so it is re-examined on **every** review tick for as long as it exists —
  the starvation shape the outbox has already been bitten by twice. **Three** terms bound it, and the third is
  required because the first two do not cover every row:
  - **AC-4.5a's obsolescence** — a held row whose appointment has passed fails as obsolete, becoming an ordinary
    terminal row the existing purge collects after `RetentionDays`. This is what drains the overwhelming majority,
    and it does so **without waiting for the month to roll over**.
  - **The rollover re-evaluation** (AC-4.2) — which mostly *feeds* the obsolescence above rather than releasing
    anything sendable.
  - **A reason-agnostic age bound** — a row parked longer than `Reminders:HeldMaxDays` (30) fails as obsolete
    whatever parked it. This is the load-bearing one for a reminder with **no appointment**: a « Relancer » row
    carries no appointment id, so nothing can ever make it obsolete, and AC-5.3 creates exactly that row on purpose.
    The same defect already exists today for rows parked because a channel was switched off.

  So « never purged » means « never purged **while it could still be sent** », and every held row reaches a terminal
  state on its own — most within a day or two of its visit, and all within a month.
- **AC-4.5:** A released reminder whose appointment has already passed is **not sent** — it fails as obsolete, and the
  practice is not made to send a patient a reminder for last week.
  ⚠️ **This does not exist today and is new work in this feature.** The existing `AnnouncesStaleMoment` check
  compares the *frozen message body* against the appointment's *current* moment, so it catches a **moved**
  appointment; it takes no clock and nothing else in the dispatcher compares the appointment against « now ».
  Past-appointment filtering happens only at **enqueue**, which a held row passed long ago. So a reminder released
  after its visit is **sent** as things stand — already true on the subscription-release path, and this feature's
  month-rollover release would make it routine.
- **AC-4.5a:** The guard is added at **dispatch**, beside the moved-appointment check, and applies to **every**
  appointment-bearing reminder rather than only to rows released from held — a merely delayed `Pending` row has the
  same defect, and two rules for one question is how the pre-existing case was missed in the first place.
- **AC-4.6:** SMS reminders for the same appointment are **unaffected** and go out normally. Exhausting the WhatsApp
  allowance does not silence the practice.
- **AC-4.7:** A cabinet whose **subscription** has lapsed is held for **that** reason, not this one. Subscription is
  evaluated first; a practice is never told to buy reminders when the thing it must fix is its subscription.
- **AC-4.8:** When a held reminder becomes eligible again, **every** blocking condition is re-checked before it
  returns to the queue — a reminder released by a grant must not go out for a cabinet that is meanwhile suspended.
- **AC-4.9:** The number of held reminders and the reason are visible on the existing « Rappels » delivery log, so a
  secretary can see **which patients** were not reminded and phone them instead. The log already lists held rows with
  the patient's name, a « Bloqués » counter and a filter chip, and already renders the French sentence — so what this
  feature adds is the **machine-readable block reason** on the log row, beside the sentence that is there now.
  ⚠️ Without it the screen can only tell an allowance hold from a subscription hold by **matching French prose**,
  which is the practice this repo deleted in `adoption-gaps-remediation` — rewording a sentence would silently change
  behaviour. It is also what lets « N rappels en attente de forfait » be counted at all, as distinct from the
  undifferentiated « Bloqués » total.

### US-5: A manual relance is refused honestly
As a **secretary**, I want to be told when a « Relancer » cannot be sent, so that I do not record a patient as
contacted when nobody has been reached.

**Acceptance Criteria:**
- **AC-5.1:** Clicking « Relancer » on a patient while the WhatsApp allowance is exhausted is **refused**, in French,
  naming the cause and the alternative (« Marquer comme contacté »).
- **AC-5.2:** The patient is left exactly as they were: still on the relance list, not snoozed, not marked contacted.
- **AC-5.3:** If SMS is configured and sendable for that cabinet, the relance is sent by SMS and **not** refused —
  a channel being exhausted is not the same as having no channel. This falls out of the existing design: a relance
  enqueues **one row per sendable channel**, so the SMS row is created and the outcome is a success; only the
  WhatsApp row is held.
- **AC-5.4:** The refusal carries its **own outcome**, distinct from « aucun canal configuré ». Today's vocabulary
  would answer a WhatsApp-only cabinet with the no-channel refusal, whose sentence tells the practice to configure a
  channel it has already configured — advice it cannot act on, which is the `subscription_missing` lesson in a new
  place. The new outcome names the allowance and offers « Marquer comme contacté », and like every other
  non-success branch it leaves the patient **untouched** (AC-5.2).

### US-6: The vendor allocates and adjusts an allowance
As the **vendor**, I want to set a cabinet's monthly allowance and top it up for a busy month, so that I can respond
to a practice's real usage without changing its plan permanently.

**Acceptance Criteria:**
- **AC-6.1:** From a cabinet's file in the console, the vendor can record a **standing monthly allowance** ("500 per
  month from now on") and a **one-off top-up for a named month** ("+300 for August").
- **AC-6.2:** Both are entries in an append-only record. Nothing is edited in place and nothing is deleted.
- **AC-6.3:** **Raising** the standing allowance takes effect **immediately** for the current month, and held
  reminders are released within one dispatch cycle.
- **AC-6.4:** **Lowering** the standing allowance takes effect from the **next** Tunisian month. A practice is never
  cut off mid-afternoon by a change it had no warning of.
- **AC-6.4a:** Which of the two an entry is, is decided by the **server** from the figure in force for the current
  month (FR-2), never chosen by the caller. The recorded entry states its own effective month, so the journal and the
  cabinet file show when it starts rather than leaving it to be re-derived.
- **AC-6.5:** A top-up may name the **current or a future** month, never a past one — a past top-up releases nothing
  and would rewrite a figure the practice has already been shown.
- **AC-6.6:** Every allocation records what the vendor was paid, if anything, and by what means; a complimentary
  allowance carries no amount rather than an amount of zero.
- **AC-6.7:** A repeated submission of the same allocation produces **one** entry and returns the first outcome. Two
  genuinely different allocations both land and are both kept.
- **AC-6.8:** Every console action here is written to the console's own access journal, in the same operation as the
  change it records. An action that cannot be attributed does not succeed.
- **AC-6.9:** These actions are refused for a console account that has been deactivated, on its next request.

### US-7: The vendor corrects a mistaken allocation
As the **vendor**, I want to strike out an allocation I recorded by mistake, so that the record shows what really
happened rather than being rewritten.

**Acceptance Criteria:**
- **AC-7.1:** An allocation entry can be cancelled with a **mandatory** written motif.
- **AC-7.2:** The entry is **kept**, shown struck through and labelled « Annulé » in words, carrying its motif, who
  cancelled it and when.
- **AC-7.3:** Before confirming, the vendor is shown **what the cabinet's allowance will become** — computed by the
  server from the real record with that entry marked cancelled, never estimated in the browser.
- **AC-7.4:** A cancellation is a **correction of a mistaken record, not a lowering**, so it applies to **every month
  the entry fed, the current month included** — unlike AC-6.4, which governs a genuine change of plan. Cancelling an
  allocation the cabinet has **already consumed against** does not make anything negative: the consumed figure is
  **untouched**, the remaining figure is `max(0, allowance − consumed)`, and a month whose allowance has fallen below
  what was already spent simply reads **« épuisé »** — reminders are held from that moment, and nothing is unsent or
  clawed back.
- **AC-7.4a:** The distinction is deliberate. A mis-keyed « +3000 » must be correctable in the month it was keyed
  into; deferring it would leave the vendor paying for a figure both parties know to be wrong until the month ends.
  This is the shape money already has here — an avoir corrects the current period rather than waiting for the next.
- **AC-7.5:** Cancelling an entry that is already cancelled is refused with a distinct, machine-readable outcome, and
  the file re-reads so the existing motif and author appear.

### US-8: The vendor sees consumption across the portfolio
As the **vendor**, I want to see what each cabinet is consuming, so that I can top up a practice before it hits the
wall and see what my Meta bill is made of.

**Acceptance Criteria:**
- **AC-8.1:** A cabinet's file shows its current allowance, what it has consumed this month, what remains, its
  connection and template state — including the template's **category** stated in words whenever it is not
  `UTILITY` (FR-7b) — and the full allocation history.
- **AC-8.2:** The portfolio list carries the cabinet's **consumption against its allowance for the current month** as
  a single figure, and can be filtered to cabinets that are exhausted or near it.
- **AC-8.3:** A cabinet that sent nothing this month reads **« 0 »**; only a cabinet with **no counting row** reads
  « non mesuré » (AC-2.4). The exhausted/near-exhausted filter of AC-8.2 treats « non mesuré » as neither — an
  unmeasured cabinet is a bookkeeping finding, not a cabinet near its limit.
- **AC-8.4:** A failed read renders as « je n'ai pas pu lire », never as an empty portfolio.
- **AC-8.5:** No new field on any console surface names a patient, an act, an amount owed by a patient, or any
  clinical fact. What the console may return stays a closed, reviewed set.
- **AC-8.6:** A report is available without the console — listing, per cabinet, its allowance, its consumption and
  whether it is exhausted — so the vendor can see the portfolio's messaging position from a terminal.

### US-9: The vendor operates without the console
As the **vendor**, I want to allocate and correct allowances from a terminal, so that the console being unreachable
never blocks a practice.

**Acceptance Criteria:**
- **AC-9.1:** Console commands exist to grant a standing allowance, grant a top-up, cancel an entry with a motif, and
  report on the portfolio.
- **AC-9.2:** They are the same operations the console performs, producing the same records and the same journal
  entries, with the same refusals.
- **AC-9.3:** No clinic-facing endpoint anywhere can change a cabinet's own allowance. A practice able to raise its
  own allowance does not have one.
- **AC-9.4:** The report distinguishes « exhausted » (a finding — act on it) from « no allowance record » (a
  different finding — our bookkeeping is wrong) from a **template no longer categorised `UTILITY`** (a third finding
  — our cost per message has moved, FR-7b) and exits with a distinct code when any is present.

---

## Functional Requirements

### FR-1: What is counted
- One unit is **one WhatsApp reminder message that the send call accepted**.
- The unit is counted in the **same commit** that records the reminder as sent — the increment is *staged* into the
  dispatcher's existing per-row save, not written afterwards. A message that never left costs nothing, and a crash
  loses **both or neither**. This is `PlatformAccessLedger.RecordAsync`'s shape: the record and the thing it records
  ride one transaction, because a side effect written separately is one that can be missing.
- Because the two are atomic, the counter can never lag the send log. The reconciliation check (EC-14) is kept as a
  **backstop against causes it does not cover** — a send Meta accepted but whose response was never recorded — so any
  finding it reports is a real defect rather than expected drift.
- A message the product sent twice (delivery is at-least-once and there is no provider-side de-duplication) counts
  **twice**, because the vendor is billed twice. This is stated on the clinic-facing screen's help text rather than
  hidden.
- A send that times out or whose outcome is unreadable counts **nothing** and is retried. If the retry succeeds, one
  unit.
- ⚠️ **The count is an approximation of the bill, and it errs in both directions.** Meta charges on **delivery**;
  this counts on **acceptance**. So a message accepted and never delivered is counted but not billed
  (**over**-counting, against the practice), while a send whose outcome was unreadable is billed if it did in fact
  go and is not counted (**under**-counting, in the practice's favour). The net direction is **unknown**, not
  favourable — closing it would need the delivery webhook this feature puts out of scope. What is guaranteed is
  narrower and worth stating plainly: the count never charges a cabinet for a message the product did not attempt.
- SMS messages are **never** counted. Email is never counted.
- Consumption is attributed to the **Tunisian calendar month** in which the message was sent, never to a UTC month.

### FR-1a: The counting row exists whether or not anything was sent
- There is **one counting row per cabinet per Tunisian month**, and it is **provisioned for every cabinet**, not
  created on first send. A cabinet that sends nothing has a row reading **0**.
- The row carries **both** the month's **allowance** and its **consumed** count. The allowance on the row is a
  **snapshot of FR-2's fold for that month**, written when the month is provisioned and **rewritten by every
  allocation or cancellation that touches that month**.
- ⚠️ **Why the allowance is stored and not folded on read.** AC-8.2 filters and sorts the whole portfolio on
  consumption *against allowance*, and AC-2.4a's rule is that such a figure must exist for every cabinet **before a
  page is cut** — the vendor console's list is one bounded LEFT JOIN whose filters are SQL and whose « today » is a
  query parameter. Folding each cabinet's ledger inside that query is the unbounded read the console's repository
  explicitly forbids. Storing the fold's answer per month is the same move `ClinicSubscription.LatestCoverKind`
  makes, and for the same stated reason.
- ⚠️ It also settles what the twelve-month history means: a past month shows **the allowance that was actually in
  force**, not today's figure applied backwards.
- ⚠️ **A derived copy can drift, and nothing in the model can say it must not** — so `verify-schema` gains
  **`monthly-allowance-matches-ledger`**, re-deriving each row's allowance through the **real** fold and reporting
  both directions. This is the `subscription-cover-kind-matches-ledger` pattern; a snapshot with no such check is the
  failure mode this repo has already paid for once.
- This is what makes « non mesuré » mean something. If the row were created on first send, a quiet month and a broken
  counter would be the same picture — and EC-14's whole purpose is to tell them apart.
- It is also what lets AC-8.2's portfolio filter read a figure for **every** cabinet before a page is cut, rather
  than only for those that have sent something.
- The row is bounded by construction: one per cabinet per month regardless of message volume.

### FR-2: What the allowance is
- A cabinet's allowance for a month = its **standing monthly allowance** in force for that month, **plus** any
  one-off top-ups recorded for that month.
- Every standing entry carries an **effective month**, decided by the server when the entry is recorded and never
  supplied by the caller:
  - the new figure **≥** the figure in force for the current month ⇒ effective **this** month (a raise, AC-6.3);
  - the new figure **<** the figure in force for the current month ⇒ effective **next** month (a lowering, AC-6.4).
- **The fold, stated once.** The allowance for month `M` is:
  - the `MessagesPerMonth` of the **latest non-cancelled standing entry whose effective month ≤ `M`** (in recorded
    order; none ⇒ the cabinet has no allowance record, FR-4's second branch), **plus**
  - the sum of every **non-cancelled top-up naming `M`**.
- The fold is **pure, total and clock-free**: it takes `M` as a parameter and reads no clock, so recomputing it later
  yields the same answer. This is `SubscriptionLedger`'s property and it is load-bearing for the same reason —
  a retried write must compute the same figure as the write it raced.
- Comparing against « the figure in force for the current month » is what resolves a lowering followed by a raise:
  each entry is judged against the figure actually in force when it is recorded, not against the previous entry. So
  500 → 800 on the 3rd is effective immediately, and 800 → 400 on the 4th is effective next month.
- There is **no rollover**. Unused allowance lapses at the end of the Tunisian month.
- A cabinet may have **no** allowance record only as a fault state; every cabinet is meant to have one.

### FR-3: Provisioning
- Every newly created cabinet receives a standing allowance from operator configuration, recorded in the **same
  operation** as the cabinet itself — a cabinet and its allowance arrive together or neither does.
- A cabinet created mid-month receives the **full** standing allowance for that partial month; there is no proration.
- A cabinet in its free trial receives an allowance like any other.
- At rollout, **every existing cabinet** on the deployment is given the same standing allowance, so no practice's
  reminders stop because this feature shipped.

### FR-4: Enforcement
- Before a WhatsApp reminder is sent, the cabinet's consumption for the current Tunisian month is compared against
  its allowance for that month.
- Consumed **≥** allowance ⇒ the reminder is **held** with reason « allowance exhausted ».
- No allowance record ⇒ the reminder is **held** with reason « allowance missing ».
- Holding is **not** failing: the reminder keeps its place, is never purged, and returns to the queue when the
  condition clears.
- The check is evaluated **after** the subscription check and before the message is sent.
- Where vendor messaging is not available on the deployment, this check reads nothing at all and costs nothing.

### FR-5: Enforcement is a hard stop
- A cabinet cannot exceed its allowance. There is no overrun, no burst and no per-cabinet override.
- The last units are consumed in the queue's existing order (oldest due first). A reminder for a nearer appointment
  is **not** prioritised over an older queued one; the practice sees which reminders were held and can act.

### FR-6: Warnings
- Thresholds are **80 %**, **95 %** and **100 %** of the month's allowance, rounded down.
- De-duplicated on **(cabinet, month, threshold)** — a real dedupe key, never a message prefix.
- **Evaluated where the counter is incremented**, so 80 % is announced when it is crossed rather than the following
  morning. ⚠️ This deliberately departs from `SubscriptionWarningJob`'s daily-only shape, and the reason is that the
  two quantities move differently: an end date advances one day per day, so a daily pass never misses much, while
  consumption can cross all three thresholds between two runs. US-3 exists so a practice can ask for more **before**
  patients stop being reminded; a warning that arrives after exhaustion cannot do that.
- **Every threshold newly crossed produces its own row**, so a jump from 70 % to 100 % in one afternoon yields three.
  ⚠️ This is the opposite of `SubscriptionStateReader.ThresholdReached`, which returns only the **largest** reached —
  correct there, because a cabinet that slept past several day-thresholds only needs telling where it now stands,
  wrong here, because the 80 % row is the one that could still have been acted on.
- The warning write is **post-commit and best-effort**, on `INotificationGenerator`'s existing swallow-and-log
  contract: a failure to notify must never fail or roll back the send it follows.
- **A daily pass remains**, as the reconciling second writer — it re-checks cabinets in case a post-commit hook was
  lost, performs AC-3.6's withdrawal after a grant, and performs AC-3.7's month-rollover withdrawal and re-arming.
  Neither writer can emit a duplicate, because the dedupe key forbids it.
- A cabinet with an allowance of zero produces the 100 % warning only.
- A cabinet whose subscription has lapsed, or that is suspended, is **not** warned — it is already refused for a
  reason that this one does not explain.

### FR-7: Sender identity and template
- A cabinet's WhatsApp number and business account are **its own**, connected through Meta's guided flow.
- The reminder template is submitted by the product on the cabinet's behalf at connection, in French, and is a
  **utility** template. It must not begin or end with a variable.
- A template under review, refused, paused or disabled means the cabinet is **not ready to send**; reminders due in
  that state are held under a reason naming it, consume nothing, and are released on approval.
- Recovering from a refused or disabled template is the **vendor's** action. The cabinet is told the state and given
  a contact route; it is never asked to edit template wording.

### FR-7a: How the template's state is learned
The template state is **stored** on the cabinet (a status plus the moment it was last confirmed) and has **two
writers**, deliberately:
- **A Meta webhook** (`message_template_status_update` on the cabinet's WABA) — this is what makes « les rappels
  partiront dès la validation » true in minutes rather than by the next day.
- **A reconciling poll**, low-frequency, over cabinets **not** in a terminal state. A webhook that Meta never
  delivered, or that arrived while the application was down, would otherwise strand a cabinet at « en attente de
  validation » **for ever** with its reminders piling up held and no recovery path. `exploration.md` § 6.8 records
  that Meta's webhook behaviour in this area is partly unconfirmed, which is itself the argument for a backstop.
- Neither writer is a substitute for the other: the webhook without the poll has no recovery, the poll without the
  webhook makes AC-1.5's promise a daily one.
- ⚠️ **None of this exists today.** There is no template call of any kind in the product — onboarding makes four
  Graph calls, none about templates — and no stored template status. Submission, the state column, both writers and
  the release of held rows are all new work in this feature.
- ⚠️ Webhook field names are a known trap (`exploration.md` § 6.7): messaging-limit changes arrive on
  `business_capability_update` / `account_alerts`, **not** `account_update`. Subscribing to the wrong field yields a
  callback that is silently never called.

### FR-7b: The template's category is watched, because Meta can change it
- The stored template state carries its Meta **category** alongside its status, written by the same two writers as
  FR-7a.
- ⚠️ **Why this is here at all.** Since 9 April 2025 Meta **auto-recategorises**: a UTILITY submission it judges to
  be MARKETING *is approved as MARKETING*, and the business *"accepts the charges associated with the category
  applied to the template at time of use"* — 24 h notice, 60 days to appeal (`exploration.md` § 6.5). Marketing
  messages are **always** charged, with no free window at all. So a reclassification silently multiplies the
  vendor's bill **across every cabinet using that wording, at once** — while this feature's count, being one unit per
  message, stays perfectly correct. The product would report a stable cost while the real one moved.
- A category that is not `UTILITY` is stated **in words** on the cabinet's console file, and is a **finding** in
  `messaging-report` (exit code 2), so the vendor meets it inside the 60-day appeal window rather than on an invoice.
- It is **not** surfaced clinic-side and does **not** hold reminders: the practice cannot act on it, the wording is
  not theirs to change (FR-7), and stopping a cabinet's reminders over the vendor's own commercial exposure is
  exactly the ungentle enforcement this feature is defined against.
- It does not change the unit. One message is one unit whatever Meta charges for it; the category changes what a
  unit **costs us**, which is the vendor's question and belongs on the vendor's surfaces.

### FR-8: Meta's own refusals
- The sender distinguishes Meta's outcomes rather than treating every non-success alike:
  - **Throttles** (application, account and platform rate limits, and too-many-to-one-recipient) leave the reminder
    queued and retried later. They consume no retry budget and no unit.
  - **Meta has stopped this number** (a messaging restriction, or a limit reached through template classification)
    **holds** the reminder under its own reason, because retrying burns capacity and cannot succeed.
  - Anything else keeps the existing transient-failure behaviour.

### FR-8a: The extension points this feature must declare
Four existing mechanisms are **derived guards**: they fail the build, or throw, if this feature adds a case without
declaring it. None is visible before that happens, so they are named here rather than discovered.

| Extension point | What this feature adds | The guard |
|---|---|---|
| `OutboxBlockReason` | members for allowance exhausted, allowance missing, template not ready, and Meta stopped the number | mapped `HasConversion<int>()` — **no migration**; a missing member is simply an unrepresentable hold |
| `PlatformAccessAction` | members for recording an allocation and for cancelling one | same ordinal mapping, **no migration**; the enum's own rule is that a member *arrives with the write that produces it* |
| `NotificationCategory` | one member for the allowance warning | `StaffNotificationRules.ReachesALockedPhone` is a **total switch that throws** on an unclassified category — so AC-3.4 is honoured by classifying it **`false`**, never by leaving it out |
| `PlatformReadShape.AllowedLeafNames` | every new console field name | asserted in **both** directions: an undeclared name fails, **and an unused declaration fails too**, so names cannot be added ahead of the DTO that returns them |

⚠️ The read-shape set today contains no `Message`, `Quota`, `Template` or `Phone` name. The existing `Note` and
`Reference` names are declared as **vendor-payment** fields; reusing them for messaging is a semantic overload, not
a free pass.

### FR-8b: Tunisian months need a primitive that does not exist
`ClinicClock` is the single authority on what Tunisia's clock means, and it currently has **day and year helpers
only** — it contains no month concept at all. This feature is specified in Tunisian calendar months throughout, so
the month key (`AAAA-MM`), the month's UTC bounds and the French month label are **added to `ClinicClock`**.

⚠️ Both already exist elsewhere in private form — a `ClinicMonthRangeUtc` inside a platform query, and a French month
label on the console's own label helper. They **move** to `ClinicClock`; a second private copy is exactly the defect
shape this repo has catalogued repeatedly, and month arithmetic is where it would be least visible, since two copies
agree for eleven months out of twelve.

### FR-9: Availability
- Vendor-purchased messaging exists only on the hosted multi-tenant deployment. It is derived from the deployment
  kind and **no operator setting can turn it on elsewhere**.
- Where it is unavailable, every surface in this spec is **absent**: no clinic section, no console section, no
  notifications, no enforcement, no scheduled work.
- Whether the deployment's own Meta credentials are configured is a **separate** question from whether the
  deployment may do this at all, and is answered separately.

### FR-10: Wording
All clinic-facing sentences state **what still works** before what does not, name the date or the remedy, and never
mention signing in or out. Drafts:

| Situation | Sentence |
|---|---|
| Exhausted (held reminder, and the screen) | « Votre forfait de rappels WhatsApp est épuisé pour ce mois-ci. Vos rendez-vous, vos dossiers et vos rappels SMS continuent normalement. Les rappels en attente partiront dès que nous augmentons votre forfait ; votre forfait se renouvelle le {date}. Consultez « Rappels » pour savoir quels patients n'ont pas été prévenus. » |
| No allowance record | « Le forfait de rappels WhatsApp de ce cabinet est introuvable. Vos rappels WhatsApp sont en attente — contactez-nous, nous le rétablissons. » |
| « Relancer » refused | « Votre forfait de rappels WhatsApp est épuisé pour ce mois-ci. Vous pouvez contacter ce patient autrement, puis utiliser « Marquer comme contacté ». » |
| Template under review | « WhatsApp est connecté. Meta valide votre modèle de message — cela peut prendre jusqu'à 24 h. Les rappels prévus d'ici là partiront dès la validation. » |
| Template refused | « Meta a refusé le modèle de message. Nous nous en occupons — vos rappels sont en attente. » |
| Warning, 80 % / 95 % | « Vous avez utilisé {seuil} % de votre forfait de {N} rappels WhatsApp pour {mois}. Vos rappels SMS ne sont pas concernés. » |
| Warning, 100 % | « Votre forfait de {N} rappels WhatsApp pour {mois} est épuisé. Les rappels en attente partiront dès que nous augmentons votre forfait ; votre forfait se renouvelle le {date}. » |

Clinic-side the feature is called **« Forfait de rappels WhatsApp »** — never « Messagerie », which a practice reads
as patient chat.

---

## API Endpoints

### Read the cabinet's allowance (clinic)
```
GET /api/clinics/reminder-allowance
Authorization: Bearer <token>          — any clinic role

Response 200:
{
  "month": "2026-08",                  // Tunisian calendar month
  "monthLabel": "août 2026",
  "allowance": 200,
  "consumed": 143,
  "remaining": 57,                     // floored at 0
  "exhausted": false,
  "resetsOn": "2026-09-01",
  "measured": true,                    // false ⇒ never counted; never render 0
  "senderState": "Ready",              // NotConnected | PendingReview | Ready | TemplateRefused | Suspended
  "senderStateLabel": "Prêt à envoyer",
  "senderNumber": "+216 •• ••• •12" | null,
  "contactEmail": "…" | null,
  "contactPhone": "…" | null
}

Response 404: the deployment does not do vendor-purchased messaging (absent, not refused)
```

### Read the cabinet's consumption history (clinic)
```
GET /api/clinics/reminder-allowance/history
Authorization: Bearer <token>          — any clinic role

Response 200:
{
  "months": [
    { "month": "2026-08", "monthLabel": "août 2026", "allowance": 200, "consumed": 143, "measured": true },
    { "month": "2026-07", "monthLabel": "juillet 2026", "allowance": 200, "consumed": null, "measured": false }
  ]
}
```
Twelve preceding months plus the current one. Months before the cabinet existed are omitted, not zeroed.

### Record an allowance (vendor console)
```
POST /api/platform/clinics/{clinicId}/messaging-allowances
Authorization: Bearer <console token>

Request (exactly one of the two forms):
{
  "idempotencyKey": "…" | null,
  "messagesPerMonth": 500,             // standing allowance, from now on
  "topUpMessages": null,
  "appliesToMonth": null,
  "amountDt": 45.000 | null,
  "method": "Transfer" | "Cash" | "Cheque" | "Card" | null,
  "reference": "…" | null,
  "note": "…" | null
}
{
  "idempotencyKey": "…" | null,
  "messagesPerMonth": null,
  "topUpMessages": 300,                // one-off, for a named month
  "appliesToMonth": "2026-08",         // current or future only
  …
}

Response 200:
{ "entryId": "…", "allowanceThisMonth": 800, "previousAllowanceThisMonth": 500, "alreadyRecorded": false }

Response 404: { "error": "…", "code": "clinic_not_found" }
Response 400: { "error": "…" }        — both forms supplied, a past month, a non-positive count
```

### Cancel an allowance entry (vendor console)
```
POST /api/platform/clinics/{clinicId}/messaging-allowances/{entryId}/cancellation
Authorization: Bearer <console token>

Request: { "reason": "…" }             — mandatory

Response 200: { "entryId": "…", "allowanceThisMonth": 500, "allowanceNextMonth": 200 }
Response 404: { "error": "…", "code": "clinic_not_found" | "allowance_entry_not_found" }
Response 409: { "error": "…", "code": "allowance_entry_already_cancelled" }
```

### Cabinet file (vendor console) — extended
`GET /api/platform/clinics/{clinicId}` gains a `messaging` object: the current month's allowance, consumed,
remaining and measured flag; the sender and template state; and the allocation history, each entry carrying what the
allowance **would become** if that entry were cancelled.

### Portfolio list (vendor console) — extended
`GET /api/platform/clinics` gains the current month's allowance and consumption per cabinet, and a filter for
cabinets that are exhausted or within 10 % of it.

### Console commands
```
messaging-grant  --clinic <id|email> (--per-month N | --top-up N --month AAAA-MM)
                 [--amount …] [--method …] [--reference …] [--note …]
messaging-cancel --clinic <id|email> --entry <id> --reason "…"
messaging-report [--clinic <id|email>] [--month AAAA-MM]     # exit 0 clean / 1 couldn't run / 2 findings
```

---

## Device & Interface Behaviour

**Leading device:** the **desk machine** for the console surfaces (the vendor works at a desk, over a tunnel), and the
**chairside tablet** for the clinic surfaces — the person who meets a refused « Relancer » is standing at a chair with
a finger, on a 1180 px screen that is not a desktop.

| Surface | Phone (< 640) | Tablet portrait (640–1023) | Desktop |
|---|---|---|---|
| Clinic « Forfait » summary (allowance / consumed / remaining) | Stacked figures, each with its label above it; the remaining figure leads. No horizontal scroll. | Three across | Three across |
| Clinic 12-month history | **Card list** — one card per month, month name as the card title, allowance and consumed as labelled fields. Not a reflowed table. | Table (four columns — mois, forfait, consommé, restant) | Table |
| Clinic connection/template state | Full-width statement block, wrapping; the contact route is a real `tel:`/`mailto:` link with a 44 px box | Same | Same |
| Console cabinet « Messagerie » section | Figures stacked; allocation history as a card list | Section fills the column | Two-up beside the subscription section |
| Console « Enregistrer un forfait » / « Ajouter un complément » | Bottom sheet in `dvh`, footer pinned as a sibling of the scrolling body so the primary action survives the keyboard | Bottom sheet | Centred panel |
| Console « Annuler cette allocation » (motif) | Bottom sheet; the consequence sentence is above the motif field, never below the fold | Bottom sheet | Centred panel |
| Console portfolio columns | The two new figures appear as **fields in the existing card list**, not as extra table columns at phone width | Table already switches to cards below `lg:` — the new figures join the card | Two extra columns |

- **Touch paths:** nothing in this feature is revealed on hover. The console's history rows expose « Annuler cette
  allocation » as a persistent button on live entries only — a control that opens onto a refusal is a dead control.
- **Named exceptions:** none. Every surface here works at 320 px.
- The clinic history is a **table plus a card list**, not a table that reflows — a `display:block` table strips the
  implicit row and cell roles a screen reader needs.

---

## Scope

### In Scope
- The monthly allowance: how it is recorded, changed, corrected and folded into a number for a given month.
- Counting sent WhatsApp reminders per cabinet per Tunisian month, durably, as the billing record.
- Holding and releasing reminders on exhaustion, including the month-rollover release and the re-check on release.
- A **dispatch-time past-appointment guard** (AC-4.5a) — new work, and it also closes the same defect on the existing
  subscription-release path.
- Refusing a manual « Relancer » when exhausted, with the patient left untouched.
- The three in-app warnings, their de-duplication, withdrawal and re-arming.
- The clinic-facing section on « Rappels »: current month, twelve months, connection and template state, contact.
- The vendor console: the cabinet-file section, the two write dialogs, the cancellation dialog with its consequence
  sentence, the portfolio figures and filter, and the journal entries for every action.
- The three console commands and the report.
- Automatic template submission on connection, and the states that follow from Meta's review — including the stored
  template state and **both** of its writers, the webhook and the reconciling poll (FR-7a).
- Classifying Meta's error outcomes so a stopped number is held rather than retried to exhaustion.
- Closing the manual WhatsApp credential fields on the hosted deployment.
- Provisioning a new cabinet with an allowance, and backfilling every existing cabinet.
- The deployment capability, and the separate question of whether credentials are configured.
- **Confirming which Embedded Signup version to build on, and migrating if it is v2** — the first task of the
  connection slice, not a later one.
- Schema verification for the invariants no other layer can see, and the derived guard tests this touches.
- Operator documentation for the commands and the Meta account setup they assume.

### Out of Scope
- **Becoming a Meta Solution Partner and attaching the credit line.** A commercial and account-configuration task,
  not product work. This feature assumes it is done.
- **Delivery confirmation.** Counting is on acceptance; no delivery webhook is built. The gap is stated, not closed.
- **SMS.** Unchanged: the cabinet's own gateway, the cabinet's own bill, uncounted.
- **Charging a cabinet for messaging.** Nothing here reaches an invoice, la caisse, « Créances » or a patient's
  balance. What a cabinet pays for its allowance is the vendor's revenue, recorded as such.
- **Per-cabinet Meta cost reconciliation.** Meta does not report cost per number on a shared credit line; the
  product's own count stands in for it.
- **Letting a cabinet edit its reminder template wording.**
- **A cabinet requesting more allowance in-product.** The screen gives contact details; there is no request workflow.
- **Rollover, overrun, burst allowances and per-cabinet overrides.**
- **Migrating cabinets already using their own WhatsApp credentials.** Named as an operator task in the runbook.

---

## Edge Cases

### EC-1: A cabinet exhausts its allowance with reminders due tomorrow
- **Scenario:** 200/200 consumed on the 20th; reminders are queued for visits on the 21st and 22nd.
- **Expected:** Both are held, both are listed on « Rappels » with the reason and the patient's name, so a secretary
  can telephone. Neither is sent when the month rolls over — the appointments have passed, so they fail as obsolete.

### EC-2: A grant lands while reminders are held
- **Scenario:** The vendor grants +300 for the current month at 14:00.
- **Expected:** Held reminders are released within one dispatch cycle. Each is re-checked against **every** condition
  first — subscription, suspension, channel, template state — not only the one that held it.

### EC-3: The vendor lowers a standing allowance below what is already consumed
- **Scenario:** A cabinet has consumed 400; the standing allowance is lowered from 500 to 300.
- **Expected:** The current month is unaffected — the cabinet keeps 500 until the month ends. The new figure applies
  from the first of the next Tunisian month. Nothing goes negative and nothing stops mid-day.

### EC-4: A consumed top-up is cancelled
- **Scenario:** A cabinet has a standing 500 and a +300 top-up for the current month, of which 750 is already spent.
  The top-up is cancelled.
- **Expected:** The allowance for the current month falls to **500**; the consumed figure stays **750**; remaining is
  **0**, not −250; the month reads « épuisé » and further WhatsApp reminders are held from that moment. Nothing
  already sent is unsent. The console shows exactly this before the vendor confirms (AC-7.3).
- **Note:** this is **not** EC-3's rule. EC-3 is a change of plan and defers to the next month; a cancellation is a
  correction of the record and applies to the month the entry fed (AC-7.4).

### EC-5: Two vendor allocations at once
- **Scenario:** Two console sessions record an allowance for the same cabinet simultaneously.
- **Expected:** Both land and both are kept. The cabinet's allowance reflects both. A surplus is corrected by a
  cancellation, never by refusing an allocation already paid for.

### EC-6: A double-click on « Enregistrer »
- **Scenario:** The vendor submits the same allocation twice within a second.
- **Expected:** One entry. The second submission returns the first one's outcome, not a conflict.

### EC-7: A message sent at 23:59 on the last day of the month
- **Scenario:** A reminder is sent at 23:59 Tunis on 31 August.
- **Expected:** It counts against **August**. A message sent at 00:01 on 1 September counts against September and the
  allowance has reset.

### EC-8: A cabinet both expired and exhausted
- **Scenario:** The subscription lapsed on the 5th; the allowance ran out on the 3rd.
- **Expected:** Reminders are held for the **subscription** reason and the cabinet is told about its subscription.
  Extending the subscription does not release them if the allowance is still exhausted — they are re-held under the
  allowance reason, and the cabinet is then told that.

### EC-9: A cabinet connects WhatsApp and books appointments the same afternoon
- **Scenario:** Template review is still pending when the first reminders come due.
- **Expected:** They are held under the template reason, consume nothing, and are sent on approval — unless their
  appointment has passed by then, in which case they fail as obsolete.

### EC-10: Meta refuses the template
- **Scenario:** The submitted template comes back `DECLINED`.
- **Expected:** The clinic's card says so and gives a contact route; the console flags the cabinet; reminders are
  held; nothing is consumed; no automatic resubmission loop runs.

### EC-11: Meta stops the number
- **Scenario:** A send returns a messaging restriction or a template-classification limit.
- **Expected:** The reminder is held under its own reason rather than retried three times and failed. No unit is
  consumed. The state is visible to the vendor.

### EC-12: The allowance record cannot be read
- **Scenario:** The database is unreachable when the clinic opens « Rappels ».
- **Expected:** The section says it could not be read and offers a retry. It never shows « 0 restant », which is a
  statement about the cabinet where the truth is a statement about us.

### EC-13: A cabinet created on the 29th
- **Scenario:** A practice signs up two days before month end.
- **Expected:** It receives the **full** standing allowance for those two days, and a fresh full allowance on the 1st.

### EC-14: The process stops mid-send
- **Scenario:** The process stops between the send call and the commit.
- **Expected:** The reminder is **neither** marked sent **nor** counted — the two are one commit (FR-1), so there is
  no window in which they disagree. Delivery is at-least-once, so the row is redelivered on a later tick; if that
  send succeeds it counts **once**, and if Meta had in fact accepted the first attempt the duplicate is counted twice
  (EC-15), which is the honest answer because the vendor is billed twice.
- **Not covered by atomicity, and this is why verification stays:** a send Meta accepted whose response never reached
  us at all. Verification compares the counter against the surviving reminder rows **inside the 90-day retention
  window** (`Sent`/`Failed` rows are purged after that) and reports any discrepancy. A finding is now a **defect to
  investigate**, not expected drift — which is what makes the check worth reading.

### EC-15: The same reminder is sent twice
- **Scenario:** At-least-once delivery causes a duplicate.
- **Expected:** Two units. The vendor is billed twice, so counting once would hide a real cost. The clinic-facing
  help text states that a rare duplicate is counted.

### EC-16: A cabinet on a deployment that does not do this
- **Scenario:** A clinic's own Windows PC, or the Auth0 cloud deployment.
- **Expected:** No section, no notifications, no enforcement, no scheduled work, and the endpoints answer as though
  they do not exist. Existing WhatsApp behaviour is byte-for-byte unchanged.

---

## Non-Functional Hints

- **Performance:** The allowance check runs on every WhatsApp reminder dispatch. It must cost at most one read per
  cabinet per dispatch cycle, not one per reminder. The clinic's history read covers thirteen months, not all time.
- **Security:** A cabinet must not be able to change its own allowance by any route. The counting record and the
  allocation record are the vendor's, not the practice's. No console surface may return a patient's name or any
  clinical fact. The manual credential path being closed on the hosted deployment must not weaken the existing rule
  that a tenant supplying part of a channel inherits none of the deployment's credentials.
- **Accessibility:** Every state is stated in **words**, never by colour alone — « épuisé », « en attente de
  validation », « annulé ». The remaining figure has a text label, not just a coloured bar. Every icon-only control
  has an accessible name. The consumption figures update in a live region so a screen-reader user hears a refresh.
  Failure states use an alert role; genuine empties use a status role; the two are never the same component.
- **Scalability:** Meta's messaging limits are shared across every number on the vendor's portfolio, so the product's
  own per-cabinet cap is what stops one practice starving the others. The counting record must remain bounded — one
  row per cabinet per month — regardless of message volume.

---

## Dependencies

- **`features/clinic-subscription/`** — the subscription must be evaluated before this allowance, and its refusal
  wins. This feature reuses its shape, not its code paths.
- **`features/platform-console/`** — the cabinet file, the access journal, the closed set of returnable field names,
  and the console write pattern.
- **`features/sms-whatsapp-reminders/`** and **`features/whatsapp-embedded-signup/`** — the outbox, the dispatcher,
  the settings resolution and the Meta connection flow this feature meters.
- **Meta WhatsApp Business Platform** — Solution Partner status and a credit line attached to each cabinet's account.
  See `exploration.md` § 6; several rules there are marked unconfirmed and need checking in a logged-in browser.
- ⚠️ **Embedded Signup v2 is deprecated 15 October 2026, and v2 is what is deployed.** The shipped integration is
  `FB.login` with `config_id`, `response_type: "code"` and `extras.sessionInfoVersion = "3"`, with **no
  `featureType`** — the v3 marker — and Graph pinned to `v21.0` in **two independent places** that do not derive from
  each other (a hard-coded browser constant and a server config default). This feature makes that flow load-bearing
  for the vendor's billing arrangement **and** builds template submission on top of it. **Confirm the target version
  before building on it**, and migrate if the answer is v2; discovering this after the connection slice is built is
  the expensive order. Consolidating the two version pins is the natural moment.
- ⚠️ ~~**Meta's per-message rates change on 1 October 2026.** Both free rules — service messages, and utility
  templates inside an open 24 h window — end that day, and the replacement rates were due to be announced by
  1 September 2026 and are not yet published (`exploration.md` § 6.4).~~
  🔴 **CORRECTED by the Story 0 spike — this was wrong.** Meta's pricing page lists **both free rules as current,
  with no end date**: « utility templates delivered within an open customer service window are free », and service
  conversations have been free for all businesses since 1 Nov 2024. What falls on **1 October 2026** is a
  **rate-card update** moving named markets out of regional pricing — Bangladesh, Iraq, Nepal, Sri Lanka,
  Kazakhstan, Kuwait, Morocco, Oman, Ukraine — and **Tunisia is not among them**. Tunisia (calling code **216**,
  ISO **TN**) is priced as **« Rest of Africa »**; rates may change only on 1 Jan / 1 Apr / 1 Jul / 1 Oct, with at
  least a month's notice, and the current cards took effect 1 Jul 2026.
  **The conclusion the old text drew still holds, for a different reason:** the unit is unchanged — the allowance is
  counted in **messages sent** and only the vendor's cost per unit moves — but the default figure is **not** blocked
  on an unpublished rate. It is blocked only on a commercial decision.
  ⚠️ **What this confirms rather than corrects:** a reminder is a *proactive* utility template sent **outside** any
  customer-service window, so it **is** charged. The free-in-CSW rule does not cover what this feature meters.
  ⚠️ **And one thing in the vendor's favour, newly known:** utility rates fall by **monthly volume**, aggregated
  « at the business portfolio level, across all WhatsApp Business accounts owned by the portfolio » — so every
  cabinet's traffic counts toward one tier and the cost per message falls as the portfolio grows.
- **`exploration.md`** in this folder — the codebase and Meta research behind every decision above.

---

## Open Questions

- [ ] **Meta rules that could not be verified.** The display-name guideline list, the template edit caps, whether one
      payment instrument can serve several accounts in a portfolio, and Tunisia's actual per-message rates are all on
      JavaScript-rendered Meta pages a fetcher cannot read. They need opening in a logged-in browser before the
      acceptance criteria that touch them are trusted. (`exploration.md` § 6.8)
- [ ] **The default standing allowance figure.** Stated as operator configuration; the number itself is a commercial
      decision not yet made — and one that cannot be finalised before Meta publishes the rates replacing the free
      rules that end **1 October 2026** (due by 1 September 2026, see Dependencies). The feature does not wait on it:
      the figure is configuration, so shipping with a provisional number costs nothing but an edit.
- [x] ~~**Whether the 12-month history should show the allowance in force for each past month.**~~ **Settled** by
      FR-1a: each monthly row stores the allowance in force for that month, so the history shows what was really in
      force rather than today's figure applied backwards. The same column is what makes AC-8.2's portfolio filter
      possible before a page is cut.
- [ ] **Cabinets already using their own WhatsApp credentials**, if any exist on the hosted deployment, need a
      migration path. Named as an operator task; the count is unknown.
- [ ] **Tunisia's data-protection position (Loi n° 2004-63)** on appointment reminders over WhatsApp — outside Meta's
      documentation, needs local counsel. It does not block this feature but bears on message content.

---

## A note on size

This spec carries **nine** user stories, which is past the point where a feature usually splits. That is deliberate —
the whole feature was specified in one pass rather than sliced, on the instruction that nothing be deferred. The
natural boundaries for `/break-plan` are: (1) allowance record + counting + enforcement, (2) the clinic-facing
surface and warnings, (3) the vendor console, commands and report, (4) template submission and Meta outcome
classification. They should be planned as one feature and built in that order.
