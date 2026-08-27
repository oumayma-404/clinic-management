# Implementation Plan: Abonnement du cabinet (essai gratuit puis abonnement payant)

**Status:** APPROVED
**Challenged:** Yes
**Created:** 2026-08-10
**Approved:** 2026-08-10
**Spec:** [features/clinic-subscription/spec.md](./spec.md)
**Companion (not planned here):** [features/platform-console/spec.md](../platform-console/spec.md)

---

## Overview

A cabinet's right to **record new work** becomes a dated entitlement: one `ClinicSubscription` aggregate root per
clinic, whose `EndsOn` is a **denormalised full re-fold** of an append-only, cancellable `SubscriptionPeriod` ledger.
Enforcement is a single `SubscriptionGateMiddleware` sitting immediately after `TenantScopeMiddleware` — where the
clinic id is already on `ITenantScope` and the account is already cached by `RequestAccount` — refusing every non-GET
request under `/api` with **HTTP 402** unless the endpoint carries `[AllowsWithoutSubscription("why")]`. Reads are
untouched by construction: the middleware never inspects a GET.

Six decisions shape everything below, all taken during the interview:

1. **Middleware + endpoint attribute**, not a path allow-list, not a MediatR behaviour. FR-3 requires the exempt set
   be stated as *what*, so a route rename cannot silently change it; an attribute lives on the action it describes and
   a derived coverage test pins the whole set. Middleware is also the only seam that covers non-MediatR writes
   (`AuthController`, the Google OAuth plumbing) and the only one that can actually emit a 402 — a pipeline behaviour
   can only throw or return a generic `TResponse`.
2. **`EndsOn` is stored, and always recomputed by a full re-fold** — never `EndsOn += duration`. The gate then reads
   one indexed row on the hot path, and `verify-schema` pins `stored == fold`, the shape every other denormalisation
   in this product is held to.
3. **⚠️ The fold takes no clock, and it folds over an EXCLUSIVE cursor.** Each entry anchors on **its own recorded
   clinic-day**, not on "today". Passing `today` into the fold — the naive reading of AC-5.2 — makes the result depend
   on *when* it is recomputed, so a lapsed entry would restart from today on every re-fold, cancelling one entry would
   move unrelated dates, and `verify-schema`'s `stored == fold` check would flap daily. The fold must be a pure
   function of the entries alone; AC-5.2's "later of current end or today" is reproduced exactly because at the moment
   an entry is recorded, its recorded day *is* today.

   ⚠️ **The cursor is exclusive — « the first day not yet covered » — and that is what makes one formula correct for
   both anchors.** An inclusive running end and a recorded day are **not** the same kind of value: a recorded day is
   an inclusive *start* (creation day is day 1, AC-1.1) while a running end is an inclusive *end*, so a single
   `anchor + duration` over both is wrong in one of the two cases whichever way it is written — it yields a **31-day**
   trial (AC-1.1 says 10 Aug → 8 Sep) or a lapsed grant one day long (EC-3 says end 20 Sep + 12 months → 20 Sep 2027,
   with no −1). Folding on an exclusive cursor removes the asymmetry instead of branching on it:

   ```
   DateTime? end = null; DateTime? cursor = null;          // cursor = first day NOT yet covered
   foreach (var e in nonCancelledInRecordedOrder) {
       if (e.IsOpenEnded) return null;                     // collapse (FR-1's real "no expiry" state)
       var start = cursor is null || cursor < e.RecordedOnClinicDay
                 ? e.RecordedOnClinicDay                   // first entry, or the cabinet had lapsed
                 : cursor.Value;                           // still valid: resume where cover ran out
       cursor = e.DurationMonths is int m ? start.AddMonths(m)
              : e.DurationDays   is int d ? start.AddDays(d)
              : e.ExplicitEndsOn!.Value.AddDays(1);
       end = cursor.Value.AddDays(-1);                     // the inclusive end day FR-1 stores
   }
   ```
   Trial: 10 Aug → cursor 9 Sep → **end 8 Sep** ✓. EC-3: end 20 Sep → cursor 21 Sep → +12 m → cursor 21 Sep 2027 →
   **end 20 Sep 2027** ✓. `AddMonths` still clamps (31 Jan + 1 month → 28/29 Feb, FR-2/EC-3).

   ⚠️ **Consequently the trial's `EndsOn` is NOT written directly.** `SubscriptionProvisioning` builds the trial entry
   and calls `ClinicSubscription.RecomputeFrom([entry])` like every other date, keeping decision 2's « one write path
   to `EndsOn` » literally true. A hand-computed `creationDay.AddDays(trialDays - 1)` beside a fold that disagrees
   with it is the same defect twice: the arithmetic is stated in two places, and
   `subscription-end-date-matches-ledger` goes red on **every newly created cabinet** — the shape most likely to be
   dismissed as « the new check is noisy ».
4. **Warnings dedupe on a real key**, a new nullable `StaffNotification.SubscriptionThresholdDays`, not on a French
   message prefix. AC-3.4 needs four genuinely-new unread rows (7/3/1/0), which is the opposite of what the stale-backup
   and expiring-stock alerts do, and recovering behaviour by matching French prose is the defect this repo deleted in
   `adoption-gaps-remediation`.
5. **Parking happens at dispatch**, in both queues, keyed on a new machine-readable `OutboxBlockReason` enum column —
   FR-8's named gap is that today's un-park review asks only whether the *channel* can send, so a row parked for expiry
   would be released and dispatched on the next tick. Dispatch-time is also the only place that needs no actor: midnight
   has none.
6. **No realtime broadcast.** `Subscriptions` joins `RealtimeResourceResolver.ExcludedAreas`; FR-15's re-read (interval
   while warning/expiry is in force · window focus · immediately on any 402) is the mechanism, because a vendor command
   runs out-of-process with no notifier and an entitlement ending at midnight has no actor to broadcast from.

**Everything is gated on a new 16th `DeploymentProfile` capability, `RequiresSubscription`** — true for
`HostedMultiTenant` only, decided by the deployment's *kind* and by nothing an operator can set (AC-7.3). On the two
other kinds the entitlement is created **open-ended** so FR-13 holds everywhere while nothing can ever expire.

**Deliberate scope note:** the user chose a single implementation story against the skill's sizing heuristic. The story
is therefore structured into **seven ordered, dependency-respecting parts**, each a vertical increment with its own
commit and validation. See **R-1**.

**Safety window worth knowing:** after Part A ships, every pre-existing cabinet is open-ended and every new one has 30
days — so **no cabinet anywhere can be refused for at least 30 days after deployment**. The intermediate states between
parts are therefore safe to ship in order, and Part B's gate lands long before it can refuse anybody.

---

## Files to Modify/Create

### Files to Create — Domain

| File | Purpose |
|------|---------|
| `api/ClinicManagement.Domain/Entities/ClinicSubscription.cs` | `AggregateRoot<Guid>` (FR-12 — a non-root gets **no audit row**). `ClinicId` (unique), `Plan`, `EndsOn` (nullable, inclusive clinic-local day), `IsSuspended`, `SuspensionReason`, `SuspendedAtUtc`, `SuspendedBy`. `RecomputeFrom(entries)` calls the ledger fold. |
| `api/ClinicManagement.Domain/Entities/SubscriptionPeriod.cs` | `AggregateRoot<Guid>` ledger entry. `ClinicId`, `Kind`, exactly one duration form, `Amount?`, `Method?`, `Reference?`, `Note?`, `RecordedAtUtc`, **`RecordedOnClinicDay`**, `RecordedBy`, `IsCancelled`, `CancelledAtUtc`, `CancelledBy`, `CancelReason`. `Cancel(reason, by, whenUtc)`; never edited, never removed. |
| `api/ClinicManagement.Domain/Services/SubscriptionLedger.cs` | The pure fold, over an **exclusive cursor** — **no clock parameter** (decision 3). `FoldWithSpans(inRecordedOrder) → (DateTime? EndsOn, IReadOnlyList<PeriodSpan> Spans)` is the implementation and `Fold(entries) → DateTime?` is a one-line call onto it, so the write path, `verify-schema` and the history screen cannot hold three arithmetics. `PeriodSpan(EntryId, FromDay, ThroughDay)` is FR-2's **derived** « period covered » — a **cancelled** entry gets `(null, null)` (shown « Annulé », contributing nothing) and an **open-ended** one `(FromDay, null)` (« sans échéance »). Month arithmetic via `DateTime.AddMonths`, which already clamps 31 Jan + 1 month → 28/29 Feb (FR-2, EC-3). An open-ended non-cancelled entry collapses `EndsOn` to `null`. |
| `api/ClinicManagement.Domain/Enums/SubscriptionPeriodKind.cs` | `Trial=1, Paid=2, Grandfathered=3, Complimentary=4` (FR-2). |
| `api/ClinicManagement.Domain/Enums/SubscriptionPaymentMethod.cs` | `Transfer=1, Cash=2, Cheque=3, Card=4`. **Deliberately not the clinic's `PaymentMethod`** — FR-2 says these amounts are the vendor's revenue and never the clinic's, and a shared enum is the first step toward a shared aggregation. |
| `api/ClinicManagement.Domain/Enums/SubscriptionState.cs` | `Trial, Active, Expired, Suspended` — derived, computed, **never stored** (FR-1). |
| `api/ClinicManagement.Domain/Enums/SubscriptionPlan.cs` | `Cabinet=1, Clinique=2, SurMesure=3` (FR-10). A label and a price; gates nothing. |
| `api/ClinicManagement.Domain/Enums/OutboxBlockReason.cs` | `ChannelUnsupported=1, ChannelDisabled=2, ChannelUnconfigured=3, SubscriptionExpired=4`. Shared by `Notification` and `PushDelivery` — FR-8's "machine-readable reason on the row". |
| `api/ClinicManagement.Domain/Repositories/IClinicSubscriptionRepository.cs` | `GetByClinicAsync`, `GetEntriesAsync(clinicId, PageRequest?)`, `AddAsync`, `AddEntryAsync`, `UpdateAsync`, `GetClinicsWithoutSubscriptionAsync`, `GetForReportAsync(withinDays)`. |

