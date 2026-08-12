# Exploration — vendor-purchased WhatsApp messaging quota

**Explored:** 2026-08-11
**For:** `features/vendor-whatsapp-messaging-quota/spec.md`
**Reused by:** `/challenge-spec`, `/plan-feature`

> Two sources: a `/think-solution` pass over the reminder pipeline, the subscription/entitlement machinery and
> the repo's conventions; and a four-agent sweep over the clinic settings UI, the vendor console, the
> refusal/warning contract, and Meta's own current rules. **§ 6 is the one that changes the feature.**

---

## 1. The WhatsApp reminder pipeline as it stands

### Senders
- `Infrastructure/Services/IReminderChannelSender.cs:34-40` — `SendAsync(phoneE164, message, ResolvedReminderSettings, ct)`.
  Outcomes are `Sent` / `TransientFailure` / `NotConfigured` (`:7-25`). **There is no permanent-failure outcome**;
  terminal `Failed` is decided by the job exhausting `RetryCount`.
- `Infrastructure/Services/WhatsAppSender.cs` — Meta Graph API, **template messages only, never free text**.
  Endpoint `{WhatsAppApiUrl}/{WhatsAppPhoneNumberId}/messages` (`:39`); recipient is `phoneE164.TrimStart('+')` (`:45`);
  payload branches on `WhatsAppTemplateHasBodyParam` (`:51-84`); default template language `"fr"` (`:16`).
- `Infrastructure/Services/HttpReminderChannelSender.cs` — 15 s timeout, `Bearer` auth, 2xx ⇒ `Sent`, anything else
  ⇒ `Transient`. **Never throws.** The gateway's response *body* is logged but never returned (`:15-19`) — it would
  reach `reminder-log`, which is `AnyClinicRole`.

### The outbox
- `Domain/Entities/Notification.cs` — `ClinicId` is **nullable** (`:8-13`, legacy/global rows). `BlockedReason`
  (`:26-32`). `MarkAsBlocked(reason, sentence)` ↔ `Unblock()` do **not** touch `RetryCount`.
- `Domain/Enums/NotificationStatus.cs` — `Pending=1, Sent=2, Failed=3, Blocked=4`. `Blocked`'s docstring records the
  starvation defect it fixed: unsendable rows sorted to the front of an oldest-first scan and consumed every tick.
- `Domain/Enums/OutboxBlockReason.cs` — `ChannelUnsupported=1, ChannelDisabled=2, ChannelUnconfigured=3,
  SubscriptionExpired=4`. Mapped `HasConversion<int>()`, column already nullable ⇒ **a 5th member needs no migration.**
- `Infrastructure/Repositories/NotificationRepository.cs:25-80` — `GetDueForDispatchAsync(batchSize, perClinicBound)`:
  clinics served **oldest-due-first**, `Reminders:DispatchBatchSize` = 50, `Reminders:PerClinicDispatchBound` = 20.
  `GetBlockedForReviewAsync` (`:82-126`) is the same shape clause for clause.
- `API/BackgroundJobs/NotificationJob.cs` — minutely, `[DisableConcurrentExecution(600)]`, connectivity-gated,
  `UseSystemWide`, **commits per row** (`SaveAsync`, `:494`, with a comment that delivery is explicitly
  **at-least-once** — no provider idempotency key). Dispatch decision order at `:236-364`; the subscription gate is
  asked at `:317-321`, immediately before `sender.SendAsync`.
- `ReviewBlockedRowsAsync` (`:147-193`) — bounded by the same batch + per-clinic bound; asks
  `OutboxSubscriptionGate` **first, for every parked row**, then the three channel checks; `Unblock()` only, never
  re-enters a sender.
- `PurgeExpiredRowsAsync` (`:216`) — deletes **`Sent`/`Failed` only**, older than `Reminders:RetentionDays` (**90**).
  `Blocked` is out of scope by construction.

