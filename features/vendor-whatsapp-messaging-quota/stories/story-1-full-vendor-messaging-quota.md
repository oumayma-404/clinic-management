# Story 1: FULL — Vendor-purchased WhatsApp messaging quota

**Status:** APPROVED
**Story Status:** not-started
**Layer:** **Full** (BE · jobs · console verbs · clinic UI · console UI) — see the departure note below
**Depends On:** 0
**Blocks:** None

> ⚠️ **This is deliberately one full-stack story, departing from `/break-plan`'s single-layer default.** The
> granularity was settled in [`../plan.md`](../plan.md) at the user's explicit instruction and registered as **R-1**
> with its split point named. Steps are grouped by the plan's **six ordered parts**, each a vertical increment and each
> a **commit point** `/implement-story` can land at across sessions. No part reaches backwards.
>
> **If this needs splitting, split at a part boundary** — the natural cut is **Parts 0–1 · Part 2 · Part 3 · Parts 4–5**,
> and **Part 4 can split off alone** (R-2's contingency): enforcement, counting and the console all work on the
> existing WhatsApp connection, so Parts 1–3 do not depend on it.

## Objective

A practice connects WhatsApp without ever touching Meta, sees how many reminders it has left this month, is warned
before it runs out, has its reminders **held rather than overspent** when it does, and resumes by itself — while the
vendor allocates, corrects and watches the allowance from the console or a terminal. The vendor's own count of what it
sent becomes the billing record, because Meta does not report per-cabinet cost on the shared credit line this feature
depends on.

Available on **`HostedMultiTenant` only**, through the 18th `DeploymentProfile` capability `SellsVendorMessaging`
(derived from the deployment **kind**, flippable by no operator setting). On the other two kinds every surface is
**absent** rather than present-and-refusing, and existing WhatsApp behaviour is byte-for-byte unchanged (EC-16).

## Acceptance Criteria

_From spec:_

**US-1 — a cabinet connects WhatsApp without configuring anything** (Part 4)
- [ ] AC-1.1 « Connecter WhatsApp » offered to an admin where vendor messaging is available; Meta's own guided flow
- [ ] AC-1.2 the one-time code arrives on the practice's own handset, stated **before** the flow starts
- [ ] AC-1.3 the product submits the French reminder template on the cabinet's behalf; no template editor is ever shown
- [ ] AC-1.4 exactly five states, each in words; « connecté » is never presented as « prêt à envoyer »
- [ ] AC-1.5 while under review, the screen says up to 24 h and that reminders booked meanwhile are held
- [ ] AC-1.6 absent — no card, no button, no message — where vendor messaging is unavailable
- [ ] AC-1.7 the manual WhatsApp credential fields are **not offered** where it is available

**US-2 — a cabinet sees what it has left** (Part 2)
- [ ] AC-2.1 allowance / consumed / remaining for the current Tunisian month
- [ ] AC-2.2 readable by **every** clinic role including a secretary
- [ ] AC-2.3 the twelve preceding months with their allowance and consumption
- [ ] AC-2.4 a quiet month reads « 0 rappel envoyé »; only a month with **no row** reads « non mesuré »; a month before
      the cabinet existed is **not listed**
- [ ] AC-2.5 a failed read renders as a failure with a retry, never an empty or zeroed table
- [ ] AC-2.6 the section states SMS reminders are unaffected
- [ ] AC-2.7 exhausted ⇒ says so, names the reset date, gives the vendor's contact from **operator config**; renders
      **no** contact route at all where unconfigured

**US-3 — warned before it runs out** (Part 2)
- [ ] AC-3.1 in-app notification at **80 / 95 / 100 %**, at the moment crossed; one row for **each** threshold crossed
- [ ] AC-3.2 each threshold is one genuinely new unread row, so each badges the bell
- [ ] AC-3.3 clinic-wide, no actor, no target user, deep-linking to « Rappels »
- [ ] AC-3.4 **never** delivered as an OS push
- [ ] AC-3.5 wording derived from threshold + allowance + month, never the live count
- [ ] AC-3.6 a grant putting the cabinet below a crossed threshold **withdraws** those rows
- [ ] AC-3.7 at a new month, the previous month's rows are withdrawn and all three thresholds re-armed

**US-4 — stop rather than overspend, resume by themselves** (Part 1)
- [ ] AC-4.1 fully consumed ⇒ the reminder is **held**, not sent and not failed, with a French reason
- [ ] AC-4.2 released within one dispatch cycle of a **grant** — the case holding exists for. The month rollover
      **re-evaluates** held rows but is not a rescue: their visits have passed, so they fail as obsolete (AC-4.5).
      No clinic-facing sentence may promise « they will go out on the 1st »
- [ ] AC-4.3 **no allowance record** ⇒ held under its **own** distinct reason and sentence
- [ ] AC-4.4 held reminders are never purged **while they could still be sent** (and the bound is real — step 15a)
- [ ] AC-4.5 a released reminder whose appointment has passed is **not sent**; it fails as obsolete
- [ ] AC-4.5a the guard is at **dispatch**, for **every** appointment-bearing reminder, not only released ones
- [ ] AC-4.6 SMS for the same appointment is **unaffected**
- [ ] AC-4.7 a lapsed **subscription** holds for *that* reason; subscription is evaluated first
- [ ] AC-4.8 on release, **every** blocking condition is re-checked before the row returns to the queue
- [ ] AC-4.9 the held count and the **machine-readable** reason are visible on the « Rappels » delivery log

**US-5 — a manual relance is refused honestly** (Part 2)
- [ ] AC-5.1 « Relancer » while exhausted is refused, in French, naming the cause and « Marquer comme contacté »
- [ ] AC-5.2 the patient is left exactly as they were — not snoozed, not marked contacted
- [ ] AC-5.3 SMS sendable ⇒ sent by SMS and **not** refused; only the WhatsApp row is held
- [ ] AC-5.4 the refusal carries its **own** outcome, distinct from « aucun canal configuré »

**US-6 — the vendor allocates and adjusts** (Part 3)
- [ ] AC-6.1 a standing monthly allowance **and** a one-off top-up for a named month, from the cabinet's file
- [ ] AC-6.2 both are entries in an append-only record; nothing edited in place, nothing deleted
- [ ] AC-6.3 **raising** takes effect immediately for the current month; held reminders release within one cycle
- [ ] AC-6.4 **lowering** takes effect from the **next** Tunisian month
- [ ] AC-6.4a which of the two an entry is, is decided by the **server**; the entry states its own effective month
- [ ] AC-6.5 a top-up may name the **current or a future** month, never a past one
- [ ] AC-6.6 every allocation records what the vendor was paid, if anything; complimentary carries **no** amount
- [ ] AC-6.7 a repeated submission produces **one** entry and returns the first outcome
- [ ] AC-6.8 every console action is journalled **in the same operation** as the change it records
- [ ] AC-6.9 refused for a deactivated console account, on its next request

**US-7 — the vendor corrects a mistaken allocation** (Part 3)
- [ ] AC-7.1 cancellable with a **mandatory** written motif
- [ ] AC-7.2 the entry is **kept**, struck through, labelled « Annulé » in words, carrying motif / who / when
- [ ] AC-7.3 before confirming, the vendor sees what the allowance **will become**, computed **server-side**
- [ ] AC-7.4 a cancellation applies to **every month the entry fed, the current one included**; consumed is untouched,
      remaining is `max(0, allowance − consumed)`, and the month reads « épuisé »
- [ ] AC-7.4a the distinction from AC-6.4 is deliberate
- [ ] AC-7.5 cancelling an already-cancelled entry is refused with a distinct machine-readable outcome

**US-8 — consumption across the portfolio** (Part 3)
- [ ] AC-8.1 the cabinet's file shows allowance / consumed / remaining, connection + template state (**category in
      words when not `UTILITY`**), and the full allocation history
