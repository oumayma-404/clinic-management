# Progress — Forfait de rappels WhatsApp (vendor-purchased messaging quota)

**Feature:** [spec.md](./spec.md) · [plan.md](./plan.md) · [stories/](./stories/README.md)

---

## Story 0 — Embedded Signup version confirmation

**Status:** steps 1 and 4 **done** · steps 2, 3, 5 **awaiting a logged-in Meta browser session**

### Step 1 — What is actually deployed ✅

Read from source, not from the spec.

**The `FB.login` config** (`web/components/reminder-settings.tsx:284-289`):

```js
window.FB.login((response) => void finishConnect(response), {
  config_id: META_CONFIG_ID,
  response_type: "code",
  override_default_response_type: true,
  extras: { setup: {}, sessionInfoVersion: "3" },
})
```

**The spec's assertion is CONFIRMED** — `extras.sessionInfoVersion = "3"` is present and there is **no
`featureType`**. That exact pair is what step 2 has to look up. (Recording the confirmation deliberately: without
it the next person re-runs this whole check.)

**`FB.init`** (`:232`) is called with `version: META_GRAPH_VERSION`, i.e. the browser SDK is versioned separately
from the server's Graph client.

**The two `v21.0` pins, confirmed independent:**

| Pin | Was | Configures |
|---|---|---|
| `api/…/Infrastructure/Services/MetaConfig.cs:14` | `DefaultGraphApiVersion = "v21.0"`, overridable by `Meta:GraphApiVersion` | the **server's** Graph API calls |
| `web/components/reminder-settings.tsx:47` | `META_GRAPH_VERSION = "v21.0"`, **hard-coded** | the **browser JS SDK** via `FB.init({ version })` |

Neither derived from the other, so `Meta:GraphApiVersion` moved the server and left the browser behind.

⚠️ **Nuance the plan slightly overstates:** these configure two *different clients*, not one value written twice.
They should still move together (Meta versions them as one release), which is what step 4 now enforces — but
"duplicate constant" is not quite the right description of what it was.

### Step 4 — The two pins are now one ✅

Consolidated via a **single `.env` key**, `META_GRAPH_API_VERSION`, rather than a YAML anchor — the hosted compose
already uses `${VAR:-default}` throughout, so this matches its own idiom and puts the value in `.env.hosted`.

| File | Change |
|---|---|
| `web/components/reminder-settings.tsx:47` | `process.env.NEXT_PUBLIC_META_GRAPH_VERSION ?? "v21.0"` — matches its two siblings (`META_APP_ID`, `META_CONFIG_ID`), which were already env-driven; it was the only literal of the three |
| `web/Dockerfile` | `ARG` + `ENV NEXT_PUBLIC_META_GRAPH_VERSION` |
| `deploy/docker-compose.hosted.yml` | `api: Meta__GraphApiVersion: ${META_GRAPH_API_VERSION:-v21.0}` and `web build args: NEXT_PUBLIC_META_GRAPH_VERSION: ${META_GRAPH_API_VERSION:-v21.0}` |
| `deploy/.env.hosted.example` | the `META_GRAPH_API_VERSION` key + why it is one key |

⚠️ **The web half had to be a build ARG, not a runtime variable.** `NEXT_PUBLIC_*` is substituted into the bundle
by `npm run build`, so a runtime value never reaches the browser — the trap `web/Dockerfile` already documents for
`NEXT_PUBLIC_API_URL` (it shipped as an `ENV` with no `ARG` for the file's whole life, silently ignoring the
compose arg). Consequence for the operator: changing this key needs `up -d --build`, not a restart.

⚠️ **The version value itself is unchanged** (`v21.0` everywhere). Step 4 moved *where the number lives*; changing
it is Branch B's business.

### 🔴 Finding — the hosted deployment has NO Meta configuration at all

`grep -rn "META\|Meta__" deploy/` returned **nothing** before this change. So on `HostedMultiTenant`:

- `Meta__AppId` / `Meta__AppSecret` are unset ⇒ the server cannot complete the Embedded-Signup token exchange
- `NEXT_PUBLIC_META_APP_ID` / `NEXT_PUBLIC_META_CONFIG_ID` are unset ⇒ `META_APP_ID` is `""`, so the SDK-loading
  effect returns early and the connect button no-ops

Meanwhile `DeploymentProfile.ExposesMetaOnboarding` is **true** for this kind, so the UI presents a flow that
cannot run. This is not a Story 0 defect and is **not fixed here** — but it is a prerequisite for Part 4, and the
plan's Migration 2 (configuration) should carry the four Meta keys alongside the `Messaging__*` ones. A note has
been left in the compose file at the point where they belong.

---

## Step 2 — Version confirmed ✅ — **and it is neither branch the plan declared**

**Source:** `developers.facebook.com/documentation/business-messaging/whatsapp/embedded-signup/implementation`
(page dated **28 June 2026**).

> « L'inscription intégrée **v2** sera abandonnée le **15 octobre 2026**. Migrez votre intégration vers la **v4**
> avant cette date. »

### 🔴 The current version is **v4**, not v3

The plan's D-1 declared two branches — **A** (already current, i.e. v3) and **B** (v2, migrate). Meta documents a
**v4**, so the real answer is a **third outcome** the plan did not carry, and R-2's framing is out of date.

**Where we actually stand — we are on v3, not v2:**

| | Our `FB.login` config | Meta's **v4** sample |
|---|---|---|
| `config_id` | ✅ | ✅ |
| `response_type: 'code'` | ✅ | ✅ |
| `override_default_response_type: true` | ✅ | ✅ |
| `extras.setup: {}` | ✅ | ✅ |
| `extras.sessionInfoVersion` | **`"3"`** | **absent** |

⚠️ **The only difference is one key.** `sessionInfoVersion: "3"` is the v3 marker; v4's sample carries no
`sessionInfoVersion` at all. So the 15 Oct 2026 deprecation targets **v2 and does not name us** — we are a version
behind current, not on the condemned one.

⚠️ **And the migration looks like deleting one line, not Branch B's rewrite.** Our `message`-event handler already
reads exactly v4's documented success payload (`data.waba_id`, `data.phone_number_id`, `type ===
'WA_EMBEDDED_SIGNUP'`, `event === 'FINISH'`), so nothing downstream of the config appears to change.

⚠️ **Not yet confirmed:** whether **v3 itself** has an end date. The page points to a « Versions » section for the
full upgrade path, which has **not** been read. That is the one open question on this step.

### Other findings from the same page — three affect the plan

1. **`account_update` is REQUIRED for Embedded Signup**, and the plan says the opposite by implication.
   > « Vous devez vous abonner au webhook `account_update` … déclenché dès qu'un·e client·e termine le processus. »

   Part 4 § 34 warns that messaging-limit changes arrive on `business_capability_update`/`account_alerts`
   « **not** `account_update` ». That warning is about *messaging limits* and remains plausible, but as written it
   reads as « `account_update` is the wrong field », which is **false**: it is mandatory for onboarding, and the
   pricing page shows it also carries `VOLUME_BASED_PRICING_TIER_UPDATE`. **Template status is still
   `message_template_status_update`** — the plan's actual subscription choice is unaffected. The *wording* needs
   correcting so nobody reads it as « do not subscribe to `account_update` ».

2. **The exchangeable code lives 30 seconds** (« une durée de vie de 30 secondes »). `ConnectWhatsAppCommand` must
   exchange promptly; Part 4 § 33 adds **template submission** after that exchange, so the submission must happen
   *after* the token is obtained, never between the code arriving and its exchange.

3. **Meta's own sample recommends Graph `v26.0`**; we pin **`v21.0`** — five versions behind. Now a one-key change
   (`META_GRAPH_API_VERSION`, step 4), but **bumping it is not part of Story 0** and needs its own test.

### Two smaller findings, recorded rather than acted on

- **Our origin check is stricter than Meta's, and correctly so.** The sample uses
  `event.origin.endsWith('facebook.com')` — which also matches `notfacebook.com`. Ours is an allow-list of
  `https://www.facebook.com` / `https://web.facebook.com`. **Do not "align with the sample" here.**
- **We handle only `event === 'FINISH'`.** The doc lists five finish types — `FINISH`, `FINISH_ONLY_WABA`,
  `FINISH_WHATSAPP_BUSINESS_APP_ONBOARDING`, `FINISH_OBO_MIGRATION`, `FINISH_GRANT_ONLY_API_ACCESS` — plus `ERROR`.
  A cabinet finishing through any other type is silently dropped and the connection appears to have failed. Also,
  the payload now carries **`business_id`**, which we do not capture. Both are Part 4 work.

---

## Step 3 — Three of four answered ✅ / ⚠️

### ✅ Tunisia's pricing region — **« Rest of Africa »**

Calling code **216**, ISO **TN**, mapped to **Rest of Africa**. The rate *numbers* are in per-currency CSV/PDF rate
cards linked off the pricing page, not in its text, so the figure itself is still unread.

### 🔴 The spec's « both free rules end 1 October 2026 » is **wrong**

The spec's Dependencies section says both free rules — service messages, and utility templates inside an open 24 h
window — **end** on 1 Oct 2026, and that replacement rates were overdue. The pricing page says otherwise:

- « **Utility templates delivered within an open customer service window are free** » — stated as **current**
  behaviour, with no end date.
- « Service conversations are now free for all businesses » — since **1 Nov 2024**, no end date.
- What actually happens on **1 Oct 2026** is a **rate-card update** moving named markets out of regional pricing:
  Bangladesh, Iraq, Nepal, Sri Lanka, Kazakhstan, Kuwait, Morocco, Oman, Ukraine. **Tunisia is not among them.**
- Rates may change only on 1 Jan / 1 Apr / 1 Jul / 1 Oct, with ≥ 1 month's notice; the current cards are effective
  1 Jul 2026.

⚠️ **Consequence for R-12:** the risk is **weaker than recorded**. Tunisia stays in Rest of Africa and is not named
in the October change, so the default allowance figure is not blocked on an unpublished rate. It remains operator
config — ship a provisional number — but the stated reason for the delay does not hold.

⚠️ **Consequence for the feature's premise — unchanged and now confirmed.** A reminder is a *proactive* utility
template sent **outside** any customer-service window, so it **is** charged. The free-in-CSW rule does not apply to
us, which is exactly what the allowance is metering.

### ✅ FR-7b's premise — **confirmed verbatim**

> « Whenever a template is used, a business accepts the charges associated with the category applied to the template
> at time of use. »