### Files to Create — Application

| File | Purpose |
|------|---------|
| `Features/Subscriptions/SubscriptionProvisioning.cs` | `CreateForNewClinic(clinicId, requiresSubscription, clinicToday, trialDays)` → `(ClinicSubscription, SubscriptionPeriod)`. The **one** helper both clinic-construction doors call (AC-1.2a). Trial when `requiresSubscription`, open-ended otherwise (AC-1.2). ⚠️ Takes **primitives, never a `DeploymentProfile`** — see `ISubscriptionPolicy` below. |
| `Features/Subscriptions/SubscriptionStateReader.cs` | **The one rule** FR-1 demands: `(subscription, clinicToday) → (State, AllowsWrites, ShouldWarn, DaysRemaining?)`. Read by the gate, the screen, the banner, the warning job, the report and every vendor verb. |
| `Features/Subscriptions/SubscriptionRefusals.cs` | The three French sentences + their codes (`subscription_required`, `subscription_suspended`, `subscription_missing`) in one place, so the middleware and any future console cannot word them differently. |
| `Features/Subscriptions/Queries/GetSubscriptionQuery.cs` | `GET /api/subscription`'s handler. |
| `Features/Subscriptions/Queries/GetSubscriptionHistoryQuery.cs` | `GET /api/subscription/history`, paged through `PageRequest`. ⚠️ **It reads the whole ledger (`paging: null`), folds it once through `FoldWithSpans`, and cuts the page with `PagedResult.FromSource`** — the third member of the family « an ordered result no single query knows a row's position in » (« Créances » and l'extrait de caisse are the other two). SQL paging is **not** an option here and the reason is structural, not performance: `fromDay`/`throughDay` are derived by folding **every earlier** entry, so a page-2 query restarts the cursor and prints overlapping or restated periods; and a **cancelled** entry is displayed while being excluded from the fold, so the page's rows are not the fold's rows. |
| `Features/Subscriptions/Commands/GrantSubscriptionPeriodCommand.cs` | Records an entry, re-folds, saves. AC-5.1/5.2/5.7. |
| `Features/Subscriptions/Commands/CancelSubscriptionPeriodCommand.cs` | Cancels with a mandatory reason, re-folds (possibly into the past). AC-5.5, EC-4. |
| `Features/Subscriptions/Commands/SetSubscriptionSuspensionCommand.cs` | Suspend / unsuspend with a mandatory reason (FR-7). |
| `Common/Maintenance/SubscriptionReportService.cs` | The report's core, in Application so it is unit-testable and **not DI-registered** (the `AdminPasswordRecoveryService` lesson — no HTTP-reachable grant path, FR-6). |
| `Common/Interfaces/ISubscriptionPricing.cs` | Application-side seam for per-deployment prices, payment instructions and contact details. Application references no configuration package — the same reason `IPublicAppUrlProvider` exists. |
| `Common/Interfaces/ISubscriptionPolicy.cs` | ⚠️ **The capability seam, and it is structurally required, not stylistic.** `DeploymentProfile` lives in **Infrastructure** and `ClinicManagement.Application.csproj` references **Domain alone**, so no Application type can name it — every existing capability reaches this layer through an interface (`IOsPushAvailability`) or is asked in the API controller (`AllowsPublicClinicSignup`). Two members: `bool RequiresSubscription` and `int TrialDays`. Impl `Infrastructure/Services/SubscriptionPolicy.cs`, registered by **`AddInfrastructure`** (so the `provision-clinic` verb, whose container is `AddInfrastructure` alone, can resolve it). ⚠️ `RequiresSubscription` returns `_profile.RequiresSubscription` and reads **no configuration key at all** — AC-7.3 — while `TrialDays` is operator config; a test pins that no config value can flip the first, which is the `IOsPushAvailability` split (the two questions kept apart on purpose) rather than one interface answering both. |
| `Common/Models/SubscriptionDto.cs`, `Common/Models/SubscriptionPeriodDto.cs` | The two wire shapes in the spec's *API Endpoints* section, verbatim. |
| `Common/Authorization/AllowsWithoutSubscriptionAttribute.cs` | `[AttributeUsage(Method|Class)]`, carries a **mandatory** `Reason` string. Beside `AuthorizationPolicies` so API and tests both see it. |

### Files to Create — Infrastructure

| File | Purpose |
|------|---------|
| `Persistence/Configurations/ClinicSubscriptionConfiguration.cs` | Unique index on `ClinicId`; `EndsOn` as `date`; money columns written with **no** `HasColumnType`/`HasPrecision` (the model-wide `(18,3)` convention — an explicit annotation would be reported as drift). |
| `Persistence/Configurations/SubscriptionPeriodConfiguration.cs` | Index `(ClinicId, RecordedAtUtc)`; FK to `Clinics`; length caps mirroring the entity's `MaxXLength` constants. |
| `Repositories/ClinicSubscriptionRepository.cs` | Guarded `UpdateAsync` (the detached-`xmin`-0 trap `ClinicSignupRepository` documents). `GetEntriesAsync` is ordered `RecordedAtUtc` then `.ThenBy(x => x.Id)` — the unique tie-break every ordered read here needs — and **`paging` is honoured only for callers that want a window; the history read passes `null`** and pages in memory (see `GetSubscriptionHistoryQuery`). The fold itself always reads the **whole** ledger: a fold over a page is not a fold. |
| `Services/SubscriptionPricing.cs` | `ISubscriptionPricing` over `IConfiguration`, reading a new `Subscription` section on the `SmtpConfig` one-accessor-per-section rule. |
| `Migrations/<ts>_AddClinicSubscriptions.cs` (+ `.Designer.cs` + snapshot edit) | **Hand-written** — `dotnet ef` cannot scaffold on this machine. Two tables, three columns, and the grandfathering backfill. See *Migrations*. |

### Files to Create — API