- [ ] AC-8.2 the portfolio list carries consumption against allowance, filterable to exhausted or **within 10 %**
- [ ] AC-8.3 « 0 » for a cabinet that sent nothing; « non mesuré » only with **no counting row**, and it is **neither**
      for the filter
- [ ] AC-8.4 a failed read renders « je n'ai pas pu lire », never an empty portfolio
- [ ] AC-8.5 no new console field names a patient, an act, a per-patient amount or any clinical fact
- [ ] AC-8.6 a report is available **without** the console

**US-9 — the vendor operates without the console** (Part 3)
- [ ] AC-9.1 verbs to grant standing, grant a top-up, cancel with a motif, and report
- [ ] AC-9.2 the same operations, records, journal entries and refusals as the console
- [ ] AC-9.3 **no clinic-facing endpoint** anywhere can change a cabinet's own allowance
- [ ] AC-9.4 the report distinguishes exhausted · no allowance record · a template no longer `UTILITY`, and exits with
      a distinct code when any is present

**Cross-cutting**
- [ ] EC-16 on `SelfHostedLan` / `CloudBrowser`: no section, no notifications, no enforcement, no scheduled work,
      endpoints answering as though they do not exist, existing WhatsApp behaviour byte-for-byte unchanged
- [ ] FR-9 availability is derived from the deployment **kind**; whether Meta credentials are configured is a
      **separate** question answered separately

_Story-specific:_

- [ ] Each of the six parts ends at a **commit point** with the build and the unit suite green
- [ ] `verify-schema` and `reconcile-money` are run **before and after** the migration batch and **diffed**
- [ ] The six challenge findings the plan now carries are implemented as written, not as they first appear:
      the webhook's tenant scope + WABA lookup (Part 4 § 34), the held-row age bound (Part 1 § 15a), the gate's ordered
      terms (§ 12 + § 33a), `MonthToDateRangeUtc` (§ 2–3), full-body Meta classification (§ 37), and the ensure-create
      before the send (§ 14a)

## Entry Criteria

Before starting this story, ensure:

- [x] **Story 0 is `done`** — the version is recorded in [`../progress.md`](../progress.md)
- [x] **The version question is settled, and by a third outcome:** current is **v4**, we are on **v3**, and the
      15 Oct 2026 deprecation names **v2 only**. Part 4 **§ 31 migrates to v4** (plan **D-1a**) — four edits in one
      file. Part 4 does **not** split off; R-2 is retired and replaced by **R-2a** (Graph five versions behind,
      deliberately a follow-up rather than § 31 work)
- [x] Exactly one `v21.0` authority remains — `META_GRAPH_API_VERSION`, feeding both the server's
      `Meta__GraphApiVersion` and the web image's `NEXT_PUBLIC_META_GRAPH_VERSION` build arg (Story 0 step 4)
- [ ] Read § 31 and § 34 before starting Part 4: **§ 34's `account_update` warning was corrected** — that webhook is
      **required** for Embedded Signup, not the wrong field, and an earlier draft read as the opposite