The auto-recategorisation exposure FR-7b is built around is real and stated in Meta's own words.

### ✅ Bonus finding — **volume tiers work in the vendor's favour**

Lower utility rates unlock by monthly volume, and « messages are aggregated at the **business portfolio level,
across all WhatsApp Business accounts owned by the portfolio** ». So every cabinet's traffic counts toward one
tier: the vendor's cost per message **falls** as the portfolio grows. Tiers are per market–category and reset
monthly. An `account_update` webhook (`VOLUME_BASED_PRICING_TIER_UPDATE`) fires on reaching a tier. **Out of scope**
for this feature — one message is one unit regardless — but it is the economics behind the default allowance.

### ⚠️ Partially answered — one payment instrument across several accounts

Not directly stated. Supporting evidence: Business Manager supports a **business-owned line of credit** with
**Dynamic Credit Allocation** spreading it across accounts, and pricing aggregates across **all WABAs in one
portfolio**. Both point to yes, neither says it. **Still needs Billing Hub / a Meta rep to confirm.**

### ❌ Not answered — template edit caps

The page supplied was the **Messenger Platform** templates doc (Button / Generic / Receipt / Coupon …), not
WhatsApp message templates. The WhatsApp equivalent was not read.

What *was* found, and is a different thing — **display name** limits: changeable **10 times per 30 days**, and after
approval the number must be **re-registered within 14 days** or the name goes back for review
(`phone_number_name_update` webhook, `APPROVED` / `DECLINED`). Useful, but not the template cap.

---

## Step 5 — Branch decision: **pending, and the choice is not A or B**

Neither declared branch fits. The real options are recorded in the session; the outstanding inputs are:

- [ ] Does **v3** have its own end date? (the « Versions » section, unread)
- [ ] Is migrating to v4 accepted as Part 4 work? Current evidence says it is **deleting one key**, i.e. Branch-A
      sized rather than Branch-B sized.

**Chosen:** ✅ **Migrate to v4 inside Part 4 § 31** (plan D-1a). Neither declared branch — a third outcome.

**Size estimate:** Branch-A sized. Four edits in **one file** (`web/components/reminder-settings.tsx`): drop
`extras.sessionInfoVersion`, capture `data.business_id`, accept all five finish types (with `FINISH_ONLY_WABA` as
its own state — it means « finished with no phone number »), and **keep our stricter origin allow-list**. Part 4
does **not** split off; Parts 1–5 proceed as planned.

**Explicitly out of scope for § 31:** the Graph `v21.0` → `v26.0` bump (now a one-key change after step 4, but it
moves every server Graph call at once) — captured as a follow-up in plan § 43 and risk **R-2a**.

---

## Story 0 — CLOSED ✅

| Step | Outcome |
|---|---|
| 1 · Read what is deployed | ✅ Spec's `sessionInfoVersion: "3"` / no `featureType` **confirmed** |
| 2 · Confirm the version | ✅ **v4 is current; we are on v3; the 15 Oct 2026 deprecation names v2 only** |
| 3 · Four gated pages | ✅ Tunisia = « Rest of Africa » · ✅ FR-7b's premise confirmed verbatim · 🔴 the spec's « free rules end 1 Oct 2026 » **disproved** · ⚠️ payment-instrument only indirect · ❌ template edit caps unread (wrong doc) |
| 4 · Consolidate the pins | ✅ One `META_GRAPH_API_VERSION` key; `tsc` + 15 responsive checks + `build` all green |
| 5 · Choose the branch | ✅ Migrate to v4 in Part 4 § 31 |

### Documents amended from these findings

- **`spec.md`** — the « both free rules end 1 October 2026 » claim struck through and corrected (it was wrong).
- **`plan.md`** — **D-1** marked resolved · new **D-1a** (migrate to v4) · **§ 31** rewritten from « apply the
  branch » to the four concrete edits · **§ 34**'s `account_update` warning corrected (it read as « do not use
  `account_update` », which would have broken onboarding — it is **required**) · **§ 43** carries the three open
  items · **R-2** retired with its outcome · new **R-2a** (Graph five versions behind) · **R-12** weakened with the
  real pricing facts.

### Still open, and none of it blocks Story 1

1. **Does v3 have its own end date?** Meta's « Versions » section is unread. It changes only whether § 31 was
   optional, never what it does.
2. **WhatsApp template edit caps** — the page supplied was the *Messenger Platform* templates doc.
3. **One payment instrument across several accounts** — needs Billing Hub or a Meta representative.

⚠️ **What Story 0 bought.** Both branches the plan declared were wrong, and that was found **before a line of
connection work existed** — the exact failure « the expensive order » names. It also turned up two live defects in
the shipped connect path (only one of five finish types handled; `business_id` dropped) and one incorrect claim in
an approved spec. None of that was reachable without opening the page.

---

## Story 1 — Parts 0 and 1

**Session scope, chosen by the user:** Parts **0 and 1** (R-1's split point). Parts 2–5 are a later session.

**Branch:** `feature/windows-desktop-app`, continued at the user's instruction rather than a new
`feature/vendor-whatsapp-messaging-quota`.

### Working tree note (start of session)

Story 0's step-4 deliverable was **uncommitted** (`deploy/.env.hosted.example`,
`deploy/docker-compose.hosted.yml`, `web/Dockerfile`, `web/components/reminder-settings.tsx`), which would have made
this file's « Story 0 CLOSED ✅ » contradict the repository. Committed on its own (`137e1b8`) before Part 0.

Excluded from every commit, as unrelated work by another author: `api/.idea/.idea.ClinicManagement/.idea/jsLibraryMappings.xml`,
`features/hosted-security-hardening/`, `features/landing-website/agent-prompt.md`. Nothing was staged with
`git add -A`.

---

## Part 0 — Groundwork ✅

| Step | Outcome |
|---|---|
| 2 · `ClinicClock` month primitives | ✅ `MonthKey` · `CurrentMonthKey` · `TryParseMonthKey` · `MonthRangeUtc` · **`MonthToDateRangeUtc`** · `MonthLabelFr` (both overloads) · `NextMonthKey` · `PrecedingMonthKeys` · `FirstDayOfNextMonth` |
| 3 · Move, do not copy | ✅ `GetPlatformSummaryQuery.ClinicMonthRangeUtc` and `PlatformAccessLabels.Month` **deleted**; both symbols return zero hits outside `ClinicClock` |
| 4 · The FR-8a extension points | ✅ four `OutboxBlockReason` members · `NotificationCategory.MessagingAllowanceLow` + its `false` classification · `NotificationTargetKind.MessagingAllowance`. **The two `PlatformAccessAction` members are deferred to Part 3 — DEV-1** |
| 5 · The 18th capability | ✅ `SellsVendorMessaging` (`HostedMultiTenant` only) + `IVendorMessagingAvailability`/`VendorMessagingAvailability` |
| 6 · The allowance policy | ✅ `IMessagingAllowancePolicy`/`MessagingAllowancePolicy`, parsed by hand, registered in **`AddInfrastructure`** |

⚠️ **`ClinicMonthRangeUtc` moved onto `MonthToDateRangeUtc`, never onto `MonthRangeUtc`** — its upper bound is the
last tick of *today*, and its one caller feeds `PlatformSummaryDto.VendorCollectedThisMonthDt`, which
`MoneyReadConsistencyTests` deliberately does not cover (FR-2). A one-primitive « move » would have widened that
window by the rest of the month with nothing in the solution able to see it.
`ClinicClockMonthTests.The_Month_To_Date_Range_Reproduces_The_Private_Copy_It_Replaced` pins the old pair, and
`…Differ_Mid_Month` pins that the two primitives are not interchangeable.

### Deviations

#### DEV-1: The two `PlatformAccessAction` members are deferred to Part 3
**Date:** 2026-08-12 · **Story:** 1 (Part 0) · **Category:** Scope

**Original plan:** Step 4 declares `GrantedMessagingAllowance` and `CancelledMessagingAllowance` alongside the other
three extension points.

**Actual implementation:** Not declared. They arrive in Part 3 with `GrantMessagingAllowanceCommand` /
`CancelMessagingAllowanceCommand`.

**Justification:** `PlatformAccessAction`'s own doc comment states the opposite rule verbatim — « Each member arrives
with the write that produces it, never ahead of it … a member nothing can produce is a value the journal can never
show and a filter can never match, and the reader has no way to tell « jamais fait » from « pas encore possible » »
(the `platform-console` DEV-6 decision). **The spec's own FR-8a table repeats that rule in the same row.** Unlike the
other three points there is no guard that fires: `PlatformAccessLabels.Action` has a fall-through, so an undeclared
member costs nothing at build time, and a declared one costs the journal's honesty for three parts.

**Impact:** Part 3 declares them. Nothing in Parts 0–2 reads them.

**Approved:** Yes (user).

#### DEV-2: No `PushLabel` entry for `MessagingAllowanceLow`
**Date:** 2026-08-12 · **Story:** 1 (Part 0) · **Category:** Technical

**Original plan:** Step 4 asks for the `NotificationCategory` member « plus its `StaffNotificationRules`
classification (`false`, AC-3.4) **and** its `PushLabel` ».

**Actual implementation:** The `false` classification only.

**Justification:** The two are mutually exclusive by construction. `PushLabel` is reached **only** after
`ReachesALockedPhone(category)` returns true (`PushNotificationGeneratorDecorator.cs:201` guards `:219`), so a label
for a category classified `false` is unreachable — and its own fall-through message reads « Cette catégorie ne
produit pas de notification système ». All five existing non-pushing categories (`SubscriptionExpiring`, `LowStock`,
`StockExpiringSoon`, `BackupStale`, `ReminderFailed`) deliberately have no entry.

**Impact:** None. AC-3.4 is honoured by classifying, which is what it asks for.

**Approved:** Yes (user).

### Part 0 gate

| Check | Result |
|---|---|
| `ClinicMonthRangeUtc` / `PlatformAccessLabels.Month` outside `ClinicClock` | **0 hits** (one historical mention in a doc comment) |
| `ClinicClockMonthTests` | ✅ EC-7's 23:59-on-the-31st and 00:01-on-the-1st (both instants are 31 August by UTC); `MonthRangeUtc` ≠ `MonthToDateRangeUtc` mid-month **and** equal on the last day |
| `GetPlatformSummaryQuery`'s window unchanged | ✅ asserted against the deleted method's own expression for a mid-month « today » |
| `DeploymentProfileTests` | ✅ the two shipped kinds' truth table still holds (`SellsVendorMessaging` joins `hostedOnlyCapabilities`); the new capability is unreachable from `Messaging:*` **or** `Meta:*` |
| `dotnet build` (`--no-incremental`, `BaseOutputPath` outside the repo) | ✅ **0 errors**, 55 warnings — all pre-existing baseline, **0 in changed files** (grepped by filename) |
| Unit suite (`-c Release`, outside the repo) | ✅ **2823 passed / 0 failed** |