### Per-clinic settings and the security boundary
- `Domain/Entities/ClinicReminderSettings.cs` — `Id` **is** the clinic id (1:1 shared PK). WhatsApp fields:
  `WhatsAppPhoneNumberId`, `WhatsAppTemplateName`, `WhatsAppTemplateLanguage`, `WhatsAppApiUrl`,
  `WhatsAppAccessTokenEncrypted`, plus Embedded-Signup metadata (`WhatsAppBusinessAccountId`,
  `WhatsAppConnectionStatus`, `WhatsAppLastError`, `WhatsAppConnectedAt`). Not an `AggregateRoot` ⇒ **no audit row**.
- **`ReminderSettingsProvider.ClaimsItsOwnWhatsApp` (`:177-180`)** is the security boundary:
  ```csharp
  Provided(clinic?.WhatsAppApiUrl)
  || Provided(clinic?.WhatsAppPhoneNumberId)
  || Provided(clinic?.WhatsAppAccessTokenEncrypted)
  ```
  Supply **any** of endpoint / identity / secret and you own **all** of it, inheriting nothing further. This is the
  fix for `SECURITY_REVIEW_2026-08` finding A (a tenant supplying an endpoint and inheriting the install's bearer
  token = remote theft of an install-wide secret). Pinned by `ReminderSettingsChannelIsolationTests`.
  **Consequence: a clinic that types only a phone number id today inherits no token and reads « non configuré ».**
  Only wording inherits — template name/language, and `TemplateHasBodyParam` is config-only.
- `ResolvedReminderSettings.WhatsAppConfigured` is the single sendability authority, read by the sender, the
  enqueuer (`ReminderScheduler.IsSendable`), the dispatcher and the admin `effectiveStatus`.
  It carries **no counter, no quota, no clinic id, no plan.**
- Secrets: `ReminderSecretProtector` (Singleton, Data Protection, purpose `ClinicManagement.ReminderSecrets.v1`).
  A rotated key ⇒ `null` ⇒ « not configured », logged once per scope, never thrown.

### Existing Meta onboarding
- `Infrastructure/Services/WhatsAppOnboardingService.cs` — **Meta Embedded Signup**: `oauth/access_token` code
  exchange → `POST /{wabaId}/subscribed_apps` → `POST /{phoneNumberId}/register` with a CSPRNG 6-digit PIN.
  `MetaConfig` = `Meta:AppId` / `Meta:AppSecret` / `Meta:GraphApiVersion` (default `v21.0`).
  Client half in `reminder-settings.tsx:209-290` (FB JS SDK, `WA_EMBEDDED_SIGNUP` postMessage, `config_id`).
  Gated on `DeploymentProfile.ExposesMetaOnboarding` (both hosted kinds ✓).
- **It stores a WABA-scoped token per clinic. It does not provision a vendor-owned number, and nothing in the flow
  records a pricing category or a conversation id.**

### Counting: there is none
Greps for `quota|credit|usage|allowance|consumption|MessageCount|messagesSent|sendCount|billableMessages` across
`api/ web/ console/ mobile/ desktop/` return **only unrelated hits** (avoirs/notes de crédit, rate-limit windows,
the CNAM ceiling, `StockConsumptionService`, HuggingFace token usage). Concretely absent:
- no column on `Notification` or `ClinicReminderSettings` counting sends;
- no cap/budget/plan field on `ResolvedReminderSettings`, no rate key in `RemindersConfig`;
- **no count check before `sender.SendAsync`** — the only pre-send gates are channel-enabled, channel-configured, subscription;
- `SubscriptionPlan` (`Cabinet`/`Clinique`/`SurMesure`) **gates nothing**; the subscription is time-based, not usage-based;
- the console's `ClinicActivitySnapshot` counters carry no message figure, and `Notification` is deliberately
  **excluded** from the audit ledger the counters derive from.
- The nearest things are `ReminderLogCounts` and `ReminderOutboxDepth` — live queue-health counts, computed on read,
  never persisted, never aggregated over a period.