- [ ] [`../plan.md`](../plan.md) and [`../spec.md`](../spec.md) read — this file indexes them, it does not replace them
- [ ] `.claude/rules/frontend-web.md` read before any frontend work (the device + UX contract and its gate)
- [ ] A **throwaway PostgreSQL database seeded with several cabinets** is available for the Part 1 migration rehearsal
- [ ] `docker compose up -d` (postgres + minio) and the API run locally; `dotnet build` clean on a **clean** tree
- [ ] `git diff HEAD --numstat` reviewed — this branch carries in-flight work from other authors, so a broad
      `git add` would swallow it

## Steps

Step numbers match [`../plan.md`](../plan.md) exactly, so the two can be read side by side.

### Part 0 — Groundwork: months, extension points, the capability

*(Step 1, the spike, is **Story 0**. Start here.)*

2. **Add the month primitives to `ClinicClock`** (FR-8b) — `MonthKey`, `CurrentMonthKey`,
   `MonthRangeUtc(monthKey)` (the **whole** month, inclusive both ends), **`MonthToDateRangeUtc(clinicToday)`**
   (the 1st → the last tick of **today**), `MonthLabelFr` (`fr-FR` pinned), `NextMonthKey`, `PrecedingMonthKeys`,
   `FirstDayOfNextMonth`.
   - ⚠️ **Two range primitives, not one.** The names are the guard — see step 3.

3. **Move, do not copy, the two existing private implementations** — delete
   `GetPlatformSummaryQuery.ClinicMonthRangeUtc` (`:93`) and `PlatformAccessLabels.Month` (`:38`); grep to confirm
   neither symbol survives outside `ClinicClock`.
   - ⚠️ **`ClinicMonthRangeUtc` moves onto `MonthToDateRangeUtc`, NOT `MonthRangeUtc`.** Read it first: its upper bound
     is `LastTickOfLocalDayUtc(todayLocal)` — the end of *today*, not of the month. Its caller feeds
     `VendorCollectedThisMonthDt`, so a one-primitive « move » would widen that window by the rest of the month, and
     **nothing in the suite would catch it** (`MoneyReadConsistencyTests` covers the clinic's money, not the vendor's).
   - `PlatformAccessLabels.Month` takes `(int year, int month)` — keep that overload shape or adapt its call sites
     deliberately.

4. **Declare the four FR-8a extension points** — the four `OutboxBlockReason` members, the two `PlatformAccessAction`
   members, the `NotificationCategory` member **plus** its `StaffNotificationRules` classification (`false`, AC-3.4)
   **and** its `PushLabel`, and the `NotificationTargetKind` member. Both `StaffNotificationRules` switches **throw**
   on an unclassified member, so omitting either fails at runtime rather than silently.

5. **Add the 18th capability `SellsVendorMessaging`** to `DeploymentProfile` (`HostedMultiTenant` only), plus
   `IVendorMessagingAvailability` + `VendorMessagingAvailability` for the separate credentials question (FR-9).

6. **Add `IMessagingAllowancePolicy` + `MessagingAllowancePolicy`** reading `Messaging:DefaultMessagesPerMonth`,
   `:ContactEmail`, `:ContactPhone` — parsed **by hand**, falling back rather than throwing (`GetValue<T>` throws, and
   this is read while a cabinet is being provisioned).

**Part 0 commit point.** Validation:
- [ ] `ClinicMonthRangeUtc` and `PlatformAccessLabels.Month` return zero hits outside `ClinicClock`
- [ ] `ClinicClockMonthTests` covers EC-7's 23:59-on-the-31st and 00:01-on-the-1st, **and pins
      `MonthRangeUtc` ≠ `MonthToDateRangeUtc` mid-month**
- [ ] `GetPlatformSummaryQuery`'s window is **unchanged** for a mid-month « today »
- [ ] `DeploymentProfileTests` still holds the two pre-existing kinds' truth table; the new capability is unreachable
      from any config key
- [ ] `dotnet build` + unit suite green (built **outside** the repo)

### Part 1 — The allowance exists, sends are counted, reminders are held rather than overspent

*(Spec boundary 1 · covers US-4 end to end, and the machinery US-2/3/6/7/8 read.)*

7. **Domain**: `MessagingAllowanceEntry`, `ClinicMessagingMonth`, `MessagingAllowanceKind`,
   `IMessagingAllowanceRepository`, and `MessagingAllowanceLedger` — the fold, **pure, total, clock-free**, taking
   `monthKey` as a parameter (FR-2).
8. **EF configurations + repository + migration scaffold** `AddClinicMessagingAllowances`. **Remove the scaffolded
   `xmin` columns by hand** (PostgreSQL rejects them) and place the backfill **below every DDL statement**.
9. **The rollout backfill** (FR-3) — one `Standing` entry per existing cabinet at the configured default, effective the
   rollout month, **gated on « this cabinet has no standing entry »**, plus that month's `ClinicMessagingMonth` row.
10. **`LocalClinicProvisioning.StageMessagingAllowanceAsync`** — the entry **and** the month row in the **same save**
    as the clinic. Update all three callers (`CreateClinicCommand`'s Local branch, `provision-clinic`,
    `VerifyClinicSignUpCommand`) — a compile error by design (R-11).
11. **`MessagingAllowanceRefold`** (5-attempt `ConflictException` retry, detaching the ledger) and
    **`MessagingRefusals`**.
12. **`OutboxMessagingGate`** — WhatsApp rows only, after the subscription gate, per-tick instance with a per-cabinet
    cache, reading nothing where the capability is off. `ReviewAsync` answers over **ordered terms**:
    template-not-ready (Part 4 § 33a) → allowance-missing (AC-4.3) → allowance-exhausted (AC-4.1), returning the
    **first** applicable `OutboxBlock`. Part 1 implements the two allowance terms; the ordering is declared here so
    Part 4 adds a *term*, not a gate.