⚠️ `--no-incremental` matters: an incremental build recompiles nothing and reports « 0 Warning(s) » by skipping the
projects whose warnings it would have re-emitted.

---

## Part 1 — The allowance exists, sends are counted, reminders are held ✅

| Step | Outcome |
|---|---|
| 7 · Domain | ✅ `MessagingAllowanceEntry` (AggregateRoot) · `ClinicMessagingMonth` (plain `Entity<Guid>`, D-6) · `MessagingAllowanceKind` · `IMessagingAllowanceRepository` · `MessagingAllowanceLedger` (pure, total, clock-free, `monthKey` a parameter) |
| 8 · EF + repository + migration | ✅ two configurations · `MessagingAllowanceRepository` · both tables filtered in `ApplicationDbContext` · `20260812114707_AddClinicMessagingAllowances` with **both scaffolded `xmin` columns removed by hand** |
| 9 · The rollout backfill | ✅ below every DDL statement, gated on « no **standing** entry » (R-13), figure read back out of the ledger |
| 10 · Provisioning | ✅ `StageMessagingAllowanceAsync` — entry **and** month row in the same save; **all four doors** wired (a compile error by design, R-11) |
| 11 · Refold + refusals | ✅ `MessagingAllowanceRefold` (5-attempt `ConflictException` retry, detaching **both** the ledger and the month rows) · `MessagingRefusals` |
| 12 · The gate | ✅ `OutboxMessagingGate` — WhatsApp only, per-tick instance, per-cabinet cache, ordered terms with Part 4's template slot declared |
| 13 · Wired in both places | ✅ `DispatchAsync` after the subscription gate **and** `ReviewBlockedRowsAsync` for every parked row |
| 14 · FR-1's counting | ✅ `RecordSend()` staged into the **existing** `SaveAsync(notification)` — one commit |
| 14a · Ensure-create before the send | ✅ its own save, unique violation caught and re-read |
| 15 · AC-4.5a | ✅ the past-appointment guard at dispatch, `nowUtc` a parameter |
| 15a · The held-row age bound | ✅ `Reminders:HeldMaxDays` (default 30), reason-agnostic, asked first in the review pass |

### Deviations

#### DEV-3: The held-row age bound is keyed on `ScheduledFor`, not on a « parked at » column
**Date:** 2026-08-12 · **Story:** 1 (Part 1, step 15a) · **Category:** Technical

**Original plan:** « a row parked longer than `Reminders:HeldMaxDays` … keyed on the **parked age**, not the reason ».

**Actual implementation:** keyed on `Notification.ScheduledFor` — when the send became **due**.

**Justification:** `Notification` carries **no** `BlockedAtUtc` and no `UpdatedAt`, so there is no parked-age column to
read. Adding one is worse than it looks: `Unblock()` would have to clear it, and a row released by one term and parked
by another — which is the *normal* case now that two gates park rows — would restart its 30 days on every cycle,
**re-arming exactly the starvation the bound exists to stop**. `ScheduledFor` is monotonic, is already the column both
the due scan and the blocked scan order by (and the column each derives its per-clinic « oldest » from), and it is what
the plan's own gloss asks for: « one rule for *how long may a send wait?* » — waiting starts when the send was due, not
when we noticed.

**Impact:** No schema column. A row scheduled in the future is never aged out (the subtraction is negative), which is
correct. `NotificationJobMessagingTests` pins both directions — a row past the window drains, a row inside it is still
held — plus the configured-window case, so the setting is shown to be read rather than compiled in.

**Approved:** Implemented and flagged; grounded in the plan's own R-5 reasoning.

#### DEV-4: Three `NotificationJobTests` fixtures corrected — appointments moved from « now » to tomorrow
**Date:** 2026-08-12 · **Story:** 1 (Part 1, step 15) · **Category:** Technical

Step 15 is the plan's own **Breaking Change 3** (« a reminder for an appointment that has already started is no longer
sent »), and seven pre-existing tests went red on it. All seven stubbed the appointment at `DateTime.UtcNow` — which is
*by definition* already started — so they described a reminder for a patient already in the chair, i.e. precisely the
row the guard exists to stop. The fixtures were corrected (`AddDays(1)`), not the implementation.

⚠️ One of them, `A_Reminder_Still_Naming_The_Right_Moment_Sends_Normally`, pinned a **fixed literal**
`2026-03-10` that had silently become the past. It is now relative and **truncated to the minute** — relative because a
literal rots, truncated because the message carries `dd/MM/yyyy HH:mm` and the staleness check compares the formatted
round-trip.

### Part 1 gate

