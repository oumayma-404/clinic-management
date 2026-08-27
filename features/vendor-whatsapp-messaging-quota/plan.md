# Implementation Plan: Forfait de rappels WhatsApp (vendor-purchased messaging quota)

**Status:** APPROVED
**Challenged:** Yes
**Created:** 2026-08-12
**Spec:** [features/vendor-whatsapp-messaging-quota/spec.md](./spec.md) (APPROVED, challenged)
**Exploration:** [features/vendor-whatsapp-messaging-quota/exploration.md](./exploration.md)

---

## Overview

The spec's nine user stories are delivered as **one user story with six ordered internal parts**, at the user's
explicit instruction. Each part is a vertical increment (domain → repository → handler → API → UI where the part has
one), and each part boundary is a commit point `/implement-story` lands at. The oversize is registered as **R-1** with
its split point named, so the decision is reversible without re-planning.

The architecture is **`clinic-subscription`'s, applied to a second entitlement** — which is not a coincidence but the
spec's own instruction (« this feature reuses its shape, not its code paths »). Concretely, four of that feature's
load-bearing decisions are reproduced rather than reinvented:

- **An append-only ledger folded by a pure, total, clock-free function.** `MessagingAllowanceLedger.Fold(entries,
  monthKey)` takes the month as a parameter and reads no clock, exactly as `SubscriptionLedger.Fold` takes no
  « today » — so a retried write computes the same figure as the write it raced, and `verify-schema` does not flap
  daily. Cancellation is `SubscriptionPeriod.Cancel`'s shape: the row is kept, struck through, motif mandatory.
- **A stored denormalisation of that fold, because the console filters the portfolio on it.** `ClinicSubscription.
  LatestCoverKind` exists so « en essai » can be a SQL predicate before a page is cut (AC-2.4a); here
  `ClinicMessagingMonth.AllowanceMessages` is the same move for AC-8.2's exhausted/near-exhausted filter, and
  `verify-schema`'s `monthly-allowance-matches-ledger` re-derives it through the **real** fold, on
  `subscription-cover-kind-matches-ledger`'s precedent.
- **One gate consulted at dispatch *and* at un-park.** `OutboxMessagingGate` mirrors `OutboxSubscriptionGate`
  line for line — one instance per tick, per-cabinet cache, reads nothing where the capability is off — and it is
  asked in **both** places from the start, because Part G already paid for learning that a reviewer asking only about
  the channel releases a row parked for a different reason within a minute (AC-4.8). It answers **FR-4 and FR-7
  together**, over ordered terms (template-not-ready → allowance-missing → allowance-exhausted), for that same
  argument one level down: two gates would be two things to remember at each of the four call sites, and the template
  term *must* be pre-send or FR-7's « consume nothing » is unachievable.
- **The vendor's writes are verbs first, and the console reuses their pieces rather than sending their commands.**
  `RecordMessagingAllowanceFromConsoleCommand` stages the `PlatformAccessEntry` **before** the single save, so
  AC-6.8's « in the same operation as the change it records » is true of one instant.

Three things are genuinely new to this repo and carry the design risk: **counting** (an increment staged into the
dispatcher's existing per-row save, so a crash loses both or neither — FR-1), **Meta error classification** (today
`HttpReminderChannelSender` collapses every non-2xx into one `TransientFailure`, and FR-8 needs five outcomes), and
**template state with two writers** (a webhook and a reconciling poll, neither a substitute for the other — FR-7a).
There is no template call of any kind in the product today.

### Decisions taken during planning