13. **Wire it into `NotificationJob` in both places** — `DispatchAsync` immediately after the subscription gate, and
    `ReviewBlockedRowsAsync` for **every** parked row (AC-4.8, EC-2, EC-8).
14. **FR-1's counting** — on `Sent` for a WhatsApp row, load the clinic's row for `ClinicClock.CurrentMonthKey()`,
    call `RecordSend()`, and let the **existing** `SaveAsync(notification)` commit both, so the unit and the `Sent`
    mark ride one transaction.
15. **AC-4.5a** — the past-appointment guard beside the moved-appointment check in `DispatchAsync`, for **every**
    appointment-bearing reminder: `appointment.AppointmentDateTime <= nowUtc` ⇒ fail as obsolete (D-3), `nowUtc` a
    parameter.

**14a. The ensure-create happens BEFORE the send, in its own save.**
   - ⚠️ Staging the `INSERT` into `SaveAsync` would make a unique-violation collision on `(ClinicId, MonthKey)` — raised
     by the daily provisioning pass racing it — fail the commit **that marks the reminder `Sent`**, after Meta accepted
     it. The row stays un-`Sent`, the next tick re-sends, and one message is paid for and uncounted while its duplicate
     counts twice (EC-15). `[DisableConcurrentExecution]` does not cover this: it serialises the job against *itself*.
     The window is exactly month rollover — when the daily pass runs.

**15a. The held-row age bound** — in `ReviewBlockedRowsAsync`, a row parked longer than `Reminders:HeldMaxDays`
    (default **30**) **fails as obsolete** whatever parked it, becoming a terminal row the existing purge collects.
   - ⚠️ **Step 15's guard cannot do this job.** AC-4.5a keys on the appointment, and `ReminderScheduler` creates a
     **recall** row with `appointmentId: null` (`:128`), so the whole appointment block is skipped for one. Such a row
     is non-terminal, excluded from the purge by construction, and re-examined every review tick for ever — the
     starvation shape this outbox has been bitten by **twice**. And **AC-5.3 manufactures it deliberately**.
   - ⚠️ Keyed on the **parked age, not the reason**: `ChannelDisabled`/`ChannelUnconfigured` have the identical defect
     on a recall row **today**. One rule for « how long may a send wait? ».

**Part 1 commit point.** Validation:
- [ ] `MessagingAllowanceLedgerTests` — clock-free and idempotent; a raise is effective this month and a lowering next
      (EC-3); a cancellation applies to **every month the entry fed, the current one included** (AC-7.4, EC-4);
      remaining is `max(0, allowance − consumed)` and never negative; no entry folds to **`null`**, not 0
- [ ] `OutboxMessagingGateTests` — SMS never consulted (AC-4.6); the subscription refusal wins (AC-4.7, EC-8); the
      capability off issues **zero** queries; a missing row holds under its **own** reason (AC-4.3); **term order**
      holds (template before either allowance term)
- [ ] `NotificationJobMessagingTests` — send-and-count is **one commit** (EC-14); 23:59/00:01 Tunis month boundary
      (EC-7); a held row is never purged while it could still be sent (AC-4.4); a released row whose appointment has
      passed fails as obsolete (AC-4.5, EC-1) — asserted on the **subscription** release path too; **a recall row
      (`appointmentId: null`) held past `HeldMaxDays` drains**; **a unique-violation on the month row's creation cannot
      cost a send** (§ 14a)
- [ ] `ClinicCreationMessagingAllowanceTests` derives the door set by scanning for `new Clinic(`, not by listing
- [ ] Migration rehearsed on a **throwaway** DB seeded with several cabinets: gating and counts confirmed; then applied
      for real with `verify-schema` **diffed** before/after

### Part 2 — The practice sees what it has left and is warned before it runs out

*(Spec boundary 2 · US-2, US-3, US-5.)*

16. `GetReminderAllowanceQuery` + `GetReminderAllowanceHistoryQuery`, floored per **D-5**
    (`max(creation month, earliest row)`; a gap inside the range is « non mesuré », a month below the floor is
    **omitted**). Add `MessagingSenderState` so AC-1.4's five states have one derivation.
17. Expose both on `ClinicsController` as **`AnyClinicRole`** (AC-2.2), 404 **before the mediator** where the
    capability is false (AC-1.6, EC-16).
18. `MessagingAllowanceThresholds` (**all** crossed thresholds, not only the largest — FR-6) and the
    `INotificationGenerator` pair, deduped on **(cabinet, month, threshold)** via the two new `StaffNotification`
    columns. Message derived from threshold + allowance + month (AC-3.5).
19. Evaluate the thresholds **post-commit, best-effort** where the counter is incremented, so 80 % is announced when
    crossed rather than the next morning.
20. `MessagingAllowanceJob` (D-2), `Cron.Daily`, capability-gated with `RemoveIfExists` in the else, « today » as a
    parameter: provision the month row for every cabinet, **then** reconcile the warnings (AC-3.6 withdrawal, AC-3.7
    rollover withdraw + re-arm). A suspended or expired cabinet is **not** warned. One try/catch **per cabinet per
    duty**; provisioning runs **first** (R-9).
21. US-5 — `RecallDispatchOutcome.MessagingAllowanceExhausted`, the `ReminderScheduler` branch (WhatsApp-only ⇒ refuse
    and enqueue nothing; SMS also sendable ⇒ enqueue both, AC-5.3), and `SendRecallCommand`'s refusal leaving the
    patient untouched.