---

## 2. The entitlement machinery to mirror (`clinic-subscription`)

- `Domain/Entities/ClinicSubscription.cs` — one row per clinic. `RecomputeFrom(wholeLedger, whenUtc)` is the **only**
  writer of `EndsOn` **and** `LatestCoverKind`, both from **one** fold. Takes the instant as a parameter (testability).
  `SubscriptionState` is **derived, never stored** — "a stored copy would have to change at midnight with no write".
- `LatestCoverKind` (`:46-62`) is the pattern for a **clock-free denormalisation**: it exists so « en essai » can be a
  SQL predicate for the console, which filters/sorts the whole portfolio *before a page is cut* (AC-2.4a); the
  obvious column (« what covers **today** ») is unstorable because it is a function of the ledger *and of today*.
- `Domain/Entities/SubscriptionPeriod.cs` — append-only, `AggregateRoot` **so the audit interceptor sees it**.
  `Create(...)` enforces **exactly one duration form or none** (`:105-119`); `ExplicitEndsOn` is floored at
  `RecordedOnClinicDay` and ceilinged at `MaxExplicitEndYears = 5`. `Cancel(reason, by, whenUtc)` — mandatory motif,
  row kept, refuses a double cancel. `ToLedgerEntry()` is the one bridge to the fold.
- `Domain/Services/SubscriptionLedger.cs` — pure, total, **clock-free**, **exclusive cursor**. Both properties are
  load-bearing and documented at length.
- `Application/Features/Subscriptions/SubscriptionRefold.cs` — bounded 5-attempt retry over `ConflictException`
  (`xmin`), correct **only because `EndsOn` is derived**; detaches the ledger too, or a concurrent cancellation is
  invisible through EF's identity map.
- `Application/Features/Clinics/LocalClinicProvisioning.cs:195-209` — `StageEntitlementAsync` stages the entitlement
  **into the same save** as the clinic (FR-4). `ClinicCreationEntitlementTests` derives the door set by scanning for
  `new Clinic(` rather than listing today's two.
- `OutboxSubscriptionGate.cs:68` — one instance per tick, per-clinic `_decided` cache, reads **nothing** where
  `!RequiresSubscription`, and **a clinic with no entitlement row keeps sending** (`:85-90`) — deliberately, because
  that is the vendor's bookkeeping fault and silencing a practice over it is invisible to them.
- Capability split: `DeploymentProfile.RequiresSubscription` (kind-derived, **no config key can flip it**) vs
  `ISubscriptionPolicy.TrialDays` / `ISubscriptionPricing` (operator config). Mirrors `PermitsOsPush` /
  `IOsPushAvailability`. `DeploymentProfile` currently has **17** capabilities.

---

## 3. Refusals, warnings and the client error contract

### `SubscriptionRefusals.cs` — the wording law
Three code+sentence pairs in one place (`subscription_required` / `_suspended` / `_missing`).
**Every sentence says what still works before what does not**, and none mentions signing in or out:
> « Votre abonnement a expiré le {date}. **Vous pouvez toujours consulter et exporter vos données.** Rendez-vous dans « Abonnement » pour le renouveler. »

`MissingCode` is distinct on purpose (EC-6): no entitlement row is *our* fault, so « renouvelez » would be advice the
cabinet cannot act on. `IsDomainRefusal(ex)` admits `ArgumentException`/`InvalidOperationException` but **excludes**
`ArgumentNullException`/`ArgumentOutOfRangeException` — those are programming faults whose English framework text is
not a refusal.

### The threshold-warning pattern (`NotificationGenerator.cs:296-383`)
- Dedupe key is **(clinic, threshold)** via `GetSubscriptionWarningAsync(clinicId, thresholdDays)` — a real
  `int? SubscriptionThresholdDays` column, **not** a message prefix.
- The message is derived from the **threshold + end date**, never the live countdown, so a threshold holding for four
  days restates nothing. The countdown lives in the **title** only.