| File | Purpose |
|------|---------|
| `Controllers/SubscriptionController.cs` | `GET /api/subscription` (`AnyClinicRole` — AC-2.2's deliberate exception), `GET /api/subscription/history` (`AdminOnly` — AC-2.3). 404 when `!RequiresSubscription`. |
| `Middleware/SubscriptionGateMiddleware.cs` | The gate. See Part B for the exact predicate. |
| `BackgroundJobs/SubscriptionWarningJob.cs` | Daily, on `StockExpiryJob`'s template: `RunAs` + `UseSystemWide`, one bounded pass over clinics, try/catch per clinic. |
| `Maintenance/SubscriptionGrantCommand.cs` | `subscription-grant --clinic <id\|email> --months N [--ends-on] [--plan] [--amount] [--method] [--reference] [--note]`. |
| `Maintenance/SubscriptionCancelCommand.cs` | `subscription-cancel --entry <id> --reason "..."`. |
| `Maintenance/SubscriptionSuspendCommand.cs` | `subscription-suspend --clinic <id\|email> --reason "..."`. |
| `Maintenance/SubscriptionUnsuspendCommand.cs` | `subscription-unsuspend --clinic <id\|email>`. |
| `Maintenance/SubscriptionReportCommand.cs` | `subscription-report [--within-days N]`. Exit **2** when it finds expiring/expired cabinets (AC-5.9). |

### Files to Create — Tests (`api/ClinicManagement.UnitTests/`)

| File | Purpose |
|------|---------|
| `Domain/SubscriptionLedgerTests.cs` | The fold: AC-5.2 later-of, AC-5.4 cancelling a **middle** entry moves the date, EC-3 pay-early, EC-4 cancel-into-the-past, month clamping, open-ended collapse, **and that the fold is clock-free** (same entries → same answer on two different simulated days). Plus the two arithmetic cases the exclusive cursor exists for: a **trial-only** ledger folds to `creationDay.AddDays(29)` exactly (AC-1.1, 30 days not 31) and a **lapsed** cabinet's 12-month grant starts its count on the recorded day, while EC-3's still-valid one adds twelve months to the old end with no off-by-one. A named test asserts `stored == fold` on a freshly provisioned cabinet, since that is the pair `verify-schema` compares in production. |
| `Features/Subscriptions/SubscriptionStateReaderTests.cs` | AC-1.1's day-1 arithmetic, FR-1's `daysRemaining == 0` on the last working day, the four thresholds, `Suspendu` beating `Expiré`, `null` end date → `Actif` for ever. |
| `Features/Subscriptions/SubscriptionProvisioningTests.cs` | Trial on `HostedMultiTenant`, open-ended on the other two kinds (AC-1.2, AC-7.1/7.2), AC-1.5 (a later config change moves no existing date). Plus **`SubscriptionPolicy` reads `RequiresSubscription` from the kind alone** — a `Subscription:*` key cannot turn it on or off (AC-7.3). |
| `Features/Subscriptions/GrantSubscriptionPeriodCommandHandlerTests.cs` | AC-5.1/5.3/5.7, EC-5 (both concurrent grants land and are kept). |
| `Features/Subscriptions/CancelSubscriptionPeriodCommandHandlerTests.cs` | AC-5.5, mandatory reason, nothing deleted. |
| `Features/Subscriptions/SubscriptionWarningTests.cs` | AC-3.4's four rows, AC-3.5's idempotence (a daily re-run yields no fifth row), FR-5's re-arm on extension. |
| `Api/SubscriptionGateMiddlewareTests.cs` | The predicate over fabricated endpoints: GET always passes; expired/suspended/missing each get their own code; `Unset`/`SystemWide` scope passes through (the console's future shape, FR-3 ⚠️). |
| `Api/SubscriptionExemptionCoverageTests.cs` | **Derived.** Every non-GET action across all controllers is gated or carries the attribute with an approved FR-3 reason; named facts pin the three compute-only POSTs as exempt and **AI chat as not**. |
| `Common/ClinicCreationEntitlementTests.cs` | **Derived source scan.** Every Application/API source containing `new Clinic(` must also call `SubscriptionProvisioning.CreateForNewClinic` — the `SystemWideCallerCoverageTests` shape, and what catches a *third* door added later (AC-1.2a). ⚠️ **Scope the scan to `ClinicManagement.Application/` + `ClinicManagement.API/` and assert exactly two production sites**: `new Clinic(` currently has **19** occurrences in the solution and **17** are test fixtures, so an unscoped scan fails on the next test that builds a `Clinic` — noise that gets the guard deleted rather than a third door caught. |
| `Features/Subscriptions/SubscriptionTenantIsolationTests.cs` | The per-handler layer, matching every other clinic-scoped feature. |

### Files to Create — Frontend

| File | Purpose |
|------|---------|
| `web/lib/api/subscription.ts` | `subscriptionApi.get()`, `.history(page, pageSize)`, DTO types. |
| `web/lib/subscription/subscription-context.tsx` | `SubscriptionProvider` + `useSubscription()`, on `ConnectivityProvider`'s shape. Owns FR-15's three re-read triggers and the per-browser per-day dismissal (AC-3.2, `sessionStorage`-free: keyed on the clinic day, so it lapses on its own). |
| `web/components/subscription/subscription-banner.tsx` | One line wrapping to two, 44 px `coarse:` targets, dismiss control **absent** once expired (AC-3.3), `role="status"` and never a modal, text + icon never colour alone. |
| `web/components/subscription/subscription-history-table.tsx` | `CardList` below `md:` / `<Table>` above, the canonical two-tree pattern. A cancelled entry marked « Annulé » **in words** as well as struck through. |
| `web/app/abonnement/page.tsx` | State card → price → payment instructions (never behind a disclosure) → contact; two columns at `xl:`. For a non-admin, render **`<AccessDeniedCard description=… />` in place of the history section** — the section's own component is not mounted at all, so its fetch never fires and cannot stack 403 toasts. That is the shape all nine existing callers use (`/caisse`, `/cheques`, `/creances`, `/factures`, `/journal`, …), and it is `@/components/ui/access-denied-card`. EC-13's retryable « Réessayer » state. |

### Files to Modify

| File | Changes |
|------|---------|
| `api/ClinicManagement.Infrastructure/Deployment/DeploymentProfile.cs` | Add the 16th capability `RequiresSubscription` — `HostedMultiTenant` only. `DeploymentProfileCoverageTests` picks it up by reflection for free. |
| `api/ClinicManagement.API/Controllers/AuthController.cs` | `GetMode()`'s anonymous object gains `requiresSubscription = deployment.RequiresSubscription`. |
| `Application/Features/Clinics/LocalClinicProvisioning.cs` | Stage the entitlement + trial entry into the **same** `SaveChangesAsync` (FR-4, "one indivisible operation"). Construction door 1 of 2 — and a **signature** change with **three** call sites behind it (`CreateClinicCommand` Local branch · `provision-clinic` verb · `VerifyClinicSignUpCommand`); see Part A step 4. |
| `Application/Features/Clinics/Commands/CreateClinicCommand.cs` | Same, in the Auth0/Cloud branch that builds its own `Clinic`. **Door 2 of 2 — AC-1.2a's whole point.** |
| `Application/Features/Auth/Commands/SignUpClinicCommand.cs` | AC-1.3: « 30 jours d'essai gratuit, sans carte bancaire » in the verification e-mail body. |
| `Infrastructure/Persistence/ApplicationDbContext.cs` | Two `DbSet`s + two `HasQueryFilter` lines. `TenantScopeFilterTests` **requires** the filters the moment `ClinicId` exists — a hard failure if omitted, no test edit if present. |
| `api/ClinicManagement.API/Program.cs` | `UseMiddleware<SubscriptionGateMiddleware>()` **after `LocalAuthEnforcementMiddleware`** (i.e. last before `MapControllers`, line ~596) — not immediately after `TenantScopeMiddleware`; see Part B step 2's ⚠️ on ordering. Five verb branches in the top-level dispatch block; `RecurringJob.AddOrUpdate<SubscriptionWarningJob>("warn-subscription-expiry", …, Cron.Daily(7))` guarded on `profile.RequiresSubscription` with a `RemoveIfExists` in the else. |
| `Application/Common/Behaviors/RealtimeResourceResolver.cs` | `ExcludedAreas` += `"Subscriptions"`, with a comment citing FR-15. |
| `Domain/Entities/StaffNotification.cs` | `int? SubscriptionThresholdDays` + a `ForSubscription(...)` construction path. |
| `Domain/Enums/NotificationCategory.cs` | `SubscriptionExpiring = 10`. |
| `Domain/Enums/NotificationTargetKind.cs` | `Subscription = 5` (no id — the kind alone names the screen, as `Recall` and `BackupSettings` already do). |
| `Application/Common/Services/StaffNotificationRules.cs` | `ReachesALockedPhone` → **`false`** (AC-3.6). ⚠️ The switch *throws* on an unclassified category, so omitting this breaks **every** notification write, not just the new one. |
| `Application/Common/Interfaces/INotificationGenerator.cs` + `Common/Services/NotificationGenerator.cs` | `EnsureSubscriptionWarningAsync(clinicId, threshold, endsOn)` / `ClearSubscriptionWarningsAsync(clinicId)`, both inside `SafelyAsync`. |
| `Domain/Repositories/IStaffNotificationRepository.cs` + impl | `GetSubscriptionWarningAsync(clinicId, threshold)` and `GetSubscriptionWarningsAsync(clinicId)`, siblings of `GetBackupStaleAsync`. |
| `Domain/Entities/Notification.cs` | `OutboxBlockReason? BlockedReason`; `MarkAsBlocked(reason, sentence)` writes both. `Unblock()` clears both. |
| `Domain/Entities/PushDelivery.cs` | Same, beside the existing `FailureReason`. |
| `api/ClinicManagement.API/BackgroundJobs/NotificationJob.cs` | Dispatch: park on an expired/suspended clinic before calling a sender. `ReviewBlockedRowsAsync`: a row whose `BlockedReason == SubscriptionExpired` is only released when the clinic may write again — **the FR-8 gap**, and the half that must not ship alone. |
| `api/ClinicManagement.API/BackgroundJobs/PushDispatchJob.cs` | The identical pair. |
| `Application/Common/Maintenance/SchemaVerificationService.cs` | Three named checks (below). |
| `Application/Common/Maintenance/ISchemaVerificationReader.cs` | `DataMigrationCounts` gains three fields. |
| `Infrastructure/Persistence/SchemaVerificationReader.cs` | Three `ScalarOrNullAsync` queries, guarded on `requiredTable`/`requiredColumn` so a pre-migration run reports *not applicable* rather than `0`. |
| `api/ClinicManagement.API/appsettings.json` | A `Subscription` section: `TrialDays: 30`, per-plan `PriceMonthlyDt`/`PriceAnnualDt`, `PaymentInstructions`, `ContactEmail`, `ContactPhone`. Empty operator-owned defaults; real values arrive through `InstallConfiguration.AddInstallLayers()`. **No secret-bearing key.** |
| ~14 controller actions | `[AllowsWithoutSubscription("<FR-3 reason>")]` — see Part B's table. |
| `web/lib/api/client.ts` | `ApiErrorCode.SubscriptionRequired/Suspended/Missing`; a `402` row in `STATUS_FALLBACK_FR`; an `onSubscriptionRequired(listener)` set fired in the same block as `onClientTooOld` (line ~344). ⚠️ Must **not** touch `handleRequest`'s one-shot 401 retry — AC-4.5 says the refusal never signs the user out. |
| `web/lib/errors.ts` | `isPaymentRequiredError(err)` beside `isConflictError`/`isNetworkError`. |
| `web/app/layout.tsx` | `<SubscriptionProvider>` inside `<SessionProvider>`; `<SubscriptionBanner/>` above `{children}`'s shell. |
| `web/lib/nav.ts` | `/abonnement` unconditional in `buildConfigItems`; `HIDDEN_PATHS` += `/signup`, `/signup/verifier`. **Not** added to `SECRETARY_HIDDEN_HREFS`. |
| `web/lib/zones.ts` | `ROUTE_ZONES` += `["/abonnement", "config"]`, or `PageHeader`'s eyebrow says « Quotidien ». |
| `web/lib/api/auth.ts` | `AuthModeDto.requiresSubscription?: boolean`, read strictly as `=== true` (the rolling-deploy convention the field's siblings document). |
| `web/components/setup-wizard.tsx` | AC-1.3's trial sentence on the signup flow, before anything is submitted. |
| `api/ClinicManagement.UnitTests/Api/ControllerAuthorizationCoverageTests.cs` | No new anonymous endpoint; `SubscriptionController`'s two actions must resolve to named policies (they do). Expect no edit — confirm rather than assume. |

---

## Implementation Stories

### US-1: A cabinet's right to record work is a dated, correctable entitlement

**Goal:** On the hosted deployment a new cabinet gets 30 free days and is then read-only until the vendor records a
payment; every existing cabinet is untouched; the two other deployment kinds behave byte for byte as they do today.
**Blocked by:** None
**Layers:** DB, Domain, Service, API, Middleware, Jobs, Console, UI

> **Sizing:** this is one story by explicit user decision, against the skill's ~10–12-file heuristic (~70 files here).
> It is structured into **seven ordered parts**, each an independently committable vertical increment with its own
> validation. **Part boundaries are the split points** if the work must be resumed or divided — see **R-1**.

---

#### Part A — Every cabinet has an entitlement, at every door and for all of history

*Covers US-1, US-6, US-7 · FR-1, FR-2, FR-4, FR-9, FR-10, FR-13 · AC-1.1, AC-1.2, AC-1.2a, AC-1.5, AC-6.1–6.4, AC-7.1–7.3*

1. Add `RequiresSubscription` to `DeploymentProfile` (`HostedMultiTenant` only) and to its truth-table test.
2. Create the two entities, five enums, `SubscriptionLedger`, and the repository interface + implementation.
   - `SubscriptionPeriod` guards that **exactly one** duration form is set: `DurationMonths`, `DurationDays`,
     `ExplicitEndsOn`, or open-ended.
   - Trial uses `DurationDays = 30`; its `EndsOn` comes from **`RecomputeFrom([trialEntry])`**, never from a
     hand-written `AddDays(trialDays - 1)` (decision 3's ⚠️). Creation day is **day 1** — the exclusive cursor makes
     AC-1.1 fall out: 10 Aug → 8 Sep, and the cabinet may work all of 8 Sep.
3. Add the EF configurations, the two `DbSet`s and the two `HasQueryFilter` lines.
4. Write `SubscriptionProvisioning.CreateForNewClinic` and call it from **both construction doors**, staged into each
   door's existing single `SaveChangesAsync` (FR-4's « one indivisible operation »).
   ⚠️ **Two construction doors, but `LocalClinicProvisioning.ProvisionAsync` has THREE callers**, and since it is a
   `static` helper taking its repositories as explicit parameters, adding the entitlement changes its **signature** and
   breaks all three at compile time. Name them so none is discovered late:
   1. `CreateClinicCommand`'s **Local** branch (an HTTP request);
   2. the **`provision-clinic`** console verb — ⚠️ its container is `AddInfrastructure` **only** (no mediator, no
      `IClinicContext`), so `IClinicSubscriptionRepository` and `ISubscriptionPolicy` must be registered there or the
      verb cannot resolve them; and it declares **`UseClinic(id)`** before the writes, so the entitlement write is
      already inside that scope — nothing extra to declare;
   3. **`VerifyClinicSignUpCommand`** (public self-signup — the door AC-1.1's « created through public self-signup »
      actually means, and the one that will create most trials).
   Door **2 of 2** is separate from all three: `CreateClinicCommand`'s **Auth0/Cloud** branch builds its own `Clinic`
   and calls `CreateForNewClinic` directly. That branch always produces an **open-ended** entitlement
   (`CloudBrowser` ⇒ `RequiresSubscription` false), which is *why* it is easy to forget and why AC-1.2a names it.
5. Add `ISubscriptionPricing` + `SubscriptionPricing`, **`ISubscriptionPolicy` + `SubscriptionPolicy`**, and the
   `Subscription` appsettings section. Both impls are Infrastructure and registered by **`AddInfrastructure`**, not
   `AddApplication` — the console verbs build their container from that method alone (the reason `ITenantScope` is
   registered there too). ⚠️ Nothing in `Features/Subscriptions/` may name `DeploymentProfile`.
6. Hand-write the migration: two tables, `StaffNotification.SubscriptionThresholdDays`, `Notification.BlockedReason`,
   `PushDelivery.BlockedReason`, and the grandfathering backfill (one open-ended entitlement + one `Grandfathered`
   entry per existing clinic, reason recorded — AC-6.2). Hand-edit `.Designer.cs` and `ApplicationDbContextModelSnapshot.cs`.
7. Add the three `verify-schema` checks:
   - `every-clinic-has-an-entitlement` — count of clinics with no `ClinicSubscription` must be **0** (FR-13's derived
     count over every cabinet, never a list of known doors).
   - `subscription-end-date-matches-ledger` — clinics whose stored `EndsOn` differs from the fold must be **0**.
   - `subscription-grandfathered-entries` — reported as **Info** with its count, because AC-6.4's "equals the number
     of cabinets that existed" is established by FR-9's prescribed before/after run and diff, not by a figure the
     command can know on its own after new cabinets start arriving.
8. Tests: `SubscriptionLedgerTests`, `SubscriptionStateReaderTests`, `SubscriptionProvisioningTests`,
   `ClinicCreationEntitlementTests` (the derived source scan), `SubscriptionTenantIsolationTests`.

**Validation:**
- [ ] `verify-schema` reports `every-clinic-has-an-entitlement: 0` and a grandfathered count equal to the clinic count
- [ ] A new signup on a `HostedMultiTenant` config lands a `Trial` entry ending on day 30 counting the creation day as day 1
- [ ] The same code on `SelfHostedLan` and `CloudBrowser` lands an **open-ended** entitlement
- [ ] `ClinicCreationEntitlementTests` fails red when the `CreateClinicCommand` call is removed, then passes
- [ ] `TenantScopeFilterTests` passes with no edit to its `UnfilteredByDesign` dictionary
- [ ] Unit suite green

---

#### Part B — An expired cabinet keeps its records and loses only recording

*Covers US-4 · FR-3, FR-11 · AC-4.1–4.11, EC-1, EC-6, EC-10*

1. Create `AllowsWithoutSubscriptionAttribute` (mandatory `Reason`) and `SubscriptionRefusals`.
2. Create `SubscriptionGateMiddleware`, registered **after `LocalAuthEnforcementMiddleware`** — last before
   `MapControllers`.
   ⚠️ **Not immediately after `TenantScopeMiddleware`, and the four lines matter.** `LocalAuthEnforcementMiddleware`
   runs in `HostedMultiTenant` (`EnforcesTokenState` is true there) and enforces the two blocking preconditions of a
   self-issued JWT: token-version revocation (**401**) and a pending forced password change (**403**
   `must_change_password`). Placed before it, the gate would answer **402** for both on an expired cabinet — a
   deactivated or demoted colleague would be told the *subscription* had lapsed, and a user who must change their
   password would be routed by `client.ts`'s `onSubscriptionRequired` to « Abonnement » instead of by its dedicated
   `onMustChangePassword` to `/change-password`, leaving the account stuck in both directions. Placed after, the gate
   still has everything it needs: the tenant scope is set, `RequestAccount` has the account cached, and endpoint
   metadata is available (implicit `UseRouting` runs before **all** user middleware, which is why `UseAuthorization`
   works here with no explicit `UseRouting` call). Predicate, in order:
   - not `RequiresSubscription` → pass (FR-11);
   - `!path.StartsWithSegments("/api")` → pass (the front door also serves the web app);
   - `HttpMethods.IsGet/IsHead/IsOptions` → pass (**this is what makes AC-4.1 structural**);
   - endpoint carries `[AllowsWithoutSubscription]` → pass;
   - `ITenantScope.Kind != Clinic` → **pass**. ⚠️ FR-3's console note: a caller who is not a cabinet has no
     entitlement to find, and refusing them under `subscription_missing` would land the fault code on precisely the
     future console endpoints whose purpose is to end a refusal. Authentication already covers the anonymous case;
   - no entitlement row → 402 `subscription_missing`; suspended → 402 `subscription_suspended`;
     `!AllowsWrites` → 402 `subscription_required` naming the end date in `dd/MM/yyyy`;
   - otherwise pass.
   Body written with `WriteAsJsonAsync(new { error, code })`, the `ClientVersionMiddleware`/426 template.
3. Apply `[AllowsWithoutSubscription]` to FR-3's fixed set, each with its stated reason:

   | Endpoint (by *what* it is) | Reason |
   |---|---|
   | `AuthController` login / refresh / setup / register / signup / verify / **change-password** | AC-4.7, EC-2 |
   | `SubscriptionController` (both actions) | AC-4.8 — the one screen that says how to pay |

   ⚠️ **Two of these rows are already true by construction, and the table states them anyway.** Every
   `SubscriptionController` action is a **GET**, so the gate never inspects it (AC-4.8 holds structurally), and
   `AuthController`'s anonymous actions arrive with an **`Unset`** tenant scope, which passes on the rule above. Only
   **`change-password`** — authenticated, clinic-scoped, non-GET — genuinely needs the attribute. They are kept because
   FR-3 requires the exempt set to be stated as *what*, and a reader must not have to re-derive « is a GET refused? »
   to know. But `SubscriptionExemptionCoverageTests` classifies **non-GET actions only**, so it cannot fail red when
   the attribute is removed from a GET-only controller: do not write it as though it could, and do not read a green
   suite as proof those two rows are load-bearing.
   | `MetaController` client-requirements, upload-policy; `/health` | Not clinic work |
   | CNAM reimbursement estimates (batch POST) | AC-4.9 — computes, persists nothing |
   | Patient CSV import **preview** (dry run) | AC-4.9 — a `Query`, not a `Command`, by design |
   | Document render-for-immediate-download | AC-4.9 |
   | Mark a notification read / mark all read | FR-3 — otherwise AC-3.4's own expiry notice can never be dismissed |
   | Register / deregister a device push token | FR-3 — fired at every mobile sign-in; AC-4.7 |
   | Create a patient's default file folders | FR-3 — fired on first visit to the Files tab; a **read** would fail (AC-4.1) |
   | The signed-in user's own dashboard layout preference | FR-3 — personal interface state |
   | Run a backup on demand | FR-3 — the AC-4.2 argument; the scheduled one already keeps running |
   | Activate / deactivate a colleague's account | FR-3 — offboarding must not wait on an invoice |

   ⚠️ **The AI chat is deliberately not on this list** (FR-3, AC-4.9): its action set books and cancels appointments.
   ⚠️ **The Google OAuth callback is deliberately not exempted**: it is a GET that writes, but the request that
   *starts* the flow is refused, so the callback is unreachable on an expired cabinet.
4. Tests: `SubscriptionGateMiddlewareTests` and the derived `SubscriptionExemptionCoverageTests`, including the two
   named facts above.

**Validation:**
- [ ] With a simulated expired entitlement: every read, every CSV export and every PDF download succeeds
- [ ] `POST /api/appointments` returns 402 with `code: "subscription_required"` and a French sentence naming the date
- [ ] `POST /api/ai/chat` returns 402; the three compute-only POSTs return 200
- [ ] Sign-in and a forced password change both succeed on an expired cabinet (EC-2)
- [ ] On an expired cabinet, a **revoked** token still gets **401** and a user owing a password change still gets
      **403 `must_change_password`** on a non-exempt write — not 402 (the ordering ⚠️ in step 2)
- [ ] `SubscriptionExemptionCoverageTests` fails red when the attribute is removed from any approved endpoint
- [ ] `ControllerAuthorizationCoverageTests` still green

---

#### Part C — The clinic can see where it stands and how to pay

*Covers US-2 · FR-10, FR-15 (read half) · AC-2.1–2.5, EC-11, EC-13*

1. `GetSubscriptionQuery` + `GetSubscriptionHistoryQuery` (paged), the two DTOs, `SubscriptionController`.
   404 when `!RequiresSubscription`, checked in the controller **before** the mediator (`AuthController`'s
   `AllowsPublicClinicSignup` precedent).
2. `requiresSubscription` on `GET /api/auth/mode`; `AuthModeDto` on the client, read `=== true`.
3. `web/lib/api/subscription.ts`, `web/app/abonnement/page.tsx`, `subscription-history-table.tsx`.
4. Nav: `/abonnement` unconditional in `buildConfigItems`; `ROUTE_ZONES` row.
5. `Subscriptions` → `RealtimeResourceResolver.ExcludedAreas`.

**Validation:**
- [ ] A secretary can open « Abonnement » and read the state, date, price and payment instructions (AC-2.2, EC-10)
- [ ] A secretary cannot see the payment history; a non-admin sees the access-denied wrapper with no 403 toast storm
- [ ] An open-ended entitlement says so **in words**, not as a far-future date (AC-2.5)
- [ ] A suspended cabinet reads « Suspendu », not « Expiré » (EC-11)
- [ ] A dropped network on that screen yields a retryable « Réessayer », never « aucun abonnement » (EC-13)
- [ ] `npx tsc --noEmit`, `npm run check:responsive`, `npm run build` all clean; eye pass at 320/390/820/1180/1440

---

#### Part D — The banner, the refusal toast, and the live re-read

*Covers US-3 (banner half), US-4 (client half), US-5 (AC-5.8) · FR-15 · AC-3.1–3.3, AC-4.5, AC-4.6, EC-1*

1. `client.ts`: the three codes, the `402` French fallback, `onSubscriptionRequired`. **Do not touch the 401 retry.**
2. `SubscriptionProvider` in `app/layout.tsx`, owning FR-15's three triggers: an interval **only while a warning or
   expiry is in force**, a `window` focus listener (`web/` has none today — this is new), and an immediate re-read on
   any 402. Bounded per client, not per cabinet.
3. `SubscriptionBanner`: one line wrapping to two, ≤ ~15 % of a 380 px landscape viewport, dismissible **only while
   valid**, dismissal per browser keyed on the clinic day so it returns the next day with no server write (AC-3.2).
4. Confirm every refused save leaves its form open with input intact (AC-4.6) — the dialogs already use
   `showErrorToast` and keep the dialog open; verify rather than assume, and fix any site that closes on error.
5. `HIDDEN_PATHS` += `/signup`, `/signup/verifier`; AC-1.3 copy in `setup-wizard.tsx` and the verification e-mail.

**Validation:**
- [ ] A grant recorded by a console verb reaches the browser within one interval, with no sign-out and no reload (AC-5.8)
- [ ] A refused save raises a French toast, leaves the form populated, and the banner appears without a reload (EC-1)
- [ ] The expired banner has no dismiss control and is not a modal; « Expiré » is legible in greyscale
- [ ] Banner absent entirely when `requiresSubscription` is not `true`, and on `/login` and `/signup`
- [ ] Frontend gate clean; eye pass including a 380 px-tall landscape viewport

---

#### Part E — The clinic is warned before it stops being able to work

*Covers US-3 (notification half) · FR-5 · AC-3.4–3.7*

1. `NotificationCategory.SubscriptionExpiring`, `NotificationTargetKind.Subscription`,
   `StaffNotification.SubscriptionThresholdDays`, and `ReachesALockedPhone → false`.
2. `EnsureSubscriptionWarningAsync` / `ClearSubscriptionWarningsAsync` on `INotificationGenerator`, deduped on
   **(clinic, threshold)** — a genuinely new unread row per threshold, never a restatement.
3. `SubscriptionWarningJob`, daily, guarded on `RequiresSubscription`, `UseSystemWide` + `RunAs`, one bounded pass,
   try/catch per clinic. An extension past the window clears outstanding warnings and **re-arms** the thresholds.
4. `SubscriptionWarningTests`.

**Validation:**
- [ ] Simulating days −8 → 0 produces exactly four rows, each unread and each badging the bell
- [ ] Running the job twice on the same day adds nothing (AC-3.5)
- [ ] No push is queued for the category (AC-3.6)
- [ ] Extending past 7 days clears the rows; approaching again later warns again (FR-5)
- [ ] Every role receives the warning (AC-3.7)

---

#### Part F — The vendor unlocks a cabinet that has paid

*Covers US-5 · FR-6, FR-7, FR-12 · AC-5.1–5.9, EC-3, EC-4, EC-5*

1. The three commands, `SubscriptionReportService`, and the five `Maintenance/*Command` verb wrappers with their
   `Program.cs` branches. Gate each on `MaintenanceDatabase.HasConnectionString` — **not** on a profile capability
   (amendment M3: the hosted deployment has no local DB tooling, and these verbs must work there above all).
2. Each verb: `AddInfrastructure` only, `CreateScope`, `UseSystemWide(reason)` (or `UseClinic` where a single cabinet
   is handled) and `IAuditActorProvider.RunAs(CommandName)` — so FR-12's journal attributes the grant to the command,
   distinguishably from any clinic user. `SystemWideCallerCoverageTests` enforces this by reflection.
3. Every grant/cancel/suspend re-folds through `ClinicSubscription.RecomputeFrom` — the **one** write path to `EndsOn`.
4. `GrantSubscriptionPeriodCommandHandlerTests`, `CancelSubscriptionPeriodCommandHandlerTests`.

**Validation:**
- [ ] `subscription-grant --clinic <admin-email> --months 12` on a cabinet expiring in 10 days lands on the old end
      date + 12 months, not today + 12 (EC-3)
- [ ] `subscription-cancel` on a **middle** entry moves the end date (AC-5.4) and may push it into the past (EC-4)
- [ ] Two grants both land and are both kept (EC-5)
- [ ] Non-positive duration and unknown cabinet each refuse with a message naming which (AC-5.7)
- [ ] Every grant, cancellation and suspension appears in `GET /api/audit` for that cabinet (AC-5.6, FR-12)
- [ ] `subscription-report` exits **2** with cabinets found, **0** clean, **1** unable to run
- [ ] No HTTP path can grant — grep for a controller reference to the three commands returns nothing

---

#### Part G — Background work parks rather than sends or vanishes

*Covers FR-8 · EC-7*

1. `OutboxBlockReason` on `Notification` and `PushDelivery`; existing three French sentences keep their wording and
   gain their matching enum value.
2. `NotificationJob.DispatchAsync` and `PushDispatchJob`: park before calling a sender when the clinic may not write.
3. **Both** `ReviewBlockedRowsAsync` bodies: a `SubscriptionExpired` row is released only when the clinic may write
   again. ⚠️ Shipping the parking without this releases every parked reminder within a minute — FR-8's named gap.
4. Confirm the scheduled backup and the daily stock-expiry alert are untouched (FR-8), and that the manual backup is
   on Part B's exempt list.

**Validation:**
- [ ] A reminder queued before expiry for an appointment after it is parked with the machine-readable reason and is
      **not** sent (EC-7)
- [ ] With the channel fully configured and enabled, the review pass leaves that row parked
- [ ] Extending the cabinet releases it and it dispatches on the next tick
- [ ] `GET /api/outbox` shows the parked **reminder** rows in its `Blocked` depth. ⚠️ It reports **reminders and
      document emails only** — `GetOutboxDepthQuery` has no push section — so a parked `PushDelivery` row is checked
      directly in the table. Adding a push section to that read would be a deliberate widening of US-6's operator
      surface, not part of this feature
- [ ] Scheduled backups still run on an expired cabinet

---

## Testing Strategy

### Unit Tests (xUnit + Moq — the only automated backend gate; nothing touches a database)

- **`SubscriptionLedger.Fold`** — the highest-value target. AC-5.2 later-of; AC-5.4 cancelling a middle entry;
  month clamping (31 Jan + 1 month → 28/29 Feb); open-ended collapse; **clock-freedom** (identical entries fold to
  the same date whatever "today" is — the trap in decision 3).
- **`SubscriptionStateReader`** — AC-1.1's day-1 arithmetic; `daysRemaining == 0` on the last working day;
  `Suspendu` outranking `Expiré`; negative days never surfaced; the four thresholds.
- **`SubscriptionProvisioning`** — trial vs open-ended per deployment kind; AC-1.5.
- **The three command handlers** — refusals assert `Times.Never` on the writes as well as the failure, per convention.
- **`SubscriptionGateMiddleware`** — over `DefaultHttpContext` + fabricated endpoint metadata; every status/scope
  combination; and that a GET is never inspected.
- **Time** — always a fixed instant through `ClinicClock`, never a freshly-read `DateTime.UtcNow` (it agrees with the
  defect by construction and flakes at New Year). Include a Tunisian-midnight case for EC-8.

### Derived / guard tests (this repo's strongest pattern — a list is a second place to forget)

- **`SubscriptionExemptionCoverageTests`** — every non-GET action is gated or approved-with-a-reason; AI chat is
  asserted **not** exempt; the three compute-only POSTs are asserted exempt.
- **`ClinicCreationEntitlementTests`** — source scan over `new Clinic(`; catches a third door.
- **`TenantScopeFilterTests`** — auto-enrols both new tables the moment `ClinicId` exists; no edit expected.
- **`SystemWideCallerCoverageTests`** — auto-enrols the new job and the five verbs; each must declare a scope.
- **`DeploymentProfileCoverageTests`** — auto-enrols `RequiresSubscription`.
- **`RealtimeResourceResolverTests`** — must stay green with `Subscriptions` excluded and **no** frontend key added.
- **`MoneyReadConsistencyTests`** — must be **unchanged**, proving FR-2's "the vendor's revenue is never the clinic's".

### Schema verification (the only gate a migration has anywhere in this product)

Run `dotnet run -- verify-schema` **before and after** the migration batch and diff, per FR-9. The three new checks
plus the free index/FK/decimal diff cover the schema; the backfill is the one class of change no test can see.

### Frontend

`web/` has no test runner, no working ESLint and no CI. The gate is `npx tsc --noEmit` + `npm run check:responsive` +
`npm run build`, then an eye pass at **320 / 390 / 820 / 1180 / 1440 px** plus a **380 px-tall landscape** viewport for
the banner's ≤15 % budget. Manual scenarios: expired-cabinet read-and-export walk (EC-9), refused save with input
intact (AC-4.6), secretary opening « Abonnement » (EC-10), banner dismissal returning the next day (AC-3.2).

### Operator verification (not CI-runnable)

The five console verbs, the daily job's four thresholds over simulated days, and the reminder-parking round trip.

---

## Risk Register

| ID | Risk | Likelihood | Impact | Part | Mitigation |
|----|------|------------|--------|------|------------|
| R-1 | Single oversize story exceeds one session | High | Med | all | Seven ordered parts; split at a part boundary |
| R-2 | Hand-written migration + snapshot goes wrong | High | High | A | Copy `AddClinicSignups`; `verify-schema` before/after |
| R-3 | The gate refuses a write behind a screen the user reads | Med | High | B | FR-3's table copied verbatim; derived coverage test |
| R-4 | The gate misses a write that looks like a read (AI chat) | Low | High | B | Named fact asserting AI chat is **not** exempt |
| R-5 | Grandfathering backfill covers zero rows, invisibly | Med | High | A | `verify-schema` check + FR-9's before/after diff |
| R-6 | `EndsOn` drifts from the ledger | Low | High | A/F | Full re-fold only; one write path; clock-free fold; verify check |
| R-7 | The 402 is mistaken for a session loss | Med | High | D | Leave the 401 retry alone; AC-4.5 walk-through |
| R-8 | Parked reminders released and sent on an unpaid cabinet | Med | High | G | Both halves in one part; a test with a fully-configured channel |
| R-9 | Unclassified notification category throws on every write | Low | High | E | Same commit as the category; the switch fails loud |
| R-10 | The Auth0 door ships without an entitlement | Med | High | A | Derived source-scan guard over `new Clinic(` |
| R-11 | Inclusive-date off-by-one (30 vs 31 days, « 1 jour ») | Med | Med | A | `ClinicClock` only; table-driven threshold tests |
| R-12 | Vendor revenue leaks into la caisse / the dashboard | Low | High | A | Separate tables and enum; `MoneyReadConsistencyTests` unchanged |
| R-13 | The banner eats the chairside agenda | Med | Med | D | ≤15 % budget; eye pass at 380 px-tall landscape |
| R-14 | The 402 masks a 401 or a `must_change_password` 403 | Med | High | B | Gate registered **after** `LocalAuthEnforcementMiddleware`; a named validation step for both |

### R-1: The single story is larger than one session
- **Description:** ~70 files across Domain, Application, Infrastructure, API, jobs, console verbs and `web/`. The
  skill's heuristic is ~10–12 files per story.
- **Likelihood:** High · **Impact:** Medium (schedule, not correctness)
- **Mitigation:** Seven ordered parts, each a vertical increment with its own validation block and its own commit.
  Parts A→G respect dependencies strictly. The user chose one story deliberately; this is recorded, not re-litigated.
- **Contingency:** Split at a part boundary. **A|B|C** and **D|E|F|G** are the two most natural halves — A–C is a
  complete, shippable "entitlement exists, is enforced, and is visible" increment.

### R-2: The hand-written migration or model snapshot is wrong
- **Description:** `dotnet ef` cannot scaffold on this machine (a running API holds `api/**/bin`; Smart App Control
  refuses freshly-built design-time assemblies), so the migration, its `.Designer.cs` **and**
  `ApplicationDbContextModelSnapshot.cs` are all written by hand. An uncommitted or wrong snapshot makes every later
  migration duplicate this one.
- **Likelihood:** High · **Impact:** High
- **Mitigation:** Copy `20260807102000_AddClinicSignups` verbatim as the shape. Add the two tables, three columns and
  the backfill in **one** migration so the schema and its data land together under `MigrationLock`'s advisory lock.
  Run `verify-schema` before and after and diff — the index/FK/decimal halves diff the EF model against the catalog
  for free, so a snapshot that disagrees with the migration shows up immediately.
- **Contingency:** `Down()` drops the tables and columns; re-running `Up()` re-grandfathers idempotently (the backfill
  inserts only for clinics with no entitlement).

### R-3: The gate refuses a write issued by a screen the user experiences as reading
- **Description:** FR-3 names three of these explicitly — default file folders on the Files tab, push-token
  registration at mobile sign-in, marking a notification read. Each failure presents as "an expired cabinet cannot
  open a patient / cannot sign in on the tablet / cannot clear its notifications", none of which reads as a
  subscription matter to the person meeting it. A fourth of the same shape is one new endpoint away.
- **Likelihood:** Medium · **Impact:** High
- **Mitigation:** FR-3's table is copied verbatim into the approved-exemptions list, each row carrying its reason.
  `SubscriptionExemptionCoverageTests` enumerates every non-GET action across all controllers and fails on an
  unclassified one, so a new write endpoint is a build failure until somebody decides which side it is on.
- **Contingency:** Adding an attribute is a one-line, no-schema fix, deployable independently of everything else.

### R-5: The grandfathering backfill covers zero rows
- **Description:** A backfill is the one class of change no test can see — it can cover nothing and every suite still
  passes. The symptom is not an error: it is every existing cabinet becoming read-only 30 days after deployment, or
  immediately if the trial branch is taken for them.
- **Likelihood:** Medium · **Impact:** High (a whole deployment locked out of their own practices — the exact outcome
  US-6 exists to prevent)
- **Mitigation:** `every-clinic-has-an-entitlement` must read **0**, and the grandfathered count is reported so FR-9's
  before/after diff can confirm it equals the pre-deployment cabinet count. The reader's `requiredTable`/`requiredColumn`
  guard makes a pre-migration run report *not applicable* rather than a misleading `0`.
- **Contingency:** The backfill is re-runnable — it inserts only where no entitlement exists — so a partial run is
  repaired by re-running it, never by hand-editing rows.

### R-6: The stored `EndsOn` drifts from the ledger
- **Description:** Three failure modes. (a) An incremental `EndsOn += duration` makes AC-5.4 false — cancelling any but
  the latest entry would change no date. (b) Passing `today` into the fold makes the result depend on when it is
  recomputed, so a lapsed entry restarts from today and `verify-schema` flaps daily. (c) **A second place that computes
  a date at all** — the trial's `EndsOn` written directly at provisioning — disagrees with the fold by one day and
  fails `subscription-end-date-matches-ledger` on every new cabinet (decision 3's ⚠️; the trial now goes through
  `RecomputeFrom` too).
- **Likelihood:** Low (both are designed out) · **Impact:** High (money and access both wrong, silently)
- **Mitigation:** `SubscriptionLedger.Fold` takes **no clock** and is the single implementation, used by the write path
  and by `verify-schema` alike. `ClinicSubscription.RecomputeFrom` is the only writer of `EndsOn`.
  `subscription-end-date-matches-ledger` catches any bypass.
- **Contingency:** Re-folding every clinic is a pure, idempotent recomputation from data that was never lost.

### R-8: Parked reminders are released and sent on an unpaid cabinet
- **Description:** FR-8 names this precisely: today's un-park review asks only whether the *channel* can send, and a
  row parked for expiry passes all three of its checks — so it is released and dispatched on the next tick.
- **Likelihood:** Medium (it is the default outcome of shipping half the change) · **Impact:** High
- **Mitigation:** Both halves are in Part G and must land together. The machine-readable `OutboxBlockReason` exists
  precisely so the review can interrogate the reason rather than a French sentence. A test parks a row for expiry,
  runs the review with a **fully configured and enabled** channel, and asserts the row stays parked.
- **Contingency:** The parked rows are intact and re-dispatch correctly once the review term is added; nothing is lost.

### R-13: The banner eats the chairside agenda
- **Description:** The banner is the one element on every screen, competing with the agenda and the patient file on the
  tablet those are used on. The spec budgets it at ~15 % of a 380 px-tall landscape viewport.
- **Likelihood:** Medium · **Impact:** Medium
- **Mitigation:** One line wrapping to at most two; state + date + « Renouveler » only; dismiss control absent once
  expired. Eye pass explicitly includes a 380 px-tall landscape viewport, not only the five widths.
- **Contingency:** Collapse to state + link only below `sm:` — the date is on the « Abonnement » screen one tap away.

---

## Breaking Changes

### Change 1: Writes are refused on an expired hosted cabinet
- **What breaks:** Every create/modify/delete under `/api` returns **402** once the entitlement has ended. This *is*
  the feature.
- **Who is affected:** Cabinets on `HostedMultiTenant` only, and none for at least 30 days after deployment (every
  existing cabinet is grandfathered open-ended, every new one gets 30 days).
- **Handling:** Seven days of banner and four in-app notifications precede it; reads and exports are untouched.

### Change 2: Creating a clinic now writes an extra row in the same transaction
- **What breaks:** A failure constructing the entitlement fails clinic creation, at both doors. Previously nothing
  could fail there.
- **Who is affected:** Public self-signup, `provision-clinic`, self-hosted first run, and the Auth0 branch.
- **Handling:** FR-4 requires exactly this — a cabinet must not come into existence without an entitlement. The
  provisioning helper performs no I/O of its own, so the added failure surface is the `SaveChangesAsync` that was
  already there.

### Change 3: `GET /api/auth/mode` gains a field; two entities gain a nullable column
- **What breaks:** Nothing. `requiresSubscription` is optional on the wire and read `=== true`; the three new columns
  are nullable.
- **Who is affected:** No one. A web build newer than the API it talks to reads the flag as absent and mounts nothing.

---

## Migrations

### Migration 1: `AddClinicSubscriptions` (schema + data, one migration)
- **What:**
  - `CREATE TABLE "ClinicSubscriptions"` (unique index on `ClinicId`, FK to `Clinics`)
  - `CREATE TABLE "SubscriptionPeriods"` (index on `(ClinicId, RecordedAtUtc)`, FK to `Clinics`)
  - `ALTER TABLE "StaffNotifications" ADD "SubscriptionThresholdDays" integer NULL`
  - `ALTER TABLE "Notifications" ADD "BlockedReason" integer NULL`
  - `ALTER TABLE "PushDeliveries" ADD "BlockedReason" integer NULL`
  - **Backfill:** one open-ended `ClinicSubscription` + one `Grandfathered` `SubscriptionPeriod` per existing clinic,
    inserted only where no entitlement exists, with a written reason (AC-6.1, AC-6.2)
- **When:** At startup with every other migration, under `MigrationLock`'s session-level advisory lock. The
  pre-migration backup already aborts the migration if it fails.
- **Rollback:** `Down()` drops both tables and the three columns. The grandfathering data is lost with the tables,
  which is harmless — re-running `Up()` regenerates it from the clinic list.
- **Steps:**
  1. `dotnet run -- verify-schema` on the target, save the output
  2. Deploy; migrations apply at startup
  3. `dotnet run -- verify-schema` again; diff
  4. Confirm `every-clinic-has-an-entitlement: 0` and that the grandfathered count equals the pre-deployment clinic count
  5. `dotnet run -- subscription-report` to confirm no cabinet reads as expiring

### Configuration (deploy-time, not a code change)
Set `Subscription:TrialDays` (default 30), the per-plan monthly/annual prices, `PaymentInstructions`, `ContactEmail`
and `ContactPhone` in the operator-owned config layer. The spec's three **Open Questions** are all values in this
section — real prices, the French payment-instruction text, and the annual figures — and none blocks implementation.
Align the Tarifs page and the « Essai accompagné — 2 semaines » landing copy with the configured 30 days before go-live.

---

## Deviations from `/plan-feature`

- **Questions were asked in batches of up to four rather than strictly one at a time.** The decisions were mutually
  independent, and the spec they derive from is APPROVED and already challenged; ten sequential round trips would have
  produced the same answers more slowly. The spec itself records the same deviation.
- **No browser exploration.** There is no browser tooling in this repository (`agent-browser` absent), the same
  deviation `features/landing-website/design.md` and this feature's own spec record.
- **No `design.md` was produced or consulted** — none exists for this feature; the spec's *Device & Interface
  Behaviour* table is detailed enough to plan the three surfaces from, and it is treated as authoritative.
- **One story instead of several, by explicit user decision**, structured into seven ordered parts. Recorded as **R-1**
  with a stated split point rather than re-litigated.

---

## Challenge Record (`/challenge-plan`, 2026-08-10)

Nine issues found, nine applied. Exploration was **targeted verification** rather than a fresh four-agent sweep: this
plan's own file lists and decisions came from parallel exploration in the session that produced it, so the pass
re-checked only the load-bearing or still-unverified assumptions against source.

**Verified and held** (no change needed): the middleware pipeline order; that endpoint metadata is available to a
custom middleware even though `Program.cs` never calls `UseRouting` (the implicit one runs before all user
middleware — which is *why* `UseAuthorization` works there); `ClinicManagement.UnitTests` references both API and
Infrastructure, so the reflection guards compile; `NotificationCategory = 10`, `NotificationTargetKind = 5` and
`NotificationStatus.Blocked` are free/present as assumed; `MarkAsBlocked`/`Unblock` exist on **both** outbox entities;
`ConfigureConventions` applies `(18,3)` model-wide, so the money columns really must carry **no** annotation;
`PagedResult.FromSource`, `AccessDeniedCard`, `CardList`, `buildConfigItems`, `HIDDEN_PATHS`, `onClientTooOld`,
`onMustChangePassword` and `STATUS_FALLBACK_FR` all exist in the shape the plan assumes; exactly **two** production
`new Clinic(` sites; and — the one that could have made FR-12 silently false —
**`AuditSaveChangesInterceptor.ResolveClinicId` prefers the aggregate's own `ClinicId`** over the scoped one, so a
vendor grant recorded under `UseSystemWide` lands in the right cabinet's journal with no extra work (AC-5.6).

| # | Sev | Issue | Resolution |
|---|-----|-------|-----------|
| 1 | Critical | `SubscriptionProvisioning.CreateForNewClinic` took a `DeploymentProfile`, which lives in **Infrastructure** — Application references Domain alone, so it could not compile | New Application seam **`ISubscriptionPolicy`** (`RequiresSubscription` from the kind only, `TrialDays` from config), impl in Infrastructure, registered by `AddInfrastructure`; the helper takes primitives |
| 2 | Critical | The fold's single `anchor + duration` spanned two anchors with different semantics (inclusive *start* vs inclusive *end*), giving a **31-day** trial or a free day on a lapsed grant; and the trial's `EndsOn` was written directly, so it disagreed with its own fold and `subscription-end-date-matches-ledger` went red on every new cabinet | Fold restated over an **exclusive cursor** (« first day not yet covered »), pseudocode inline; the trial's date now comes from `RecomputeFrom` so decision 2's one-write-path is literally true; two named arithmetic tests added; R-6 gained failure mode (c) |
| 3 | Major | `SubscriptionPeriodDto.fromDay`/`throughDay` (FR-2, AC-2.3) had no source — `Fold` returns one scalar — and SQL paging cannot produce them (page 2's spans need page 1's entries; cancelled entries are shown but not folded) | `FoldWithSpans` is the implementation and `Fold` a call onto it; the history read folds the whole ledger and pages via **`PagedResult.FromSource`**, joining « Créances » and l'extrait as the third read that pages in memory by necessity |
| 4 | Major | « Door 1 of 2 » hid that `LocalClinicProvisioning.ProvisionAsync` has **three** callers whose compile breaks on the signature change, one of which resolves from `AddInfrastructure` alone | Part A step 4 names all three plus the DI and `UseClinic` constraints, and separates « two construction doors » (AC-1.2a) from « three helper callers » |
| 5 | Major | Registered after `TenantScopeMiddleware`, the gate answered **402** where `LocalAuthEnforcementMiddleware` owes **401** (revoked token) or **403 `must_change_password`** — routing that user to « Abonnement » instead of `/change-password`, stuck in both directions | Gate moved to **after** `LocalAuthEnforcementMiddleware` (last before `MapControllers`); validation step added; new **R-14** |
| 6 | Minor | Part G claimed `GET /api/outbox` shows the parked rows — it has no push section | Step reworded to reminders only; push parking checked in the table; widening the read named as deliberate scope |
| 7 | Minor | `[AllowsWithoutSubscription]` on `SubscriptionController` (GET-only) and AuthController's anonymous actions is unreachable/redundant | Kept as FR-3 documentation, with the limit of the derived test stated so nobody reads green as proof those rows bite |
| 8 | Minor | « a wrapper, not a branch » contradicted its own stated reason and all nine existing `AccessDeniedCard` call sites | Restated as « render `AccessDeniedCard` in place of the history section », matching the existing pattern and import path |
| 9 | Minor | The `new Clinic(` source scan would fire on test fixtures (17 of 19 matches) | Scan scoped to Application + API, asserting exactly two production sites |