22. **Client** — `web/lib/api/reminder-allowance.ts` and the three `web/components/rappels/` components, mounted in
    `web/app/rappels/page.tsx`. Tri-state availability; a failed read is a `LoadFailureNotice` with a retry, **never**
    an empty or zeroed table (AC-2.5, EC-12). AC-2.6's SMS sentence, AC-2.7's contact route (**no** route at all where
    unconfigured), and **FR-1's duplicate disclosure** in the help text. **Accessibility**: the figures in a **live
    region**; the failure state an **alert** role and a measured zero a **status** role — never the same component.
23. AC-4.9 — the machine-readable reason on `reminder-log-table.tsx` and the « en attente de forfait » counter,
    distinct from the undifferentiated « Bloqués ».

**Part 2 commit point.** Validation:
- [ ] Three thresholds crossed in one afternoon produce **three** unread rows, each badging the bell (AC-3.1/3.2); a
      threshold holding for days restates nothing (AC-3.5); a zero allowance produces the 100 % row only
- [ ] A grant below a threshold **withdraws** the rows no longer met (AC-3.6); a rollover withdraws and re-arms (AC-3.7)
- [ ] `StaffNotificationRules.ReachesALockedPhone` returns **`false`** — asserted, since AC-3.4 is honoured by
      classifying, never by omitting
- [ ] A quiet month reads « 0 rappel envoyé »; only a month with no row reads « non mesuré »; a pre-rollout month is
      **absent** (AC-2.4, D-5)
- [ ] `RecallMessagingRefusalTests` — AC-5.1–5.4, patient untouched, outcome distinct from `NoChannelConfigured`
- [ ] The figures announce in a live region; failure = alert role, measured zero = status role
- [ ] The duplicate-counting disclosure is on the screen (FR-1)
- [ ] `npm run check:responsive` + `npx tsc --noEmit` + `npm run build`, then an eye pass at 320/390/820/1180/1440 px

### Part 3 — The vendor allocates, corrects, and sees the portfolio

*(Spec boundary 3 · US-6, US-7, US-8, US-9.)*

24. `GrantMessagingAllowanceCommand` + `CancelMessagingAllowanceCommand` (the **vendor** pair — no controller may name
    them, AC-9.3) and their console wrappers, each staging the `PlatformAccessEntry` **before**
    `MessagingAllowanceRefold`'s single save (AC-6.8).
25. AC-6.4a the server decides standing-vs-top-up from the figure in force for the current month; AC-6.5 a top-up
    naming a past month is refused; AC-6.6 complimentary carries **no** amount rather than zero.
26. `PlatformMessagingController` — its own controller, two routes, `[AllowsWithoutSubscription]`, 409 on AC-7.5.
27. Extend `GetPlatformClinicDetailQuery` (AC-8.1, per-entry `IfCancelled` re-folded **server-side** — AC-7.3) and
    `ListPlatformClinicsQuery` + `ClinicActivityRepository`'s `PortfolioJoin` (AC-8.2/8.3, a SQL predicate over the
    **stored snapshot**, LEFT-joined so an unmeasured cabinet still appears). **« Near » is
    `consumed >= 0.90 × allowance`**, one named constant read by both the predicate and the console's chip.
28. Declare every new leaf in `PlatformReadShape.AllowedLeafNames` — and **only** those the DTOs return, since an
    **unused** declaration fails too. ⚠️ Do **not** reuse `Note`/`Reference` (`:111-112`), which are declared as
    vendor-**payment** fields.
29. Console UI — the « Messagerie » section, the two sheets, the cancellation dialog, plus the BFF hops.
30. The three verbs and `MessagingReportService` — exit 0 clean / 1 couldn't run / **2** findings, AC-9.4's three
    finding kinds distinguished. `messaging-report` takes **`--clinic`** *and* **`--month AAAA-MM`**.

**Part 3 commit point.** Validation:
- [ ] A double-click produces **one** entry and replays the first outcome (AC-6.7, EC-6); two *different* allocations
      both land and are both kept (EC-5)
- [ ] `MessagingVendorCommandReachabilityTests` — no controller source names either vendor command, and **every verb is
      dispatched by `Program.cs`** (a missing branch boots the web host and reads as « it did nothing »)
- [ ] `PlatformReadShapeTests` green **in both directions**; nothing in the new DTOs names a patient, an act or a
      per-patient amount (AC-8.5)
- [ ] A failed portfolio read renders « je n'ai pas pu lire », never an empty portfolio (AC-8.4, EC-12)
- [ ] **AC-6.9 confirmed by trying it**: sign in, run `platform-account --deactivate`, call both writes with the same
      token — refused on the next request. ⚠️ `PlatformAccountStateMiddleware` was **inert in production for
      `platform-console` Parts 1–6** while every layer reported it present; « already handled upstream » is exactly the
      assumption that failed
- [ ] « Near exhausted » is `>= 90 %` in **both** the SQL predicate and the chip label, from one constant
- [ ] `messaging-report --month` answers for a **closed** month
- [ ] `console/`'s own `check-responsive.mjs` + `tsc --noEmit` + `build`

### Part 4 — The cabinet connects, the template is submitted, Meta's refusals are told apart

*(Spec boundary 4 · US-1 and FR-7/7a/7b/8.)* **⚠️ Decide before starting: does Branch B make this its own story (R-2)?**

31. **Apply Story 0's chosen branch.**
32. `IWhatsAppTemplateService` + `WhatsAppTemplateService` — submit the French `UTILITY` reminder template, whose body
    must **not** start or end with a variable: « Rappel de votre rendez-vous chez {{1}} le {{2}}. », never
    « {{1}} : rappel… ».
33. `ClinicReminderSettings`' four template columns and `ConnectWhatsAppCommand`'s post-exchange submission (AC-1.3).