- `WithdrawStaleSubscriptionWarningsAsync` removes rows for *other* thresholds naming a **different** date — so a pure
  escalation 7→3 keeps both rows, a date change withdraws the others. It returns `bool` so the realtime broadcast
  fires even when this threshold's own row is unchanged.
- Contrast the `EnsureStockExpiringSoonAsync` / `EnsureBackupStaleAsync` shape beside it: one row per item, deduped on
  a **message prefix** (the message carries a live countdown), `Restate` is the normal path, `Clear` removes one row.
- Everything runs through `SafelyAsync` — swallow-and-log at Error, broadcast `"notifications"` only on `true`.

### `SubscriptionWarningJob.cs`
`WarnExpiringSubscriptions()` → `WarnExpiringSubscriptions(ClinicClock.ClinicToday())`. One `today` for the whole pass.
Re-asks the capability itself (a reprofiled install can still hold the Hangfire registration). `RunAs` + `UseSystemWide`.
**Suspended and Expired are both skipped and their rows are NOT cleared** (`:119-123`) — an expired cabinet is now
meeting refused saves and those rows are what explain them.
`SubscriptionStateReader.WarningThresholds = { 7, 3, 1, 0 }`; `ThresholdReached` returns the **largest reached**, so a
pass that slept four days warns at the threshold the cabinet is actually at.
Registration `Program.cs:981-998` — `Cron.Daily(7)` behind `profile.RequiresSubscription`, with `RemoveIfExists` in the else.

### Enums and rules
`NotificationCategory` — 10 members, `SubscriptionExpiring = 10`. `NotificationTargetKind` — 5, `Subscription = 5`
(carries **no id**). `StaffNotification.ForSubscription(...)` is a **factory, not a 12th ctor parameter**.
`StaffNotificationRules.ReachesALockedPhone` is a **total** switch that **throws** on an unclassified category;
`SubscriptionExpiring ⇒ false` ("an accounting reminder is not time-critical to a person").

### Client contract (`web/lib/api/client.ts`)
`ApiError` carries `status` + optional `code`. `throwIfNotOk` (`:332-427`) reads `{ error }` **first** (`title`/`message`
only cover ProblemDetails), ignores a non-string `error`, has an **unconditional** French status fallback, and then:
- **426** → `clientTooOldListeners` → `<ClientVersionGate>` takes the screen;
- **any `code` in `SUBSCRIPTION_CODES`** → `subscriptionRequiredListeners` — **and changes nothing else**: the server's
  French sentence travels on verbatim, the form stays open with everything typed in it, and it never touches the
  one-shot 401 retry;
- **`must_change_password`** → replaces the message (the server sends English there) and routes.
`ApiControllerBase.HandleFailure` emits `{ error, code }` when `Result.Code` is set, `{ error }` otherwise.
`Result.Code`'s own doc: *"Do not add a code unless a caller genuinely branches on it — an unused code is a contract nobody is honouring."*

### The "enqueue refused, tell the user" precedent
`RecallDispatchOutcome` (`Enqueued` / `NoChannelConfigured` / `NoDeliverablePhone` / `Failed`) is the **only** thing
`IReminderScheduler` returns; the three appointment methods return bare `Task`. `SendRecallCommand:72-112` branches
and **leaves the patient untouched** on a non-`Enqueued` outcome, with a French sentence naming both the fix and the
alternative. These refusals carry **no `Code`** because nothing in the browser branches on them.

---

## 4. Clinic-facing UI conventions

- **`reminder-settings.tsx` is no longer mounted by `ClinicSettings`** — `clinic-settings.tsx:1343-1363` is a signpost
  card linking to `/rappels`, where the real card lives inside a right `Sheet` (`app/rappels/page.tsx:294-314`), and
  the scroll belongs to an **inner wrapper** because `ui/sheet.tsx` pins its ✕ against the content element.
