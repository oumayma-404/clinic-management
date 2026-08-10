# ClinicManagement.UnitTests — Test Suite Guide

xUnit + Moq unit tests for the whole `api/` solution. ~90 test classes, one folder per layer, mirroring the source tree. **Fast, isolated, mock-based** — no database, no HTTP server, no external calls. (There is no separate integration/E2E project; these are the backend's only automated tests.)

> Sub-guide under the root `CLAUDE.md`. This maps the test project; read the layer guides for the code under test.

## Stack

- **xUnit 2.5.3** (`[Fact]`, `[Theory]`), **Moq 4.20.72**, coverlet for coverage. .NET 8, nullable + implicit usings on; `Xunit` is a global `using` (`.csproj`).
- References **Application, Infrastructure, AND API** projects. The API reference exists so reflection tests can enumerate controller types; `FrameworkReference Microsoft.AspNetCore.App` is pulled in for `DefaultHttpContext` / MVC `ControllerBase` / authorization-options types used by the Phase-4 auth-gate + controller-coverage tests.

## Layout (mirrors the solution)

```
Api/            → controller/base + startup/job/maintenance tests (thin API layer)
Common/         → cross-cutting: MediatR behaviors, exception middleware, clinic provider, authz policies, admin recovery
Domain/         → pure entity/value-object/calculator rules (no mocks needed)
Features/       → CQRS handler tests, grouped by feature area (the bulk of the suite)
Hubs/           → SignalR realtime: ClinicHub, ClinicGroups, SignalRRealtimeNotifier
Infrastructure/ → service/repo/persistence tests: renderers, senders, backup, cert, storage, seeds
```

## Conventions (match these when adding tests)

- **Moq harness pattern.** Handler tests build a small private `Harness`/`Handler()` helper that wires mocked repositories + `ICurrentClinicResolver`/`IClinicContext` + `IUnitOfWork` and returns the real handler. See `Features/Notifications/NotificationGenerationTests.cs` for the canonical shape (nested `GeneratorHarness`/`StockHarness`, `NullLogger<T>.Instance` for loggers).
- **Spec-ID traceability.** Class-level XML `<summary>` and per-test `//` comments cite the spec item they cover (`[US-2]`, `[AC-4]`, `[FR-E3]`, `[R-3]`). Preserve this — it's how tests map back to feature specs.
- **Tenant isolation is a first-class guard.** Every clinic-scoped feature has a `*TenantIsolationTests` proving another clinic's row reads as "not found" for get/update/cancel/delete and lists are caller-scoped (fixed GUIDs `aaaa…`/`bbbb…`; e.g. `Features/Invoices/InvoiceTenantIsolationTests.cs`, and the same for Appointments/Documents/Files/Notifications/ProcedureTypes/**TreatmentPlans**).
- **Best-effort side effects assert non-failure.** Notification/reminder generators are tested to *swallow* persistence failures without breaking the core op (`Generator_Swallows_Persistence_Failure`).
- Fixed UTC `DateTime`s and deterministic GUIDs — no `DateTime.Now`/`Guid.NewGuid()` in assertions that must be stable.

## Release-gate / guard tests (fail loud when someone regresses a hardening decision)

- **`Api/ControllerAuthorizationCoverageTests.cs`** — reflection scan of every controller/action; the set of `[AllowAnonymous]` endpoints must *exactly* match the reviewed `ExpectedAnonymous` allow-list (the auth bootstrap, `Connectivity.Get`, `GoogleCalendar.Callback`, the four LAN trust routes, and `Meta.ClientRequirements`). Adding any new anonymous endpoint fails the build until reviewed — this is the Local-mode fail-closed guarantee (FR-E3).
- **`Common/Authorization/AuthorizationPoliciesTests.cs`** — the `FallbackPolicy` is installed only in Local mode.
- **`Common/Behaviors/RealtimeBroadcastBehavior*`** — the MediatR pipeline behavior that auto-broadcasts SignalR resource-changed events.
- **`Common/Behaviors/RealtimeResourceResolverTests.cs`** — the backend↔frontend realtime key contract, and a worked example of a guard that derives both sides instead of listing them (AC-P4.23–4.25). It reflects over every `IBaseRequest` in the Application assembly for the emitted set, **parses `web/lib/realtime/clinic-hub.ts`** for the declared set, and asserts they are **equal in both directions**; two allow-lists (emit-only / listen-only) exist for intentional asymmetry and are asserted **empty**. It replaced a 16-row `[InlineData]` table that stayed green throughout the entire period five keys were broadcast with nothing listening (audit § 9.1) — a table can only fail on rows someone remembered to write, never on the new area. It finds the frontend file via **`[CallerFilePath]`**, not `AppContext.BaseDirectory`, because the suite is routinely built to a scratch OutDir outside the repo (the SAC workaround); and it **throws** rather than skipping when the file is absent.
- **`Common/TenantScopeFilterTests.cs`** + **`Common/SystemWideCallerCoverageTests.cs`** (`multi-tenant-cloud` US-2)
  — the pair that holds the query filter's inversion from fail-open to refusing. The filter test asserts **SQL**,
  not rows (no database, no in-memory provider — `ToQueryString()`, same technique and same reason as
  `RecallQueryTranslationTests`), and it iterates **every filtered root read off `db.Model`**, so a 22nd
  clinic-owned aggregate is covered the day it is configured. It also pins the two cases that look alike and are
  not: an **`Unset` scope** (a scope exists and said nothing ⇒ compares against `Guid.Empty` ⇒ no rows) versus **no
  provider at all** (the design-time factory and hand-built contexts ⇒ everything, or `dotnet ef` and half this
  project stop working). Its one hand-written list is the four clinic-owned tables deliberately left unfiltered
  (`User`, `Clinic`, `AuditEntry`, `Notification`), asserted **equal** to the model in both directions.
  The coverage test derives its candidates from the *criterion* — « reads a filtered entity with no HTTP context »
  — by reflecting over the API assembly for background jobs, `IHostedService`s and `Maintenance/*Command`s plus a
  source scan for `CreateScope()`; reading it off « is it a job? » produced a wrong list in both directions during
  planning. It carries its own red-proof (`The_Guard_Rejects_A_Job_Whose_Declaration_Is_Removed`) rather than
  asking a reviewer to delete a call by hand. ⚠️ **Its console-verb branch matched NOTHING until
  `clinic-subscription` Part F**: the candidate filter was `IsAbstract: false`, and a `static class` — which every
  verb in this product is — is abstract *and* sealed in metadata, so the nine `Maintenance/*Command` types were
  covered only incidentally by the `CreateScope()` scan. Found by a new guard writing the same filter and asserting
  a count, which came back **0**. Worth asserting a **non-zero candidate count** in any derived guard, so « found
  nothing » cannot read as « nothing was wrong ». ⚠️ Exclude compiler-generated **nested** types when reflecting over a
  namespace: in a Debug build an async state machine is a *class*, so `<FlagExpiringStock>d__3` arrives as a
  candidate whose source file does not exist.
- **`Domain/SubscriptionLedgerTests.cs`** + **`Features/Subscriptions/*`** + **`Common/ClinicCreationEntitlementTests.cs`**
  (`clinic-subscription` Part A). The ledger class is the highest-value one in the feature: every screen, the gate, the
  warning job and every vendor verb read the date it produces, so an off-by-one is wrong everywhere at once and
  visible nowhere in particular. It pins the two properties that carry the risk — **clock-freedom** (identical entries
  fold to the same date, and there is no clock to pass) and the **exclusive cursor**, via the two cases a single
  `anchor + duration` gets wrong in opposite directions: a trial-only ledger ends on `creationDay.AddDays(29)`
  (AC-1.1 — 30 days, not 31) and a **lapsed** cabinet's 12-month grant counts from its recorded day while EC-3's
  still-valid one adds twelve months to the old end **with no −1**. Plus cancelling a *middle* entry moving the date
  (the assertion an incremental `EndsOn += duration` cannot satisfy), `AddMonths` clamping, and open-ended collapse.
  ⚠️ **Every date is a fixed literal and there is no `DateTime.UtcNow` in the file** — a test that reads the clock
  agrees with a clock-dependent fold by construction, the same trap `ClinicClockTests` documents.
  `ClinicCreationEntitlementTests` is the derived source scan that catches a **third** clinic-construction door,
  scoped to Application + API because `new Clinic(` has ~19 matches of which 17 are test fixtures; it carries its own
  red-proof rather than asking a reviewer to delete a call by hand.
- **`Api/SubscriptionGateMiddlewareTests.cs`** + **`Api/SubscriptionExemptionCoverageTests.cs`**
  (`clinic-subscription` Part B). **Most of the first class asserts what the gate must NOT refuse**, and that is where
  its value is: it sits in front of every controller on the hosted deployment, so a wrong « refuse » verdict does not
  degrade a feature — it takes a working cabinet's ability to record anything at all, mid-consultation. Hence the
  over-refusal cases outnumbering the three refusals, and hence `RepositoryReads == 0` being asserted alongside
  « passed »: a read, a non-`/api` path and a non-enforcing deployment must not even *look the entitlement up*, which
  is what makes « an expired cabinet keeps all of its records » structural rather than an allow-list.
  ⚠️ Its dates are decades away (2020 / 2099) **because the gate reads the real clock** — unlike
  `SubscriptionStateReaderTests`, which takes today as a parameter and can pin the midnight boundary. A fixture near
  today would pass or fail depending on when the suite runs.
  ⚠️ **The load-bearing case is `The_Gate_Runs_After_Token_State_Enforcement_And_Before_The_Controllers`**, asserted
  against **`Program.cs`'s own source** on `AccountStateEnforcementTests`' precedent: registered one block earlier the
  gate answers 402 to a revoked token (401) and a pending forced password change (403), and the middleware is
  perfectly correct in isolation — only its *position* is wrong, so no behavioural test can see it.
  The coverage class derives FR-3's exempt set off the compiled controllers and asserts it equals the reviewed list in
  **both** directions; the second is the one that matters, since a silently un-exempted `change-password` locks an
  expired cabinet out of the action that unblocks it. ⚠️ **It classifies non-GET actions only, and says so**: the gate
  never inspects a read, so it *cannot* go red when the attribute is removed from a GET-only row — a limitation made
  executable by `The_Guard_Is_Deliberately_Blind_To_An_Exempted_Read`, beside the ordinary red-proof, so nobody reads
  a green run as covering those rows. An action declaring **no** HTTP method counts as a write (it answers every verb).
- **`Features/Subscriptions/GetSubscriptionQueryTests.cs`** + **`GetSubscriptionHistoryQueryTests.cs`** +
  **`Api/SubscriptionControllerTests.cs`** (`clinic-subscription` Part C). The fold's arithmetic belongs to
  `SubscriptionLedgerTests` and the state rule to `SubscriptionStateReaderTests`; what these three add is what neither
  can see. The single highest-value case is
  `GetSubscriptionQueryTests.A_Cabinet_On_Its_Free_Days_Reads_Essai_Gratuit`: it is the **only** assertion that fails if
  the handler stops reading the ledger, because every other field would still be right and every cabinet would simply
  read « Actif ». Its sibling is
  `GetSubscriptionHistoryQueryTests.Page_Two_Continues_Page_Ones_Periods_Rather_Than_Restarting_Them` — hand the fold a
  paged window and page 2's dates stay entirely plausible while describing periods the cabinet was never entitled to.
  ⚠️ **`GetSubscriptionQueryTests` anchors its fixtures on `ClinicClock.ClinicToday()`, which is the OPPOSITE of what
  `ClinicClockTests` and `SubscriptionGateMiddlewareTests` do**, and deliberately: the property under test is « which
  ledger entry covers *today* », so a fixture decades away has no covering entry at all and the case ceases to exist.
  The history class has no clock in it at all, because nothing in that read depends on today.
  `SubscriptionControllerTests` holds the two facts that live in *composition*: the **404 is answered before the
  mediator** (AC-7.1/7.2 is « byte for byte unchanged », not « unchanged plus two reads » — asserted as
  `Assert.Empty(mediator.Invocations)`, not merely as a status), and the AC-2.2 policy split (`AnyClinicRole` on the
  screen, `AdminOnly` on the history alone) with a **drift guard** carrying its own executed red-proof, so a later
  action cannot widen the secretary exception by omission.
- **`Features/Subscriptions/SubscriptionWarningTests.cs`** (`clinic-subscription` Part E). It runs the **real**
  `NotificationGenerator` over an in-memory `IStaffNotificationRepository` rather than asserting the job called a
  mock, because every AC here is about the **rows**: four of them, each with its own id so it badges the bell, none of
  them a fifth, all withdrawn on an extension. A mocked generator would prove a method was invoked and nothing about
  any of that. « Simulating days −8 → 0 » is done by running the same pass against a **moving** date, which the job's
  `WarnExpiringSubscriptions(DateTime clinicToday)` overload exists for.
  ⚠️ **The highest-value case is `The_Wording_Does_Not_Change_While_The_Threshold_Holds`**, and it is the one a row
  count cannot replace: keying the dedupe on the live countdown instead of the threshold compiles, produces plausible
  French, and writes a row **every day** of the countdown while restating the wording — proven by probe, which reddens
  four cases including that one and the four-rows headline.
  ⚠️ Its fake repository **throws** on every member the feature does not use, deliberately: a fake that quietly
  answers an unrelated read would let a wrong implementation pass by taking another path.
  ⚠️ `Every_Other_Category_Is_Still_Classified` is the **R-9 split-point guard** — `StaffNotificationRules`
  *throws* on an unclassified category, so omitting `SubscriptionExpiring => false` breaks **every** notification
  write in the product rather than only the new one. Proven red by removing that line.
  ⚠️ And `A_Grant_That_Moves_The_Threshold_Writes_A_New_Row_Rather_Than_Rewriting_The_Old_One` records the case that
  **looks like a bug and is not**: rewriting would carry the read markers of a warning already dismissed, so the
  escalation would land on a bell that had been cleared. It was written the other way round first and the failing run
  was the finding.
- **`Features/Subscriptions/{Grant,Cancel}SubscriptionPeriodCommandHandlerTests.cs`** +
  **`SetSubscriptionSuspensionCommandHandlerTests.cs`** + **`Common/SubscriptionReportServiceTests.cs`** +
  **`Api/SubscriptionVendorCommandReachabilityTests.cs`** (`clinic-subscription` Part F). They run the real
  commands over an **in-memory ledger** (`SubscriptionVendorHarness`) rather than asserting a mock was called,
  because every AC here is about what the ledger ends up holding: entries accumulating (AC-5.3), a cancelled row
  staying (AC-5.5), a date moving because a *middle* entry went (AC-5.4). ⚠️ **The highest-value cases are
  `Paying_Ten_Days_Early_Never_Costs_Days`** (EC-3 — it fails on `today + duration`, which is exactly how AC-5.2
  reads in prose, and the failure is silent money) and **`Two_Simultaneous_Grants_Both_Land_And_Both_Are_Kept`**
  (EC-5 — the entitlement carries an `xmin` token, so the natural implementation 409s the second writer, which the
  spec forbids in both halves). ⚠️ **The command fixtures anchor on `ClinicClock.ClinicToday()`**, the opposite of
  `SubscriptionGateMiddlewareTests`' decades-away dates, because the handler stamps the entry's recorded day from
  the real clock and that day *is* the fold's anchor; `SubscriptionReportServiceTests` uses fixed literals, since
  that service takes today as a parameter. ⚠️ And an **exact** date expectation on a cancelled entry
  (`endBefore.AddMonths(-12)`) is *not* the inverse of the fold when `AddMonths` clamps to a shorter month — it was
  written that way first and would have flaked; it is a range plus an independent-fold assertion now.
  The reachability class is the FR-6 guard, derived over the commands by reflection so a *fourth* is covered for
  free, with an executed red-proof.
- **`Api/MigrationLockTests.cs`** (`multi-tenant-cloud` US-6) — the startup advisory lock, and a worked example of asserting the two things a mistake would actually look like when the mechanism itself is out of reach (nothing here touches a database). Both statements must name the **same fixed** key — two instances naming different numbers serialise nothing, and the failure is invisible until two containers migrate at once — and the lock must be **session-level**, because `pg_advisory_xact_lock` releases at the first commit *inside* the migration, leaving the rest of it unprotected while looking correct. The third property is asserted against **`Program.cs`'s own source**: a lock the startup path forgot to wrap is exactly as broken as no lock, and nothing else in the build can see it.
- **`Api/AuthAttemptAccountTests.cs`** + the US-6 half of **`Api/RateLimitingTests.cs`** — the login limiter's re-key onto the submitted account. Most of the file is about the cases that must produce **nothing**: a non-JSON body, a truncated one, an oversized one, `auth/refresh` (no email at all). Any of those throwing would take the login endpoint off the air, which is strictly worse than the lockout the re-key exists to prevent. The two partition cases that matter are the ones a naive fix gets wrong: **the same account shares one bucket regardless of address** (a compound `account+address` key would hand one attacker a fresh budget per address) and **an account key can never collide with an address key** (an email is caller-supplied text).
- **`Api/HealthCheckTests.cs`** — the grading, which is the whole substance: storage down is **`Degraded`** (still 200) and the database is `Unhealthy` (503). Also that storage which cannot even be **resolved** degrades rather than 500s — where MinIO is unconfigured `AddInfrastructure` deliberately registers a factory that throws, so a constructor-injected `IFileStorage` would throw while the framework was *building* the check.
- **`Api/SecurityHeadersMiddlewareTests.cs`** — `Security:EnforceCsp` flips only the header **name**, report-only is the default in **every** profile, and a policy an upstream component already set is never overwritten (two CSP headers make the browser enforce their intersection). ⚠️ It installs a fake `IHttpResponseFeature` that keeps the `OnStarting` callbacks: `DefaultHttpContext.Response.StartAsync()` never invokes them — that is Kestrel's job — so without it the middleware looks like it writes no headers at all.
- **`Api/Maintenance/MaintenanceDatabaseTests.cs`** — the console verbs' gate is « is a connection string configured? » and **answers the same in all three profiles** (amendment M3). Its sibling cases in `{VerifySchema,ReconcileMoney}CommandTests` were rewritten here for the same reason and are worth reading as a pair: they used to assert the refusal named `CloudBrowser`, which was the defect rather than the contract.
- **`Features/Notifications/PushFanOutTests.cs`** + **`Api/PushDispatchJobTests.cs`** +
  **`Features/PushDevices/DeviceRegistrationTenantIsolationTests.cs`** (`mobile-native-shells` P6). The
  load-bearing case is `PushFanOutTests.The_Push_Label_Is_The_Feed_Rows_Own_Title`: the **real**
  `NotificationGenerator` runs inside the **real** decorator over mocked repositories, so the `StaffNotification`
  and the `PushDelivery` produced by one call are compared *with each other* — AC-47's « a fixed French label » and
  AC-45's « the audience equals the feed's » are asserted against the feed rather than a retyped table, because a
  constant in the test would be a second authority and the drift it allowed would be a lock screen saying something
  the app does not. ⚠️ `A_Reminder_Falling_In_Quiet_Hours_Is_Deferred_While_The_Feed_Row_Is_Not` exists because the
  obvious assertion hid a real distinction: the feed has **no** quiet-hours floor (an in-app row at 02:00 wakes
  nobody) while the push does. Its fixtures pin the hour (13:00 / 01:00 UTC) — a `UtcNow.AddDays(5)` fixture passes
  or fails depending on when the suite runs, which is how the first version failed. `PushDispatchJobTests` is mostly
  about the checks that run **at send time**, since a push has no request behind it: a rebound token, a deactivated
  account and a cancelled appointment are all things no request-time guard can still catch. The isolation class
  states the deliberate **asymmetry** — registration crosses clinics (the token is globally unique, so a scoped
  lookup makes a rebind a 500) while deregistration must not.
- **`Features/Platform/PlatformReadShapeTests.cs`** (`platform-console` Part 2) — the guard that *is* US-7. It reflects over
  every `IRequest` in `Features.Platform`, unwraps `Result<T>`, recurses into nested DTOs and collections, and asserts every
  property name at every depth is in `PlatformReadShape.AllowedLeafNames`. ⚠️ **Names, not types**: a type allow-list is
  satisfied by adding a field to a type already on it, which is exactly how a patient's name would arrive — as one more
  property on the row somebody was already editing, not as a new DTO. Asserted in **both** directions (an unused allowance is
  a pre-approved hole), with a non-vacuity test naming three field names it must have reached — reflection tests fail *open*,
  and a renamed namespace would leave this passing for ever while checking nothing — and a **red proof** that runs the real
  collector over a `SmuggledPatientRow` carrying `PatientName`, which is the plan's own « verify by trying it » step.
- **`Features/Platform/PlatformCounterPassTests.cs`** — mostly about the two AC-2.2 exclusions, because they are the only part
  that fails *silently*: a miscounted total is visible to anyone who looks twice, while a background job counted as cabinet
  activity makes an empty practice read as busy, and the vendor's response to that is to leave a churning cabinet alone. Also
  pins that active days are bucketed in the **clinic's** day (23:30 UTC is already tomorrow in Tunis) — every fixture is a
  fixed instant, for `ClinicClockTests`' reason.
- **`Features/Platform/PlatformPortfolioQueryTests.cs`** — every filter reaching the repository **verbatim** (the matching
  itself is SQL and out of this suite's reach), the sort fallback, `PageRequest` clamping, freshness as the **oldest**
  measurement on the page, « jamais mesuré » kept distinct from zero (EC-15), the subscription placeholder returning four
  nulls rather than a guessed « Actif », and — the load-bearing one — that a handler reached with **no declared cross-clinic
  scope throws** instead of reading zero rows and reporting success (EC-12).
- **`Features/Platform/PlatformAccessLedgerTests.cs`** (`platform-console` Part 3) — the detail read and the console's
  own access ledger. Its load-bearing case runs the **real** detail handler and the **real** journal handler over
  **one** ledger, so the row the write produced is compared with the row the read serves rather than with a
  hand-written expectation — a write-only ledger and a ledger nobody reads back look identical from outside.
  ⚠️ Its second is `Loading_The_List_Cannot_Write_A_Ledger_Row`, asserted on the **constructor**
  (`ClinicHubTenantScopeTests`' technique): AC-3.5 is a promise about something that does *not* happen, and « I ran
  the list and no row appeared » passes just as well when the ledger is broken for every caller. Also pins that an
  unattributable read **fails** rather than succeeding unrecorded, that a vanished cabinet is refused by **code**
  and records nothing, and that the trend's six buckets keep « pas encore mesuré » (`DaysMeasured == 0`) distinct
  from a measured zero — with the window derived from `ClinicClock` rather than hard-coded, deliberately, since
  « what is today in Tunis » is `ClinicClockTests`' business and a literal would flake for one hour of every day.
- **`Hubs/ClinicHubTenantScopeTests.cs`** — asserts on the hub's **constructor**, because the defect it guards
  against cannot be caught behaviourally: HTTP middleware does not run per hub invocation, so a hub method reading
  a clinic-filtered entity returns an **empty result and reports success**.
- **`Features/Patients/ClinicalRecordTenantIsolationTests.cs`** — the four PHI tables with no `ClinicId` column
  (`DentalRecord`, `PatientMedicalHistory`, `PatientFamilyHistory`, `ToothState`) that had no by-id isolation test.
  No filter is possible for them, so the per-handler DB check is their only layer and this is the only place it can
  be held; `features/fix-patient-file-tenant-isolation` exists because this class already leaked once.
- **`Infrastructure/Persistence/RecallQueryTranslationTests.cs`** — proves the bounded relance read is genuinely **in SQL** (AC-P4.41) by calling `ToQueryString()` on `PatientRepository.RecallCandidateQuery` and asserting `EXISTS`, `MAX(`, `IsArchived`, `RecallSnoozedUntil` and `PhoneNumber` all appear. Needed because every other test mocks the repository and so cannot distinguish "pushed to SQL" from "filtered in memory", and because an untranslatable LINQ expression fails at **runtime on the request**, not at build. It opens no connection (Npgsql is configured only because SQL generation is provider-specific) — so it does not break the no-database rule below. The production method is shared rather than copied, so the test cannot drift into a parallel implementation.
- **`Api/TreatmentPlansControllerAuthorizationTests.cs`** — pins `CancelPlan` to `AdminOrDoctor` (altering a numbered financial document) and every other action to *no* method-level policy. Carries a **drift guard** (`Every_Action_Is_Classified_By_This_Test`) that fails when a new action is added without deciding its policy — deliberate, so slice B's `amend`/`revise-installments`/`items/order` cannot land unclassified.
- **`Features/Common/ConcurrencyConflictTests.cs`** — the optimistic-concurrency contract. Reflection-based where it can be, so a new entity or DTO is covered without editing the test: every `Entity<>` carries the token, the six round-tripped DTOs and their update commands expose it, a `ConflictException` **escapes** the handler catch-alls rather than being flattened, and the handler actually calls `SetExpectedVersion` (without which the whole feature is inert while looking present).
- **`Features/Dashboard/DashboardPeriodTests.cs`** — the highest-value class in `dashboard-insights`, because every comparable figure is measured against the window this type derives: a boundary bug there silently corrupts eight KPIs at once while the rest of the suite stays green. It pins the end-of-month `AddMonths` clamp (on 31 March the previous period is all of March→February, not a one-day sliver), the leap February, the year boundary, the Monday-based week for every weekday including Sunday, that every bound is explicitly `Utc`, that the bounds are *clinic-local* days (a UTC+1 month starts at 23:00 the previous day), and — the load-bearing one — that the current and previous windows are **exactly one tick apart**: adjacent with no overlap and no gap, which is what stops a midnight payment being counted twice.
- **`Features/Invoices/CreditNoteReadTests.cs`** — avoirs are readable, and « Total encaissé » nets them in **both** branches of the revenue read (the no-period branch is the one `/factures` actually loads).
- **`Features/Patients/PatientContactOptionalTests.cs`** — contact is optional, the tri-state clears, no sentinel is written, and one phone-less patient no longer 500s the patient list.
- **`Features/Billing/MoneyReadConsistencyTests.cs`** — « Solde patient », « Créances » and the dashboard must report the same outstanding figure for one shared fixture. Its repository mocks intentionally reimplement `TreatmentPlanRepository`/`InvoiceRepository`'s SQL filters, so the test targets the *handlers* feeding `Domain/Services/PlanBillingRules` the same rule. Paired with `Domain/PlanBillingRulesTests.cs` (the rule itself). **`dashboard-insights` extended it to a fourth read**: the dashboard's « Encaissé / Dépenses / Net » must equal la caisse's `cashIn`/`cashOut`/`net` over the same window from the same fixture (with a non-zero avoir in play, since the refund is the term the old dashboard KPI omitted); the dashboard's own three figures must add up; and the *previous* window must be read with its own bounds — without that last one every money delta would compare a figure against itself, inert while looking present. The dashboard side now goes through `DashboardMoneyReader` rather than the deleted `GetDashboardStatsQueryHandler`, and the old `[AC-12a]` billed-plan assertion was carried across, not dropped.
- **`Common/ClinicClockTests.cs`** — the clinic wall clock, and the class that can actually fail on §§ 4.1/4.2. **Every case uses a fixed instant**, which is the whole point: § 4.2 is « the invoice number takes its year from `UtcNow.Year` », and a test asserting against a freshly-read `DateTime.UtcNow.Year` evaluates the same expression the bug does — it agrees with the defect by construction and additionally flakes for one hour every New Year (§ 1 flagged exactly that in `IssueInvoiceCommandHandlerTests`; AC-P6.9). At 23:30 UTC on 31 December the clinic is already in the next year, and nothing about when the suite runs changes that. Also pins that a local day starts an hour *before* UTC midnight, and that `LastTickOfLocalDayUtc` is one tick inside the day rather than the next midnight (finding #20).
- **`Features/Invoices/IssueInvoiceCommandHandlerTests.cs`** — owns the **sequence** and the collision retry, not the year: it captures the year the handler asked the repository for (`_yearAskedFor`) instead of recomputing it. Read it together with `ClinicClockTests`, which owns the year.
- **`Features/Invoices/InvoiceAppointmentLinkTests.cs`** — the invoice↔visit link (§ 6.8). Covers both directions because the finding was a *write-only* column: `Invoice.AppointmentId` was accepted by the command, returned by the DTO and mapped by EF while nothing ever set it — and a column nobody writes is a column nobody validates, so the create path is tested for clinic **and** patient agreement alongside the read. On the read side: a cancelled invoice does not bill the visit, an issued one beats a stray draft, an empty page reads nothing, and the list resolves all rows in **one** batched call.
- **`Features/CnamNomenclature/ReimbursementEstimatesQueryTests.cs`** — the batch estimate (§ 5.10). The load-bearing case is `The_Batch_Agrees_With_The_Single_Act_Query`: two endpoints over one calculator, and if they ever disagree the editor's live figure and the BS1's computed one are two numbers for the same act — the § 5.10 defect relocated. Also pins index alignment when a middle row is not estimable, `null` rather than `0` for an unknown lettre clé (« — » and « 0,000 DT » are different claims), and that a per-item care date beats the bulletin's (a bulletin's acts can straddle a birthday, so it genuinely has two rates).
- **`Features/Billing/CaisseLedgerTests.cs`** — the « extrait de caisse ». Its load-bearing case is
  `The_Movements_Sum_To_The_Caisse_Totals`: over one fixture with all four movement kinds, a voided row in each
  payment ledger and non-round figures, `Σ In − Σ refunds − Σ expenses` must equal the summary's `CashIn`/`Refunds`/
  `CashOut`/`Net`, and the **last running balance must equal Net**. That assertion is only writable because the
  statement is a *read* over the rows the totals sum — a `CashMovement` table would have made it unfalsifiable,
  which is the whole argument for the design. Also pins oldest-first order, stability across two reads of one window
  (a statement whose rows shuffle looks like the data changed), that a voided row is listed with motif + actor and
  leaves the balance untouched, and that the billed-plan de-dup reaches the installment read.
- **`Features/Invoices/InvoiceFromDentalRecordTests.cs`** — billing a fiche de soins. Most of the file is about
  *when* a refusal happens rather than whether: issuing consumes a gapless number, so a bad amount, an unknown
  method, a future date or an over-payment must all be caught **before** the transaction opens —
  `AssertNothingWasIssued()` checks no invoice was added, no sequence was read and no transaction was begun. Also
  pins the one-transaction chain, rollback on a failed save, that the payment date defaults to the **session's**
  date and not today, and (as pure tests over `DentalRecordInvoiceLines`) the per-tooth pricing rule that used to
  live in the browser. `SessionTtc = 331m` is spelled out because a new `Clinic` enables the 1,000 DT timbre fiscal
  by default — a fixture quietly assuming 330 would have read as a pricing bug.
- **`Common/Csv/CsvTableTests.cs`** (`adoption-qa-l` L5) — the CSV writer, and the clearest example in the suite of a
  test earning its place immediately: it caught that **`CsvTable` emitted no BOM**. `new UTF8Encoding(true)` only
  changes what `GetPreamble()` returns — `GetBytes` never emits it — so every export was a BOM-less UTF-8 file that
  Excel on Windows reads in the system codepage, turning « Béchir » into « BÃ©chir ». The file is valid UTF-8 and
  opens correctly in everything that is *not* Excel, which is exactly why nothing else would have found it. Also
  pins the `;` delimiter (Excel's list separator in fr-TN), the RFC 4180 quoting including **leading/trailing
  whitespace** (a spreadsheet silently trims it, so a phone number would round-trip differently), money as three
  decimals with a comma and **no thousands separator** (a space makes a spreadsheet treat the cell as text and
  refuse to sum the column — the whole reason an accountant asked for the file), and that an *instant* is exported
  in the clinic's day while a *calendar day* is not converted at all.
- **`Infrastructure/Persistence/*SeedTests.cs`** — CNAM + medication + **DCH dental-act** catalog seed integrity.
  `DentalActCatalogSeedTests` (`adoption-qa-k`) carries the one that matters most: **`The_Two_Catalogues_Are_Disjoint`**
  pins *why* K1 existed — `CnamCatalogSeed`'s `CodeActe` values are 26 internal mnemonics (`DETART`, `OBT-2F`…)
  while the real nomenclature is the 100 `DCH…` codes here, and the BS1 picker was reading the former, so every
  bulletin was refused at the caisse on the code column. It asserts the sets do not intersect *and* that no
  mnemonic looks like a DCH code, so "unifying" the two catalogues has to be a deliberate edit of this test rather
  than something that quietly makes the two reads interchangeable again. It also pins K11 (no Prothèse act requires
  an accord préalable) and that the families the research **could not verify** are deliberately left flagged —
  inventing that list is the failure mode the spec names. `CnamCatalogSeedTests` gained the K10 convention values
  (`Cd 30,000` / `Cds 45,000` / `D 3,000`, derived from `Domain/Services/CnamConventionTariffs` rather than retyped)
  and `SupersededLetterValue`, the third term of the startup correction's predicate.
- **`Features/Documents/BulletinMandatoryFieldsTests.cs`** (`adoption-qa-k` K2/K7) — the bulletin write gate. Its
  load-bearing case is `Every_Regime_And_Lien_Value_Is_Accepted` plus the two near-miss theories: the régime and
  lien are French strings the renderer matches with `==`, so « Convention bilaterale » without its accent printed
  an **empty** régime box while every layer reported success. Nothing else in the suite can fail on that — it is a
  silent no-op, not an exception.
- **`Features/Documents/CnamClosedSetContractTests.cs`** (`adoption-qa-k` K2) — the browser's copy of those closed
  sets. Parses `web/lib/cnam.ts` via `[CallerFilePath]` (same reason as `RealtimeResourceResolverTests`: the suite
  is routinely built to a scratch `OutDir` outside the repo) and asserts its arrays **equal** `CnamInfo`'s, ordered,
  plus that the digit-cell count is one number and not two. ⚠️ Its shape guard strips `//` and `/* */` comments
  before scanning for stray literals — without that it matches the module's own prose (which quotes words like
  "normalise") and a guard that fires on its own documentation gets deleted rather than fixed.
- **`Api/MedicalDocumentPdfErrorTests.cs`** (`adoption-qa-k` K9) — the PDF-download failure path. The canonical
  `{ error }` shape is `ApiControllerBaseTests`' business; this covers only *which* exception is surfaced verbatim
  (`InvalidOperationException`, the type the three fail-fast French operator messages use) and — the case that
  matters as much — that any other exception's message **does not leak** a path or a connection string.
- **`Infrastructure/Services/`** `QrCodeGeneratorTests` (the LAN trust page's QR); reminders: `ReminderChannelSenderTests`/`ReminderScheduler`/`ReminderSettingsProvider`/`ReminderPhone`/`ReminderSchedule`; plus `CertificateProvisionerTests`, `PgDumpBackupServiceTests`, `InternetProbeTests`, `CnamBs1BulletinRendererTests`, document renderers (`Certificat`/`Liaison`/`Generic`/`PractitionerRenderSnapshot`).

## Gotchas

- **`Features/Patients/DentalRecordPostVisitCompletionTests.cs.deferred`** — the `.deferred` extension deliberately excludes it from compilation (parked, not deleted). Don't rename it back without checking why it was parked.
- **Running the suite on this machine — Smart App Control blocks freshly-built DLLs, and it is INTERMITTENT rather
  than location-fixed.** SAC is enforcing (`HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy` →
  `VerifiedAndReputablePolicyState = 1`) and refuses a new assembly with `0x800711C7`. ⚠️ **The « build into the repo
  tree and it loads fine » rule below is not reliable** — `clinic-subscription` Part A saw three successful runs from
  `%TEMP%\clinic-testrun\`, then the *same command* blocked, then the in-repo `api/.testrun/` blocked too, then
  `%TEMP%` worked again. **Retrying is what works**; treat a block as transient and do not go rewriting the run
  strategy around it (multi-tenant-cloud Part F already lost a round to that). Both output locations remain worth
  trying:
  ```bash
  dotnet build api/ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj -p:OutDir=<repo>/api/.testrun/
  dotnet vstest api/.testrun/ClinicManagement.UnitTests.dll            # add --TestCaseFilter:"FullyQualifiedName~X"
  ```
  ⚠️ **In PowerShell, never end an `OutDir`/`BaseOutputPath` argument with a backslash inside double quotes**:
  `-p:OutDir="…\.testrun\"` has the trailing `\"` **escape the quote**, so the argument is mangled, MSBuild silently
  builds to the default `bin/` and reports success — and `vstest` then runs the *stale* assembly and says « No test
  matches the given testcase filter », which reads as a filter problem and is not one. Put the path in a variable
  with no trailing separator.
  ⚠️ `dotnet test` **in place** is a separate, unrelated failure: when the user's `ClinicManagement.API` is running it holds `api/ClinicManagement.API/bin/Debug`, so the build dies on `MSB3021 — the file is locked by ClinicManagement.API (PID …)`. That is not SAC; read the error. A scratch `OutDir`/`BaseOutputPath` sidesteps the lock, which is why the two problems used to look like one. `multi-tenant-cloud` Part F spent a round on a throwaway reflection-based test runner before finding the location rule — do not repeat that; try the in-repo `OutDir` first.
- **Nothing here touches a database — so migrations are outside this suite's reach entirely.** An index can be missing, an exclusion constraint can be non-partial, a data backfill can cover zero rows, and a model change can have no applied migration at all, while every test in this project passes. That class of change is gated by the **`verify-schema` console verb** instead (`Application/Common/Maintenance/SchemaVerificationService` + `Infrastructure/Persistence/SchemaVerificationReader`), run before and after a migration batch and diffed. `SchemaVerificationServiceTests` covers the assertions against a **mocked reader** — which is why the reader seam exists at all. Do **not** add a database-touching test here to cover a migration; extend `verify-schema` and its service tests.
- **A handler test failing on `Assert.True(result.IsSuccess)` is almost always a fixture that has not kept up with
  the handler's dependencies — not a behaviour change.** When a handler grows a read that returns a **collection**
  (`GetByIdsAsync`, `GetDistinctCategoriesAsync`, `GetTreatmentPlanLinksAsync`, …), Moq's default for an unstubbed
  one is **null**; the handler dereferences it, and this codebase's `catch → Result.Failure` convention converts the
  `NullReferenceException` into a French business error. So the test fails on the *success* assertion and the
  message points nowhere near the missing stub. Check what the handler calls before theorising about behaviour —
  six of the 24 failures cleared in `multi-tenant-cloud` Part B's session were exactly this, and all six had been
  mis-diagnosed as filter drift on first read.
- **When a filter moves from a handler into SQL, its handler tests become vacuous rather than wrong — rewrite them,
  don't delete them.** A mocked repository applies no predicate, so « hand it the whole catalogue and assert the
  handler narrows it » silently tests a capability the handler has correctly lost. What is still worth holding is
  that **every argument reaches the repository verbatim** (including *untrimmed* — normalisation belongs to
  `SearchTerm` inside the repository): a silently dropped `category` or a term the handler "helpfully" trims is a
  real defect nothing else in this project can see. See `GetCnamNomenclatureQueryHandlerTests` /
  `GetMedicationsQueryHandlerTests` for the shape, and say out loud in the class docstring that the matching itself
  is SQL and therefore out of this suite's reach.
- **A failing test here has three times been a stale fixture, not a defect.** `data-and-money-integrity` inherited
  an "8-failure baseline" that turned out to be exactly that, in all three cases with the production code correct
  and the test drifted behind it: `ReminderSchedulerTests` stubbed `ResolveEnabledChannelsAsync` while the
  scheduler reads the full `ResolveAsync`; `DoctorCachetTests` uploaded three arbitrary bytes after the handler
  grew a **magic-byte** check (so the guard itself had no coverage); `DocumentTypeAndFilenameTests` left
  `ICurrentClinicResolver` unconfigured after the tenant guard moved ahead of the patient lookup (so it passed
  its main assertion for the wrong reason). Diagnose before assuming environmental.