**33a. FR-7's hold — add the template term to `OutboxMessagingGate`** (the slot § 12 declared), so a cabinet whose
    template is `NotSubmitted`/`PendingReview`/`Rejected`/`Paused`/`Disabled` has its reminders **held** under
    `MessagingTemplateNotReady`, consuming nothing, released on approval (EC-9, EC-10).
   - ⚠️ **This cannot live in the sender.** A sender runs *after* the send call, so « consume nothing » is already
     lost: with no pre-send term, `WhatsAppSender` calls Meta with an unapproved template, Meta refuses, § 37
     classifies it as **transient**, and the row burns three retries and dies — the opposite of EC-9. If Meta happens
     to accept, FR-1 counts a unit against a template the cabinet cannot use.
   - ⚠️ Add it in **both** § 13 call sites, or a template-parked row is released the moment the *allowance* is topped up.

34. **Writer 1: `MetaWebhookController`** — `[AllowAnonymous]`, `X-Hub-Signature-256` over the **raw** body. Subscribe
    to **`message_template_status_update`** (messaging-limit changes arrive on
    `business_capability_update`/`account_alerts`, **not** `account_update` — the wrong field yields a callback that is
    silently never called). Add both actions to `ControllerAuthorizationCoverageTests`' `ExpectedAnonymous`.
   - ⚠️ **(a) Declare `ITenantScope.UseSystemWide("Meta template status webhook")` as the action's first act.**
     Anonymous ⇒ no `User` row ⇒ `TenantScopeMiddleware` leaves the scope `Unset` ⇒ `ClinicReminderSettings` (filtered
     on `Id == ScopedClinicId`, `ApplicationDbContext:238`) reads **zero rows with no error**. Without it the webhook
     verifies, parses, resolves nothing, writes nothing and answers **200 to Meta**.
   - ⚠️ **(b) Add `GetByWhatsAppBusinessAccountIdAsync`** — there is **no** WABA→cabinet lookup in the product today.
   - ⚠️ **Neither derived guard catches this**: `SystemWideCallerCoverageTests` derives from « reads a filtered entity
     with **no HTTP context** », and a webhook has one — so add this controller explicitly. And the symptom is not an
     error: FR-7a's poll picks the state up next pass, so the only effect is AC-1.5 silently degrading from minutes to
     a day.
35. **Writer 2: the reconciling poll** as `MessagingAllowanceJob`'s third duty, over cabinets **not** in a terminal
    state (FR-7a — the webhook without the poll has no recovery).
36. FR-7b — store the template **category**, state it in words on the console file when not `UTILITY`, make it a
    `messaging-report` finding. **Not** surfaced clinic-side; does **not** hold reminders.
37. FR-8 — the two new sender outcomes and `WhatsAppSender`'s classification (D-8: the body never reaches the result),
    wired into `NotificationJob`'s switch.
   - ⚠️ **Classify on the FULL body.** The base truncates at **200 characters** for its log line
     (`HttpReminderChannelSender:24,77`), and Meta puts a long `message` before `code` — so classifying off the
     truncated string matches nothing on a real payload: `131048`/`131064` burn three retries instead of holding
     (EC-11) and the throttle codes consume retry budget, with FR-8 reading as implemented at every layer.
   - ⚠️ **A short fixture cannot see this.** `{"error":{"code":131048}}` is under 200 chars, so the suite would be green
     against a sender that fails on every genuine response. Use a **realistic full-length** envelope.
   - ⚠️ **Make `DispatchAsync`'s outcome `switch` exhaustive** — it has **no `default`** today (`:324-363`), so an
     unnamed member falls through with no save, no log, no state change, and the row is re-attempted for ever.
38. **Client** — `whatsapp-connect-card.tsx` with AC-1.2's handset sentence **before** the flow starts, AC-1.4's five
    states in words, AC-1.5's 24 h sentence; and AC-1.7's closure of the manual credential fields, refused server-side
    in `UpdateClinicReminderSettingsCommand` too.

**Part 4 commit point.** Validation:
- [ ] `WhatsAppSenderErrorClassificationTests` covers the six codes **and** asserts no response body reaches the result
      (the `SECURITY_REVIEW_2026-08` finding must not regress)
- [ ] At least one code per outcome asserted against a **full-length** Meta envelope (>200 chars, `code` after a long
      `message`)
- [ ] `DispatchAsync`'s outcome `switch` has a `default` that parks and logs, asserted
- [ ] A template under review holds reminders **pre-send** (the sender is never called, no unit counted), consumes
      nothing, releases on approval (EC-9); a `DECLINED` template says so with a contact route and runs **no**
      resubmission loop (EC-10); `131048`/`131064` hold rather than burning three retries (EC-11)
- [ ] A template-parked row is **not** released by an allowance top-up (AC-4.8)
- [ ] A forged webhook signature is rejected; the verify handshake answers `hub.challenge`
- [ ] **The webhook actually writes** — a valid `message_template_status_update` for a known WABA moves that cabinet's
      template state, asserted with the tenant scope left as production leaves it (`Unset`). A test that sets a scope by
      hand asserts the one arrangement that is broken
- [ ] `ReminderSettingsChannelIsolationTests` still green, **byte-for-byte unchanged** — closing the manual fields must
      not weaken `ClaimsItsOwnWhatsApp` (R-8). If the test needs *any* edit, stop and re-read the security review

### Part 5 — Verification, guards and documentation