- Card skeleton: `mounted` ref + guarded async, one `hydrate(dto)`, `loading`/`error`/data ternary, sub-sections as
  `space-y-3 rounded-lg border p-3`, right-aligned Save whose label morphs. Secrets are **write-only** (sent only when
  typed; placeholder states « •••••••• (inchangée) »); state is cleared after a successful save.
- Badges: `secretBadge(configured)` and `readinessBadge(toggle, status)` — the amber one is `text-warning-ink`, not
  `text-warning` (contrast at 11 px).
- **`md:text-sm` on every `Input`/`Textarea`** — `ui/input.tsx` ships `text-base md:text-sm` as the iOS focus-zoom
  guard and an unprefixed `text-sm` replaces it under tailwind-merge.
- Header idiom is now `CONFIG_CHIP` (`clinic-settings.tsx:65`), **not** the retired `w-1 h-6 bg-primary` bar.
- **Closest precedents for a vendor-controlled state**: `backup-settings.tsx:206-254`'s `managedByHost` early return
  (whole card becomes a statement, no button — and it still offers the one thing the clinic *can* do, so it is not a
  dead end) and `push-availability-card.tsx` (per-platform verdict + the **server's own French reason** rendered
  verbatim). Both take the flag as a **server field**, never inferred from an empty value —
  `lib/api/backup.ts:56-68`: « aucune sauvegarde » and « non géré ici » are the same picture and opposite facts.
- **Closest precedent for a one-field admin form**: `stock-expiry-settings-card.tsx` — `isAdmin` prop,
  `isDirty && isValid` gating Save, `role="status"` summary line, « Seul un administrateur peut modifier ce réglage. »
- `app/abonnement/page.tsx` — `Availability` is a **tri-state** (`unknown`/`available`/`unavailable`); render order is
  unavailable → error → loading → data; a failed read uses `LoadFailureNotice`, **never** an `EmptyState`. Grid is
  `lg:grid-cols-2` (a tablet portrait is 820 px). The date line forks on `state === "Expired"`, never on `allowsWrites`.
- `SubscriptionProvider` — 5-min re-read only *while warning in force*, focus/visibility, and any 402. A **404 turns
  the feature off**; any other failure keeps the last good value. Dismissal is keyed on the server's own
  `` `${endsOn}|${daysRemaining}` `` pair, never a browser-computed date.
- `SubscriptionBanner` mounts in `AppShell` (flex sibling of `<main>`), not `layout.tsx`.
- Rail: `buildConfigItems(isAdmin, showSubscription = true)` — the capability defaults to **showing**, and is threaded
  from exactly one call site (`dashboard-sidebar.tsx:53`). A new route also needs `lib/zones.ts:ROUTE_ZONES`.
- Realtime keys (`lib/realtime/clinic-hub.ts:15-50`): 20 of them; `RealtimeResourceResolverTests` asserts equality with
  the backend's emitted set **in both directions**. WhatsApp settings live under `clinics` today.
- `npm run check:responsive` runs **15** checks, no exemptions. The ones that will police this work: `card-fallback`
  (derived — a `<Table>` with no `<CardList>` beside it), `dialog-max-w` (`md:` only), `sheet-vh` (`dvh` not `vh`),
  `failed-read-as-empty` (`.catch(() => [])`), `api-headers` (`Bearer` outside `client.ts`), `type-scale`.

---

## 5. Vendor console conventions

- Routes: `/` → redirect `/cabinets`; `/cabinets`; `/cabinets/[clinicId]`; `/journal`; `/mot-de-passe`. **No nav rail,
  no bell, no clinic chrome** — a requirement, not a simplification. `robots: noindex`.
- Cabinet fiche section order (`[clinicId]/page.tsx:72-113`): `Subscription` → **`Suspension` (deliberately its own
  section — placing it under billing would present it as a billing lever)** → Activité → trend → admin + journal link.
  Preceded by a `role="note"` disclaimer that the fiche holds no patient data and that opening it is journalled.