| Check | Result |
|---|---|
| `dotnet build` (`--no-incremental`, outside the repo) | ✅ **0 errors**, 55 warnings — the identical pre-existing baseline, **0 in changed files** |
| Unit suite (`-c Release`, outside the repo) | ✅ **2886 passed / 0 failed** (2823 before Part 1; **63 new**) |
| `MessagingAllowanceLedgerTests` | ✅ 31 — no entry folds to **`null` not 0`**; a raise is effective this month and a lowering next (EC-3); a cancellation applies to **every** month the entry fed including the current one (AC-7.4, EC-4); remaining floored at 0; idempotent and order-independent; **no `DateTime.UtcNow` in the file** |
| `OutboxMessagingGateTests` | ✅ 13 — SMS never consulted (AC-4.6); the capability off issues **zero** queries (EC-16); a missing row holds under its **own** reason and sentence (AC-4.3); one query per cabinet per tick; EC-7's 31 Aug / 1 Sep boundary |
| `NotificationJobMessagingTests` | ✅ 17 — send-and-count is **one commit** (EC-14); **a unique-violation collision on the month row's creation cannot cost a send** (§ 14a, EC-15); a **recall row (`appointmentId: null`) held past `HeldMaxDays` drains** while one inside the window stays held; a released reminder whose visit has passed fails as obsolete |
| `ClinicCreationMessagingAllowanceTests` | ✅ 2 — the door set **derived** by scanning for `new Clinic(`, with its own red-proof |
| `verify-schema` **before** | exit **2** — 5 DRIFT, every one a `MISSING` index or FK of the two new tables |
| `verify-schema` **after** | exit **0** — « schema matches the model » |
| The **diff** | ✅ **only the intended objects**: the 3 indexes and 2 FKs move `MISSING → present`, plus `MessagingAllowanceEntries.Amount: (18,3)` arriving from the model-wide convention. Nothing else changed. |
| Backfill applied for real | ✅ **5 clinics = 5 entries = 5 month rows**; `Kind = 1` (Standing), `Messages = 200`, `EffectiveMonth = 2026-08` (the **Tunisian** month), `Consumed = 0`; the snapshot equals the fold |
| R-13 idempotence, **proven by re-running** | ✅ both backfill statements re-executed against the populated database → **`INSERT 0 0`** twice, counts unchanged, zero probe rows |
| `reconcile-money` | ✅ exit **0**, « no drift detected »; the monthly « encaissé » baseline is unchanged. ⚠️ **The `before` side was not captured** — only `verify-schema` was run beforehand. The migration adds two tables and touches **no** money table, so there was nothing it could have moved, but the prescribed before/after pair is only half-satisfied for this verb. |

⚠️ **The scaffolder emitted `xmin` in both `CreateTable` blocks**, exactly as the plan predicted — PostgreSQL rejects it
(`column name "xmin" conflicts with a system column name`). Removed by hand; the only three `xmin` mentions left in the
file are in its doc comment.

---

## Story 1 — Part 2

**Session scope, chosen by the user:** Part **2** alone. Parts 3–5 are later sessions.

**Branch:** `feature/windows-desktop-app`, continued.

### Working tree note (start of session)

Clean apart from two untracked items excluded from every commit, as unrelated work by another author:
`features/hosted-security-hardening/`, `features/landing-website/agent-prompt.md`. Nothing was staged with
`git add -A`.

⚠️ **The dev database is ahead of this branch.** `clinic-postgres`'s `clinic_management` carries
`20260812103207_AddUserSecondFactorAndSessionFamilies`, which does **not** exist in this tree — another author's
in-flight migration. It affected nothing here (`verify-schema` reports only model→catalog *omissions*, so their extra
objects are invisible to it), but it is the reason the before/after diff below is not a clean-room rehearsal.

---

## Part 2 — The practice sees what it has left and is warned before it runs out ✅

| Step | Outcome |
|---|---|
| 16 · The two reads | ✅ `GetReminderAllowanceQuery` · `GetReminderAllowanceHistoryQuery` (floored per D-5) · `ReminderAllowanceDto`/`…MonthDto`/`…HistoryDto` · **`MessagingSenderState` + `MessagingSender.From/Label`** · `WhatsAppTemplateStatus` (Part 0's undelivered enum) |
| 17 · Exposed | ✅ `GET /api/clinics/reminder-allowance` + `…/history`, **`AnyClinicRole`**, 404 **before the mediator** on `!SellsVendorMessaging` |
| 18 · Thresholds + the pair | ✅ `MessagingAllowanceThresholds.Crossed` (**all** crossed, FR-6) · `StaffNotification.MessagingThresholdPercent`/`MessagingAllowanceMonth` + `ForMessagingAllowance` · `IStaffNotificationRepository.GetMessagingWarningAsync`/`GetMessagingWarningsAsync` · `INotificationGenerator.EnsureMessagingAllowanceWarningAsync`/`ClearMessagingAllowanceWarningsAsync` · migration `20260812131227_AddMessagingAllowanceWarningColumns` |
| 19 · Announced where the counter moves | ✅ `NotificationJob.AnnounceAllowanceThresholdsAsync`, post-commit on the `Sent` branch, taking the gate's own `RenewsOn` |
| 20 · The daily pass | ✅ `MessagingAllowanceJob` — provision **first**, then reconcile; `Cron.Daily(5)`, capability-gated with `RemoveIfExists` in the else; « today » a parameter; one try/catch **per cabinet per duty** (R-9) |
| 21 · US-5 | ✅ `RecallDispatchOutcome.MessagingAllowanceExhausted` · `ReminderScheduler`'s enqueue branch (WhatsApp-only ⇒ refuse; SMS also sendable ⇒ enqueue both) · `SendRecallCommand` reusing `MessagingRefusals.RecallExhausted` |
| 22 · Clinic surface | ✅ `web/lib/api/reminder-allowance.ts` · `messaging-allowance-card.tsx` · `messaging-allowance-history.tsx`, mounted in `app/rappels/page.tsx` behind a tri-state availability probe |
| 23 · AC-4.9 | ✅ `ReminderStatusDto.BlockReason` (the enum member's own name) · `ReminderLogCounts.HeldByAllowance` + `ReminderLogDto.HeldByAllowance` · the « en attente de forfait » line + the per-row hold-kind badge |

⚠️ **One reconciling call, not two, and it is the shape the two withdrawal criteria share.**
`ClearMessagingAllowanceWarningsAsync(clinicId, keepMonthKey, keepThresholds)` withdraws everything the cabinet no
longer meets: a grant shrinks `keepThresholds` (AC-3.6) and a rollover changes `keepMonthKey` (AC-3.7). Two methods
would be two obligations at every writer — the `fixes-dont-propagate` shape — and the *keep* framing is what preserves
the read markers on a row still true, which a clear-and-re-ensure would destroy.

⚠️ **The 100 % wording and `MessagingRefusals` are held to AC-4.2 by a test, not by eye.**
`No_Sentence_Promises_That_Held_Reminders_Go_Out_At_The_Rollover` asserts over the produced rows *and* all three
refusal strings that none says « partiront le », « dès le renouvellement » or « au renouvellement ». The remedy every
one of them offers is the **top-up**; the renewal date is stated as a fact about the forfait.

### Deviations

#### DEV-5: Part 2 carries its own migration; the plan's « Migration 1 » is split by part
**Date:** 2026-08-12 · **Story:** 1 (Part 2, step 18) · **Category:** Scope

**Original plan:** Migration 1 (`AddClinicMessagingAllowances`) bundles the two tables, the two `StaffNotifications`
columns **and** Part 4's four `ClinicReminderSettings` template columns.

**Actual implementation:** Part 1 shipped the two tables alone (already committed), so Part 2 adds
`AddMessagingAllowanceWarningColumns` for its own two columns and Part 4 will add its four.

**Justification:** Forced rather than chosen — Part 1 is committed. It is also consistent with the plan's own wording:
step 40 and the exit criteria say « before and after the migration **batch** », which already anticipates more than one.
A part cannot ship a column it does not write, and shipping Part 4's four now would put four unwritten columns in the
schema for two parts.

**Impact:** Part 5's before/after diff covers the batch, as planned. `verify-schema` needs no change: it diffs
indexes, FKs and decimal precisions, and this migration adds none.

**Approved:** Implemented and flagged; grounded in the plan's own « batch » wording.

#### DEV-6: `MessagingSender.From` takes a **nullable** template status
**Date:** 2026-08-12 · **Story:** 1 (Part 2, step 16) · **Category:** Technical

**Original plan:** « Add `MessagingSenderState` so AC-1.4's five states have one derivation », from
`(WhatsAppConnectionStatus, WhatsAppTemplateStatus)`.

**Actual implementation:** `From(connection, WhatsAppTemplateStatus? template)`, where **null means « this deployment
does not track a per-cabinet template yet »** and the connection alone decides.

**Justification:** The four template columns arrive in Part 4 (§ 33), so before then the answer is genuinely
*unknown* — not `NotSubmitted`. Defaulting to `NotSubmitted` would report « en attente de validation » for every
cabinet that is sending perfectly well today on the install's own pre-approved template: a statement about us,
rendered as a statement about them, which is the AC-2.4 mistake one field over. The five states are all derived now,
so Part 4 supplies a **value** rather than a second rule.

**Impact:** Part 4 passes the stored status and the null branch stops being reachable for a connected cabinet.
`Connected_Is_Never_Ready_While_The_Template_Is_Not` already pins all ten (connection × template) combinations.

**Approved:** Implemented and flagged.

#### DEV-7: `senderNumber` is always null, and says why
**Date:** 2026-08-12 · **Story:** 1 (Part 2, step 16) · **Category:** Technical

**Original plan:** The spec's endpoint contract carries `"senderNumber": "+216 •• ••• •12" | null`.

**Actual implementation:** The field exists on the DTO and is **always null**.

**Justification:** Nothing in the product stores a cabinet's WhatsApp **number** — onboarding keeps Meta's
`phone_number_id`, which is an opaque id. Masking an id into something shaped like a phone number would be an invented
fact on the one screen whose whole job is to state what is true. The field stays on the wire (a stable null, the
`PlatformSubscriptionPlaceholder` precedent) so Part 4 can fill it once it reads the number back from Meta.

**Impact:** The card renders no number today. Part 4 populates it.

**Approved:** Implemented and flagged.

### Auto-approved deviations

| Deviation | Classification | Reason |
|---|---|---|
| `OutboxMessagingGate.RenewsOn` added | Trivial-adjacent (a new member on a Part-1 class of this feature) | The parked sentence and the 100 % warning must name **one** date. Without it the job would read the clock a second time, and two reads either side of Tunisian midnight disagree. It also removes the inline `FirstDayOfNextMonth` call the gate already had. |
| `ClinicClock.ClinicToday()` threaded into `MessagingAllowanceJob.MayBeWarnedAsync` | Trivial | Same one-clock-per-pass rule the job already applies to its month key. |
| The three throwing `IStaffNotificationRepository` members in `SubscriptionWarningTests`' fake | Trivial | Required to compile, and *throwing* is that fake's own documented rule (« everything else throws »), which a subscription warning reaching a messaging read would violate. |
| `ReminderSchedulerTests`' harness gains two mocks | Trivial | A constructor change on an existing service; `SellsVendorMessaging` is stubbed **false** so those fixtures stay byte-for-byte the pre-Part-2 recall path. |

### Part 2 gate

| Check | Result |
|---|---|
| `dotnet build` (`--no-incremental`, `BaseOutputPath` outside the repo) | ✅ **0 errors**, 55 warnings — the identical pre-existing baseline, **0 in changed files** (the only hit under a changed filename is `Program.cs:462`, Hangfire's obsolete `UsePostgreSqlStorage`, ~540 lines from the edit) |
| Unit suite (`-c Release`, outside the repo) | ✅ **2939 passed / 0 failed** (2886 before Part 2; **53 new**) |
| `MessagingAllowanceWarningTests` | ✅ 20 — three thresholds from one jump are **three** rows with three ids (AC-3.1/3.2); a zero allowance yields the 100 % row alone; a grant withdraws what is no longer met and **keeps the id** of what is (AC-3.6); a rollover withdraws last month's and **re-arms** (AC-3.7); an expired cabinet is not warned; the capability off reads **nothing at all**; provisioning from the **fold** (not the policy default); no row for a cabinet with no ledger (AC-4.3); a drifted snapshot rewritten (R-6); a failing warning pass **cannot cost the counting row** (R-9) |
| `ReminderAllowanceQueryTests` | ✅ 22 — the three-way « 0 rappel envoyé » / « non mesuré » / failed-read distinction; remaining floored at 0 over a cancellation; the contact route **absent, not empty**; the D-5 floor in both directions (pre-rollout months omitted, a gap **inside** the range still « non mesuré »); a past month showing the allowance in force **then**; all ten (connection × template) states |
| `RecallMessagingRefusalTests` | ✅ 11 — WhatsApp-only + spent ⇒ refused and **nothing queued** (AC-5.1); SMS also sendable ⇒ **both** rows queued and no refusal (AC-5.3); a cabinet with **no** counting row is *not* refused as exhausted (AC-4.3's mirror at enqueue); the patient left untouched (AC-5.2); the two refusals read differently (AC-5.4) |
| **Red-proof, executed** | ✅ Two deliberate defects — `Crossed` returning only the largest, and the reconciliation ignoring `keepThresholds` — turned **7** cases red including all four headline ones, then were reverted and the suite re-run green (118 messaging tests) |
| `verify-schema` **before** | exit **0** — « schema matches the model » |
| `verify-schema` **after** | exit **0** — « schema matches the model » |
| The **diff** | ✅ **only the generated timestamp**. Expected and correct: this migration adds two **nullable** columns and no index, FK or decimal, which is precisely the set `verify-schema` diffs. It is not a null result — it is the gate confirming the migration moved nothing it can see. |
| Migration applied for real | ✅ `MessagingAllowanceMonth varchar(7) NULL` + `MessagingThresholdPercent integer NULL`, **both with no `column_default`** — read back out of `information_schema` |
| `reconcile-money` | ✅ exit **0**, no drift. ⚠️ **After only.** The migration touches `StaffNotifications` alone — no money table — so there was nothing it could move, but the prescribed before/after pair is again only half-satisfied for this verb (the same note Part 1 carries). |
| `web`: `npx tsc --noEmit` | ✅ clean |
| `web`: `npm run check:responsive` | ✅ **15/15** |
| `web`: `npm run build` | ✅ compiled; `/rappels` 13.2 kB |
| Responsive eye pass at 320/390/820/1180/1440 px | ⚠️ **Not done — no browser automation on this machine** (`agent-browser` is not installed). Fell back to the mechanical gate plus a re-read of the frontend diff against `DEVICE-CONTRACT.md` § 1, which **found a real defect** — see below. It remains owed. |

⚠️ **The diff re-read earned its keep: two adjacent `.touch-target` contact links.** AC-2.7's e-mail and phone were
inline in a paragraph separated by « · », each carrying `.touch-target` — which overlays a 44 px hit area **without
repainting**, so their overlays overlapped and the later sibling, painted last, would steal taps aimed at the first.
That is § 2's named wrong-action bug, and no mechanical check can see it. They are their own `flex flex-wrap` row now
with real `coarse:min-h-11` boxes and a `gap-x-4`, so the hit areas cannot overhang each other.

⚠️ **`WhatsAppConnectionStatus.Error` has no writer anywhere in the product** — `ApplyWhatsAppConnection` and
`ClearWhatsAppConnection` are the only mutators and neither sets it. So `MessagingSenderState.Suspended` is
unreachable in production until Part 4's Meta classification writes it. Derived and tested now; noted so nobody reads
the state as live.

### Still owed for Part 2

- [ ] The **responsive eye pass** at 320/390/820/1180/1440 px + a landscape phone + a keyboard walk on `/rappels`
- [ ] `reconcile-money` **before** a migration batch, so the prescribed pair is whole (a Part 5 concern now)

---

## Story 1 — Part 3

**Session scope, chosen by the user:** Part **3** alone. Parts 4–5 are later sessions.

**Branch:** `feature/windows-desktop-app`, continued.

### Working tree note (start of session)

Clean apart from two untracked items excluded from every commit, as unrelated work by another author:
`features/hosted-security-hardening/`, `features/landing-website/agent-prompt.md`. Nothing was staged with `git add -A`.

⚠️ **The dev database is still ahead of this branch** (`20260812103207_AddUserSecondFactorAndSessionFamilies`), the same
note Part 2 carries. It affected nothing here.

---

## Part 3 — The vendor allocates, corrects, and sees the portfolio ✅

| Step | Outcome |
|---|---|
| 24 · The vendor pair | ✅ `GrantMessagingAllowanceCommand` · `CancelMessagingAllowanceCommand` (no controller names either — AC-9.3) + the two console wrappers `RecordMessagingAllowanceFromConsoleCommand` / `CancelMessagingAllowanceFromConsoleCommand`, each staging the `PlatformAccessEntry` **before** `MessagingAllowanceRefold`'s single save |
| 25 · AC-6.4a/6.5/6.6 | ✅ **`MessagingAllowancePlan.Decide`** — extracted rather than written twice, see DEV-8 |
| 26 · The controller | ✅ `PlatformMessagingController`, its own file, two routes, both `[AllowsWithoutSubscription]`, 404/409 by **code** |
| 27 · The reads | ✅ the detail's `messaging` object (per-entry `IfCancelled` re-folded server-side) + the portfolio's two figures, one filter and `PlatformMessagingFilter`, over the **stored** counting row LEFT-joined on `(ClinicId, MonthKey)` |
| 28 · `PlatformReadShape` | ✅ 27 new leaves, and **only** those the DTOs return; ten names *reused* from the subscription block rather than re-declared under `Messaging*` (they mean the same thing — a vendor payment's own fields) |
| 29 · Console UI | ✅ `messaging-section.tsx` · `record-allowance-sheet.tsx` · `cancel-allowance-dialog.tsx` · two portfolio columns + two card fields + two filter chips · `bff/forfaits` + `bff/forfaits/annulations` |
| 30 · The three verbs | ✅ `messaging-grant` / `messaging-cancel` / `messaging-report` (+ `MessagingVerbs`, `MessagingReportService`), exit 0/1/**2**, AC-9.4's four finding kinds apart, `--month` answering for a **closed** month |

⚠️ **The access ledger needed a column, not a reuse — and that decision is the one to know about here.**
`PlatformAccessEntry` already carries `SubscriptionPeriodId`, and pointing it at a `MessagingAllowanceEntry` would have
been one line and no migration. It is refused for two reasons that only show up later: the journal would then assert
that a *forfait de rappels* extended the cabinet's right to record work, and AC-6.7's replay — which reads the row found
by its idempotency key and answers with the entry it names — would hand the console back the wrong kind of id. Hence
**`AddMessagingAllowanceAccessLedgerLink`** (DEV-9), one nullable `uuid`, no index and no FK.

⚠️ **The « presque épuisé » threshold is one figure end to end, and the console never types it.**
`PlatformPortfolioFilter.MessagingNearExhaustedPercent` (90) is read by the SQL predicate *and* served on every
portfolio page as `messagingNearThresholdPercent`; the chip's label is `100 − that`. Two spellings of a threshold is how
a filter and its own label come to disagree with neither screen looking wrong on its own.
⚠️ The predicate is **integer** arithmetic (`consumed × 100 ≥ allowance × 90`), not `≥ 0.90 × allowance`: the boundary is
read as exact, and a floating-point comparison would put « 450 sur 500 » in the list on some rows and not others.

⚠️ **`Messaging` joined `RealtimeResourceResolver.ExcludedAreas`** — `Subscriptions`' case verbatim, and the contract
test is what surfaced it. Both commands are reachable only from a `messaging-*` verb (a separate process with no
caller's token and no notifier in its container) or from the console (an account belonging to no clinic), so the
audience the behavior derives is **nobody, silently**, on both doors. The practice learns its new figure by the
ordinary re-read `/rappels` already does.

### Deviations

#### DEV-8: `MessagingAllowancePlan` is extracted, not a private method on the grant handler
**Date:** 2026-08-12 · **Story:** 1 (Part 3, step 25) · **Category:** Technical

**Original plan:** Step 25 describes AC-6.4a/6.5/6.6 as behaviour of `GrantMessagingAllowanceCommand`.

**Actual implementation:** A pure `MessagingAllowancePlan.Decide(…)` over the ledger, called by the vendor command
**and** by `RecordMessagingAllowanceFromConsoleCommand`.

**Justification:** There are **two doors**, because the console wrapper cannot send the vendor command — that command
commits on its own and AC-6.8's journal row would be a second transaction (the shape
`RecordSubscriptionPeriodCommand` settled). So « is this a raise or a lowering, and which month does it start in? »
would have had two implementations, and one of them would be wrong only for a *lowering* — i.e. rarely, invisibly, and
in the vendor's favour. That is the `fixes-dont-propagate` shape caught before it existed. Being pure also makes both
refusals and the standing-vs-top-up rule assertable without a repository, which is where every one of them is decided.

**Impact:** None on the wire. `MessagingVendorCommandTests` exercises the rule through the command; the plan's own
refusal constants live on the shared type.

**Approved:** Implemented and flagged.

#### DEV-9: Part 3 carries a migration the plan did not schedule
**Date:** 2026-08-12 · **Story:** 1 (Part 3, step 24) · **Category:** Scope

**Original plan:** Part 3 adds no schema change; Migration 1 was to bundle everything.

**Actual implementation:** `20260812150500_AddMessagingAllowanceAccessLedgerLink` — one nullable
`PlatformAccessEntries.MessagingAllowanceEntryId`.

**Justification:** AC-6.7's replay has to answer with the **first** submission's entry, and the only row carrying the
idempotency key is the access-ledger row — so it must be able to name the allocation it created. See the ⚠️ above for
why reusing `SubscriptionPeriodId` was refused. It continues DEV-5's split-by-part, which the plan's own « before and
after the migration **batch** » wording already allows for.

**Impact:** Part 5's before/after diff covers it. `verify-schema` needs no change: the column carries no index, FK or
decimal, which is precisely the set that verb diffs — so the diff below is *expected* to show nothing.

**Approved:** Implemented and flagged.

#### DEV-10: `MessagingReportService.Classify` is public, not internal
**Date:** 2026-08-12 · **Story:** 1 (Part 3, step 30) · **Category:** Technical

**Original plan:** Implicit — `SubscriptionReportService.IsFinding` is `internal`.

**Actual implementation:** `public`, with the reason on the member.

**Justification:** AC-9.4 *is* the classification and its ordering (« aucun forfait » asked before « épuisé », because a
cabinet with no forfait cannot meaningfully be out of one). `internal` puts that behind a fixture that has to
manufacture each state, which is how the ordering ends up untested; the subscription sibling could only offer its own
assembly and consequently has no direct test of its order.

**Approved:** Implemented and flagged.

### Corrections found by running the code

⚠️ **`messaging-report --month 2026-07` printed « forfait non mesuré » where the truth was « aucun forfait ».** One
helper (`MessagingVerbs.Count`) was formatting both a null *forfait* and a null *consumption*, and those are the two
facts this whole feature is built to keep apart: no allocation reaches the month (record one) versus no counting row
exists for it (the daily pass has not run). Split into `Count` / **`Allowance`**. Found by running the verb against a
closed month, where every cabinet legitimately has no allocation — the one case the unit suite's fixtures do not
produce, because they seed the month they assert on.

⚠️ **Two derived guards fired on my own prose**, both correctly, and both are recorded because the next person will hit
them: `MessagingVendorCommandReachabilityTests` matched the vendor command names in `PlatformMessagingController`'s
**docstring** (a comment is enough to fail it, as its own note says), and the near-threshold check matched `0.90` in the
SQL predicate's docstring explaining why it is *not* `0.90`. The controller now describes the commands rather than
naming them, and the scan strips comments first — the `CnamClosedSetContractTests` lesson.

⚠️ **One test of mine asserted the wrong thing and the code was right.** A standing forfait of **zero** is a *lowering*
like any other, so it defers to next month; I had asserted it applied immediately. Kept and renamed, because the
reading is not obvious — « zéro » sounds like « stop now », and getting it the other way round would silence a practice
on the afternoon the vendor typed it. Turning a cabinet off immediately is a **cancellation**, not a zero.

### Part 3 gate

| Check | Result |
|---|---|
| `dotnet build` (`--no-incremental`, `BaseOutputPath` outside the repo) | ✅ **0 errors**, 55 warnings — the identical pre-existing baseline, **0 in changed files** (grepped by filename) |
| Unit suite (`-c Release`, outside the repo) | ✅ **2988 passed / 0 failed** (2939 before Part 3; **49 new**) |
| `MessagingVendorCommandTests` | ✅ 18 — a raise rewrites the snapshot; **a lowering defers and leaves this month alone**; a raise is not read as a lowering because a top-up inflated the month; a past top-up refused **under its own code** and a future one accepted; a month on a standing figure refused; an amount of **0 refused** and « offert » carrying null; two different allocations both kept (EC-5); a cancellation reaching the **current** month with consumption untouched and remaining floored; a standing cancellation restoring the **earlier** standing figure; AC-7.5 keeping the first motif; another cabinet's allocation unreachable |
| `PlatformMessagingWriteTests` | ✅ 12 — the journal row **staged before the single save** and naming the messaging entry (not `SubscriptionPeriodId`); a double-click producing **one** entry and replaying the first outcome; a lost unique-index race replaying rather than failing; AC-7.5 journalling **nothing**; an undeclared scope **throwing** (EC-12); an unknown method refused; « non mesuré » kept distinct from zero; the section **absent** where the capability is off (EC-16) |
| **`The_Preview_On_The_Fiche_Equals_What_Cancelling_Actually_Does`** | ✅ the load-bearing case: the real read and the real write over **one** ledger, and the standing-entry case (200 → 900 → cancel → **200**, not 0) that the plausible « current minus this entry » gets wrong |
| `MessagingReportServiceTests` | ✅ 12 — the four buckets and the exit code; « aucun forfait » outranking « épuisé »; a quiet month reading **0** not « non mesuré »; the single-cabinet verdict coming from the **same** classification; a cancelled allocation listed and marked; **a closed month answering differently from the current one over one ledger** |
| `MessagingVendorCommandReachabilityTests` | ✅ 5 — no controller names either command (2 found by reflection, asserted non-empty); all 3 verbs dispatched by `Program.cs`, with an executed red-proof; distinct `messaging-` words; the near threshold a single named constant with no literal in the predicate |
| `SubscriptionExemptionCoverageTests` | ✅ the two new writes added to the **reviewed** list — the guard fired first, in both directions |
| `RealtimeResourceResolverTests` | ✅ green after `Messaging` joined `ExcludedAreas` — the guard fired first |
| `PlatformReadShapeTests` | ✅ green **in both directions**: nothing in the new DTOs names a patient, an act or a per-patient amount, and no allowance is unused |
| `verify-schema` **before** | exit **0** — « schema matches the model » |
| `verify-schema` **after** | exit **0** — « schema matches the model » |
| The **diff** | ✅ **only the generated timestamp.** Expected and correct, exactly as in Part 2: this migration adds one **nullable** column with no index, FK or decimal — precisely the set `verify-schema` diffs. Not a null result: the gate confirming the migration moved nothing it can see |
| Migration applied for real | ✅ `MessagingAllowanceEntryId uuid NULL`, **no `column_default`**, read back out of `information_schema` |
| `reconcile-money` | ✅ exit **0**, « no drift detected » — after the four live writes below, which is the stronger reading: the vendor's money reached no clinic money read (FR-2) |
| `web` untouched | ✅ no file under `web/` changed in Part 3 (the clinic surface is Part 2's) |
| `console`: `npx tsc --noEmit` | ✅ clean |
| `console`: `node scripts/check-responsive.mjs` | ✅ **14/14** |
| `console`: `npm run build` | ✅ compiled; `/cabinets` 3.99 kB, `/cabinets/[clinicId]` 6.12 kB, both new BFF routes listed |

### Verified over the wire, against the real database

Not a substitute for the unit suite — these are the cases only a live run can reach.

| Walk | Outcome |
|---|---|
| `messaging-report` (current month) | ✅ exit **0**, 5 cabinets, « No findings » |
| `messaging-report --month 2026-07` | ✅ exit **2**, all 5 in « No allowance record » — correct: the rollout backfill's entries are effective `2026-08`, so nothing reaches July. **This is the run that found the « non mesuré » wording defect above.** |
| `messaging-grant --top-up 300 --month 2026-08 --amount 45.000 --method Transfer` | ✅ exit 0, 200 → **500**, allocation id printed |
| `messaging-grant --per-month 50` (a **lowering**) | ✅ exit 0, effective **2026-09**, this month **unchanged at 500**, and the deferral stated out loud (AC-6.4) |
| `messaging-grant --top-up 100 --month 2026-07` | ✅ exit 1, refused naming « juillet 2026 » and the earliest legal month (AC-6.5) |
| `messaging-cancel` the top-up | ✅ exit 0, 500 → **200 in the current month** (AC-7.4), « Already sent: 0 (untouched) » |
| `messaging-cancel` it again | ✅ exit 1, « Cette allocation est déjà annulée » (AC-7.5) |
| `messaging-report --clinic <id>` | ✅ the three allocations oldest-first with their ids, both cancelled ones marked **[ANNULÉE]** with their motifs — the ids `messaging-cancel` takes, printed nowhere else |

⚠️ **The dev database was left at 200/month**: the test lowering was cancelled rather than deleted, so the cabinet's
September figure is back where it was and the two test allocations remain struck through with their motifs. That is the
append-only rule working as designed, not leftover mess — but the two rows are visible on that cabinet's fiche.

### Still owed for Part 3

- [ ] **AC-6.9 confirmed by trying it**: sign in to the console, run `platform-account --deactivate`, call both new
      writes with the same token. The plan asks for this explicitly rather than by inheritance, because
      `PlatformAccountStateMiddleware` was **inert in production for the whole life of `platform-console`** while every
      layer reported it present. Not done this session — it needs a running console listener and a tunnel.
- [ ] The **responsive eye pass** on `console/` at 320/390/820/1180/1440 px + a landscape phone + a keyboard walk over
      the two new sheets. No browser automation on this machine (`agent-browser` is not installed); the mechanical gate
      (14/14 + `tsc` + `build`) passed and the new components reuse `record-payment-sheet`/`cancel-period-dialog`'s
      verified `dvh` + pinned-footer arrangement verbatim, but the eye pass remains owed.
- [ ] `CLAUDE.md` and the four sub-guides — **Part 5's step 42**, deliberately not done here (Parts 0–2 did the same).

---

## Verification owed before Story 0 closes

- [ ] `dotnet build` clean + the two Meta test classes green (`WhatsAppOnboardingServiceTests`,
      `ReminderChannelSenderTests`) — neither asserts the browser constant, so neither should be affected
- [ ] `npx tsc --noEmit` + `npm run build` clean in `web/`
- [ ] `grep -rn "v21.0" web/components api/ClinicManagement.Infrastructure` shows the two **defaults** only, each
      now fed by `META_GRAPH_API_VERSION`, with no third pin
- [ ] The WhatsApp connect flow still loads in the browser (this story must not break the existing path)

---

## Story 1 — Part 4

**Session scope, chosen by the user:** Part **4** alone. Part 5 is a later session.

**Branch:** `feature/windows-desktop-app`, continued.

### Working tree note (start of session)

Clean apart from two untracked items excluded from every commit, as unrelated work by another author:
`features/hosted-security-hardening/`, `features/landing-website/agent-prompt.md`.

⚠️ **More of another author's work arrived mid-session** and is excluded too: a `.gitignore` addition,
`.playwright-mcp/`, `agenda-1440-week.png`, `features/agenda-grid-gestures/`, `web/components/agenda-grid-drag.ts`
and `web/scripts/shots.mjs`. Every path was staged explicitly; nothing was staged with `git add -A`.

⚠️ **The dev database is still ahead of this branch** (`20260812103207_AddUserSecondFactorAndSessionFamilies`), the
same note Parts 2 and 3 carry. It affected one thing: `dotnet ef database update --no-build` reported « already up to
date » against a **stale in-repo `bin/`** while `migrations list` showed the migration Pending. Dropping `--no-build`
applied it.

### `stories/context.md` was written this session

Parts 0–3 had none, so each session re-derived the same facts (the gate commands and their quirks, which file to
imitate, where each authority lives). It is pointers only — Step 6's rule — and carries the staleness diff Part 5
should run first.

---

## Part 4 — The cabinet connects, the template is submitted, Meta's refusals are told apart ✅

| Step | Outcome |
|---|---|
| 31 · Embedded Signup v3 → v4 | ✅ and **extracted rather than edited in place**: `web/lib/hooks/use-whatsapp-embedded-signup.ts` — `sessionInfoVersion` gone, `business_id` captured, **all five finish types** accepted with `FINISH_ONLY_WABA` given its own outcome, and the stricter-than-Meta's-sample origin allow-list kept verbatim |
| 32 · The template service | ✅ `IWhatsAppTemplateService` + `WhatsAppTemplateService` (submit + read back), plus **`WhatsAppReminderTemplate`** as the one definition of the name, language, category and body |
| 33 · The four columns + submission | ✅ `ClinicReminderSettings.SetWhatsAppTemplateState` / `ApplySubmittedReminderTemplate`, `20260812161339_AddWhatsAppTemplateState`, and `ConnectClinicWhatsAppCommand` submitting post-exchange (AC-1.3) |
| 33a · The gate's template term | ✅ term **1** of `OutboxMessagingGate`'s ordered terms, so both § 13 call sites inherit it — dispatch **and** un-park (AC-4.8) |
| 34 · The webhook | ✅ `MetaWebhookController` — `X-Hub-Signature-256` over the **raw** body, `UseSystemWide` as its first act, `GetByWhatsAppBusinessAccountIdAsync`, both actions added to `ControllerAuthorizationCoverageTests` |
| 35 · The reconciling poll | ✅ `MessagingAllowanceJob`'s third duty over `GetAwaitingTemplateReviewAsync`'s candidates |
| 36 · FR-7b's category | ✅ stored, stated in words on the console file, and a `messaging-report` finding — Part 3's four `null` placeholders filled, **no console source change needed** |
| 37 · Meta's refusals | ✅ `Throttled` + `Blocked` outcomes, `WhatsAppSender.Classify` over the **full** body, and `DispatchAsync`'s `switch` given the `default` it never had |
| 38 · The client | ✅ `whatsapp-connect-card.tsx` (AC-1.2 before the flow · AC-1.4's five states in words · AC-1.5's 24 h) + AC-1.7's closure of the manual fields, refused server-side |

⚠️ **The plan's own template body could not be used, and the reason is the sender.** § 32 shows
« Rappel de votre rendez-vous chez {{1}} le {{2}}. » — **two** variables — while `WhatsAppSender` sends **one** body
parameter carrying the whole pre-rendered French sentence. Two variables is « #132000 number of params does not
match » on every send; formatting inside the sender instead would move the wording away from `ReminderScheduler`,
which is where `ReminderMessage.AnnouncesStaleMoment` reads it from to catch a moved appointment (L3b). The user chose
**« Bonjour, {{1}} À bientôt, votre cabinet dentaire. »** — one variable, neither first nor last, no redundancy
against the rendered sentence, and the sender and the backstop both untouched. See DEV-11.

⚠️ **Two of this part's decisions exist to keep EC-16 true, and both would have failed silently.**
**(a)** The template submission is gated on `SellsVendorMessaging`: a stored template state is what makes the gate
hold a cabinet's reminders, and on the other two deployment kinds *neither writer that could clear it exists* — the
webhook 404s and the daily pass is not registered. Submitting there would have held a working cabinet's reminders for
ever. **(b)** The gate passes a cabinet with **no** stored status. Reading null as `NotSubmitted` would have held
every WhatsApp reminder on the deployment the day § 33a shipped, for a template Meta approved long ago — which is also
why the migration's four columns carry **no default** (a scaffolded `defaultValue: 0` *is* `NotSubmitted`).

⚠️ **AC-1.7 needed a third fix nobody planned: an ordinary save was going to erase the connection.**
`ApplyNonSecretSettings` replaces every field it is given, so a screen that no longer renders the WhatsApp identity
posts nulls — and the next save of an unrelated SMS setting would wipe the phone-number id « Connecter WhatsApp »
wrote, silently un-configuring the channel (`ClaimsItsOwnWhatsApp` reads exactly those columns). The handler now
carries the four fields over **from what is stored**, and the browser sends `null` for them rather than
round-tripping them into the new refusal. Both halves have a case.

⚠️ **`ToDto` gained a *required* parameter rather than a defaulted one.** Four handlers produce
`ReminderSettingsDto` — the read, the settings save, the connect and the disconnect — and a screen that hid the manual
fields on load and got them back after connecting would be worse than never hiding them. A default would let one
caller forget in silence; a parameter made the compiler list them. **The new mapper test then caught that the
parameter was unused** — signature changed, object initializer not — which is exactly the case a required parameter
cannot catch by itself.

⚠️ **`WhatsAppTemplateStatuses.AwaitingMeta` is an array, not a predicate**, because the poll's candidate query is
SQL: a `switch` does not translate, so the alternative was the same set written a second time as a `WHERE` clause.
`IsTerminal` is derived from it, so the two answers cannot disagree. **`Paused` is deliberately in it** — Meta
un-pauses a recovered template with no guaranteed webhook, and a cabinet parked there for ever with its reminders held
is the stranding the poll exists to end.

### Deviations

#### DEV-11: the reminder template carries ONE body variable, not the plan's two
**Date:** 2026-08-12 · **Story:** 1 (Part 4, step 32) · **Category:** Technical

**Original plan:** § 32's body is « Rappel de votre rendez-vous chez {{1}} le {{2}}. ».

**Actual implementation:** « Bonjour, {{1}} À bientôt, votre cabinet dentaire. » — one variable, neither first nor
last.

**Justification:** `WhatsAppSender` sends exactly one body parameter carrying the whole rendered sentence, so a
two-variable template is refused by Meta on every send (#132000). Making the sender format instead would move the
wording out of `ReminderScheduler` — where `ReminderMessage.AnnouncesStaleMoment` reads it to catch an appointment
moved under a queued row (L3b) — and would also change the SMS channel's shared wording. The plan's snippet
illustrates the *rule* it states (never start or end with a variable), which the chosen body obeys. Surfaced to the
user before any code was written; they picked this option over the two alternatives.

**Impact:** None on the sender, the scheduler or the stale-moment backstop, all untouched. The patient reads one
sentence with no redundancy.

**Approved:** Yes — chosen by the user from three options.

#### DEV-12: Part 4 carries a migration, and one more DTO field than the plan lists
**Date:** 2026-08-12 · **Story:** 1 (Part 4, steps 33 / 38) · **Category:** Scope

**Original plan:** Migration 1 bundles the four template columns with Part 1's tables; the plan's DTO tables name no
`canConnect` or `whatsAppVendorManaged`.

**Actual implementation:** `20260812161339_AddWhatsAppTemplateState` (four nullable columns + one partial index), plus
`ReminderAllowanceDto.CanConnect` and `ReminderSettingsDto.WhatsAppVendorManaged`.

**Justification:** The migration continues DEV-5/DEV-9's split-by-part, which the plan's own « before and after the
migration **batch** » wording allows. The two fields are what make AC-1.1 and AC-1.7 renderable without a second
capability probe: `canConnect` is `CanOnboardCabinets` (so the button is **absent** rather than dead where the Meta
credentials are missing — a separate question from whether the deployment sells messaging), and
`whatsAppVendorManaged` travels on the very DTO whose handler refuses the fields, so the form that hides a field and
the save that rejects it read one answer rather than two that can disagree.

**Impact:** `verify-schema` went exit 2 → **0** with the diff showing only the intended index. Part 5's before/after
diff covers the migration.

**Approved:** Implemented and flagged.

#### DEV-13: the Embedded-Signup flow was extracted to a hook, not edited in place
**Date:** 2026-08-12 · **Story:** 1 (Part 4, steps 31/38) · **Category:** Technical

**Original plan:** § 31 is « four edits in `web/components/reminder-settings.tsx` ».

**Actual implementation:** `web/lib/hooks/use-whatsapp-embedded-signup.ts`, called by that card **and** by the new
connect card.

**Justification:** § 38 adds a second surface that runs the same flow. Two copies of a five-outcome popup protocol is
how one of them keeps handling only `FINISH` for ever — which is the defect § 31 exists to fix, so shipping the fix
into a shape that invites it back would be self-defeating. The four edits are all in the hook, once.

**Impact:** `reminder-settings.tsx` lost its local Meta constants, its SDK effect and its `signupDataRef`; behaviour
where the vendor does not manage WhatsApp is unchanged.

**Approved:** Implemented and flagged.

### Corrections found by running the code

⚠️ **Twelve tests went red on a `null` a mock returned, not on anything the feature did.**
`Mock.Of<IClinicReminderSettingsRepository>()` answers **null** for `GetAwaitingTemplateReviewAsync`, which § 35's
pass then enumerates — so every case in `MessagingAllowanceWarningTests` failed on an NRE thrown a layer away from
anything it asserts. The documented `UnitTests/CLAUDE.md` gotcha, hit exactly as written; the fixture stubs the read
explicitly now and says why.

⚠️ **The new mapper test failed on its first run and the production code was wrong** — `ToDto` had gained the
parameter and never used it, so `whatsAppVendorManaged` was always `false` and the manual fields would have stayed on
screen on the one deployment that must not offer them. A required parameter proves every caller *passes* something;
it cannot prove the callee reads it.

⚠️ **Two raw string literals needed `$$$`, not `$$`.** A JSON fixture ending `…"code":1234}}}` closes with three
braces, and C# reads `}}` as an escape — the compiler's message (« does not start with enough `$` characters ») names
the cause but points at the line's end.

### Part 4 gate

| Check | Result |
|---|---|
| `dotnet build` (`--no-incremental`, `BaseOutputPath` outside the repo) | ✅ **0 errors**, 55 warnings — the identical pre-existing baseline, **0 in changed files** (the warning file list was diffed against the changed set) |
| Unit suite (`-c Release`, outside the repo) | ✅ **3049 passed / 0 failed** (2988 before Part 4; **61 new**) |
| `WhatsAppSenderErrorClassificationTests` | ✅ 12 — the six codes across both outcomes, each against a **full-length** envelope with `code` after a long `message`; an unknown code and an unparseable body staying transient; **the fixture's own premise asserted** (>200 chars, `code` past the cut) so shortening it later cannot make the class vacuous; and no `fbtrace_id`, type or Meta prose reaching the result (D-8) |
| `MetaWebhookTests` | ✅ 15 — a valid `message_template_status_update` **actually writing**, asserted with the **real** `TenantScope` so a missing `UseSystemWide` fails rather than passing on a hand-set scope; a forged signature and an unconfigured secret both refusing and writing nothing; the `hub.challenge` handshake and its two refusals; 404 where the capability is off; and the payload reader on its own — a **numeric** `message_template_id`, another template's status ignored, five malformed shapes yielding nothing, an unknown status word falling on the holding side |
| `OutboxMessagingGateTests` | ✅ 23 (7 new) — all five non-approved statuses holding **with the month never read**; an approved one passing; **a cabinet with no stored status passing** (the EC-16 case that would have stopped the deployment); the template named **before** an exhausted forfait; « waiting » told apart from « refused » in the sentence; an SMS row still never looked up |
| `NotificationJobMessagingTests` | ✅ 23 (6 new) — a template under review held **pre-send** (sender never called, nothing counted); **a template-parked row not released by a top-up** (AC-4.8) and released by an approval; a throttle leaving the row `Pending` with its retry budget intact; a stopped number parked; and an **unnamed outcome** parking instead of staying `Pending` for ever |
| `WhatsAppTemplateLifecycleTests` | ✅ 15 — the submission recording status/category/id **and the template's own name**; a failed submission still leaving the cabinet connected; **no submission at all where the deployment does not sell vendor messaging**; an unreadable re-submission not downgrading an approved template; a status confirmation preserving the stored category; disconnect clearing all four; the poll recording an approval the webhook never delivered and a category Meta changed; a null read and an undecryptable token changing nothing; `AwaitingMeta` and `IsTerminal` proven to be one answer; AC-1.7's refusal on each of the three credentials; and an ordinary save **not erasing** a vendor-provisioned connection |
| `ControllerAuthorizationCoverageTests` | ✅ green in **both** directions with the two webhook actions added to the reviewed anonymous list |
| `SystemWideCallerCoverageTests` | ✅ green, **and extended**: its criterion is « no HTTP context » and a webhook has one, so a second reviewed set (`ScopedDespiteHavingAnHttpContext`) names this controller with its reason, plus a sibling assertion that the file still exists so a rename cannot leave it checking nothing |
| **`ReminderSettingsChannelIsolationTests`** | ✅ green and **byte-for-byte unchanged** (R-8) — confirmed with `git diff --stat`, which is empty for that file |
| `verify-schema` **before** | exit **2** — one DRIFT: `ClinicReminderSettings(WhatsAppBusinessAccountId)` MISSING, i.e. exactly the new index |
| `verify-schema` **after** | exit **0** — « schema matches the model » |
| The **diff** | ✅ the one intended index resolved and **nothing else** — the first part of this feature whose diff is non-empty, and it names only what the migration adds |
| Migration applied for real | ✅ all four columns `YES` nullable with **no `column_default`** (read back out of `information_schema`), and the index confirmed **partial** (`WHERE "WhatsAppBusinessAccountId" IS NOT NULL`) in `pg_indexes` |
| `reconcile-money` | ✅ exit **0**, « no drift detected » (FR-2 — nothing here touches clinic money) |
| `messaging-report` | ✅ exit **0**, 5 cabinets, « No findings » — with FR-7b's fourth bucket now fed by a real category rather than a `null` |
| `web`: `npx tsc --noEmit` | ✅ clean |
| `web`: `npm run check:responsive` | ✅ **15/15** |
| `web`: `npm run build` | ✅ compiled; `/rappels` builds with the new card |
| `console`: `npx tsc --noEmit` | ✅ clean |
| `console`: `node scripts/check-responsive.mjs` | ✅ **14/14** |
| `deploy/docker-compose.hosted.yml` | ✅ parses under `yaml.safe_load` after gaining `Meta__AppId` / `Meta__AppSecret` / `Meta__WebhookVerifyToken` and the two public browser build args |

### Still owed for Part 4

- [ ] **The responsive eye pass** at 320/390/820/1180/1440 px + a landscape phone + a keyboard walk over the new
      connect card. No browser automation on this machine, so the mechanical gate (15/15 + `tsc` + `build`) plus a
      diff re-read against `DEVICE-CONTRACT.md` § 1 is what was done: the card is a single-column `Card` with a
      wrapping header row, its one button carries `coarse:h-11`, and every state is a paragraph rather than a table.
      ⚠️ Another author added `web/scripts/shots.mjs` mid-session, which may make this cheap for Part 5.
- [ ] **A real Meta walk**: the template has never been submitted to Meta, the webhook has never been called by Meta,
      and no `Meta:AppId`/`AppSecret` exists on any deployment (Story 0's 🔴). Everything here is verified against the
      documented payload shapes and the unit suite. § 34's field name and the five finish types are Story 0's reading.
- [ ] **The v4 flow in a browser** — § 31's four edits are unverified against a live popup for the same reason.
- [x] `CLAUDE.md` and the sub-guides — **Part 5's step 42**, done there.

---

## Story 1 — Part 5

**Session scope, chosen by the user:** Part **5**, the closing part.

**Branch:** `feature/windows-desktop-app`, continued.

### Working tree note (start of session)

`stories/context.md`'s staleness diff (`db2b371..HEAD` over the paths it names) came back **empty**, so every pointer
in it held and Step 6's exploration was skipped entirely — the first session of this feature to get that.

Another author's in-flight agenda work was present throughout and excluded from every commit: `.gitignore`,
`web/app/appointments/page.tsx`, `web/components/appointment-calendar.tsx`,
`web/components/create-appointment-dialog.tsx`, `web/package.json`, `web/package-lock.json`,
`web/components/agenda-grid-drag.ts`, `web/scripts/shots.mjs`, `.playwright-mcp/`, twelve `*.png` screenshots,
`features/agenda-grid-gestures/`, `features/hosted-security-hardening/`, `features/landing-website/agent-prompt.md`.
Every path was staged explicitly; nothing was staged with `git add -A`.

---

## Part 5 — Verification, guards and documentation ✅

| Step | Outcome |
|---|---|
| 39 · The three `verify-schema` checks | ✅ `monthly-allowance-matches-ledger` (rows, through the **real** fold, both directions) · `messaging-month-covers-every-clinic` (derived over every cabinet, capability-gated) · `messaging-allowance-entry-has-one-form` + the `MessagingAllowanceFacts` projection in `SchemaVerificationReader` |
| 40 · Before/after over the **batch**, diffed | ✅ rehearsed on a throwaway database at the pre-feature migration: **6 DRIFT → 0**, and `reconcile-money`'s diff is **only the timestamp** |
| 41 · EC-16 by reprofiling | ✅ `verify-schema` walked under all three profiles over one database, plus two new guards (below) |
| 42 · Documentation | ✅ root `CLAUDE.md` + the four `api/*` sub-guides + `web/CLAUDE.md` + `UnitTests/CLAUDE.md` + the **operator runbook** in `deploy/README.md` |
| 43 · Follow-ups | ✅ `follow-up/vendor-messaging-open-questions.md` (6 items, each with its remedy chosen) + the index row |

### The defect § 39 found in itself, before it shipped

⚠️ **`Fold(...) ?? 0` would have reported the vendor's own documented behaviour as drift.** The first draft of
`monthly-allowance-matches-ledger` collapsed a null fold to zero. But **both** writers —
`MessagingAllowanceRefold` and `MessagingAllowanceJob.ProvisionMonthAsync` — deliberately leave a month's snapshot
standing when the fold is null, because null means « no allocation reaches this month » and is not `0` (FR-4). So
cancelling every allocation feeding the current month — **AC-7.4's own case**, where consumption is supposed to keep
reading against the old figure — would have shown as « stores MORE than its ledger » on every cabinet it happened
to. Found by reading the two writers rather than by a failing test; such rows are now excluded from the comparison
and **stated** in the finding (« N more reach no allocation and are not compared »), never silently dropped.

### Two new guards, and why each is a *source* scan rather than a behavioural test

⚠️ **`MessagingCapabilityRegistrationTests`.** Every other EC-16 assertion in this feature hands a component a
mocked `IVendorMessagingAvailability` answering `false` and watches it do nothing — which proves the component
behaves, and proves nothing about whether the deployment ever *asks*. `MessagingAllowanceJob` is registered by a
composition-root `if` that no mock reaches: moved one block out of it, the daily pass runs on a clinic's own
Windows PC for ever, provisioning forfaits nobody sells. The guard brace-matches the `SellsVendorMessaging` block
in `Program.cs`'s own source and asserts every `AddOrUpdate<…MessagingAllowanceJob>` falls inside it, deriving the
registrations by regex and asserting the set is **non-empty** so « found nothing » cannot read as « nothing was
wrong ». A second case pins that the `else`'s `RemoveIfExists` names the **same** job id — a mistyped id there
leaves the old registration running on a reprofiled install, the one failure the defensive branch exists to
prevent and the one it cannot report.

⚠️ **The two clinic reads had no test at all.** `GET /api/clinics/reminder-allowance` and `…/history` 404 before
the mediator on `!SellsVendorMessaging`, and nothing asserted it. They are now driven over the **real**
`DeploymentProfile.For(kind)` rather than a mocked capability — so the case also fails if `DeploymentProfile` ever
starts answering `true` for the wrong kind — and the assertion is **`Assert.Empty(mediator.Invocations)`**, not the
status: a 404 raised *after* the handler, its repository and the allowance policy were resolved would satisfy a
status check and miss « byte for byte unchanged ».

Both red-proofs were **executed**: moving the registration out of its `if` reddens the first (and only the first);
reverting the null handling to `?? 0` and dropping the `understated` direction reddens exactly the two cases that
pin them.

### The gap § 42 found: `deploy/` carried no `Messaging__*` at all

⚠️ Writing the runbook surfaced that **`grep -rn "Messaging__" deploy/` returned nothing** — Part 0 registered
`IMessagingAllowancePolicy` reading `Messaging:DefaultMessagesPerMonth` / `ContactEmail` / `ContactPhone`, and the
only deployment that enforces the feature could not configure any of them. Exactly the shape of Story 0's 🔴 about
the Meta keys, and of `clinic-subscription`'s « `deploy/` now carries the ten `Subscription__*` variables ». The
three keys are now in `docker-compose.hosted.yml` and `.env.hosted.example` with their fall-backs stated.
⚠️ It also caught my own runbook naming keys that do not exist (`MESSAGING__DEFAULTMESSAGESPERMONTH`) — corrected
against `MessagingAllowancePolicy`'s own source before shipping.

### What the live run found on the dev database

⚠️ **`messaging-month-covers-every-clinic` reported 1 of 6 cabinets uncovered on its first run against a real
database — and it is right.** « Cabinet Chaîne Test », created 17:16 UTC that afternoon, has an entitlement, a user
and 19 procedure types — the full provisioning signature — and **zero** messaging rows. Not a defect in this
branch: `ClinicCreationMessagingAllowanceTests` (which derives the door set by scanning for `new Clinic(`) is
green, and there are `dotnet` processes running since **09 and 11 August**, i.e. a dev API whose binary predates
Part 1 entirely. `messaging-report` independently lands it in « aucun forfait » rather than « non mesuré » — the
two facts this feature exists to keep apart — and exits **2**.

### Part 5 gate

| Check | Result |
|---|---|
| `dotnet build` (`--no-incremental`, `BaseOutputPath` outside the repo) | ✅ **0 errors**, 55 warnings — the identical pre-existing baseline, **0 in changed files** |
| Unit suite (`-c Release`, outside the repo) | ✅ **3068 passed / 0 failed** (3049 before Part 5; **19 new**) |
| `SchemaVerificationServiceTests` | ✅ 66 (**15 new**) — both directions of the fold comparison; a **cancelled** ledger keeping its snapshot; five malformed allocation shapes; the capability-off « not applicable »; and the pre-tables case |
| `MessagingCapabilityRegistrationTests` | ✅ 5, with an executed red-proof on the registration gate |
| `verify-schema` **before** the batch (throwaway DB, 2 seeded cabinets) | exit **2** — 6 DRIFT, every one a `MISSING` index or FK of this feature's own objects; the three new checks « not applicable » |
| `verify-schema` **after** | exit **2** → the only remaining DRIFT is `clinic-activity-snapshot-covers-every-clinic`, pre-existing on a hand-seeded database and **identical before and after**, so it does not appear in the diff |
| The **diff** | ✅ **only the intended objects**: 3 indexes + 2 FKs `MISSING → present`, `MessagingAllowanceEntries.Amount: (18,3)` arriving from the model-wide convention, and the three new checks moving « not applicable » → green with real figures. Nothing else |
| `reconcile-money` **before and after** — the pair Parts 1–3 owed | ✅ exit **0** both times, and the diff is **only the timestamp**: the batch moved no closed month (FR-2) |
| Reprofile walk (`Deployment__Profile`, one database) | ✅ `SelfHostedLan` and `CloudBrowser` both read « not applicable — this deployment does not sell vendor messaging »; `HostedMultiTenant` reports the real figure. The other two checks still run on all three |
| `messaging-report` against the dev database | ✅ exit **2**, one « aucun forfait » finding (above), 5 cabinets clean |
| `web`: `npx tsc --noEmit` · `check:responsive` · `build` | ✅ clean · **16/16** · compiled |
| `console`: `npx tsc --noEmit` · `check-responsive.mjs` · `build` | ✅ clean · **14/14** · compiled |
| `deploy/docker-compose.hosted.yml` | ✅ parses under `yaml.safe_load`; the three `Messaging__*` keys asserted present with their fall-backs |

### Still owed after Part 5 — all captured, none a code gap

Every item is in [`follow-up/vendor-messaging-open-questions.md`](../../../follow-up/vendor-messaging-open-questions.md)
with its remedy already chosen. The two that were owed by earlier parts:

- [ ] **The responsive eye pass** at 320/390/820/1180/1440 px + a landscape phone, over `/rappels` (Parts 2 and 4)
      and the console's two sheets (Part 3). ⚠️ It needs more than a browser: the three cards mount **only where
      `SellsVendorMessaging` is true**, so the walk needs a `HostedMultiTenant`-profiled instance rather than the
      local dev one. `web/scripts/shots.mjs` (another author's, mid-feature) should make the width sweep cheap once
      that exists.
- [ ] **AC-6.9** confirmed by trying it — needs a running console listener and a tunnel.

⚠️ **Part 5 touched no rendering file** (`web/CLAUDE.md` only), so the eye pass is not owed *by this part's diff*;
it is inherited from Parts 2–4 and stated rather than quietly dropped.