39. Add the three `verify-schema` checks and the `SchemaVerificationReader` projection.
40. Run `verify-schema` **and** `reconcile-money` before and after the migration batch and **diff**.
41. **Confirm EC-16 by reprofiling** to `SelfHostedLan` and `CloudBrowser`: no section, no notifications, no
    enforcement, no scheduled work, endpoints answering as though they do not exist, existing WhatsApp behaviour
    unchanged.
42. Update `CLAUDE.md`, the three `api/*/CLAUDE.md`, `web/CLAUDE.md`, `console/` docs, `deploy/README.md`, and write the
    operator runbook for the three verbs and the Meta account setup they assume.
43. Capture the spec's four open questions and the « cabinets already using their own credentials » migration as
    follow-up items.

**Part 5 commit point.** Validation:
- [ ] `verify-schema` exits **0** after the batch; the before/after diff shows only the intended objects
- [ ] The whole unit suite green; `dotnet build` clean; `web` and `console` gates green
- [ ] Every `CLAUDE.md` touched by this feature reflects it

## Files to Create/Modify

The authoritative tables are in [`../plan.md`](../plan.md#files-to-modifycreate). Grouped here by part for scope:

### Files to Create (~28)

| Part | Files |
|------|-------|
| 0 | `Application/Common/Interfaces/IMessagingAllowancePolicy.cs`, `IVendorMessagingAvailability.cs`; `Infrastructure/Services/MessagingAllowancePolicy.cs`, `VendorMessagingAvailability.cs`; `Domain/Enums/MessagingAllowanceKind.cs`, `WhatsAppTemplateStatus.cs` |
| 1 | `Domain/Entities/MessagingAllowanceEntry.cs`, `ClinicMessagingMonth.cs`; `Domain/Services/MessagingAllowanceLedger.cs`; `Domain/Repositories/IMessagingAllowanceRepository.cs`; `Application/Features/Messaging/MessagingAllowanceRefold.cs`, `MessagingRefusals.cs`, **`OutboxMessagingGate.cs`**; `Infrastructure/Repositories/MessagingAllowanceRepository.cs`; two `Persistence/Configurations/*`; `Infrastructure/Migrations/<ts>_AddClinicMessagingAllowances.cs` (+ Designer, snapshot) |
| 2 | `Application/Features/Messaging/MessagingAllowanceThresholds.cs`, `MessagingSenderState.cs`, `Queries/GetReminderAllowanceQuery.cs`, `Queries/GetReminderAllowanceHistoryQuery.cs`; `API/BackgroundJobs/MessagingAllowanceJob.cs`; `web/lib/api/reminder-allowance.ts`; `web/components/rappels/messaging-allowance-card.tsx`, `messaging-allowance-history.tsx` |
| 3 | `Application/Features/Messaging/Commands/GrantMessagingAllowanceCommand.cs`, `CancelMessagingAllowanceCommand.cs`; `Features/Platform/Commands/RecordMessagingAllowanceFromConsoleCommand.cs`, `CancelMessagingAllowanceFromConsoleCommand.cs`; `Application/Common/Maintenance/MessagingReportService.cs`; `API/Controllers/Platform/PlatformMessagingController.cs`; `API/Maintenance/MessagingCommands.cs`; `console/components/messaging-section.tsx`, `record-allowance-sheet.tsx`, `cancel-allowance-dialog.tsx`; `console/app/bff/forfaits/route.ts`, `forfaits/annulations/route.ts` |
| 4 | `Application/Common/Interfaces/IWhatsAppTemplateService.cs`; `Infrastructure/Services/WhatsAppTemplateService.cs`; `API/Controllers/MetaWebhookController.cs`; `web/components/rappels/whatsapp-connect-card.tsx` |

### Files to Modify (~30)

| Part | Files |
|------|-------|
| 0 | `Application/Common/ClinicClock.cs`; `Features/Platform/Queries/GetPlatformSummaryQuery.cs`; `Features/Platform/PlatformAccessLabels.cs`; `Domain/Enums/OutboxBlockReason.cs`, `PlatformAccessAction.cs`, `NotificationCategory.cs`, `NotificationTargetKind.cs`; `Application/Common/Services/StaffNotificationRules.cs`; `Infrastructure/Deployment/DeploymentProfile.cs`; `Infrastructure/Extensions.cs` |
| 1 | `Domain/Entities/StaffNotification.cs`; `Application/Features/Clinics/LocalClinicProvisioning.cs`; `API/BackgroundJobs/NotificationJob.cs`; `API/Program.cs` |
| 2 | `Domain/Repositories/IStaffNotificationRepository.cs`; `Application/Common/Interfaces/INotificationGenerator.cs` + `Common/Services/NotificationGenerator.cs`; `Common/Interfaces/IReminderScheduler.cs`; `Infrastructure/Services/ReminderScheduler.cs`; `Features/Recall/Commands/SendRecallCommand.cs`; `API/Controllers/ClinicsController.cs`; `web/app/rappels/page.tsx`; `web/components/rappels/reminder-log-table.tsx` |
| 3 | `Features/Platform/PlatformReadShape.cs`; `Features/Platform/Queries/GetPlatformClinicDetailQuery.cs`, `ListPlatformClinicsQuery.cs`; `Infrastructure/Repositories/ClinicActivityRepository.cs`; `console/app/cabinets/[clinicId]/page.tsx`; `console/components/clinic-portfolio.tsx`, `portfolio-filters.tsx` |
| 4 | `Domain/Entities/ClinicReminderSettings.cs`; **`Domain/Repositories/IClinicReminderSettingsRepository.cs` + `Infrastructure/Repositories/ClinicReminderSettingsRepository.cs`** (the WABA lookup); `Infrastructure/Services/IReminderChannelSender.cs`, `WhatsAppSender.cs`, `HttpReminderChannelSender.cs`; `Features/Clinics/Commands/UpdateClinicReminderSettingsCommand.cs`, `ConnectWhatsAppCommand.cs`; `web/components/reminder-settings.tsx` |
| 5 | `Application/Common/Maintenance/SchemaVerificationService.cs`; `Infrastructure/Persistence/SchemaVerificationReader.cs`; `deploy/docker-compose.hosted.yml`, `.env.hosted.example`, `README.md`; `CLAUDE.md` + the four sub-guides |

## Verification Steps

**Verification commands:**
```bash
# ── Backend: build + tests OUTSIDE the repo (Smart App Control, R-14) ────────────
cd api
dotnet build -p:BaseOutputPath="$TEMP/cm-build/"
dotnet test -c Release -p:BaseOutputPath="$TEMP/cm-test/"

# Targeted, per part
dotnet test -c Release -p:BaseOutputPath="$TEMP/cm-test/" \
  --filter "FullyQualifiedName~MessagingAllowanceLedgerTests|FullyQualifiedName~OutboxMessagingGateTests|FullyQualifiedName~NotificationJobMessagingTests"

# ── Schema + money gates: BEFORE and AFTER the migration batch, then DIFF ────────
cd ClinicManagement.API
dotnet run -- verify-schema    > "$TEMP/verify-before.txt"
dotnet run -- reconcile-money  > "$TEMP/reconcile-before.txt"
# ... apply the migration ...
dotnet run -- verify-schema    > "$TEMP/verify-after.txt"
dotnet run -- reconcile-money  > "$TEMP/reconcile-after.txt"
diff "$TEMP/verify-before.txt" "$TEMP/verify-after.txt"

# ── The three new verbs ─────────────────────────────────────────────────────────
dotnet run -- messaging-report --month 2026-07     # a CLOSED month
dotnet run -- messaging-grant --clinic <id|email> --per-month 200

# ── Frontend gates ──────────────────────────────────────────────────────────────
cd ../../web     && npm run check:responsive && npx tsc --noEmit && npm run build
cd ../console    && node check-responsive.mjs && npx tsc --noEmit && npm run build
```

Manual / operator verification (not CI-runnable): the before/after `verify-schema` **diff**; the **EC-16 reprofile
walk**; the responsive eye pass at five widths on both `web/` and `console/`; the **AC-6.9 deactivated-account walk**;
and printing/confirming anything Story 0 flagged.

## Exit Criteria

This story is complete when:

- [ ] All six parts are committed, each with the build and unit suite green at its boundary
- [ ] Every acceptance criterion above is checked
- [ ] `verify-schema` exits **0** and the before/after diff shows **only** the intended objects; the three new checks
      (`monthly-allowance-matches-ledger`, `messaging-month-covers-every-clinic`,
      `messaging-allowance-entry-has-one-form`) are green
- [ ] `reconcile-money` shows the migration moved **no** closed month
- [ ] The whole unit suite green; `dotnet build` clean; `web/` and `console/` gates green + eye pass done
- [ ] **EC-16 verified by reprofiling** to both other deployment kinds
- [ ] All derived guard tests green in **both** directions — `PlatformReadShapeTests`,
      `ControllerAuthorizationCoverageTests`, `RealtimeResourceResolverTests`, `SubscriptionExemptionCoverageTests`,
      `SystemWideCallerCoverageTests` (with the webhook added), `MessagingVendorCommandReachabilityTests`,
      `ClinicCreationMessagingAllowanceTests`
- [ ] `ReminderSettingsChannelIsolationTests` **unchanged** and green (R-8)
- [ ] `CLAUDE.md` and the four sub-guides reflect the feature; `deploy/README.md` carries the verb runbook
- [ ] The spec's four open questions and the own-credentials migration captured as follow-up items

## Notes

- **The three genuinely new things** carry the design risk and deserve the most care: **counting** (an increment staged
  into the dispatcher's existing per-row save — but see § 14a for what must *not* be staged), **Meta error
  classification** (today every non-2xx collapses into one `TransientFailure`; FR-8 needs five outcomes), and
  **template state with two writers** (a webhook and a reconciling poll, neither a substitute for the other). There is
  **no template call of any kind in the product today**.
- **Six findings from `/challenge-plan` are load-bearing and easy to undo by accident.** Three of them fail *silently*
  and would ship green: the webhook's missing scope (§ 34), the truncated-body classification (§ 37), and the absent
  template hold (§ 33a). Read those three ⚠️ blocks before touching Part 4.
- **`ClinicMessagingMonth` is a plain `Entity<Guid>`, not an `AggregateRoot`** (D-6): it is incremented on every
  WhatsApp reminder sent, minutely, and `AuditSaveChangesInterceptor` writes one row per mutated aggregate root — which
  is exactly why `Notification` is on its exclusion list. The audited artefact is the **ledger entry**.
- **`Messaging:*` config, `IMessagingAllowancePolicy` and `IVendorMessagingAvailability` register in
  `AddInfrastructure`, not `AddApplication`** — `provision-clinic` builds its container from that method **alone** and
  it creates a cabinet, which must not exist without an allowance (FR-3).
- **The vendor's money is never the clinic's** (FR-2): nothing here reaches an invoice, la caisse, « Créances », the
  dashboard's Argent section or a patient's balance, and `MoneyReadConsistencyTests` stays **unchanged**.
- R-12: ship the default allowance as a **provisional** operator-config number. Nothing in the code depends on its
  value, and the daily pass rewrites the snapshot from the fold on its next run.