- A write is four layers: server-side `lib/api/client.ts` (`ConsoleApiError` carries `status` **and `code`**, reads
  `body.error` never `message`) → its own `app/bff/<noun>/route.ts` (re-emits `{ error, code }` verbatim, 0 ⇒ 503) →
  a `"use client"` sheet → `router.refresh()` (pages are `force-dynamic`).
- **Idempotency key is a body property**, minted `crypto.randomUUID()` in `openChanged(true)` — once per *opened
  sheet*: per-submit defeats it, per-mount makes a deliberate second payment impossible. Enforced by a **partial
  unique index on the access ledger**, not a read-first check.
- 409 handling: branch on `body.code`, show the refusal **and** `router.refresh()` so the other actor's motif appears.
- Consequence sentences come from the **server** (`entry.ifCancelled`, re-folded with that entry marked cancelled) —
  the client-side « end minus duration » is wrong for any but the latest entry.
- Every dialog: `side="bottom"` + `lg:` centred override, `dvh` never `vh`, footer a `shrink-0` sibling of a
  `min-h-0 flex-1 overflow-y-auto` body, discard guard only when something was typed.
- Unknown ≠ zero, in **four** places: `measured()` → `—`, the card list drops every figure and says « Jamais mesuré »,
  a page-level freshness note, and summary tiles that appear only when `> 0`.
- A failed read replaces the whole page with `role="alert"` « Portefeuille illisible » + *« Ceci n'est **pas** un
  portefeuille vide »*. `StateBadge` states the word; tone is emphasis, never the only signal.
- **`PlatformReadShape.AllowedLeafNames`** is a closed set of **scalar field names** (ordinal), and is the whole of
  US-7. `PlatformReadShapeTests` asserts it in **both** directions — *an unused allowance is a pre-approved hole* — so
  a name declared before its DTO exists **fails the build**. `SuspensionReason` is the precedent for admitting free text.
- `PlatformAccessAction` — 6 members; each *arrives with the write that produces it*. `HasConversion<int>()` ⇒ no
  migration. `PlatformAccessLedger.RecordAsync` **stages**, so it rides the caller's transaction;
  `RequireAccountId` **throws** rather than swallowing.
- `PlatformAccessLabels` pins `CultureInfo.GetCultureInfo("fr-FR")` explicitly — never the container's ambient culture.
- `console/` shares **no code** with `web/` (verified: no imports across). It carries its own adapted `sheet.tsx`,
  `card-list.tsx`, `pager.tsx` and its own `check-responsive.mjs`.

---

## 6. ⚠️ Meta's current rules — this invalidates the assumed sender model

Researched against `developers.facebook.com/documentation/business-messaging/whatsapp/…` on 2026-08-11.
**Confidence is flagged per item; several Meta Help Center pages are JS-gated and could not be read.**

### 6.1 « Each clinic's number hosted on the vendor's WABA » is no longer a supported pattern
Three independently documented facts, each sufficient on its own:

1. **On-Behalf-Of (OBO) WABA ownership is deprecated** — *"The On-Behalf-Of WABA ownership model is deprecated and is
   no longer possible."* Last onboarding 29 Sep 2025; last transfers 1 Oct 2025; remainder auto-migrated through 2025.
2. **The Service Provider Terms forbid it** — *"You may not create a WABA on behalf of your Client without that
   Client's request and consent"* and *"Without our written consent, you may not create a WABA with the intention of
   utilizing it for a future Client."* Each client must accept the Solution Terms themselves.
3. **Hard ceilings** — **20 phone numbers per business portfolio**, no documented path beyond; and since
   **7 Oct 2025** *"Messaging limits are calculated and set at the business portfolio level and are shared by all
   business phone numbers within a portfolio."* So 20 clinics would **share** one 250 / 2 000 / 10 000 quota —
   adding a clinic no longer adds capacity.

