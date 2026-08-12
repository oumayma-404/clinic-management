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

## Verification owed before Story 0 closes

- [ ] `dotnet build` clean + the two Meta test classes green (`WhatsAppOnboardingServiceTests`,
      `ReminderChannelSenderTests`) — neither asserts the browser constant, so neither should be affected
- [ ] `npx tsc --noEmit` + `npm run build` clean in `web/`
- [ ] `grep -rn "v21.0" web/components api/ClinicManagement.Infrastructure` shows the two **defaults** only, each
      now fed by `META_GRAPH_API_VERSION`, with no third pin
- [ ] The WhatsApp connect flow still loads in the browser (this story must not break the existing path)