| # | Decision | Reasoning |
|---|---|---|
| D-1 | **Part 0 opens with a blocking Embedded-Signup version spike**, and the plan declares both branches. ✅ **RESOLVED — and by a third outcome neither branch covered: the current version is v4, and we are on v3.** See `progress.md` and D-1a. | The confirmation needs a logged-in Meta browser session. Hoisting only the spike (rather than reordering the parts) means its answer is known before any connection work is built, which is what the spec's « the expensive order » warning asks for, without moving four parts around an unknown. **It earned its keep exactly as intended**: both declared branches were wrong, and the answer arrived before a line of connection work was written. |
| D-1a | **Migrate to Embedded Signup v4 inside Part 4**, rather than deferring it | ✅ Spike result (Meta's implementation page, 28 Jun 2026): **v2 is deprecated 15 Oct 2026 and the migration target is v4**. We are on **v3** (`sessionInfoVersion: "3"`), so the deadline **does not name us** — but v4 is current and **v3's own end date is unread**. Deciding: we are about to make this flow load-bearing for the vendor's billing *and* build template submission on top of it, so shipping onto a superseded version is precisely « the expensive order » the spike exists to avoid. The delta is small enough that deferring buys almost nothing — our config differs from Meta's v4 sample by **one key**, and our `message` handler already reads v4's documented payload verbatim. |
| D-2 | **One new daily job, `MessagingAllowanceJob`**, carrying the month-row provisioning, the warning reconciliation *and* the template poll | All three read the same two rows per cabinet in the same loop, under the same capability gate and the same `clinicToday` parameter. `SubscriptionWarningJob`'s registration shape (capability-gated, `RemoveIfExists` in the else, « today » as a parameter). |
| D-3 | **« Passé » means `appointment.AppointmentDateTime <= nowUtc`** for AC-4.5a's dispatch-time guard | A reminder announces something about to happen; once the patient is due in the chair it announces nothing. `nowUtc` is a parameter, like the gate's `clinicToday`, so the boundary is testable. |
| D-4 | **Parts are: 0 groundwork · 1 ledger+counting+enforcement · 2 clinic surface+warnings · 3 console+verbs+report · 4 template+Meta classification · 5 verification** | The spec's own four boundaries, plus a groundwork part for what everything depends on (`ClinicClock` months, the four FR-8a extension points, the capability) and a closing verification part for the before/after `verify-schema` diff the repo requires of a migration. |
| D-5 | **A pre-rollout month is omitted from the history entirely**, like a month before the cabinet existed | AC-2.4's own precedent applied to the other end. The floor is `max(cabinet creation month, the cabinet's earliest `ClinicMessagingMonth` row)` — **derived, needing no config key and no floor column**, because the rollout migration writes that first row. A gap *inside* the range still reads « non mesuré », which is exactly right. |
| D-6 | **`ClinicMessagingMonth` is a plain `Entity<Guid>`, not an `AggregateRoot`** | `AuditSaveChangesInterceptor` writes one row per mutated **aggregate root**, and this row is incremented on every WhatsApp reminder sent, minutely. That is precisely why `Notification` is on the interceptor's exclusion list — a clinic's real history would be buried in machine noise within a day. The audited artefact is the **ledger entry**; the snapshot is derived from it. |
| D-7 | **The month key is an `AAAA-MM` string end to end** | The spec, both endpoints, the console commands and `ClinicClock`'s new primitive all speak it; lexicographic ordering is correct for `AAAA-MM`, so « effective month ≤ M » needs no conversion. Trade-off accepted: a `DateTime` first-of-month would be more idiomatic for this repo's dates, at the cost of a conversion at every boundary. |
| D-8 | **`WhatsAppSender` parses Meta's error body *inside the sender* and returns only a classified outcome** | `SECURITY_REVIEW_2026-08` deliberately stopped the gateway's response body reaching the result, because that string is persisted on the outbox row and served by `reminder-log` (`AnyClinicRole`) while the endpoint URL is tenant-supplied. FR-8 needs the code, not the body — so the code is read and discarded inside the sender, and the wording is ours. |

---

## Files to Modify/Create

### Files to Create

| File | Purpose |
|------|---------|
| `api/ClinicManagement.Domain/Entities/MessagingAllowanceEntry.cs` | The append-only allocation ledger row. `AggregateRoot` (so the audit interceptor sees a grant and a cancellation). Standing **xor** top-up, an `EffectiveMonth` decided by the server, `Cancel(reason, by, whenUtc)` refusing a double cancel. |
| `api/ClinicManagement.Domain/Entities/ClinicMessagingMonth.cs` | One counting row per (cabinet, Tunisian month): the month's **allowance snapshot** and its **consumed** count. Plain `Entity<Guid>` — see D-6. `RecordSend()` / `SetAllowance(int)`. |
| `api/ClinicManagement.Domain/Enums/MessagingAllowanceKind.cs` | `Standing = 1`, `TopUp = 2`. |
| `api/ClinicManagement.Domain/Enums/WhatsAppTemplateStatus.cs` | `NotSubmitted=0, PendingReview=1, Approved=2, Rejected=3, Paused=4, Disabled=5` — AC-1.4's five clinic-facing states derive from this plus the connection status. |
| `api/ClinicManagement.Domain/Services/MessagingAllowanceLedger.cs` | FR-2's fold. **Pure, total, clock-free**; `Fold(entries, monthKey) → int?` (null = no allowance record, FR-4's second branch) and `EffectiveMonthFor(entries, newFigure, currentMonthKey)` for AC-6.4a's server-side raise/lowering decision. |
| `api/ClinicManagement.Domain/Repositories/IMessagingAllowanceRepository.cs` | The ledger + the month rows. `GetEntriesAsync` deliberately **not** paged (a fold over a page is not a fold — `IClinicSubscriptionRepository`'s stated reason), `GetMonthAsync(clinicId, monthKey)`, `GetMonthsAsync(clinicId, fromKey)`, `GetForReportAsync()` returning **every cabinet** beside its row or `null`. |
| `api/ClinicManagement.Application/Features/Messaging/MessagingAllowanceRefold.cs` | Rewrites every affected month's `AllowanceMessages` from the fold and saves once, with a bounded 5-attempt `ConflictException` retry detaching the ledger — `SubscriptionRefold`'s shape and its reasoning (EC-5). |
| `api/ClinicManagement.Application/Features/Messaging/MessagingRefusals.cs` | FR-10's clinic-facing sentences and their codes, in **one** place — `SubscriptionRefusals`' law: what still works before what does not, name the date or the remedy, never mention signing in or out. ⚠️ **The exhausted sentence names the top-up as the remedy and the renewal date as a fact about the *allowance* — it must never promise that the held reminders themselves go out on the 1st.** They are for visits about a day away, so by then they are refused as obsolete (AC-4.5); the sentence points the practice at « Rappels » to see which patients were not prevented instead. |
| `api/ClinicManagement.Application/Features/Messaging/OutboxMessagingGate.cs` | FR-4's **and FR-7's** enforcement — « may this cabinet's WhatsApp reminders leave the building? ». One instance per tick, per-cabinet cache, `OutboxBlock?`; **WhatsApp rows only** (AC-4.6), asked **after** the subscription gate (AC-4.7), reads nothing where the capability is off. ⚠️ **One gate over ordered terms, not two gates**: `ReviewAsync` returns the first applicable of **template-not-ready → allowance-missing → allowance-exhausted**, so one per-cabinet cache covers *both* reads (the settings row and the month row) and all four call sites have one thing to remember — `OutboxSubscriptionGate`'s own argument against a condition written twice per queue. The **order is the wording**: a cabinet with no usable template is told *that*, not that its allowance ran out. |
| `api/ClinicManagement.Application/Features/Messaging/MessagingAllowanceThresholds.cs` | FR-6's 80/95/100 arithmetic — which thresholds a (consumed, allowance) pair has crossed, **all** of them and not only the largest (deliberately the opposite of `SubscriptionStateReader.ThresholdReached`; the 80 % row is the one that could still have been acted on). |
| `api/ClinicManagement.Application/Features/Messaging/Commands/GrantMessagingAllowanceCommand.cs` | The **vendor** write, reached only by the `messaging-grant` verb. No controller may name it (AC-9.3). |
| `api/ClinicManagement.Application/Features/Messaging/Commands/CancelMessagingAllowanceCommand.cs` | The vendor cancellation, reached only by `messaging-cancel`. |
| `api/ClinicManagement.Application/Features/Messaging/Queries/GetReminderAllowanceQuery.cs` | US-2's current-month read. |
| `api/ClinicManagement.Application/Features/Messaging/Queries/GetReminderAllowanceHistoryQuery.cs` | US-2's thirteen months, floored per D-5. |
| `api/ClinicManagement.Application/Features/Messaging/MessagingSenderState.cs` | AC-1.4's five states as one derivation from `(WhatsAppConnectionStatus, WhatsAppTemplateStatus)`, so « connecté » can never be presented as « prêt à envoyer » anywhere. |
| `api/ClinicManagement.Application/Features/Platform/Commands/RecordMessagingAllowanceFromConsoleCommand.cs` | US-6's console write. Access row staged **before** `MessagingAllowanceRefold`'s single save; idempotency key on the existing partial-unique `PlatformAccessEntry.IdempotencyKey`. |
| `api/ClinicManagement.Application/Features/Platform/Commands/CancelMessagingAllowanceFromConsoleCommand.cs` | US-7. 409 `messaging_allowance_entry_already_cancelled` (AC-7.5). |
| `api/ClinicManagement.Application/Common/Interfaces/IMessagingAllowancePolicy.cs` | The operator-config half: the default standing allowance, the vendor's contact e-mail and phone (AC-2.7). `ISubscriptionPricing`'s precedent — a figure must not be compiled in. |
| `api/ClinicManagement.Application/Common/Interfaces/IVendorMessagingAvailability.cs` | FR-9's last bullet: `SellsVendorMessaging` **AND** the deployment's Meta credentials. `IOsPushAvailability`'s split, which is what keeps the capability itself un-flippable by config. |
| `api/ClinicManagement.Application/Common/Interfaces/IWhatsAppTemplateService.cs` | FR-7: submit the French utility reminder template on the cabinet's behalf; read one template's status back for the poll. |
| `api/ClinicManagement.Application/Common/Maintenance/MessagingReportService.cs` | The `messaging-report` core (AC-8.6, AC-9.4). **Not DI-registered** — `MoneyReconciliationService`/`SubscriptionReportService`'s rule; lives here so `UnitTests` can exercise it. |
| `api/ClinicManagement.Infrastructure/Repositories/MessagingAllowanceRepository.cs` | EF implementation. Both tables carry a non-nullable `ClinicId` and are filtered, so **no `IgnoreQueryFilters()` anywhere** — a cross-cabinet caller declares `UseSystemWide`. |
| `api/ClinicManagement.Infrastructure/Persistence/Configurations/MessagingAllowanceEntryConfiguration.cs` | FK cascade to `Clinics`; indexes `(ClinicId, RecordedAtUtc)` and `(ClinicId, EffectiveMonth)`. |
| `api/ClinicManagement.Infrastructure/Persistence/Configurations/ClinicMessagingMonthConfiguration.cs` | FK cascade; **unique** `(ClinicId, MonthKey)` — what makes the daily pass idempotent. |
| `api/ClinicManagement.Infrastructure/Services/WhatsAppTemplateService.cs` | `POST /{wabaId}/message_templates` (French, `UTILITY`, body must **not** start or end with a variable — a documented Meta rejection cause) + the single-template status read. |
| `api/ClinicManagement.Infrastructure/Services/MessagingAllowancePolicy.cs` | Reads `Messaging:*`. Parsed by hand, never `GetValue<T>` (which throws on an unconvertible value, and this figure is read while a cabinet is being provisioned). |
| `api/ClinicManagement.Infrastructure/Services/VendorMessagingAvailability.cs` | `DeploymentProfile.SellsVendorMessaging` **AND** `MetaConfig` credentials present. |
| `api/ClinicManagement.Infrastructure/Migrations/<ts>_AddClinicMessagingAllowances.cs` (+ `.Designer.cs`, snapshot) | The two tables, the six new columns, and the rollout backfill. See **Migrations**. |
| `api/ClinicManagement.API/Controllers/MetaWebhookController.cs` | FR-7a's webhook writer. `GET` verify handshake + `POST` `message_template_status_update`, both `[AllowAnonymous]`, guarded by `X-Hub-Signature-256` HMAC-SHA256 over the **raw** body with `Meta:AppSecret`. ⚠️ **It must declare `ITenantScope.UseSystemWide("Meta template status webhook")` as its first act** — see the ⚠️ on Part 4 step 34: anonymous ⇒ no `User` row ⇒ `TenantScopeMiddleware` leaves the scope `Unset` ⇒ `ClinicReminderSettings` (filtered, `ApplicationDbContext:238`) reads **zero rows with no error**. |
| `api/ClinicManagement.API/Controllers/Platform/PlatformMessagingController.cs` | US-6/US-7's two writes. **Its own controller**, `PlatformClinicRestoreController`'s precedent — folding them into `PlatformSubscriptionsController` would make « the console's writes are about entitlements » false. |
| `api/ClinicManagement.API/BackgroundJobs/MessagingAllowanceJob.cs` | D-2's daily pass: provision the month row · reconcile the three warnings (withdraw AC-3.6, re-arm AC-3.7) · poll the template status of cabinets not in a terminal state. |
| `api/ClinicManagement.API/Maintenance/MessagingCommands.cs` | `messaging-grant` · `messaging-cancel` · `messaging-report` (the last taking **`--clinic`** *and* **`--month AAAA-MM`**, per the spec's CLI — a report that can only answer for the current month cannot answer for a month that has closed, which is when the vendor reconciles). Gated on `MaintenanceDatabase.HasConnectionString` (amendment M3), `RunAs("<verb>")`, `UseClinic(id)` for the two writes and `UseSystemWide` for the report. |
| `web/lib/api/reminder-allowance.ts` | The two clinic reads, through `client.ts` (no raw `fetch` — the `api-headers` check). |
| `web/components/rappels/messaging-allowance-card.tsx` | US-2's current-month section + AC-1.4's state statement + AC-2.7's contact route. Tri-state availability, `LoadFailureNotice` on a failed read. |
| `web/components/rappels/messaging-allowance-history.tsx` | The twelve preceding months — a `<Table>` **and** a `<CardList>` beside it (the `card-fallback` check; a `display:block` table strips the roles a screen reader needs). |
| `web/components/rappels/whatsapp-connect-card.tsx` | US-1's guided connection entry point and its five states, replacing the manual credential fields where the capability holds. |
| `console/components/messaging-section.tsx` | AC-8.1's cabinet-file section: figures, connection + template state (category in words when not `UTILITY`), allocation history with per-entry « Annuler ». |
| `console/components/record-allowance-sheet.tsx` | AC-6.1's two forms. `side="bottom"` + `lg:` centred, `dvh`, footer a `shrink-0` sibling; idempotency key minted in `openChanged(true)` — once per **opened sheet**. |
| `console/components/cancel-allowance-dialog.tsx` | AC-7.1/7.3: mandatory motif, and the server's own consequence sentence **above** the motif field. |
| `console/app/bff/forfaits/route.ts`, `console/app/bff/forfaits/annulations/route.ts` | The console's BFF hops; re-emit `{ error, code }` verbatim, 0 ⇒ 503. |

### Files to Modify

| File | Changes |
|------|---------|
| `api/ClinicManagement.Application/Common/ClinicClock.cs` | **FR-8b.** Add `MonthKey`, `CurrentMonthKey`, `MonthRangeUtc(monthKey)` (the **whole** month, inclusive both ends — `LastTickOfLocalDayUtc`'s reasoning), **`MonthToDateRangeUtc(clinicToday)`** (the 1st → the last tick of **today**), `MonthLabelFr` (`fr-FR` pinned explicitly), `NextMonthKey`, `PrecedingMonthKeys`, `FirstDayOfNextMonth` (AC-2.7's `resetsOn`). ⚠️ **Two range primitives, not one, and the names are the guard**: the existing private copy this replaces is month-to-**date**, so a single `MonthRangeUtc` would make the « move » below a silent widening. FR-8b's own point — month arithmetic is where a second copy is least visible — applies to the *distinction* as much as to the duplication. |
| `api/ClinicManagement.Application/Features/Platform/Queries/GetPlatformSummaryQuery.cs` | **Delete** the private `ClinicMonthRangeUtc` (`:93`) and call **`ClinicClock.MonthToDateRangeUtc`** — a genuine *move*, not a copy **and not a widening**. ⚠️ It is `(start of the 1st, LastTickOfLocalDayUtc(today))`, i.e. month-to-date, **not** the whole month; its one caller feeds `PlatformSummaryDto.VendorCollectedThisMonthDt` through `GetVendorCollectedBetweenAsync`, so substituting `MonthRangeUtc` would start counting vendor payments dated later this month as already collected (a post-dated cheque is a first-class concept here). Nothing in the suite would catch it: `MoneyReadConsistencyTests` deliberately does not cover the **vendor's** money (FR-2). |
| `api/ClinicManagement.Application/Features/Platform/PlatformAccessLabels.cs` | **Delete** `Month` (`:38`) and delegate to `ClinicClock.MonthLabelFr`. Same reason — two copies agree for eleven months out of twelve. |
| `api/ClinicManagement.Domain/Enums/OutboxBlockReason.cs` | Four members: `MessagingAllowanceExhausted=5`, `MessagingAllowanceMissing=6`, `MessagingTemplateNotReady=7`, `MessagingNumberStopped=8`. `HasConversion<int>()` ⇒ **no migration**. |
| `api/ClinicManagement.Domain/Enums/PlatformAccessAction.cs` | `GrantedMessagingAllowance=6`, `CancelledMessagingAllowance=7` — each arriving **with** the write that produces it. No migration. |
| `api/ClinicManagement.Domain/Enums/NotificationCategory.cs` | `MessagingAllowanceLow = 11`. |
| `api/ClinicManagement.Domain/Enums/NotificationTargetKind.cs` | `MessagingAllowance = 6`, carrying **no id** — the alert is about the clinic and everything it asks for is on `/rappels`. |
| `api/ClinicManagement.Application/Common/Services/StaffNotificationRules.cs` | Classify the new category `false` in `ReachesALockedPhone` (**AC-3.4**) and give it a `PushLabel`. Both are total switches that **throw** on an unclassified member, so omitting either fails at runtime, not silently. |
| `api/ClinicManagement.Domain/Entities/StaffNotification.cs` | `int? MessagingThresholdPercent` + `string? MessagingAllowanceMonth` and a `ForMessagingAllowance(...)` **factory** — `ForSubscription`'s precedent, and a real dedupe column rather than a message prefix. |
| `api/ClinicManagement.Domain/Entities/ClinicReminderSettings.cs` | `WhatsAppTemplateStatus`, `WhatsAppTemplateCategory`, `WhatsAppTemplateId`, `WhatsAppTemplateStatusCheckedAtUtc`, written through one `SetWhatsAppTemplateState(...)`. ⚠️ It is an `Entity<Guid>` keyed by the **clinic id**, not an `AggregateRoot`, so these four columns are **not** audited — the audited artefact stays the ledger entry (D-6), and the webhook's write has no actor by construction. |
| `api/ClinicManagement.Domain/Repositories/IClinicReminderSettingsRepository.cs` + `api/ClinicManagement.Infrastructure/Repositories/ClinicReminderSettingsRepository.cs` | **`GetByWhatsAppBusinessAccountIdAsync(wabaId)`** — the WABA→cabinet resolution FR-7a's webhook needs and which **does not exist today** (the interface has `GetByClinicIdAsync` alone; `BusinessAccountId` returns zero hits across every repository). It is the **one** deliberately `IgnoreQueryFilters()` read of this class, on `DeviceRegistrationRepository.GetByTokenAcrossClinicsAsync`'s precedent: a WABA id is globally unique, and the answer is needed *in order to* know whose row it is, so a scoped read structurally cannot find it. |
| `api/ClinicManagement.Domain/Repositories/IStaffNotificationRepository.cs` | `GetMessagingWarningAsync(clinicId, monthKey, thresholdPercent)` + `GetMessagingWarningsAsync(clinicId, monthKey)` — `GetSubscriptionWarningAsync`'s pair, keyed on **(cabinet, month, threshold)** per FR-6. |
| `api/ClinicManagement.Application/Common/Interfaces/INotificationGenerator.cs` + `Common/Services/NotificationGenerator.cs` | `EnsureMessagingAllowanceWarningAsync` / `ClearMessagingAllowanceWarningsAsync`. Message derived from **threshold + allowance + month**, never the live count (AC-3.5). Both run through `SafelyAsync` — a failure to notify must never fail the send it follows (FR-6). |
| `api/ClinicManagement.Infrastructure/Services/IReminderChannelSender.cs` | Two new outcomes: **`Throttled`** (stay `Pending`, consume no retry budget, defer) and **`Blocked`** carrying an `OutboxBlockReason`. Five in total. |
| `api/ClinicManagement.Infrastructure/Services/WhatsAppSender.cs` | FR-8's classification from Meta's error `code`: `4`/`80007`/`130429`/`131056` ⇒ `Throttled`; `131048`/`131064` ⇒ `Blocked(MessagingNumberStopped)`; anything else ⇒ transient. **The body is read and discarded here** (D-8). |
| `api/ClinicManagement.Infrastructure/Services/HttpReminderChannelSender.cs` | A protected classification hook so the SMS sender's behaviour is byte-for-byte unchanged and only WhatsApp overrides it. ⚠️ **The hook must be handed the FULL response body, and the 200-char truncation kept for the log alone.** `MaxBodyLogLength = 200` (`:24`) and `ReadTruncatedBodyAsync` (`:77`) exist for logging, and Meta's error envelope puts a long `message` (plus `error_user_title`/`error_user_msg`) **before** `code` — so a hook fed the truncated string finds no code and falls through to transient, leaving FR-8 silently inert. Read the body once in full, classify on it, truncate the *copy* that goes to the logger. D-8's security property is untouched: the body is still read and **discarded** here and never reaches `ReminderSendResult`. |
| `api/ClinicManagement.Infrastructure/Services/ReminderScheduler.cs` | US-5: the allowance check at **enqueue** for a recall. Exhausted **and** WhatsApp is the only sendable channel ⇒ return the new `RecallDispatchOutcome.MessagingAllowanceExhausted`, enqueue nothing (AC-5.2). Exhausted **and** SMS is sendable ⇒ enqueue both; the WhatsApp row is held at dispatch (AC-5.3). |
| `api/ClinicManagement.Application/Common/Interfaces/IReminderScheduler.cs` | `RecallDispatchOutcome.MessagingAllowanceExhausted` (AC-5.4 — distinct from `NoChannelConfigured`, whose sentence tells the practice to configure a channel it has already configured). |
| `api/ClinicManagement.Application/Features/Recall/Commands/SendRecallCommand.cs` | Branch on the new outcome with `MessagingRefusals`' sentence, leaving the patient untouched. No `Result.Code` — nothing in the browser branches on it (`Result.Code`'s own doc). |
| `api/ClinicManagement.API/BackgroundJobs/NotificationJob.cs` | **FR-1** (stage the increment into the existing per-row save on `Sent`, for WhatsApp only), **FR-4 + FR-7** (the `OutboxMessagingGate` immediately after the subscription gate), **FR-8** (the two new outcomes, and a `default` on the outcome `switch`, which has none today), **AC-4.5a** (the past-appointment guard beside the moved-appointment check, for **every** appointment-bearing reminder), **AC-4.8** (the same gate in `ReviewBlockedRowsAsync` too), and **the held-row age bound** (`Reminders:HeldMaxDays`) that makes AC-4.4's boundedness true for a row with **no appointment** — see R-5. |
| `api/ClinicManagement.API/Program.cs` | Register `MessagingAllowanceJob` (capability-gated, `RemoveIfExists` in the else) and dispatch the three `messaging-*` verbs. |
| `api/ClinicManagement.Infrastructure/Deployment/DeploymentProfile.cs` | The **18th** capability, `SellsVendorMessaging` — `HostedMultiTenant` only, derived from the **kind** and from nothing an operator can set (FR-9). |
| `api/ClinicManagement.Infrastructure/Extensions.cs` | Register the repository, `IMessagingAllowancePolicy`, `IVendorMessagingAvailability` and `IWhatsAppTemplateService`. ⚠️ The first three go here rather than in `AddApplication`, because `provision-clinic` builds its container from **this method alone** and it creates a cabinet, which must not exist without an allowance (FR-3). |
| `api/ClinicManagement.Application/Features/Clinics/LocalClinicProvisioning.cs` | `StageMessagingAllowanceAsync` — the standing entry **and** the current month's row into the **same save** as the clinic. `StageEntitlementAsync`'s precedent; breaks all three callers' signatures, which is the point. |
| `api/ClinicManagement.Application/Features/Clinics/Commands/UpdateClinicReminderSettingsCommand.cs` | **AC-1.7**: refuse the three manual WhatsApp credential fields where the capability holds. Must not weaken `ReminderSettingsProvider.ClaimsItsOwnWhatsApp` (NFR/Security). |
| `api/ClinicManagement.Application/Features/Clinics/Commands/ConnectWhatsAppCommand.cs` | **AC-1.3**: submit the template on the cabinet's behalf after the Meta exchange, and record `PendingReview`. |
| `api/ClinicManagement.API/Controllers/ClinicsController.cs` | `GET reminder-allowance` + `GET reminder-allowance/history`, `AnyClinicRole` (**AC-2.2**), 404 **before the mediator** where the capability is false (`SubscriptionController`'s precedent — AC-1.6/EC-16). |
| `api/ClinicManagement.Application/Features/Platform/PlatformReadShape.cs` | Every new console leaf name. Asserted in **both** directions, so a name added ahead of its DTO fails the build. |
| `api/ClinicManagement.Application/Features/Platform/Queries/GetPlatformClinicDetailQuery.cs` | The `messaging` object (AC-8.1), each history entry carrying `IfCancelled` — re-folded server-side with that entry marked cancelled (AC-7.3), never estimated in the browser. |
| `api/ClinicManagement.Application/Features/Platform/Queries/ListPlatformClinicsQuery.cs` | AC-8.2's figure and its exhausted/near-exhausted filter, as a **SQL predicate over the stored snapshot**; « non mesuré » is neither (AC-8.3). ⚠️ **« Near » is `consumed >= 0.90 × allowance`** — AC-8.2's « within 10 % of it » — as one **named constant** beside the predicate, never a literal repeated in the query and the console's chip label. An allowance of 0 is « exhausted », not « near ». |
| `api/ClinicManagement.Infrastructure/Repositories/ClinicActivityRepository.cs` | LEFT JOIN the current month's `ClinicMessagingMonth` into the existing `PortfolioJoin`, with the month key as a **query parameter**. LEFT so a cabinet with no row still appears, stated unknown (EC-15). |
| `api/ClinicManagement.Application/Common/Maintenance/SchemaVerificationService.cs` | Three checks — see **Testing Strategy**. |
| `api/ClinicManagement.Infrastructure/Persistence/SchemaVerificationReader.cs` | Project `MessagingAllowanceLedgerFact` so the service can call the **real** fold (`subscription-end-date-matches-ledger`'s R-6 reasoning: re-expressing the fold as SQL is a second copy no compiler checks). |
| `web/app/rappels/page.tsx` | Mount the three new sections inside the existing right `Sheet`; the scroll belongs to the **inner wrapper**. |
| `web/components/reminder-settings.tsx` | Hide the manual WhatsApp credential fields where the capability holds (AC-1.7); mount the connect card instead. |
| `web/components/rappels/reminder-log-table.tsx` | **AC-4.9**: the machine-readable block reason on the log row, and « N rappels en attente de forfait » as a counter distinct from the undifferentiated « Bloqués ». |
| `console/app/cabinets/[clinicId]/page.tsx` | The « Messagerie » section, placed as its own section (the `Suspension` precedent) rather than under the subscription block. |
| `console/components/clinic-portfolio.tsx`, `portfolio-filters.tsx` | AC-8.2's two figures — **fields in the existing card list** below `lg:`, two extra columns above — and the filter. |
| `deploy/docker-compose.hosted.yml`, `deploy/.env.hosted.example`, `deploy/README.md` | The `Messaging__*` variables and the three verbs' runbook. On the only kind that enforces, an unconfigured default allowance leaves the screen with nothing to say. |
| `CLAUDE.md`, `api/*/CLAUDE.md`, `web/CLAUDE.md`, `console/` docs | The map, per the repo's own rule. |

---

## Implementation Stories

### US-1: A cabinet's WhatsApp reminders are bought by the vendor, metered, and stop rather than overspend

**Goal:** A practice connects WhatsApp without touching Meta, sees what it has left, is warned before it runs out,
has its reminders held rather than overspent when it does, and resumes by itself — while the vendor allocates,
corrects and watches the allowance from the console or a terminal.

**Blocked by:** None
**Layers:** DB · Domain · Service · API · Background jobs · Console verbs · Clinic UI · Console UI

> ⚠️ **This is one story by explicit instruction** (see R-1). It is structured into six ordered parts, each a vertical
> increment and each a commit point. If it is split later, split at a **part boundary** — the parts are
> dependency-ordered and no part reaches backwards.

---

#### Part 0 — Groundwork: months, extension points, the capability, and the version spike

1. **Run the Embedded-Signup version spike (blocking, D-1).** Open Meta's Embedded Signup docs in a logged-in
   browser and confirm which version `web/components/reminder-settings.tsx:209-290` implements. Record the answer in
   `features/vendor-whatsapp-messaging-quota/progress.md`.
   - **Branch A — already current:** consolidate the two independent `v21.0` pins (the hard-coded browser constant
     and `MetaConfig`'s server default) into one, since neither derives from the other.
   - **Branch B — v2 (what the spec asserts: `sessionInfoVersion = "3"`, **no** `featureType`):** migrate the
     `FB.login` config, add the version marker, and consolidate the two pins in the same edit.
   - Also open the four JS-gated pages the spec's Open Questions name, and record what they say.
2. Add the month primitives to `api/ClinicManagement.Application/Common/ClinicClock.cs` (FR-8b): `MonthKey`,
   `CurrentMonthKey`, `MonthRangeUtc(monthKey)` (the **whole** month, **inclusive both ends**),
   **`MonthToDateRangeUtc(clinicToday)`** (the 1st → the last tick of **today**), `MonthLabelFr` (`fr-FR` pinned),
   `NextMonthKey`, `PrecedingMonthKeys`, `FirstDayOfNextMonth`.
3. **Move**, do not copy, the two existing private implementations: delete
   `GetPlatformSummaryQuery.ClinicMonthRangeUtc` (`:93`) and `PlatformAccessLabels.Month` (`:38`), and call the new
   helpers. Verify by grep that neither symbol survives outside `ClinicClock`.
   ⚠️ **`ClinicMonthRangeUtc` moves onto `MonthToDateRangeUtc`, NOT onto `MonthRangeUtc`.** Read it before replacing
   it: it returns `LastTickOfLocalDayUtc(todayLocal)` as its upper bound — the end of *today*, not of the month — so
   the two are different functions and a one-primitive « move » would widen its caller's window by the rest of the
   month. That caller is `VendorCollectedThisMonthDt`, and the widening is **invisible to the whole suite**, since
   `MoneyReadConsistencyTests` covers the clinic's money and this is the vendor's (FR-2). `PlatformAccessLabels.Month`
   is a true move — it takes `(int year, int month)`, so keep that overload shape on `MonthLabelFr` or adapt its two
   call sites deliberately.
4. Declare the four FR-8a extension points, each of which fails the build or throws if this feature adds a case
   without it: the four `OutboxBlockReason` members, the two `PlatformAccessAction` members, the
   `NotificationCategory` member **plus its `StaffNotificationRules` classification (`false`, AC-3.4) and its
   `PushLabel`**, and the `NotificationTargetKind` member.
5. Add the 18th capability `SellsVendorMessaging` to `DeploymentProfile` (`HostedMultiTenant` only), and
   `IVendorMessagingAvailability` + `VendorMessagingAvailability` for the separate credentials question (FR-9).
6. Add `IMessagingAllowancePolicy` + `MessagingAllowancePolicy` reading `Messaging:DefaultMessagesPerMonth`,
   `Messaging:ContactEmail`, `Messaging:ContactPhone` — parsed by hand, falling back rather than throwing.

**Validation:**
- [ ] The ES-version answer is recorded, and the two `v21.0` pins are one.
- [ ] `ClinicMonthRangeUtc` and `PlatformAccessLabels.Month` return zero hits outside `ClinicClock`.
- [ ] `ClinicClockMonthTests` covers EC-7's 23:59-on-the-31st and 00:01-on-the-1st boundaries, **and pins
      `MonthRangeUtc` ≠ `MonthToDateRangeUtc` mid-month** — the assertion that stops the two being collapsed later.
- [ ] `GetPlatformSummaryQuery`'s window is **unchanged** after the move: same `(From, ToInclusive)` for a
      mid-month « today » as the deleted private method returned.
- [ ] `DeploymentProfileTests` still holds the two pre-existing kinds' truth table; the new capability is
      unreachable from any config key.
- [ ] `dotnet build` + the unit suite green (build to a path **outside** the repo — `BaseOutputPath=<temp>`).

---

#### Part 1 — The allowance exists, sends are counted, and reminders are held rather than overspent

*(Spec boundary 1. Covers US-4 end to end, and the machinery US-2/3/6/7/8 read.)*

7. Add `MessagingAllowanceEntry`, `ClinicMessagingMonth`, `MessagingAllowanceKind` and
   `IMessagingAllowanceRepository` in Domain, plus `MessagingAllowanceLedger` — **pure, total, clock-free**,
   taking `monthKey` as a parameter (FR-2).
8. Add the two EF configurations and the repository. Scaffold the migration
   `AddClinicMessagingAllowances`; **remove the scaffolded `xmin` columns by hand** (PostgreSQL rejects them — the
   `AddClinicSubscriptions` lesson) and place the backfill **below every DDL statement**.
9. Write the rollout backfill (FR-3): one `Standing` entry per existing cabinet at the configured default effective
   the rollout month, **gated on « this cabinet has no standing entry »** so re-running is safe on a populated
   database, plus that month's `ClinicMessagingMonth` row.
10. `LocalClinicProvisioning.StageMessagingAllowanceAsync` — the entry and the month row in the **same save** as
    the clinic. Update all three callers (`CreateClinicCommand`'s Local branch, `provision-clinic`,
    `VerifyClinicSignUpCommand`).
11. Add `MessagingAllowanceRefold` (5-attempt `ConflictException` retry, detaching the ledger — `SubscriptionRefold`'s
    reasoning) and `MessagingRefusals`.
12. Add `OutboxMessagingGate`: WhatsApp rows only, after the subscription gate, per-tick instance with a
    per-cabinet cache, reading nothing where the capability is off. Its `ReviewAsync` answers over **ordered terms** —
    template-not-ready (FR-7, wired in Part 4 step 33a) → allowance-missing (AC-4.3) → allowance-exhausted (AC-4.1) —
    returning the **first** applicable `OutboxBlock`. Part 1 implements the two allowance terms; the template term
    lands with the column it reads, and the ordering is declared here so Part 4 adds a term rather than a gate.
13. Wire it into `NotificationJob` in **both** places — `DispatchAsync` immediately after the subscription gate, and
    `ReviewBlockedRowsAsync` for **every** parked row (AC-4.8, EC-2, EC-8).
14. **FR-1's counting**: on `ReminderSendOutcome.Sent` for a WhatsApp row, load that clinic's row for
    `ClinicClock.CurrentMonthKey()`, call `RecordSend()`, and let the **existing** `SaveAsync(notification)` commit
    both — so the unit and the `Sent` mark ride one transaction and a crash loses **both or neither**.
14a. **The ensure-create happens BEFORE the send, in its own save — never staged into the send's commit.** If the
    month row is absent (a rollover between the gate read and the send), create and commit it *before*
    `sender.SendAsync`, catching a unique violation on `(ClinicId, MonthKey)` and re-reading. The send's own commit
    then only ever **updates** a row that already exists.
    ⚠️ **Staging the insert into `SaveAsync` would make a collision cost the send.** That save is the one carrying
    `MarkAsSent()` (`NotificationJob.cs:494-498`), so a unique violation raised by `MessagingAllowanceJob`'s
    provisioning pass inserting the same (cabinet, month) row in that window would throw **after Meta had already
    accepted the message**: the row stays un-`Sent`, the next tick re-sends it, and one message is paid for and
    uncounted while its duplicate is counted twice (EC-15) — for a reason nobody chose.
    ⚠️ `[DisableConcurrentExecution]` does **not** cover this: it serialises `NotificationJob` against itself, not
    against the daily job. And the window is the opposite of exotic — the ensure-create fires *only* at month
    rollover, which is precisely when the provisioning pass runs.
    ⚠️ Note why the row is not simply left to the daily pass (with the gate holding under `MessagingAllowanceMissing`):
    a cabinet's first send after each rollover would then be held until the pass runs, up to 24 h of held reminders
    every month — and the first send of a month is the one *most* likely to still be useful, since its visit is a day
    away rather than in the past. (Note this is the one thing the rollover genuinely rescues; see R-5 and AC-4.2 on why
    the rollover is a **re-evaluation, not a rescue**, for held rows generally.)
15. **AC-4.5a**: add the past-appointment guard beside the moved-appointment check in `DispatchAsync`, for **every**
    appointment-bearing reminder — `appointment.AppointmentDateTime <= nowUtc` ⇒ fail as obsolete (D-3), with
    `nowUtc` a parameter.
15a. **The held-row age bound — AC-4.4's boundedness for a row with no appointment (R-5).** In
    `ReviewBlockedRowsAsync`, a row parked longer than `Reminders:HeldMaxDays` (default **30**, one full allowance
    cycle) **fails as obsolete** whatever parked it, and becomes an ordinary terminal row the existing purge
    collects after `RetentionDays`.
    ⚠️ **Step 15's guard cannot do this job, and the plan previously assumed it could.** AC-4.5a keys on
    `appointment.AppointmentDateTime`, but `ReminderScheduler` creates a **recall** row with
    `appointmentId: null` (`ReminderScheduler.cs:128`), so `DispatchAsync`'s entire appointment block — the new
    guard included — is skipped for one. A held recall row therefore has nothing that can make it obsolete, is
    non-terminal, is excluded from the purge by construction, and would be re-examined on **every** review tick for
    ever: exactly the starvation shape this outbox has already been bitten by twice (L3's original `Blocked` defect
    and Part G's un-park re-arming it). ⚠️ And **AC-5.3 manufactures that row deliberately** (step 21: exhausted +
    SMS sendable ⇒ enqueue both, hold the WhatsApp one), so it is the normal case rather than an exotic one.
    ⚠️ The bound is keyed on **the reason-agnostic parked age, not on the block reason**, because the two
    pre-existing channel reasons (`ChannelDisabled`, `ChannelUnconfigured`) have the identical defect on a recall
    row today — one rule for « how long may a send wait? » rather than a term per reason, which is the
    `fixes-dont-propagate` shape. The failure is **recorded**, so it surfaces on « Rappels » like any other.

**Validation:**
- [ ] `MessagingAllowanceLedgerTests`: the fold is clock-free and idempotent; a raise is effective this month and a
      lowering next (EC-3); a cancellation applies to **every month the entry fed, the current one included**
      (AC-7.4, EC-4); remaining is `max(0, allowance − consumed)` and never negative.
- [ ] `OutboxMessagingGateTests`: SMS is never consulted (AC-4.6); the subscription refusal wins (AC-4.7, EC-8);
      the capability off issues **zero** queries; a missing row holds under its **own** reason (AC-4.3); and the
      **term order** holds — a cabinet that is *both* template-not-ready and exhausted is told about the **template**
      (the ordering is the wording, not an implementation detail).
- [ ] `NotificationJobMessagingTests`: a send and its unit are **one commit** (EC-14); a send at 23:59 Tunis on the
      31st counts against that month and 00:01 against the next (EC-7); a held row is never purged **while it could
      still be sent** (AC-4.4); a released row whose appointment has passed fails as obsolete (AC-4.5, EC-1) —
      asserted on the **subscription** release path too, since AC-4.5a closes that pre-existing defect.
- [ ] **A held row with NO appointment drains**: a **recall** WhatsApp row (`appointmentId: null`) held past
      `Reminders:HeldMaxDays` fails as obsolete and is then purged — the case AC-4.5a's guard structurally cannot
      reach, and the one AC-5.3 creates on purpose. Asserted for a channel-parked recall row too, since the bound is
      reason-agnostic; and asserted that a row *inside* the window is still held rather than failed.
- [ ] The migration applied to a live database; `verify-schema` before/after **diffed** and the new checks green.
- [ ] `ClinicCreationMessagingAllowanceTests` derives the door set by scanning for `new Clinic(`, not by listing.

---

#### Part 2 — The practice sees what it has left and is warned before it runs out

*(Spec boundary 2. Covers US-2, US-3, US-5.)*

16. `GetReminderAllowanceQuery` + `GetReminderAllowanceHistoryQuery`, the history floored per **D-5**
    (`max(creation month, earliest row)`; a gap inside the range is « non mesuré », a month below the floor is
    **omitted**). Add `MessagingSenderState` so AC-1.4's five states have one derivation.
17. Expose both on `ClinicsController` as `AnyClinicRole` (AC-2.2), 404 **before the mediator** where the capability
    is false (AC-1.6, EC-16).
18. Add `MessagingAllowanceThresholds` (all crossed thresholds, not only the largest — FR-6) and the
    `INotificationGenerator` pair, deduped on **(cabinet, month, threshold)** via the two new `StaffNotification`
    columns. Message derived from threshold + allowance + month (AC-3.5).
19. Evaluate the thresholds **post-commit, best-effort** where the counter is incremented (FR-6), so 80 % is
    announced when it is crossed rather than the next morning.
20. Add `MessagingAllowanceJob` (D-2), `Cron.Daily`, capability-gated with `RemoveIfExists` in the else, taking
    « today » as a parameter: provision the month row for every cabinet, then reconcile the warnings — AC-3.6's
    withdrawal after a grant and AC-3.7's month-rollover withdrawal and re-arm. A suspended or expired cabinet is
    **not** warned (FR-6).
21. US-5: `RecallDispatchOutcome.MessagingAllowanceExhausted`, the `ReminderScheduler` branch (WhatsApp-only ⇒
    refuse and enqueue nothing; SMS also sendable ⇒ enqueue both, AC-5.3) and `SendRecallCommand`'s refusal leaving
    the patient untouched.
22. Client: `web/lib/api/reminder-allowance.ts`, the three `web/components/rappels/` components, mounted in
    `web/app/rappels/page.tsx`. Tri-state availability; a failed read is a `LoadFailureNotice` with a retry, **never**
    an empty or zeroed table (AC-2.5, EC-12). AC-2.6's SMS sentence and AC-2.7's contact route — rendering **no**
    contact route at all where unconfigured. **FR-1's duplicate disclosure**: the help text states that a rare
    duplicated send counts twice — FR-1 requires it « stated rather than hidden », and it is the one place the
    practice can reconcile a figure it cannot otherwise explain.
    **Accessibility (NFR)**: the three figures live in a **live region** so a screen-reader user hears a refresh, and
    the failure state uses an **alert** role while a genuine measured zero uses a **status** role — never the same
    component, since « je n'ai pas pu lire » and « vous n'avez rien envoyé » are opposite facts (AC-2.4 vs EC-12).
23. AC-4.9: the machine-readable reason on `reminder-log-table.tsx` and the « en attente de forfait » counter.

**Validation:**
- [ ] Three thresholds crossed in one afternoon produce **three** unread rows, each badging the bell (AC-3.1/3.2);
      a threshold holding for days restates nothing (AC-3.5); a zero allowance produces the 100 % row only.
- [ ] A grant putting the cabinet back below a threshold **withdraws** the rows it no longer meets (AC-3.6); a month
      rollover withdraws and re-arms (AC-3.7).
- [ ] `StaffNotificationRules.ReachesALockedPhone` returns `false` — asserted, since AC-3.4 is honoured by
      classifying, never by omitting.
- [ ] A quiet month reads « 0 rappel envoyé »; only a month with no row reads « non mesuré »; a pre-rollout month is
      **absent** (AC-2.4, D-5).
- [ ] **No clinic-facing sentence promises that held reminders go out on the 1st** (AC-4.2). The exhausted card and the
      100 % warning offer the **top-up** as the remedy, state the renewal date as a fact about the *allowance*, and
      point at « Rappels » for who was not prevented. Asserted over `MessagingRefusals`' strings, not by eye.
- [ ] The figures announce a refresh in a live region; the failure state carries an **alert** role and a measured
      zero a **status** role, as two different components (NFR accessibility, and the AC-2.4 / EC-12 distinction).
- [ ] The duplicate-counting disclosure is on the screen (FR-1), not only in this plan.
- [ ] `npm run check:responsive` (15 checks) + `npx tsc --noEmit` + `npm run build`, then an eye pass at
      320/390/820/1180/1440 px, per `.claude/rules/frontend-web.md`.

---

#### Part 3 — The vendor allocates, corrects, and sees the portfolio

*(Spec boundary 3. Covers US-6, US-7, US-8, US-9.)*

24. `GrantMessagingAllowanceCommand` + `CancelMessagingAllowanceCommand` (the **vendor** pair, no controller may
    name them — AC-9.3), and their console wrappers
    `RecordMessagingAllowanceFromConsoleCommand`/`CancelMessagingAllowanceFromConsoleCommand`, each staging the
    `PlatformAccessEntry` **before** `MessagingAllowanceRefold`'s single save (AC-6.8).
25. AC-6.4a: the server decides standing-vs-top-up effective month from the figure in force for the current month;
    the caller never chooses. AC-6.5: a top-up naming a past month is refused. AC-6.6: complimentary carries **no**
    amount rather than an amount of zero.
26. `PlatformMessagingController` with the two routes, `[AllowsWithoutSubscription]`, 409 codes on AC-7.5.
27. Extend `GetPlatformClinicDetailQuery` (AC-8.1, with per-entry `IfCancelled` re-folded server-side — AC-7.3) and
    `ListPlatformClinicsQuery` + `ClinicActivityRepository`'s `PortfolioJoin` (AC-8.2/8.3, a SQL predicate over the
    stored snapshot, LEFT-joined so an unmeasured cabinet still appears). **« Near exhausted » is
    `consumed >= 0.90 × allowance`** (AC-8.2's « within 10 % »), as a single named constant the SQL predicate and the
    console's chip both read — two spellings of the threshold is how the filter and its label come to disagree.
28. Declare every new leaf in `PlatformReadShape.AllowedLeafNames` — and only those the DTOs actually return, since
    an **unused** declaration fails too. ⚠️ Do **not** reuse `Note`/`Reference`, which are declared as
    vendor-*payment* fields; a semantic overload is not a free pass.
29. Console UI: the section, the two sheets and the cancellation dialog, plus the BFF hops.
30. The three verbs and `MessagingReportService` — exit 0 clean / 1 couldn't run / **2** findings, with AC-9.4's
    three finding kinds distinguished (exhausted · no allowance record · a template no longer `UTILITY`).
    `messaging-report` takes **`[--clinic <id|email>]` and `[--month AAAA-MM]`**, both per the spec's own CLI: the
    month argument is what makes the report answer « what did we bill for August? » after August has ended, which a
    current-month-only report cannot, and it is free given the fold takes the month as a parameter (FR-2).

**Validation:**
- [ ] A double-click produces **one** entry and replays the first outcome (AC-6.7, EC-6); two *different*
      allocations both land and are both kept (EC-5).
- [ ] `MessagingVendorCommandReachabilityTests`: no controller source names either vendor command, and every verb is
      actually dispatched by `Program.cs` (a missing branch boots the **web host** and reads as « it did nothing »).
- [ ] `PlatformReadShapeTests` green **in both directions**; nothing in the new DTOs names a patient, an act or a
      per-patient amount (AC-8.5).
- [ ] A failed portfolio read renders « je n'ai pas pu lire », never an empty portfolio (AC-8.4, EC-12).
- [ ] **AC-6.9 confirmed by trying it**, not by inheritance: sign in to the console, run
      `platform-account --deactivate`, and call both new writes with the same token — each must be refused on the
      **next** request. ⚠️ It is carried by `PlatformAccountStateMiddleware`, which was **inert in production for the
      whole life of `platform-console`** (Parts 1–6) while every layer reported it present, and was caught only by
      `platform-console` Part 7 step 51 doing exactly this over the wire. « Already handled upstream » is the precise
      assumption that failed there, so it is verified here rather than assumed.
- [ ] « Near exhausted » is `>= 90 %` in **both** the SQL predicate and the console's chip label, from one constant.
- [ ] `messaging-report --month` answers for a **closed** month, not only the current one.
- [ ] `console/`'s own `check-responsive.mjs` + `tsc --noEmit` + `build`.

---

#### Part 4 — The cabinet connects, the template is submitted, and Meta's refusals are told apart

*(Spec boundary 4. Covers US-1 and FR-7/7a/7b/8.)*

31. **Migrate Embedded Signup v3 → v4** (D-1a; Story 0 resolved the version — see `progress.md`). Four edits in
    `web/components/reminder-settings.tsx`, all in the connect path:
    - **Remove `extras.sessionInfoVersion: "3"`.** That key *is* the v3 marker; Meta's v4 sample carries no
      `sessionInfoVersion` at all, and every other member of our `FB.login` config already matches v4
      (`config_id`, `response_type: 'code'`, `override_default_response_type: true`, `extras.setup: {}`).
    - **Capture `data.business_id`** — the customer's business-portfolio id, which v4's success payload carries and
      we currently drop.
    - **Accept every finish type, not only `FINISH`.** Meta documents five — `FINISH`, `FINISH_ONLY_WABA`,
      `FINISH_WHATSAPP_BUSINESS_APP_ONBOARDING`, `FINISH_OBO_MIGRATION`, `FINISH_GRANT_ONLY_API_ACCESS` — plus
      `ERROR`. We match `=== "FINISH"`, so a cabinet completing any other way is **silently dropped** and the
      connection merely appears to have failed. ⚠️ `FINISH_ONLY_WABA` means the user finished **without a phone
      number**, so it must not be treated as a completed connection: it needs its own French state, not a crash on
      an absent `phone_number_id`.
    - ⚠️ **Keep our origin allow-list exactly as it is.** Meta's own sample uses
      `event.origin.endsWith('facebook.com')`, which also matches `notfacebook.com`; ours checks against
      `https://www.facebook.com` / `https://web.facebook.com`. **Do not « align with the sample » here** — ours is
      the stricter and correct version.
    ⚠️ **Bumping Graph `v21.0` → `v26.0` is deliberately NOT part of this step.** Meta's sample recommends v26.0 and
    we are five versions behind, but that moves *every* server Graph call (onboarding, template submission, the
    status poll) and deserves its own change and its own test. Story 0 made it a one-key edit
    (`META_GRAPH_API_VERSION`); it is captured as a follow-up in § 43.
    ⚠️ **Still unread: whether v3 itself has an end date.** Meta's page links a « Versions » section nobody has
    opened. It does not block this step — we migrate to current either way — but if v3 turns out to be dated, this
    step becomes mandatory rather than chosen, which changes nothing about what it does.
32. `IWhatsAppTemplateService` + `WhatsAppTemplateService`: submit the French `UTILITY` reminder template, whose body
    must **not** start or end with a variable — write « Rappel de votre rendez-vous chez {{1}} le {{2}}. », never
    « {{1}} : rappel… ».
33. `ClinicReminderSettings`' four template columns and `ConnectWhatsAppCommand`'s post-exchange submission
    (AC-1.3 — the admin never sees a template editor).
33a. **FR-7's hold — add the template term to `OutboxMessagingGate`** (the slot step 12 declared), so a cabinet whose
    template is `NotSubmitted`/`PendingReview`/`Rejected`/`Paused`/`Disabled` has its WhatsApp reminders **held**
    under `MessagingTemplateNotReady`, consuming nothing, and released by the review pass on approval (EC-9, EC-10).
    ⚠️ **This cannot live in the sender, and that is the whole reason it is a gate term.** A sender runs *after* the
    send call, so FR-7's « consume nothing » is already lost: with no pre-send term, `WhatsAppSender` calls Meta with
    an unapproved template, Meta refuses, step 37 classifies it as an ordinary **transient** failure, and the row
    burns its three retries and dies — the exact opposite of EC-9. And if Meta happens to accept, FR-1 counts a unit
    against a template the cabinet cannot use.
    ⚠️ It is added in **both** places the gate is asked (step 13's two call sites), or AC-4.8 fails in a specific way:
    a template-parked row would be released the moment the *allowance* is topped up, and go out on a cabinet whose
    template Meta has refused.
34. Writer 1: `MetaWebhookController`, `[AllowAnonymous]`, `X-Hub-Signature-256` verified over the **raw** body.
    ⚠️ **Template status arrives on `message_template_status_update`** — that is the field this feature subscribes
    to, and Story 0 confirmed it is the right one. Subscribing to the wrong field yields a callback that is
    silently never called.
    ⚠️ **Corrected by Story 0: `account_update` is NOT the wrong field — it is REQUIRED.** Meta's implementation
    page states « Vous devez vous abonner au webhook `account_update` … déclenché dès qu'un·e client·e termine le
    processus d'inscription intégrée », and the pricing page shows it also carries
    `VOLUME_BASED_PRICING_TIER_UPDATE`. An earlier draft of this step read as « do not use `account_update` »,
    which would have broken onboarding. The true narrow claim is only this: **messaging-limit** changes arrive on
    `business_capability_update`/`account_alerts` rather than `account_update`, so do not expect *limits* there.
    Template status and limits are three different fields; do not collapse them.
    Add both actions to `ControllerAuthorizationCoverageTests`' `ExpectedAnonymous` and, if needed, to
    `AdminSurfaceCoverageTests`' named exemptions.
    ⚠️ **Two pieces this endpoint cannot work without, and both fail silently rather than loudly.**
    **(a) Declare `ITenantScope.UseSystemWide("Meta template status webhook")` as the action's first act.** It is
    anonymous, so there is no `User` row for `TenantScopeMiddleware` to resolve and the scope lands `Unset` — where
    the clinic query filters compare against `Guid.Empty` and return **nothing**. `ClinicReminderSettings` is
    filtered (`ApplicationDbContext:238`, on `Id == ScopedClinicId`), so without the declaration the webhook
    verifies its signature, parses its payload, resolves no cabinet, writes nothing and answers **200 to Meta**.
    **(b) Add `GetByWhatsAppBusinessAccountIdAsync`** (see Files to Modify) — there is no WABA→cabinet lookup in
    the product today, so there is nothing to resolve the payload against.
    ⚠️ **Neither existing derived guard catches this.** `SystemWideCallerCoverageTests` derives its candidate set
    from « reads a filtered entity with **no HTTP context** », and a webhook *has* one — so this controller must be
    added to that test explicitly. And the symptom is not an error: FR-7a's reconciling poll runs *with* a scope and
    picks the state up on its next pass, so the only observable effect is AC-1.5's « dès la validation » silently
    degrading from minutes to a day, with reminders held meanwhile and nothing in any log naming the cause.
35. Writer 2: the reconciling poll as `MessagingAllowanceJob`'s third duty, over cabinets **not** in a terminal
    state (FR-7a — the webhook without the poll has no recovery).
36. FR-7b: store the template **category**, state it in words on the console file when it is not `UTILITY`, and make
    it a `messaging-report` finding. **Not** surfaced clinic-side and it does **not** hold reminders.
37. FR-8: the two new sender outcomes and `WhatsAppSender`'s classification (D-8 — the body never reaches the
    result), wired into `NotificationJob`'s switch.
    ⚠️ **Classify on the full body.** The base class truncates at **200 characters** for its log line, and Meta puts a
    long `message` before `code`, so classifying off the truncated string matches nothing on a real payload: `131048`/
    `131064` would burn three retries instead of holding (EC-11) and the four throttle codes would consume retry
    budget, with FR-8 reading as implemented at every layer. Truncation stays, for the **log** only.
    ⚠️ **A short test fixture cannot see this defect** — `{"error":{"code":131048}}` is under 200 characters, so the
    suite would be green against a sender that fails on every genuine Meta response. `WhatsAppSenderErrorClassificationTests`
    therefore uses a **realistic full-length** envelope (>200 chars, `code` after the message) for at least one code
    per outcome.
    ⚠️ **Make `DispatchAsync`'s `switch (result.Outcome)` exhaustive while adding to it.** It has **no `default`**
    today (`NotificationJob.cs:324-363`), so a member it does not name falls through with no save, no log and no
    state change — the row stays `Pending` and is re-attempted every tick for ever. Add a `default` that parks and
    logs, so a fifth outcome added later cannot silently mean « do nothing ».
38. Client: `whatsapp-connect-card.tsx` with AC-1.2's « the code arrives on your own handset » stated **before** the
    flow starts, AC-1.4's five states in words, AC-1.5's 24 h sentence; and AC-1.7's closure of the manual
    credential fields, refused server-side in `UpdateClinicReminderSettingsCommand` too.

**Validation:**
- [ ] `WhatsAppSenderErrorClassificationTests` covers the six codes **and** asserts no response body reaches the
      result (the `SECURITY_REVIEW_2026-08` finding must not regress).
- [ ] At least one code per outcome is asserted against a **full-length** Meta error envelope (>200 characters, with
      `code` after a long `message`) — the only fixture shape that can fail if classification is fed the log's
      truncated copy (step 37).
- [ ] `DispatchAsync`'s outcome `switch` has a `default` that parks and logs, asserted — an unnamed outcome must not
      leave a row `Pending` in silence.
- [ ] A template under review holds reminders, consumes nothing, and releases on approval (EC-9) — asserted as a
      **pre-send** hold, i.e. the sender is never called and no unit is counted, which is what distinguishes step 33a's
      gate term from a sender-side classification; a `DECLINED` template says so with a contact route and runs **no**
      resubmission loop (EC-10); `131048`/`131064` hold rather than burning three retries (EC-11).
- [ ] A template-parked row is **not** released by an allowance top-up (AC-4.8) — the un-park half of step 33a.
- [ ] A forged webhook signature is rejected; the verify handshake answers `hub.challenge`.
- [ ] **The webhook actually writes**: a valid `message_template_status_update` for a known WABA id moves that
      cabinet's stored template state — asserted end to end with the tenant scope left as production leaves it
      (`Unset`), since a test that sets a scope by hand asserts the one arrangement that is broken. The webhook is
      on `SystemWideCallerCoverageTests`' reviewed set, and `GetByWhatsAppBusinessAccountIdAsync` resolves a
      cabinet with **no clinic in scope**.
- [ ] `ReminderSettingsChannelIsolationTests` still green — closing the manual fields must not weaken
      `ClaimsItsOwnWhatsApp`.

---

#### Part 5 — Verification, guards and documentation

39. Add the three `verify-schema` checks and the `SchemaVerificationReader` projection.
40. Run `verify-schema` and `reconcile-money` **before and after** the migration batch and diff, per the repo's rule.
41. Confirm EC-16 by reprofiling to `SelfHostedLan` and `CloudBrowser`: no section, no notifications, no enforcement,
    no scheduled work, endpoints answering as though they do not exist, existing WhatsApp behaviour unchanged.
42. Update `CLAUDE.md`, the three `api/*/CLAUDE.md`, `web/CLAUDE.md`, `deploy/README.md`, and write the operator
    runbook for the three verbs and the Meta account setup they assume.
43. Capture as follow-up items: the spec's remaining open questions, the « cabinets already using their own
    credentials » migration, and the three Story 0 left open —
    **(a)** the **Graph `v21.0` → `v26.0`** bump (R-2a — a one-key change now, deliberately kept out of § 31 because
    it moves every server Graph call at once); **(b)** whether **Embedded Signup v3** carries its own end date
    (Meta's « Versions » section, unread — it does not change what § 31 does, only whether it was optional);
    **(c)** the two Meta rules the spike could **not** settle — the **WhatsApp template edit caps** (the page read
    was the *Messenger* templates doc, not WhatsApp's) and whether **one payment instrument can serve several
    accounts** in a portfolio (only indirect evidence: portfolio-level credit and volume aggregation across all
    WABAs). Neither blocks this feature.

**Validation:**
- [ ] `verify-schema` exits **0** after the batch; the before/after diff shows only the intended objects.
- [ ] The whole unit suite green; `dotnet build` clean; `web` and `console` gates green.
- [ ] Every `CLAUDE.md` touched by this feature reflects it.

---

## Testing Strategy

Nothing in `api/ClinicManagement.UnitTests/` touches a database, so a migration is the one class of change the suite
structurally cannot verify — `verify-schema` is the gate for that half, and it is run before and after and diffed.

### Unit tests (new)

- **`MessagingAllowanceLedgerTests`** — the fold is pure, total and clock-free; the raise/lowering effective-month
  rule including « lowering then raise » (each entry judged against the figure in force when it is *recorded*);
  cancellation applying to the current month (AC-7.4) as against a lowering deferring (EC-3); no rollover; a
  cabinet with no entry folds to `null`, not to 0.
- **`OutboxMessagingGateTests`** — the per-tick cache issues one query per cabinet for a 50-row batch **across both
  its reads** (the settings row and the month row); SMS rows never reach it; the subscription refusal wins (EC-8);
  the capability off reads nothing; and the ordered terms resolve template-not-ready **before** either allowance
  term, since a cabinet meeting two conditions must be told the one it can act on.
- **`NotificationJobMessagingTests`** — send-and-count atomicity (EC-14); the Tunisian month boundary (EC-7); a
  duplicate counting twice (EC-15); the past-appointment guard on **both** release paths (AC-4.5/4.5a); the un-park
  re-check (AC-4.8, EC-2); the held-row age bound draining an appointment-less recall row (step 15a); and that a
  **unique-violation collision on the month row's creation cannot cost a send** — the row is ensure-created in its
  own earlier save, so the collision is caught and re-read while the send's commit only updates (step 14a).
- **`MessagingAllowanceWarningTests`** — three rows from one jump (FR-6); the (cabinet, month, threshold) dedupe;
  withdrawal (AC-3.6) and re-arm (AC-3.7); a zero allowance producing the 100 % row only; a suspended or expired
  cabinet not warned.
- **`WhatsAppSenderErrorClassificationTests`** — FR-8's code table, that the response body is not returned, and that
  classification survives a **realistic full-length** Meta envelope. That last case is the one with teeth: a short
  fixture passes whether the hook reads the full body or the 200-char log copy, so it would certify a sender that
  never classifies anything in production.
- **`ClinicClockMonthTests`** — month key, inclusive range, French label, `resetsOn`, EC-7's two instants, and that
  the whole-month and month-to-date ranges **differ** mid-month while agreeing on the last day (the property that
  makes the `GetPlatformSummaryQuery` move safe, and the one a future collapse of the two would break).
- **`RecallMessagingRefusalTests`** — AC-5.1–5.4, including that the patient is left untouched and that the outcome
  is distinct from `NoChannelConfigured`.
- **`MessagingReportServiceTests`** — AC-9.4's three finding kinds and the exit codes; a suspended cabinet listed
  without counting as a finding.

### Derived guard tests (extended, not written from scratch)

- `PlatformReadShapeTests` — both directions, so an unused declaration fails too.
- `ControllerAuthorizationCoverageTests` — the webhook's two anonymous actions onto the reviewed set.
- `StaffNotificationRules`' total switches — throw on an unclassified category, so AC-3.4 cannot be met by omission.
- `RealtimeResourceResolverTests` — `Features.Messaging` contains **queries only**, and `Platform` is already an
  excluded area, so no new realtime key is emitted and none is declared. Assert that, rather than assume it.
- `SubscriptionExemptionCoverageTests` — the two console writes carry `[AllowsWithoutSubscription]`.
- **`SystemWideCallerCoverageTests` — `MetaWebhookController` added explicitly.** Its candidate set is derived from
  « reads a filtered entity with no HTTP context », which a webhook does not satisfy, so this is the one entry that
  has to be named rather than derived. Without it the scope omission is invisible to the whole build (Part 4 § 34).
- **`MessagingVendorCommandReachabilityTests`** (new, mirroring `SubscriptionVendorCommandReachabilityTests`) —
  no controller names either vendor command (AC-9.3), and every verb is dispatched by `Program.cs`.
- **`ClinicCreationMessagingAllowanceTests`** (new) — the door set derived by scanning for `new Clinic(`.

### Schema verification (`verify-schema`)

- **`monthly-allowance-matches-ledger`** — re-derives every `ClinicMessagingMonth.AllowanceMessages` through the
  **real** `MessagingAllowanceLedger.Fold` and reports **both** directions. The one check here that reads rows rather
  than a count, for `subscription-end-date-matches-ledger`'s R-6 reason.
- **`messaging-month-covers-every-clinic`** — a derived count, never a list of known doors; this is the figure that
  says « the pass has not run » rather than « these practices are idle » (FR-1a).
- **`messaging-allowance-entry-has-one-form`** — standing **xor** top-up. A domain invariant deliberately not
  expressed as a CHECK constraint (whose failure would be a 500 instead of the French refusal), so it is verified
  rather than enforced twice — `cheque-details-only-on-cheques`' precedent.

### Manual / operator verification (not CI-runnable)

The before/after `verify-schema` diff; the EC-16 reprofile walk; the responsive eye pass at five widths on both
`web/` and `console/`; and the Meta-side confirmations the spike produces.

---

## Risk Register

| ID | Risk | Likelihood | Impact | Part | Mitigation |
|----|------|-----------|--------|------|------------|
| R-1 | One story is far past a single implementation session | **High** | Med | all | Six ordered parts, each a commit point; split at a part boundary |
| R-2 | The Embedded Signup flow is v2 and is deprecated 15 Oct 2026 | **High** | **High** | 0, 4 | Blocking spike first; both branches declared |
| R-3 | Meta rules the spec marks unconfirmed are wrong | Med | **High** | 4 | Spike opens the four JS-gated pages; category watched (FR-7b) rather than assumed |
| R-4 | The counting increment is not atomic with the send | Low | **High** | 1 | Staged into the existing per-row save; EC-14 test; `verify-schema` backstop |
| R-5 | Held rows accumulate and starve the outbox | Med | **High** | 1 | AC-4.5a obsolescence (the primary drain) + the reason-agnostic age bound; per-clinic bound reused |
| R-6 | The stored allowance snapshot drifts from the ledger | Med | **High** | 1, 5 | `monthly-allowance-matches-ledger` through the **real** fold |
| R-7 | A console field leaks a clinical fact | Low | **High** | 3 | `PlatformReadShape` both directions; no `Note`/`Reference` reuse |
| R-8 | Closing the manual credential fields weakens tenant channel isolation | Low | **High** | 4 | `ReminderSettingsChannelIsolationTests` unchanged and re-run |
| R-9 | The daily job's three duties fail together | Med | Med | 2, 4 | Per-cabinet try/catch inside one loop; `verify-schema` sees a job that stopped |
| R-10 | The webhook is a new anonymous, internet-reachable POST | Med | **High** | 4 | Signature verified over the raw body; both actions onto the reviewed anonymous set |
| R-11 | `LocalClinicProvisioning`'s signature change breaks three callers | **High** | Low | 1 | It is a compile error by design; derived door-set test |
| R-12 | The default allowance figure is a commercial decision not yet made | **High** | Low | 0 | It is operator configuration; ship provisional |
| R-13 | The rollout backfill misfires on a populated database | Low | **High** | 1 | Gated on « no standing entry »; backfill below all DDL; before/after diff |
| R-14 | `dotnet test` blocked by Smart App Control (`0x800711C7`) | Med | Med | all | `dotnet test -c Release`, `BaseOutputPath` outside the repo |

### R-1: One story is past a single implementation session
- **Description:** The spec carries nine user stories and says a feature usually splits before this. Planned as one
  story at the user's explicit instruction; the delivery risk is that a session ends mid-part with the working tree
  in a state no commit describes.
- **Likelihood:** High · **Impact:** Medium · **Part:** all
- **Mitigation:** Six dependency-ordered parts, each a vertical increment and each a commit point. No part reaches
  backwards into an earlier one, so `/implement-story` can land and commit part by part across sessions.
- **Contingency:** Split at a **part boundary** — Parts 0–1, 2, 3, 4–5 are the natural three-way cut. Nothing in the
  plan needs rewriting to do it; the parts are already the stories.

### R-2: Embedded Signup v2 is deprecated 15 October 2026 — ✅ **RETIRED, and both declared branches were wrong**
- **Description (as recorded):** The shipped integration has `sessionInfoVersion = "3"` and **no** `featureType`, and
  Graph is pinned to `v21.0` in two independent places that do not derive from each other. This feature makes that
  flow load-bearing for the vendor's billing arrangement *and* builds template submission on top of it.
- **Outcome:** The spike (Story 0) answered it, and the answer was **neither Branch A nor Branch B**. Meta's current
  version is **v4**; the 15 Oct 2026 deprecation names **v2 only**; we are on **v3**, so the deadline does not name
  us. The realised risk is therefore **not** « we are on the condemned version » but « we are one version behind
  current, on a flow we are about to make load-bearing ».
- **Resolution:** D-1a — migrate to v4 inside **Part 4 § 31**, which is four small edits in one file (the delta to
  Meta's sample is a **single key**), plus the two defects the same reading exposed: only `FINISH` handled out of
  five finish types, and `business_id` dropped. The two version pins were consolidated in Story 0 regardless, as
  planned.
- **What remains open:** whether **v3 itself** carries an end date — Meta's « Versions » section is unread. It does
  not change what § 31 does; it only turns a chosen migration into a dated one.
- ⚠️ **The spike is the reason this is small.** Both branches the plan declared were wrong, and that was discovered
  before a line of connection work existed — which is exactly the failure « the expensive order » names.

### R-2a: Graph API is pinned five versions behind (`v21.0` vs Meta's recommended `v26.0`)
- **Description:** Story 0 found Meta's own sample recommending **v26.0**. Every server-side Graph call — the
  onboarding exchange, Part 4's template submission and the status poll — runs on v21.0.
- **Likelihood:** Medium · **Impact:** Medium · **Part:** follow-up (§ 43), deliberately **not** Part 4
- **Mitigation:** Story 0 turned it into a **one-key** change (`META_GRAPH_API_VERSION`, feeding both the server and
  the browser SDK), so the edit is trivial. It is kept out of § 31 because it moves every Graph call at once and
  deserves its own change and its own test — bundling it into the v4 migration would make one failure indivisible
  from the other.
- **Contingency:** If template submission (§ 32) turns out to need a newer Graph version, the bump moves *into*
  Part 4 and is tested there.

### R-3: Meta rules the spec marks unconfirmed turn out to be wrong
- **Description:** The display-name guideline list, template edit caps, whether one payment instrument can serve
  several accounts, and Tunisia's actual per-message rates are on JS-gated pages the spec could not read.
- **Likelihood:** Medium · **Impact:** High · **Part:** 4
- **Mitigation:** The spike opens all four. The design already absorbs the worst case: **one message is one unit
  whatever Meta charges**, so a rate surprise moves the vendor's cost and not the product's arithmetic; FR-7b watches
  the category precisely because Meta can move it under us.
- **Contingency:** A wrong rule shows up as a `messaging-report` finding inside the 60-day appeal window rather than
  on an invoice. If template submission is refused for a reason the spike missed, the cabinet reads « modèle refusé »
  with a contact route — a supported state, not a broken one.

### R-4: The counting increment is not atomic with the send
- **Description:** FR-1 requires the unit and the `Sent` mark to ride one commit. `NotificationJob` commits per row,
  so this works only if the increment is **staged** into that same `SaveAsync` rather than written afterwards.
- **Likelihood:** Low · **Impact:** High — the counter is the billing record
- **Part:** 1
- **Mitigation:** Load the month row and call `RecordSend()` **before** `SaveAsync(notification)`, never after;
  `PlatformAccessLedger.RecordAsync`'s staging shape. EC-14's test asserts a crash between the send call and the
  commit leaves neither mark.
- ⚠️ **The one thing that must NOT be staged into that save is the row's creation** (step 14a). An `INSERT` can fail
  on the unique `(ClinicId, MonthKey)` when the daily provisioning pass races it, and a failure *there* fails the
  commit that marks the reminder `Sent` — after Meta accepted it. So the ensure-create is its own earlier save, and
  the send's commit only ever updates. The race window is exactly month rollover, i.e. when the daily pass runs.
- **Contingency:** `verify-schema`'s reconciliation compares the counter against surviving reminder rows inside the
  90-day retention window; a finding is a real defect, not expected drift.

### R-5: Held rows accumulate and starve the outbox
- **Description:** A held row has no attempt counter, no expiry and is excluded from the purge by construction, so it
  is re-examined on every review tick for as long as it exists — the starvation shape this outbox has been bitten by
  **twice** (L3's original `Blocked` defect, and Part G's un-park re-arming it).
- **Likelihood:** Medium · **Impact:** High — one cabinet parked at zero would stop every other cabinet's reminders
- **Part:** 1
- **Mitigation:** Three terms, and the third had to be added because the first two do not cover every row.
  **(a)** **AC-4.5a's obsolescence is the primary drain** — a held row whose appointment has passed becomes an
  ordinary terminal row the existing purge collects, and it does so *without waiting for the month to turn*, which is
  what actually bounds the common case. **(b)** AC-4.2's month-rollover pass re-evaluates held rows, mostly *feeding*
  (a) rather than releasing anything sendable. **(c)** Step 15a's **reason-agnostic age bound**
  (`Reminders:HeldMaxDays`, default 30) covers the rows (a) and (b) cannot reach. The review scan also keeps its
  **per-clinic fair-share bound**, which is the fix Part G's own review needed.
  ⚠️ **The ordering of (a) and (b) was corrected after review**: holding was originally justified by the rollover
  « releasing » held rows, but a reminder is only measured against the allowance when it comes **due** — 24 h or 6 h
  before the visit — so by the 1st its appointment has passed and it is refused, not sent. Holding earns its keep on
  the **top-up** (AC-6.3/EC-2), not on the calendar.
- ⚠️ **(a) and (b) alone were not a bound, and the gap is a whole class of row rather than an edge case.** AC-4.5a
  keys on the appointment, and a **recall** row carries `appointmentId: null` (`ReminderScheduler.cs:128`), so
  nothing can ever make one obsolete — and AC-5.3 creates precisely that row by design. The two pre-existing channel
  reasons have the same defect on a recall row **today**, which is why the bound is on the parked age rather than on
  the reason: « how long may a send wait? » gets one answer, not one per reason.
- ⚠️ Note what the per-clinic bound does and does not do: it limits the **blast radius** to that cabinet's own share
  of each tick, so one practice can no longer starve the others — but it does not stop the rows accumulating, and
  `GET /api/outbox`'s « age of the oldest waiting row » would climb for ever with nothing able to act on it.
- **Contingency:** `GET /api/outbox` reports blocked depth and the age of the oldest waiting row — the reading that
  distinguishes a queue from a stoppage.

### R-6: The stored allowance snapshot drifts from the ledger
- **Description:** `ClinicMessagingMonth.AllowanceMessages` is a derived copy, and nothing in the model can say it
  must equal the fold. A missed rewrite on an allocation or a cancellation leaves the practice and the console
  reading a figure the ledger does not support — and both screens look right alone.
- **Likelihood:** Medium · **Impact:** High · **Part:** 1, 5
- **Mitigation:** Every writer goes through the single `MessagingAllowanceRefold`; `verify-schema`'s
  `monthly-allowance-matches-ledger` re-derives each row through the **real** fold and reports both directions.
- **Contingency:** The check names the drifting cabinets and months; the fix is to re-run the refold, since `EndsOn`'s
  analogue here is entirely derived.

### R-7: A new console field leaks a clinical fact
- **Description:** The tenant filter is deliberately lifted on the console surface, so the only guarantee is the
  closed set of returned field names — and this feature adds a whole object to two console reads.
- **Likelihood:** Low · **Impact:** High · **Part:** 3
- **Mitigation:** `PlatformReadShapeTests` recurses every leaf and fails in **both** directions. Declare only names
  the DTOs return. ⚠️ Do not reuse `Note`/`Reference`, declared as vendor-*payment* fields — a semantic overload is
  not a free pass, and the read-shape set contains no `Message`, `Quota`, `Template` or `Phone` name today.
- **Contingency:** Adding a name is the review; a reviewer refusing the diff is the control.

### R-8: Closing the manual credential fields weakens tenant channel isolation
- **Description:** `ReminderSettingsProvider.ClaimsItsOwnWhatsApp` is the fix for `SECURITY_REVIEW_2026-08` finding A
  — supply any of endpoint/identity/secret and you own **all** of it, inheriting nothing. AC-1.7 removes the fields
  from the surface, and a careless implementation could remove the *rule* with them.
- **Likelihood:** Low · **Impact:** High — remote theft of an install-wide secret
- **Part:** 4
- **Mitigation:** Close the fields at the **command** (refuse them where the capability holds), never by touching the
  provider. `ReminderSettingsChannelIsolationTests` stays byte-for-byte unchanged and is re-run.
- **Contingency:** If the test needs any edit at all, stop and re-read the security review before proceeding.

### R-9: The daily job's three duties fail together
- **Description:** D-2 puts month provisioning, warning reconciliation and the template poll in one pass. A throw in
  one duty could cost a cabinet its counting row — and a missing row is what makes « non mesuré » appear.
- **Likelihood:** Medium · **Impact:** Medium · **Part:** 2, 4
- **Mitigation:** One try/catch **per cabinet per duty** inside the loop, logged and skipped —
  `ClinicActivityCounterJob`'s shape. Provisioning runs **first**, so the cheapest and most load-bearing duty is
  never behind the two that call Meta.
- **Contingency:** `messaging-month-covers-every-clinic` is exactly the check that says the pass stopped; a cabinet
  skipped every night would otherwise cost nothing visible.

### R-10: The webhook is a new anonymous, internet-reachable POST
- **Description:** FR-7a needs a Meta-callable endpoint. It is the first anonymous write this product exposes to the
  public internet on the hosted deployment, and a forged callback could park or release a cabinet's reminders.
- **Likelihood:** Medium · **Impact:** High · **Part:** 4
- **Mitigation:** `X-Hub-Signature-256` HMAC-SHA256 over the **raw** body against `Meta:AppSecret`, verified before
  anything is parsed; a failed verification is a flat refusal that writes nothing. Both actions go onto
  `ControllerAuthorizationCoverageTests`' reviewed anonymous set, which fails the build until reviewed. The endpoint
  writes **only** the template status of the WABA the payload names, resolved back to a cabinet we already know.
- ⚠️ **Being anonymous costs it its tenant scope, which is a correctness risk and not a security one.** No `User` row
  ⇒ `TenantScopeMiddleware` leaves `ITenantScope` `Unset` ⇒ every clinic filter reads **zero rows with no error**, so
  the un-scoped version of this endpoint is not insecure, it is **inert**. It declares `UseSystemWide` and resolves
  the cabinet through the new `IgnoreQueryFilters()` WABA lookup; see Part 4 § 34 for why neither derived guard sees
  the omission and why the reconciling poll masks the symptom.
- **Contingency:** The reconciling poll is an independent second writer, so the webhook can be disabled at the proxy
  without stranding any cabinet — which is FR-7a's whole argument for having both.

### R-11: `LocalClinicProvisioning`'s signature change breaks three callers
- **Description:** `ProvisionAsync` is a `static` taking its repositories as parameters, with three callers.
- **Likelihood:** High (certain) · **Impact:** Low — it is a **compile error**, which is the design
- **Part:** 1
- **Mitigation:** Exactly the `StageEntitlementAsync` precedent; `ClinicCreationMessagingAllowanceTests` derives the
  door set by scanning for `new Clinic(` rather than listing today's doors, so a fourth door added later fails too.
- **Contingency:** None needed — a missed caller cannot compile.

### R-12: The default standing allowance is a commercial decision not yet made
- **Description (as recorded):** The figure cannot be finalised before Meta publishes the rates replacing the free
  rules that end 1 October 2026.
- ⚠️ **Story 0 found that premise to be FALSE, and the risk is weaker than written.** Meta's pricing page lists both
  free rules as **current with no end date** — « utility templates delivered within an open customer service window
  are free », and service conversations free since 1 Nov 2024. What actually falls on **1 Oct 2026** is a rate-card
  update moving named markets out of regional pricing (Bangladesh, Iraq, Nepal, Sri Lanka, Kazakhstan, Kuwait,
  Morocco, Oman, Ukraine) — **Tunisia is not among them**. Tunisia (calling code **216**) is priced as
  **« Rest of Africa »**, and rates may change only on 1 Jan / 1 Apr / 1 Jul / 1 Oct with ≥ 1 month's notice.
  So the figure is **not blocked on an unpublished rate**; it is blocked only on a commercial decision.
- ⚠️ **What is unchanged, and now confirmed:** a reminder is a *proactive* utility template sent **outside** any
  customer-service window, so it **is** charged. The free-in-CSW rule does not apply to what this feature meters.
- ⚠️ **Bonus, in the vendor's favour:** utility rates fall by **monthly volume**, aggregated « at the business
  portfolio level, across all WhatsApp Business accounts owned by the portfolio » — so every cabinet's traffic
  counts toward one tier and the cost per message drops as the portfolio grows. Out of scope (one message is one
  unit regardless), but it is the economics behind whatever default is chosen.
- **Likelihood:** High · **Impact:** Low · **Part:** 0
- **Mitigation:** It is **operator configuration** (`Messaging:DefaultMessagesPerMonth`), so shipping a provisional
  number costs one edit. Nothing in the code depends on its value.
- **Contingency:** Change the config key; the daily pass rewrites the snapshot from the fold on its next run.

### R-13: The rollout backfill misfires on a populated database
- **Description:** FR-3 gives every existing cabinet a standing allowance in the migration. Re-running it, or running
  it after new cabinets have their own entry, could double-allocate.
- **Likelihood:** Low · **Impact:** High · **Part:** 1
- **Mitigation:** Gate the insert on « this cabinet has **no standing entry** » — not on « no *rollout* entry », which
  is the mistake `AddClinicSubscriptions` explicitly avoided (gating on « no `Grandfathered` row » would have given a
  paying cabinet's trial an open-ended entry). Place the backfill **below every DDL statement**. Remove the
  scaffolded `xmin` columns by hand.
- **Contingency:** `monthly-allowance-matches-ledger` and `messaging-month-covers-every-clinic` catch both failure
  directions; the before/after diff is run on a throwaway database first.

### R-14: `dotnet test` blocked by Smart App Control
- **Description:** The dev machine intermittently refuses freshly-built test assemblies (`0x800711C7`).
- **Likelihood:** Medium · **Impact:** Medium · **Part:** all
- **Mitigation:** `dotnet test -c Release`, and `BaseOutputPath=<temp>` outside the repo — which is also what lets
  `dotnet ef migrations add` work while the dev API holds `api/**/bin`.
- **Contingency:** CI's `api` job runs the same suite on a clean runner.

---

## Breaking Changes

Additive on `SelfHostedLan` and `CloudBrowser`: the capability is off, every surface is **absent** and existing
WhatsApp behaviour is byte-for-byte unchanged (EC-16, verified in Part 5). Three changes are visible elsewhere:

### Change 1: WhatsApp reminders can now be held on `HostedMultiTenant`
- **What breaks:** A cabinet past its monthly allowance stops sending WhatsApp reminders until the month rolls over
  or the vendor grants more.
- **Who is affected:** Every cabinet on the hosted deployment.
- **Handling:** This *is* the feature. FR-3's rollout backfill gives every existing cabinet the standing allowance
  before enforcement can bite, so no practice's reminders stop because this shipped. SMS is untouched (AC-4.6).

### Change 2: The manual WhatsApp credential fields disappear on `HostedMultiTenant`
- **What breaks:** An admin can no longer paste an endpoint, phone-number id or access token there.
- **Who is affected:** Any hosted cabinet already using its own Meta credentials — **count unknown**, and named as
  an open question in the spec.
- **Handling:** Out of scope as product work and named as an **operator task** in the runbook. The count must be
  established before Part 4 ships; if it is non-zero, those cabinets need a migration path agreed first.

### Change 3b: A reminder held for more than 30 days is failed rather than held for ever
- **What breaks:** Step 15a's reason-agnostic age bound applies to the **pre-existing** hold reasons too, so a row
  parked by `ChannelDisabled` or `ChannelUnconfigured` for over `Reminders:HeldMaxDays` now becomes a recorded
  failure instead of waiting indefinitely for an operator to configure the channel.
- **Who is affected:** Every deployment. In practice: rows parked when a channel was switched off and never switched
  back on — most visibly **recall** rows, which have no appointment and so were unbounded by construction.
- **Handling:** Deliberate, and it is what makes AC-4.4's « never purged **while they could still be sent** » a bounded
  claim rather than an unbounded one. L3's original intention — « it sends once the operator configures the channel » —
  is preserved for a month, which is longer than any real configuration gap and shorter than for ever. The failure is
  **recorded** and surfaces on « Rappels », so a 40-day-old undeliverable reminder stops being invisible.

### Change 3: A reminder for an appointment that has already started is no longer sent
- **What breaks:** `AC-4.5a` adds a dispatch-time guard to **every** appointment-bearing reminder, so a merely
  delayed `Pending` row now fails as obsolete rather than texting a patient about a visit under way.
- **Who is affected:** Every deployment — this closes a pre-existing defect, including on the subscription-release
  path.
- **Handling:** Deliberate, and the spec calls it out as new work. The failure is **recorded**, not silent, and
  surfaces on « Rappels » like any other.

---

## Migrations

### Migration 1: `AddClinicMessagingAllowances`
- **What:** Two tables (`MessagingAllowanceEntries`, `ClinicMessagingMonths`), six new columns
  (`StaffNotifications.MessagingThresholdPercent` + `.MessagingAllowanceMonth`; `ClinicReminderSettings`'
  four template columns), and the FR-3 rollout backfill.
- **When:** With the Part 1 deployment, before enforcement is reachable.
- **Steps:**
  1. Run `verify-schema` and `reconcile-money` and keep both outputs.
  2. Scaffold with `dotnet ef migrations add AddClinicMessagingAllowances` (`BaseOutputPath` outside the repo).
  3. **Remove the scaffolded `xmin` columns by hand** from both `CreateTable` blocks — EF maps `Entity<T>.Version`
     onto PostgreSQL's *system* column and the differ emits it as a real one, which PostgreSQL rejects.
  4. Add the backfill **below every DDL statement**: one `Standing` entry per cabinet at
     `Messaging:DefaultMessagesPerMonth`, effective the rollout month, **gated on « this cabinet has no standing
     entry »**; then that month's `ClinicMessagingMonth` row per cabinet, gated on the unique key.
  5. Apply to a **throwaway** database seeded with several cabinets and confirm the gating and the counts.
  6. Apply for real; run `verify-schema` and `reconcile-money` again and **diff** against step 1.
- **Rollback:** `Down()` drops the two tables and the six columns. The backfill is not reversed — it writes only
  ledger rows, which are append-only and harmless without the enforcement that reads them.

### Migration 2: Configuration (not a schema change)
- **What:** `Messaging:DefaultMessagesPerMonth`, `Messaging:ContactEmail`, `Messaging:ContactPhone`, plus the Meta
  webhook verify token, added to `deploy/docker-compose.hosted.yml` and `.env.hosted.example`. Also
  **`Reminders:HeldMaxDays`** (default 30, step 15a) — read on the same fall-back-never-throw rule, and it applies to
  **every** profile, unlike the `Messaging:*` keys, because the held-row defect it bounds is pre-existing and
  deployment-independent.
- **When:** Before Part 2 ships — the screen an exhausted cabinet opens renders **no** contact route where these are
  unconfigured (AC-2.7), which is correct but not what we want in production.
- **Rollback:** Remove the keys; every value falls back rather than throwing.

### Enum extensions — deliberately **no** migration
`OutboxBlockReason` and `PlatformAccessAction` are mapped `HasConversion<int>()`, so their new members need no schema
change at all — a missing member would simply be an unrepresentable hold or an unrecordable action, which is the FR-8a
extension-point contract.