### 6.2 The supported way to buy capacity centrally
**Solution Partner (Meta Business Partner) + credit line.** Each clinic owns its **own** WABA and number; the vendor's
credit line is attached to those WABAs and *"You are the 'Bill To Party' for all businesses sharing your credit
line."* Tech Provider has **no** credit line — Meta bills the clinic directly.
⚠️ *"Credit lines cannot be changed after being attached to a WABA. If the WABA needs a different credit line, a new
WABA must be created."*
⚠️ **Per-clinic cost via API is lost on exactly that route**: `pricing_analytics` supports a `PHONE` dimension and a
`COST` metric, but `COST` **is not returned for WABAs sharing a Solution Partner's credit line**. Attribution must
come from our own send log × rate card — which is precisely what this feature's usage row is.

### 6.3 Onboarding is never one field
- Ownership OTP by SMS or voice **to the handset**; the vendor cannot intercept it. Then a **separate**
  `POST /{phone-number-id}/register` with a 6-digit PIN. Registration capped at 10 per number per 72 h.
- A number already on **consumer WhatsApp** must be deleted first (*"up to 3 minutes"* to become available; history lost).
- **Coexistence (2025)** removes that for the **WhatsApp Business app**: the clinic connects its existing account via
  Embedded Signup and runs both. Requires app **v2.24.17+**, fixes throughput at **20 mps**, disables disappearing
  messages / view-once / broadcast lists on 1:1 chats, no group sync, no calls, and history must sync **within 24 h**.
- **Display name is required and reviewed** (`AVAILABLE_WITHOUT_REVIEW` / `PENDING_REVIEW` / `DECLINED`); changeable
  10× per 30 days; 14 days to re-register after approval. ⚠️ The substantive naming rules are on JS-gated Help Center
  pages — **unverified**.
- If the number had **two-step verification**, that PIN is required; a clinic that has forgotten it is blocked.
- **UI-only, no API**: deleting a business phone number, disabling two-step verification, adding a payment method.
- Embedded Signup onboarding is throttled to **10 new customers per rolling 7 days**, → 200 after verification.
  ⚠️ **Embedded Signup v2 is deprecated 15 Oct 2026** — build on v4.

### 6.4 Pricing — this validates « messages sent » as the unit
**Per delivered template message since 1 Jul 2025**; conversation pricing is retired. Charged on **delivery**, by
template category and recipient country. Categories: Marketing (always charged), **Utility (free inside an open 24 h
customer-service window)**, Authentication. Service is free. **There is no free tier** — the old 1 000 free
conversations/month belonged to the retired model.
⚠️ **Both free rules end 1 October 2026** (service messages, and utility inside an open window). Rates for that change
are to be announced by 1 Sep 2026 and are **not yet published**.
Tunisia bills under **"Rest of Middle East"** (confirmed grouping); ⚠️ the actual rates are on JS-gated rate cards and
are **unverified**.

### 6.5 Templates
**Templates are WABA assets, referenced at send time by name + language** — so a per-clinic WABA means per-clinic
templates. Review *"can take up to 24 hours"* (the commonly quoted « minutes » is not in current docs).
⚠️ **A template may not start or end with a parameter** — `"{{1}} : rappel de votre RDV le {{2}}"` is a documented
rejection cause. Write `"Rappel de votre rendez-vous chez {{1}} le {{2}}."`
⚠️ **Auto-recategorisation**: since 9 Apr 2025 a UTILITY submission Meta judges MARKETING *"is approved as MARKETING"*,
and *"a business accepts the charges associated with the category applied to the template at time of use"* — one
promotional line turns every reminder into a billed marketing message. 24 h notice, 60 days to appeal.
Quality pausing: 1st **3 h**, 2nd **6 h**, 3rd **disabled** (terminal — needs editing + resubmission).
Inactive templates are auto-archived and **deleted after 28 days**.
Limits: 250 templates per WABA (6 000 if the portfolio is verified with an approved display name); 100 creations per
WABA per hour.

### 6.6 What breaks « one shared vendor number for every clinic »
No single sentence forbids it, and I say so plainly — but four sources assemble: OBO deprecation, the Service Provider
Terms, the impersonation clause (*"may not… misrepresent your affiliation with a business… or otherwise mislead
customers as to the nature of your business"*), and the ToS resale clause. **The sharpest practical break is opt-in**:
*"Businesses must clearly state the business's name that a person is opting in to receive messages from"* — and a
display name is **one string per phone number**. The patient opted in to hear from *their cabinet*.
Healthcare: the telemedicine clause is **conditional** on local regulation, not a flat ban, and nothing prohibits
appointment reminders — but Meta explicitly disclaims fitness for entities with heightened confidentiality
requirements. **Practical rule: no clinical content in a reminder body** (« votre rendez-vous le 12/08 à 14h30 » yes;
« détartrage » no). ⚠️ Tunisia's Loi n° 2004-63 position is outside Meta's docs and needs local counsel.

### 6.7 Throughput and error codes the dispatcher must know
**80 mps default per registered number** (auto-upgradable to 1 000; Coexistence fixed at 20). Graph BUC limits:
200 calls/hour per app per WABA, **5 000/hour** once a number is registered; usage in `X-Business-Use-Case-Usage`.

| Code | Meaning | Outbox action |
|---|---|---|
| `4`, `80007`, `130429` | app / WABA / Cloud API rate limit | stay `Pending`, defer the send time |
| `131056` | too many to the **same recipient** too quickly | retry that pair on a longer delay |
| `131048` | restriction on how many this number may send | **`Blocked`** — retrying burns quota |
| `131064` | limit reached via template-classification violations | **`Blocked`** — self-lifts |
| `133016` | too many registration attempts | terminal for onboarding; do **not** retry |

This maps directly onto `NotificationStatus.Blocked` + `OutboxBlockReason` — but note it means **the sender must
distinguish these**, and today `HttpReminderChannelSender` collapses every non-2xx into one `TransientFailure`.

⚠️ Two webhook traps: `phone_number_quality_update` now signals **throughput**, not quality; messaging-limit changes
arrive on `business_capability_update` / `account_alerts`, not `account_update`. No webhook for a phone-number
quality change is documented.

### 6.8 Explicitly unconfirmed
Phone-number quality computation · `FLAGGED`/`RESTRICTED`/`RATE_LIMITED` semantics post-Oct-2025 · whether any webhook
fires on a quality change · display-name guideline list · template edit caps (10/30 d, 1/24 h) · whether one payment
instrument can serve N WABAs in a portfolio · Tunisia's actual per-message rates · the per-recipient 6 s / 45-burst
figures · consumer-app deletion timings beyond « up to 3 minutes » · Tunisia's data-protection position.
**Four of these are JS-gated Meta pages — open them in a logged-in browser before writing AC against them.**

---

## 7. Consequences for the spec

1. **The sender model chosen in `/think-solution` (« the clinic's number on the vendor's WABA ») is not viable** —
   § 6.1. The question is reopened; see the decision recorded in `spec.md`.
2. **The quota architecture is unaffected.** Ledger + fold + per-month usage row + park-at-dispatch stands whichever
   ownership model is chosen; only *whose credential the sender uses* changes.
3. **« Messages sent » is the right unit** — § 6.4 confirms per-message billing.
4. **Our own quota becomes more valuable, not less**, because Meta's messaging limits are now portfolio-wide: one
   runaway cabinet starves every other cabinet on the same portfolio.
5. **The sender must classify Meta's error codes** (§ 6.7) rather than collapsing them into `TransientFailure` — a
   `131048`/`131064` retried 3 times burns quota and then fails the reminder for the wrong reason.
6. **Template review is an onboarding state** (up to 24 h, per WABA) — « ready to send » is not the same as
   « connected », and the clinic-facing card must be able to say which.
