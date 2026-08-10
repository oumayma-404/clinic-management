# Implementation Progress — Abonnement du cabinet

**Feature:** `features/clinic-subscription/`
**Story:** [Story 1 — Abonnement du cabinet](./story-1-full-clinic-subscription.md) (`Layer: Full`, seven parts)
**Branch:** `feature/windows-desktop-app` (user decision — see *Session decisions*)

## Status Tracker

| Story | Status |
|---|---|
| 1 — Abonnement du cabinet | in-progress (Parts A + B + C + D + E done) |

### Parts inside Story 1

| Part | Focus | Status |
|---|---|---|
| A | Every cabinet has an entitlement, at every door and for all of history | **done** (Checkpoint A green) |
| B | An expired cabinet keeps its records and loses only recording | **done** (Checkpoint B green) |
| C | The cabinet can see where it stands and how to pay | **done** (Checkpoint C green; eye pass owed) |
| D | The banner, the refusal toast, and the live re-read | **done** (Checkpoint D green; eye pass owed) |
| E | The cabinet is warned before it stops being able to work (⚠️ atomic) | **done** (Checkpoint E green; the operator's simulated-days walk is owed) |
| F | The vendor unlocks a cabinet that has paid | not-started |
| G | Background work parks rather than sends or vanishes (⚠️ atomic) | not-started |

## Session decisions

**Session 2 — scope: Part B only.** Requested explicitly (`/implement-story clinic-subscription part B`). Same branch,
same staging discipline. Part B is `api/`-only, so no `web/` file is touched and the frontend gate is genuinely not
applicable (verified by `git status`, not assumed).

**Session 1 — scope: Part A only.** Asked and answered at session start. Part A is the foundation every other part reads and
carries the two highest risks (R-2 hand-written migration + snapshot, R-6 the exclusive-cursor fold). The plan's own
R-1 names part boundaries as the split points.

**Branch: stay on `feature/windows-desktop-app`.** Asked and answered at session start; a
`feature/clinic-subscription` branch was recommended and declined. Subscription files are staged by explicit path
only, so the branch's in-flight work is never swallowed.

## Working tree note (start of session)

The branch arrived carrying **another author's uncommitted branding/theme work**, unrelated to this story. It is
left untouched and unstaged, and **excluded from every commit of this story**:

```
desktop/ClinicManagement.DesktopShell/{App.xaml, Assets/app.ico, WindowTheme.cs}
mobile/android/app/src/main/res/values/colors.xml
mobile/ios/ClinicShell/Assets.xcassets/AppIcon.appiconset/AppIcon-1024.png
web/app/{globals.css, layout.tsx, manifest.ts}
web/branding/icon.svg
web/components/{appointment-calendar.tsx, ui/table.tsx}
web/components/dashboard/{collected-trend-chart.tsx, hero-kpi.tsx}
web/lib/zones.ts
web/public/*.{png,svg}
web/scripts/generate-icons.mjs
```

**Resolved during the session:** that author committed the lot as **`b79a4f4 feat(theme): replace the teal palette
with « bleu céruléen »`**, so those files are no longer dirty and Part A's commit sits cleanly on top of it with
**zero file overlap** (Part A touches only `api/` and `features/`).

⚠️ **Two of those files are ones later parts need to edit** — `web/app/layout.tsx` (Part D's
`<SubscriptionProvider>`) and `web/lib/zones.ts` (Part C's `ROUTE_ZONES` row). Both now carry the new palette, so
C and D must **re-read them** rather than working from anything cached from before `b79a4f4`.

## Entry criteria

| Criterion | Result |
|---|---|
| `plan.md` APPROVED · Challenged: Yes | ✅ |
| `spec.md` APPROVED · Challenged: Yes | ✅ |
| Unit suite green **before** any change | ✅ `Failed: 0, Passed: 2203` (built to `%TEMP%\clinic-testrun\`) |
| Backend warning baseline recorded | ✅ **12 pre-existing warnings**, 0 in files this story touches — CS8604 ×2, CS8981 ×2 (`addclinics` migration), CS8602 ×6, CS8600 ×2, CS0618 ×1 |
| Frontend gate clean before any change | not run — Part A touches no `web/` file |
| `verify-schema` before-run saved | see *Part A gates* |
| `git diff HEAD --numstat` reviewed | ✅ — see *Working tree note* |

## Part A — steps

| # | Step | Status |
|---|---|---|
| A1 | The 16th deployment capability, `RequiresSubscription` | **done** (+ its `DeploymentProfileTests` matrix row and `hostedOnlyCapabilities` entry) |
| A2 | Domain: two aggregate roots, five enums, the fold, the repository interface | **done** — Domain builds, 0 errors, 0 new warnings |
| A3 | EF configurations, two `DbSet`s, two `HasQueryFilter` lines, repository impl | **done** (unverified — Infrastructure will not build, see the blocker) |
| A5 | `ISubscriptionPolicy` + `ISubscriptionPricing` + registrations | **done** — *corrected in session 2:* the `Subscription` appsettings section is **present** (all six keys + their comments), so the « still outstanding » note here was stale |
| A4 | `SubscriptionProvisioning` at both construction doors (3 helper callers) | **done** — Application half builds, 0 errors; the `provision-clinic` caller is unverified |
| A6 | The migration, its `.Designer.cs` and the model snapshot | **done** — scaffolded, hand-corrected, **applied**, before/after diffed |
| A7 | The three `verify-schema` checks | **done** — green against the real database |
| A8 | The five test classes | **done** — 93 new tests, all green |

*(A5 was done before A4 because A4's signature change needs the policy seam to exist.)*

**Verified so far, with the build that was available:**

| Build | Result |
|---|---|
| `ClinicManagement.Domain` (`--no-incremental`) | 0 errors · 42 warnings, **all pre-existing `CS8618`**, none in a file this story touches |
| `ClinicManagement.Application` | 0 errors · 1 warning (the pre-existing `CS8604` in `UpdateDentalRecordCommand.cs`) |
| `ClinicManagement.Infrastructure` | **3 × CS7036** — the blocker above, not this story |

⚠️ **The recorded warning baseline of 12 was an incremental-build mirage.** The entry-criteria `dotnet test` run
skipped `CoreCompile` for already-built projects, so their warnings were never re-emitted. A forced full compile of
`Domain` alone surfaces **42**. The gate for this story is therefore « no warning points at a file Part A added or
edited », checked against a `--no-incremental` solution build once the blocker clears — not « the total stays 12 ».

### Defects this part's own tests and gates caught

1. **`dotnet ef` scaffolded an `xmin` column into both `CreateTable` blocks.** PostgreSQL refuses it —
   `column name "xmin" conflicts with a system column name` — which is the same rejection that makes
   `AddConcurrencyToken`'s `Up()` deliberately empty. EF maps `Entity<T>.Version` onto the *system* column, so the
   differ emits it as a real one. Removed by hand from both tables; the migration then applied cleanly and every
   row still gets its concurrency token.
2. **`IConfiguration.GetValue<int?>` throws on an unparseable value.** `SubscriptionPolicy.TrialDays` is read while
   a cabinet is being provisioned, so an operator typo would have aborted clinic creation with a binder exception
   instead of falling back — « a mistyped setting must refuse nothing, never everything », the rule
   `ClientVersionMiddleware` already follows. Found by `The_Trial_Length_Is_Operator_Configuration_With_A_Guarded_Fallback`
   failing on first run; fixed in the production code, not the test. `SubscriptionPricing` had the same shape and
   was hardened with it — plus `CultureInfo.InvariantCulture`, since on an fr-TN host `"120.5"` would otherwise
   parse as 1205.
3. **A test reaching a private setter by reflection.** `PropertyInfo.SetValue` on `IsSuspended` would have thrown
   `Property set method not found`. Fixed by giving `ClinicSubscription` the real `Suspend`/`Unsuspend` pair the
   suspension columns had shipped without — caught by reading, since Smart App Control was blocking the suite at
   that moment.
4. **`LocalClinicProvisioningTests` and `CreateClinicLocalSetupTests` construct the changed helper and handler
   directly**, so the signature change broke their compile. Anticipated from the call sites before the build could
   show it; both fixtures now stub the two new dependencies, and the provisioning harness additionally captures the
   staged rows and counts saves so a test can pin that the entitlement rides the clinic's own save (FR-4).

## Blocker (CLEARED) — `DeploymentProfile.cs` did not compile, and not from this story

**2026-08-10.** A concurrent session added a 16th constructor parameter `bool servesPlatformConsole` to
`api/ClinicManagement.Infrastructure/Deployment/DeploymentProfile.cs` — the start of
`features/platform-console/` — with **no backing property, no assignment, and no argument at any of the three
`For(kind)` call sites**. The solution therefore fails to build:

```
DeploymentProfile.cs(257,45): error CS7036  ← SelfHostedLan
DeploymentProfile.cs(282,49): error CS7036  ← HostedMultiTenant
DeploymentProfile.cs(306,44): error CS7036  ← CloudBrowser
    3 Error(s)
```

**Decision (user): wait for that session to finish.** Not reverted, not completed here — it is another author's
work, and this story's own header records that it *blocks* `features/platform-console/`, so that capability's three
per-kind values and its `DeploymentProfileTests` matrix row belong to that feature's own commit.

**Consequence for Part A:** every gate that needs Infrastructure, API or the test project is deferred — the unit
suite, the full solution build, `verify-schema`, and `dotnet ef`. `ClinicManagement.Application` references **Domain
alone**, so it still builds and is used to verify the Application half in the meantime. **Step A6 (the migration) is
held**: scaffolding or hand-checking it against the model requires a compiling model, and a snapshot written against
a broken build is exactly the R-2 hazard.

**Resolved.** That session moved its work to a worktree. It briefly landed a fully-wired `ServesPlatformConsole`
(property + assignment + three call sites) and then withdrew it, leaving for a few minutes three orphan
`servesPlatformConsole:` **arguments** naming a parameter that no longer existed — the inverse error. Both states
were transient; the file settled back to this story's own version and the solution builds. Nothing of theirs was
reverted by this story, and no `ServesPlatformConsole` capability remains in this tree, so
`DeploymentProfileTests`' derived matrix guard needs no row for it.

⚠️ **Worth knowing for whoever picks `platform-console` up:** its capability will need a `DeploymentProfileTests`
matrix row **and** an entry in that test's `hostedOnlyCapabilities` set (the R-2 truth-table test cannot express a
capability true of neither shipped kind) — exactly the two edits `RequiresSubscription` needed here.

## Deviations

### DEV-1: `verify-schema`'s ledger check folds in the service, not in SQL
**Date:** 2026-08-10
**Story:** 1, Part A, step 7
**Category:** Technical
**Original Plan:** *Files to Modify* lists `SchemaVerificationReader.cs` as gaining "three guarded
`ScalarOrNullAsync` queries", and `DataMigrationCounts` as gaining three `int?` fields.
**Actual Implementation:** Two of the three checks are `ScalarOrNullAsync` counts as planned
(`every-clinic-has-an-entitlement`, `subscription-grandfathered-entries`). The third,
`subscription-end-date-matches-ledger`, is **not** a SQL count: `ISchemaVerificationReader` gains a
`SubscriptionLedgerFacts` member projecting, per clinic, the stored `EndsOn` plus its ledger rows as the Domain
record `SubscriptionLedgerEntry`; `SchemaVerificationService` then calls the real `SubscriptionLedger.Fold` and
counts the clinics whose stored date differs.
**Justification:** The two halves of the plan contradict each other. R-6's mitigation states that
`SubscriptionLedger.Fold` is "the single implementation, used by the write path and by `verify-schema` alike", but
comparing stored `EndsOn` against the fold *in SQL* requires re-expressing the exclusive-cursor fold as a recursive
CTE — a second copy of exactly the arithmetic R-6 exists to prevent, in a language where no compiler checks it
against the first. The plan's own decision 3 spends a page on how easy that arithmetic is to get wrong.
**Impact:** One extra member on `ISchemaVerificationReader` beyond the plan's wording. The check stays unit-testable
against a mocked reader like every other one, and the fold keeps exactly one implementation. It reads the whole
ledger rather than aggregating in SQL — a few rows per clinic on a read-only operator verb.
**Approved:** Yes — asked and answered before any code was written.

### DEV-2: calendar-day columns are `timestamp with time zone`, not `date`
**Date:** 2026-08-10
**Story:** 1, Part A, step 3
**Category:** Technical
**Original Plan:** `ClinicSubscriptionConfiguration` — « `EndsOn` as `date` ».
**Actual Implementation:** `EndsOn`, `SubscriptionPeriod.RecordedOnClinicDay` and `ExplicitEndsOn` carry no
`HasColumnType` and map to `timestamp with time zone`, like every other calendar-day column already in this model.
**Justification:** The project rule disagrees with the plan here, and the project rule has already been settled by
four shipped columns: `Payment.ChequeDueDate`, `InstallmentPayment.ChequeDueDate`, `Installment.DueDate` and
`Payment.PaidOn` are all calendar days on `timestamp with time zone`, treated as days by the code with no
conversion at the boundary. `ApplicationDbContext` also installs a **global `DateTime` → UTC value converter** on
every property that has none, plus a `ConvertDateTimesToUtc()` pass on save; `date` would be the first column in
the product to meet that machinery, and no gate in this repo can see a date-type mistake — `verify-schema` diffs
indexes, FKs and *decimal* precision only. Choosing the shape four existing columns already use costs nothing:
`ClinicClock.ClinicToday()` returns `Kind = Unspecified` midnight, `DateTime` equality ignores `Kind`, so the fold's
values and the stored ones compare identically either way.
**Impact:** None observable at the API or in the fold. Reversible while nothing has shipped — one annotation plus a
fresh migration. Noted for review because it is a persisted-shape decision.
**Approved:** Taken as a project-rule call, not asked; stated here and in the session report.

## Auto-Approved Deviations

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| `SubscriptionLedgerEntry` carries `RecordedAtUtc` and `FoldWithSpans` orders internally | Trivial | Internal to one new type; the plan said `inRecordedOrder`. Ordering inside makes the fold total, so neither the repository read nor `verify-schema`'s raw projection carries an `ORDER BY` the answer silently depends on. |
| `FoldWithSpans` does not early-`return` on an open-ended entry | Trivial | Same observable `Fold` result as the plan's pseudocode; continuing the loop is what lets every entry — including ones recorded after an open-ended grant — still get a `PeriodSpan` for the history screen. |
| `ClinicSubscription.Plan` is nullable | Trivial | New entity, no external caller. A cabinet on its free days and a grandfathered one have chosen no forfait; a default would read as a commercial choice nobody made. It gates nothing either way. |
| `IClinicSubscriptionRepository` omits `GetClinicsWithoutSubscriptionAsync` / `GetForReportAsync`, and `GetEntriesAsync` takes no `PageRequest` | Trivial | Internal scope, no behaviour change: those two members have no caller before Part F (`verify-schema` reads over raw ADO), and every caller of the ledger needs **all** of it — a fold over a page is not a fold. |
| `OutboxBlockReason` plus the three nullable model properties land in Part A | Trivial | Required by the plan's own « one migration » decision: a column the model snapshot does not know about makes every later migration re-add it (R-2). Each property documents that its writer arrives with Part E / Part G. |
| `ClinicSubscription.Suspend`/`Unsuspend` land in Part A | Trivial | Internal to a new entity, no production caller yet (Part F's command is the caller). The suspension **columns** are already Part A's by the plan's one-migration decision, and the state reader's EC-11 rule is Part A's too — so without the mutators that rule could only be tested by reflecting into a private setter, which throws. |
| `SubscriptionStateReader.Read` takes an optional `isTrial` | Trivial | New type, no external contract. Trial-vs-Active is a *label* and changes nothing about writes or warnings, so the gate — which must stay one indexed row — passes nothing, while the « Abonnement » screen, which reads the ledger anyway, can say. Deciding it inside would have forced the ledger onto the hot path, against plan decision 2. |
| `SubscriptionPricing` parses with `CultureInfo.InvariantCulture` | Trivial | Not specified by the plan. A config file is not localised: on an fr-TN host the ambient culture reads `"120.5"` as 1205 — a tenfold price nobody typed. |

## Part A gates — Checkpoint A

| Gate | Result |
|---|---|
| `dotnet build api/ClinicManagement.sln --no-incremental` | ✅ **0 errors**, 55 warnings — **every one at a pre-existing file and line**, 0 in any file this part added or edited |
| Unit suite | ✅ **2296 passed, 0 failed** (baseline 2203 → **93 new**) |
| `SubscriptionLedgerTests` | ✅ 22 |
| `SubscriptionStateReaderTests` | ✅ 29 |
| `SubscriptionProvisioningTests` | ✅ 25 |
| `SubscriptionTenantIsolationTests` | ✅ 6 |
| `ClinicCreationEntitlementTests` | ✅ 3 — derived scan finds **exactly two** production doors, both staging an entitlement, with a red-proof |
| `SchemaVerificationServiceTests` | ✅ 49 (8 new, both directions per check) |
| `TenantScopeFilterTests` | ✅ 6, **unedited** — its derived check auto-enrolled both new tables, and `UnfilteredByDesign` needed no entry |
| `DeploymentProfileCoverageTests` | ✅ **unedited** |
| `ControllerAuthorizationCoverageTests` | ✅ **unedited** — Part A adds no endpoint |
| `MoneyReadConsistencyTests` | ✅ 16, **unchanged** — the vendor's revenue reaches no clinic money read (FR-2) |
| `DeploymentProfileTests` | ✅ 27 — extended with the `RequiresSubscription` row + `hostedOnlyCapabilities` entry, as the plan prescribes |
| `dotnet ef migrations has-pending-model-changes` | ✅ « No changes have been made to the model since the last migration » — the R-2 snapshot hazard, closed by EF itself |
| Frontend gate | not applicable — Part A touches no `web/` file (verified: `git status` shows no `web/` change from this story) |

### FR-9 before/after, against the real database

`dotnet run --project api/ClinicManagement.API -- verify-schema`, saved both sides.

| | Before | After |
|---|---|---|
| exit code | **2** (drift found) | **0** |
| the two indexes + two FKs | `[DRIFT] MISSING in the database` ×4 | `[ ok ] present` ×4 (`ClinicSubscriptions(ClinicId)` unique) |
| `every-clinic-has-an-entitlement` | *not applicable — the entitlement tables do not exist yet* | **every cabinet has an entitlement** |
| `subscription-end-date-matches-ledger` | *not applicable* | **4 entitlement(s), each ending exactly where its ledger folds to** |
| `subscription-grandfathered-entries` | *not applicable* | **4** |
| `SubscriptionPeriods.Amount` | — | `(18,3)` on both sides, from the convention with no annotation |
| verdict | — | **« Result: schema matches the model. »** |

⚠️ The *not applicable* lines are the point of the before-run: a pre-migration `0` would have claimed a backfill had
succeeded before it existed, which is the confusion the nullable-count convention exists to prevent.

**AC-6.4 confirmed directly in PostgreSQL** — `4 clinics = 4 entitlements = 4 grandfathered = 4 open-ended`, so the
grandfathered count equals the pre-deployment cabinet count and **R-5's failure mode (a backfill covering zero rows,
invisibly) did not occur**. Spot-checked a row: the French reason is recorded (AC-6.2), attributed to
`job|migration:AddClinicSubscriptions`, and `RecordedOnClinicDay` is `2026-08-10 00:00:00+00` — exactly clinic-local
midnight, so the UTC+1 arithmetic in the backfill is right.

### Environmental note — Smart App Control

SAC is **enforcing** (`VerifiedAndReputablePolicyState = 1`) and blocked freshly-built test assemblies with
`0x800711C7` mid-session, in **both** `%TEMP%` and the in-repo `api/.testrun/` the test guide prescribes — after
allowing three runs from the same `%TEMP%` path minutes earlier, and then allowing them again. So the block is
**intermittent in time, not fixed by location**, which refines what `ClinicManagement.UnitTests/CLAUDE.md` currently
says. Retrying is what worked; every figure above comes from a run that actually executed.

⚠️ Also cost a detour worth recording: `-p:OutDir="…\.testrun\"` in PowerShell — the trailing `\"` **escapes the
quote**, so the argument is mangled, MSBuild silently builds to the default `bin/`, and the stale assembly in
`.testrun` then reports « No test matches the given testcase filter ». It looks exactly like a filter problem. Use a
variable with no trailing separator. Two junk directories it created were removed.

---

# Part B — An expired cabinet keeps its records and loses only recording

**Working tree note (start of session 2).** Clean apart from the other author's untracked `features/platform-console/`,
left untouched and excluded. `git diff HEAD --numstat` reviewed: **every** Part B change is additive — `0` in the
deletion column of all twelve modified files — and the four derived guards were confirmed **untouched** by name.

## Part B — steps

| # | Step | Status |
|---|---|---|
| B1 | `AllowsWithoutSubscriptionAttribute` (mandatory `Reason`) + `SubscriptionRefusals` | **done** |
| B2 | `SubscriptionGateMiddleware` + its `Program.cs` registration | **done** |
| B3 | The attribute applied to FR-3's fixed set (11 controllers) | **done** |
| B4 | `SubscriptionGateMiddlewareTests` + derived `SubscriptionExemptionCoverageTests` | **done** — 35 new tests |

### What the exempt set actually came out as

**18 writes**, plus two GET-only documentation rows. `AuthController` carries the attribute **class-level** — one
reason for all seven of its non-GET actions (AC-4.7, EC-2), and six of those are `[AllowAnonymous]` so they arrive
with an `Unset` tenant scope and pass the gate anyway; **`change-password` is the only one of the group that genuinely
needs it**. The other eleven are per-action: the three compute-only POSTs (batch CNAM estimate, CSV import *preview*,
render-for-download), the six writes experienced as reading (mark-read, read-all, push register + deregister, default
file folders, the user's own dashboard layout), and `backup` + `users/{id}/status`.

⚠️ **Two rows of the plan's table could not be applied, and that is correct rather than deferred:**
- **`SubscriptionController` (both actions)** does not exist until Part C. Both are GETs, so AC-4.8 holds structurally
  the moment the controller lands and no attribute is *required*; Part C should still add it as documentation, matching
  `MetaController`'s two.
- **`/health`** is not a controller action and sits **outside `/api`** — the gate's path check already excludes it, so
  there is nothing to annotate. Stated here so a reader of the plan's table does not go looking.

## Deviations

**None.** The plan's Part B was implementable as written; the only judgment calls were the two rows above (both facts
about the codebase, not departures) and the extra ordering test below (an addition, not a change).

## Auto-Approved Deviations

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| `AuthController`'s exemption is class-level, not seven per-action copies | Trivial | Same effective set; the plan states one shared reason for the whole row, and one attribute can carry only one reason. The derived guard still enumerates all seven actions, so a *future* `AuthController` action inheriting the exemption fails it rather than passing silently. |
| The gate reuses `SubscriptionStateReader.Read` rather than comparing `EndsOn` itself | Trivial | Internal to one new file; that reader **is** the one FR-1 rule the plan names, and re-deriving « is this cabinet expired? » in the gate is the exact drift it exists to prevent. |
| `SubscriptionGateMiddlewareTests` gained a source-level ordering assertion the plan did not list | Trivial | Additive test only. It is the one property in this part that compiles, passes every behavioural case, and is still catastrophically wrong — plan Notes call it trap 3 of 3, and the validation step (« a revoked token still gets 401 ») is otherwise unverifiable without a live hosted deployment. Follows `AccountStateEnforcementTests`' existing precedent for asserting against `Program.cs`. |
| `IsWrite` treats an action declaring **no** HTTP method as a write | Trivial | Internal to the new guard. Such a route answers every verb, so reading it as a GET would exempt a POST nobody reviewed — the conservative direction. |
| The `MetaController` GETs carry the attribute; `SubscriptionRefusals.DateFormat` is a public constant | Trivial | Documentation and one shared format string. The plan requires the exempt set stated as *what*; the constant stops the gate and the tests from spelling `dd/MM/yyyy` twice. |

## Part B gates — Checkpoint B

| Gate | Result |
|---|---|
| `dotnet build api/ClinicManagement.sln --no-incremental` | ✅ **0 errors**, **55 warnings — the identical Checkpoint A baseline**, every one at a pre-existing file, **0 in any file Part B added or edited** |
| Warnings inside `ClinicManagement.UnitTests` itself | ✅ **none** (checked with a dedicated `--no-incremental` grep — the 13 reported by a bare test-project build all come from referenced projects) |
| Unit suite | ✅ **2331 passed, 0 failed** (baseline 2296 → **35 new**) |
| `SubscriptionGateMiddlewareTests` | ✅ 25 |
| `SubscriptionExemptionCoverageTests` | ✅ 10 |
| `ControllerAuthorizationCoverageTests` | ✅ green, **unedited** — Part B adds no endpoint and no `[AllowAnonymous]` |
| `TenantScopeFilterTests` · `DeploymentProfileCoverageTests` · `MoneyReadConsistencyTests` | ✅ green, **all three unedited** (confirmed by name against `git diff --name-only`, not assumed) |
| `verify-schema` | **not applicable — and the verb was confirmed to exist and run** (`Api/Maintenance/VerifySchemaCommand` + `SchemaVerificationService`, exercised by 49 passing tests). Part B adds no migration, no column and no index: `git diff` touches nothing under `Infrastructure/`. |
| Frontend gate | **not applicable — verified, not assumed**: `git status` shows no `web/` file changed by this part |

### The two red-proofs

Both run for real rather than asserted in prose, because a coverage guard that cannot fail is the failure mode this
feature's plan names three times.

1. **The exempt set, second direction.** Removed `[AllowsWithoutSubscription]` from `Users.SetStatus`, rebuilt, ran the
   class: `Every_Reviewed_Exempt_Write_Still_Carries_The_Attribute` **FAILED**, naming the endpoint —
   « Approved exempt write(s) no longer exempt … : Users.SetStatus ». Restored; green.
2. **The middleware ordering.** Inserted a duplicate gate registration *above* the `EnforcesTokenState` block:
   `The_Gate_Runs_After_Token_State_Enforcement_And_Before_The_Controllers` **FAILED** with its own message. Reverted;
   green. ⚠️ Worth knowing: it failed **without a rebuild**, because it reads `Program.cs` from disk — which is exactly
   what makes it able to see a defect the compiled middleware cannot express.

Plus the two executable proofs carried inside the coverage class itself (`The_Guard_Detects_A_Newly_Exempted_Write`,
and `The_Guard_Is_Deliberately_Blind_To_An_Exempted_Read`, which pins the stated GET blindness so nobody reads a green
run as covering the GET-only rows).

### Verification steps — what is proven and what is still owed

| Step | Result |
|---|---|
| Every read, CSV export and PDF download succeeds on an expired cabinet | ✅ **structurally**, and that is stronger than a walk: the gate never inspects a GET and the tests assert **zero repository reads** on one. The nine-list EC-9 walk stays a story-exit item |
| `POST /api/appointments` → 402 `subscription_required` + French sentence naming the date | ✅ asserted over POST/PUT/PATCH/DELETE, including the literal `15/01/2020` and « Abonnement » |
| `POST /api/ai/chat` → 402; the three compute-only POSTs → 200 | ✅ **structurally** (`The_AI_Chat_Is_Not_Exempt` + `The_Compute_Only_Posts_Are_Exempt`). The live HTTP walk needs a hosted profile with an expired cabinet — **operator step, owed** |
| Sign-in + a forced password change succeed on an expired cabinet (EC-2) | ✅ pinned in both directions by the derived guard |
| A revoked token still 401, `must_change_password` still 403 — not 402 | ✅ via the source-level ordering assertion + its red-proof. A live walk is **owed** with the operator step above |
| `SubscriptionExemptionCoverageTests` fails red when the attribute is removed | ✅ done for real (above) |
| `ControllerAuthorizationCoverageTests` still green with no edit | ✅ |

## Learnings

- **A behavioural test suite cannot see a middleware's position.** Part B's whole risk is one `UseMiddleware` line
  being four lines too early: the class is correct in isolation, every unit case passes, and the product is broken in a
  way that reads as an auth bug. The repo already had the answer — `AccountStateEnforcementTests` asserts against
  `Program.cs`'s own text for the identical reason — and reusing that precedent cost one test. Worth reaching for
  whenever a fix's correctness lives in *composition* rather than in a type.
- **« Not applicable » and « not implemented » look identical in a gate table.** Part A wrote `not applicable` for the
  frontend gate and Part B does the same for both that and `verify-schema` — so each was checked rather than assumed:
  `git status` for `web/`, and grepping for the verb's actual existence. The verb does exist and runs (49 tests), which
  is the thing three consecutive parts of another feature failed to confirm while writing the same two words.
- **Part A left one item recorded as outstanding that was in fact already done.** Step A5's note said « the
  `Subscription` appsettings section is still outstanding »; it is present in `appsettings.json` with all six keys and
  their explanatory comments, and `Infrastructure/CLAUDE.md` already documents them. Corrected below rather than
  re-done. A carried-forward gap is worth re-reading before acting on it.

---

# Part C — The cabinet can see where it stands and how to pay

**Working tree note (start of session 3).** Clean apart from the other author's untracked `features/platform-console/`,
left untouched and excluded. `git diff HEAD --numstat` reviewed at the end: **one** deletion across all five modified
files (the `ExcludedAreas` line, replaced by the same line plus `"Subscriptions"`); everything else is additive.
⚠️ Part A's warning about `web/app/layout.tsx` and `web/lib/zones.ts` applies: both were **re-read** from disk before
editing, not worked from anything cached from before `b79a4f4`. `layout.tsx` is untouched by this part (it is Part D's).

## Part C — steps

| # | Step | Status |
|---|---|---|
| C1 | `GetSubscriptionQuery` + `GetSubscriptionHistoryQuery`, the DTOs, `SubscriptionController` | **done** |
| C2 | `requiresSubscription` on `GET /api/auth/mode` + `AuthModeDto`, read `=== true` | **done** |
| C3 | `web/lib/api/subscription.ts`, `app/abonnement/page.tsx`, `subscription-history-table.tsx` | **done** |
| C4 | Nav — `/abonnement` in `buildConfigItems`; `ROUTE_ZONES` row | **done** |
| C5 | `Subscriptions` → `RealtimeResourceResolver.ExcludedAreas` | **done** |
| C6 | Tests (not a numbered plan step — the quality policy's) | **done** — 25 new tests |

## Session decisions

**Session 3 — scope: Part C only.** Requested explicitly (`/implement-story clinic-subscription part C`). Same branch,
same explicit-path staging. Part C is the one genuinely mixed part (two read endpoints plus the screen).

**Two questions asked and answered before any code was written** — both changed what was built:

1. **The price on the wire.** AC-2.1 requires the screen to show the price, but a cabinet on its free days and every
   grandfathered one has `Plan = null`, so the spec's single `priceMonthlyDt`/`priceAnnualDt` pair is null for exactly
   the readers deciding whether to pay. Answer: **add a `plans` array** carrying the published tariff (DEV-3).
2. **Nav gating vs. AC-7.1/7.2.** The plan says the `/abonnement` entry is unconditional, while AC-7.1/7.2 say a
   deployment that does not enforce subscriptions has no « Abonnement » screen — and the client-side flag that would
   gate the rail only gets its provider in Part D. Answer: **follow the plan** (unconditional) and have the page itself
   state « Cette installation ne fonctionne pas par abonnement » where the door is shut. Part D's provider then removes
   the rail row. Recorded as a known interim state below rather than as a closed AC.

## Deviations

### DEV-3: `SubscriptionDto` carries a `plans` array beyond the spec's wire shape
**Date:** 2026-08-10
**Story:** 1, Part C, step 1
**Category:** Scope
**Original Plan:** *Files to Create/Modify* — « `SubscriptionDto.cs`, `SubscriptionPeriodDto.cs` — the spec's two wire
shapes, **verbatim** ».
**Actual Implementation:** Both shapes are present field for field, **plus** `plans: [{ plan, label, priceMonthlyDt,
priceAnnualDt }]` — the deployment's published tariff, one row per forfait in enum order, unpublished figures left
`null`. `priceMonthlyDt`/`priceAnnualDt` keep their spec meaning: the **cabinet's own** forfait's price.
**Justification:** The two halves of the requirement contradict each other for the majority case. `Plan` is nullable by
Part A's own recorded decision (« a cabinet on its free days and every grandfathered one has chosen no forfait; a
default would read as a commercial choice nobody made »), so the spec's two price fields are null for every trial and
every grandfathered cabinet — i.e. AC-2.1's « the price » is absent on precisely the screen a cabinet opens while
deciding whether to pay, which is what US-2 exists for (« paying is never blocked on not knowing what to do »). The
three alternatives are worse: defaulting to the `Cabinet` tier quotes a five-practitioner clinic the single-dentist
price, deriving a plan from the ledger invents one, and leaving the section empty makes the payment instructions carry
AC-2.1 alone.
**Impact:** One added response field, additive — no existing consumer changes and the spec's fields keep their meaning.
Still per-deployment configuration (AC-2.4), so an unconfigured deployment sends an empty array and the screen says the
tariff is not published. `SubscriptionLabels` gains one method for the forfait's French name.
**Approved:** Yes — asked with the three alternatives before any code was written.

### DEV-4: the DTOs live in `Application/DTOs/`, not `Application/Common/Models/`
**Date:** 2026-08-10
**Story:** 1, Part C, step 1
**Category:** Technical
**Original Plan:** « `Application/Common/Models/SubscriptionDto.cs`, `SubscriptionPeriodDto.cs` ».
**Actual Implementation:** `Application/DTOs/SubscriptionDto.cs` and `Application/DTOs/SubscriptionPeriodDto.cs`.
**Justification:** The project rule disagrees with the plan and has already been settled by the whole layer:
`Application/DTOs/` holds every request/response record in the product (~40, including the three sibling page shapes
`AuditPageDto`, `ClinicUsersPageDto`, `ReceivablesPageDto`), while `Common/Models/` holds `Result`, the five PDF input
models and `ResolvedReminderSettings` — none of which is a wire shape. `Application/CLAUDE.md` states both roles
explicitly. A DTO in `Common/Models/` would be the first one there.
**Impact:** None observable. Same kind of call as Part A's DEV-2: a project-rule decision, stated rather than asked.
**Approved:** Taken as a project-rule call (skill step 6.7); recorded here and in the session report.

## Auto-Approved Deviations

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| `SubscriptionHistoryPageDto` carries the full page envelope, not the spec's `{ items, totalCount }` | Trivial | Strict superset; both spec fields present. It makes the DTO structurally a `PagedResponse<T>`, which is what lets the shared `DataTablePagination` consume it unchanged — the shape `AuditPageDto` and `ClinicUsersPageDto` already have. |
| `SubscriptionDto` carries `stateLabel`/`planLabel`, `SubscriptionPeriodDto` `kindLabel`/`methodLabel` | Trivial | Four closed enums decided server-side, on `AuditLabels`' and the caisse statement's precedent: a client-side map would be a second list to extend, and the one that forgets a new member renders a raw `SurMesure` to a dentist. The stable key travels beside every label. |
| `SubscriptionLabels` is a new file rather than labels inline in the queries | Trivial | Internal scope, one new type. Both queries need the same maps; Part F's report will be the third caller. |
| `GetSubscriptionQuery` reads the **ledger**, not just the entitlement row | Trivial | No API change; it is what `SubscriptionStateReader`'s own `isTrial` parameter was written for (« the screen reads the ledger anyway, so it can say »). The gate still reads one indexed row. |
| A missing entitlement row is a `Result.Failure` carrying `subscription_missing`, rendered **400** | Trivial | Internal to one new handler. Not 404 (reserved for « this deployment has no subscriptions »), and deliberately **not 409** — that status is taken by optimistic concurrency, and `client.ts` maps it to a sentence about a concurrent edit that would be actively wrong here. The client shows the server's own French sentence whatever the status. |
| `ROUTE_ZONES` files `/abonnement` under **`config`**, not `money` | Trivial | One row in an existing table. The money zone is the clinic's own till; this is what the practice pays its software vendor, and FR-2 keeps the two apart everywhere else. |
| `SubscriptionControllerTests` exists, and the plan named no test file for Part C | Trivial | Additive tests only. Two properties nothing else in the build can see: the **404 before the mediator** (AC-7.1/7.2 is « byte for byte unchanged », not « unchanged plus two reads ») and the AC-2.2 policy split, with a drift guard so a later action cannot widen the secretary exception by omission. |

## Part C gates — Checkpoint C

| Gate | Result |
|---|---|
| `dotnet build api/ClinicManagement.sln --no-incremental` | ✅ **0 errors**, **55 warnings — the identical Checkpoint A/B baseline**, and **zero** of them name a file this part added or edited (checked by grepping the full warning list for `Subscription`/`abonnement`) |
| `dotnet build` of `ClinicManagement.UnitTests` alone | ✅ 0 errors, **0 warnings** |
| Unit suite | ✅ **2356 passed, 0 failed** (baseline 2331 → **25 new**) |
| `GetSubscriptionQueryTests` | ✅ 11 |
| `GetSubscriptionHistoryQueryTests` | ✅ 8 |
| `SubscriptionControllerTests` | ✅ 6 |
| `RealtimeResourceResolverTests` | ✅ green, **unedited** — `Subscriptions` is excluded server-side and **no** frontend key was added; both new reads are `Queries`, so they emit nothing either way (the exclusion is for Part F's commands) |
| `SubscriptionExemptionCoverageTests` | ✅ green, **unedited** — both new actions are GETs, and the guard classifies non-GET actions only (its own stated blindness, pinned by `The_Guard_Is_Deliberately_Blind_To_An_Exempted_Read`). They carry `[AllowsWithoutSubscription]` as documentation, which Part B's note asked Part C to add |
| `ControllerAuthorizationCoverageTests` · `AdminSurfaceCoverageTests` | ✅ green, **both unedited** — the new controller adds no `[AllowAnonymous]` and no mutating verb |
| `TenantScopeFilterTests` · `DeploymentProfileCoverageTests` · `MoneyReadConsistencyTests` | ✅ green, **all three unedited** (confirmed by name against `git status`, not assumed) — the vendor's revenue still reaches no clinic money read (FR-2) |
| `verify-schema` | **not applicable — and the verb was re-confirmed to exist and run** (`Api/Maintenance/VerifySchemaCommand` + `SchemaVerificationService`, 49 passing tests). Part C adds no migration, column or index: `git status` shows nothing under `Infrastructure/` |
| `npx tsc --noEmit` | ✅ clean |
| `npm run check:responsive` | ✅ **15/15**, including `card-fallback` (the new history table has its card list) and `failed-read-as-empty` |
| `npm run build` | ✅ compiled; `/abonnement` present in the route table (7.92 kB) |
| Eye pass at 320 / 390 / 820 / 1180 / 1440 px | ⚠️ **not run — no browser automation on this machine.** `agent-browser` is not installed (`command -v` finds nothing) and neither the API nor the web server was running. See *The eye pass, and what stands in for it* |

### The one red-proof

`SubscriptionControllerTests.Every_Action_Is_Classified_By_This_Test` was proven to fail: a throwaway
`[HttpGet("probe")] Probe()` action was added to `SubscriptionController`, the class was run, and it went **red**
naming the endpoint — « New action(s) on SubscriptionController with no policy decision recorded here: Probe ».
Reverted, re-run green (6/6), and the file confirmed to contain no `Probe` afterwards.

The other new assertions are behavioural rather than derived, so a red-proof would only restate the test. The two
worth naming as the ones that can genuinely fail on a plausible wrong implementation:
`A_Cabinet_On_Its_Free_Days_Reads_Essai_Gratuit` (fails the moment the handler stops reading the ledger — every other
field stays right) and `Page_Two_Continues_Page_Ones_Periods_Rather_Than_Restarting_Them` (fails if the fold is ever
handed a paged window; the dates would stay entirely plausible).

### The eye pass, and what stands in for it

No browser was available, so the widths were **not** looked at and this is recorded as owed rather than done. What was
run in its place: the mechanical gate (15/15 — the only automated check that can see a layout defect at all) plus a
deliberate re-read of both new frontend files against `DEVICE-CONTRACT.md` § 1 and `.claude/rules/frontend-web.md`,
item by item:

| Rule | How it is held in `app/abonnement/page.tsx` + `subscription-history-table.tsx` |
|---|---|
| § 1 hinge choice | The screen's two columns hinge at **`lg:`**, not `md:`, because the spec asks for a single column at a readable measure on a tablet portrait (« do not stretch to two just because the width allows it ») and 820 px is already past `md:`. The history table uses **`CARDS_ONLY_LG`/`TABLE_ONLY_LG`** for the same reason: seven columns beside the 256 px rail leaves ~532 px, and every cell in `ui/table.tsx` is `whitespace-nowrap` |
| § 2 touch on the **pointer** | The two contact links carry `coarse:min-h-11` — changed from an unconditional `min-h-11` **during this re-read**, since § 2's rule is that the painted control may stay smaller on a mouse. The « Retour à l'agenda » CTA keeps an unconditional `min-h-11`, following `AccessDeniedCard`'s precedent for a screen's single control. Nothing sits in a tight row or stack, so no `.touch-target` overlay is used and the overhang trap does not arise |
| § 3 16 px fields | No text input on either surface |
| § 4 / § 5 dialogs & sheets | None — this part adds no dialog |
| § 6 a `<Table>` never ships alone | Two trees, real `<dl>` cards below the hinge (`card-fallback` passes). « Annulé » is in **words** as well as struck through, and the cancel motif is its own card field |
| § 7 `dvh` / bottom inset | Owned by `AppShell`; nothing here is `fixed` |
| § 8 11 px floor, no `text-[Npx]` | `text-sm`/`text-2xs` only (`type-scale` passes) |
| § 9 hover | The only hover is `hover-hover:hover:no-underline` on the two links — a decoration change, and gated anyway. No affordance is hover-revealed |
| § 10 ungated grids | `grid gap-6 lg:grid-cols-2`; the tariff row is `flex flex-wrap`; no popovers |
| § 11 overflow | The table's own container scrolls (`ui/table.tsx`); nothing centres inside a scroller |
| § 12 logical utilities | `text-end` and `gap-*` only; no `left-*`/`pl-*` written here |
| § 13 UX floor | Four distinct states — skeleton, empty (`EmptyState`), **failed** (`LoadFailureNotice` + « Réessayer », EC-13) and content — plus `role="status"` on both inline notices, `aria-labelledby` on the history section, `aria-hidden` on every decorative glyph, and no English string. No `.catch(() => [])` anywhere (`failed-read-as-empty` passes) |

### Verification steps — what is proven and what is still owed

| Step (story § *Verification Steps → Part C*) | Result |
|---|---|
| A secretary can open « Abonnement » and read state, date, price and payment instructions (AC-2.2, EC-10) | ✅ **structurally**: the class policy is `AnyClinicRole`, pinned by `The_Screen_Is_Open_To_Every_Clinic_Role`, and `/abonnement` is outside `buildConfigItems`' `isAdmin` branch **and** outside `SECRETARY_HIDDEN_HREFS`. The live walk needs a hosted profile — **operator step, owed** |
| A secretary cannot see the payment history; the non-admin path renders `AccessDeniedCard` with **no 403 toast storm** | ✅ `Only_The_Payment_History_Is_Admin_Only`, and the page's history effect is guarded on `isAdmin` so the request is never issued |
| An open-ended entitlement says so **in words**, not as a far-future date (AC-2.5) | ✅ `An_Open_Ended_Entitlement_Carries_No_Date_And_No_Countdown` (null on the wire) plus the screen's « Sans échéance — cet abonnement n'expire pas. » |
| A suspended cabinet reads « Suspendu », not « Expiré » (EC-11) | ✅ `A_Suspended_Cabinet_Reads_Suspendu_And_Carries_Its_Motif`, including with a **future** end date |
| A dropped network yields a retryable « Réessayer », never « aucun abonnement » (EC-13) | ✅ by construction: only an explicit **404** is read as absence; `ApiError(0)` and every other status take the `LoadFailureNotice` path. **Not** exercised against a real dropped connection — owed with the operator walk |
| Page 2 of a long history continues page 1's periods | ✅ `Page_Two_Continues_Page_Ones_Periods_Rather_Than_Restarting_Them`, asserted against the unpaged fold |
| `RealtimeResourceResolverTests` green with `Subscriptions` excluded and **no** frontend key added | ✅ green, unedited |
| Frontend gate clean; eye pass at the five widths | ✅ gate clean · ⚠️ **eye pass owed** (no browser here — see above) |

## Known interim state, deliberately

**On `SelfHostedLan` and `CloudBrowser` the rail now shows an « Abonnement » row** that opens a page saying « Cette
installation ne fonctionne pas par abonnement. Votre licence est permanente. » with a way back. That is the plan's own
Part C step 4 (« `/abonnement` unconditional in `buildConfigItems` ») plus the user's recorded decision above; the row
itself disappears in **Part D**, the part that introduces the client-side `requiresSubscription` provider the rail
would need. **AC-7.1/7.2 are therefore not tickable at Checkpoint C** — no banner, no warning and no *data* on those
profiles (the endpoints 404 before the mediator, asserted), but one self-explaining rail row. Stated here so a reader
of the AC list does not record it as closed.

## Learnings

- **« The spec's wire shape, verbatim » can be self-contradictory once an earlier part's own decision lands.** Part A
  made `ClinicSubscription.Plan` nullable on purpose, and that is what turned the spec's two price fields into « null
  for every trial cabinet » — AC-2.1 unmet for the majority case, by two decisions that are each right. Worth
  re-reading a wire shape against the *entities as built* rather than as specified before treating « verbatim » as an
  instruction.
- **The status code for « this should be impossible » is a real design choice, and 409 is already spoken for.** A
  missing entitlement row wanted a status meaning « our fault, not your request »; 409 would have been the semantic
  fit, and `client.ts` maps 409 to « Cet enregistrement a été modifié par quelqu'un d'autre pendant votre saisie » — a
  confidently wrong sentence. Checking the client's status map before choosing a status cost one grep.
- **A « not applicable » gate is worth re-confirming every part, not once per feature.** Part B's own learning said so;
  doing it again here took one grep, and it is the difference between « Part C added no migration » and « nobody has
  looked at `verify-schema` since Part A ».
- **An eye pass that cannot run is a gap to state, not one to paper over.** `agent-browser` is absent on this machine,
  so the widths are recorded as **owed**, with the mechanical gate and a rule-by-rule re-read named as what stands in
  for them — and that re-read found one real thing (an unconditional `min-h-11` that should have been `coarse:`),
  which is the argument for doing it rather than writing « responsive ✓ ».

---

# Part D — The banner, the refusal toast, and the live re-read

**Working tree note (start of session 4).** Clean apart from the other author's untracked
`features/platform-console/`, left untouched and excluded. `git status` reviewed before any edit and again before
staging; every file below is staged by explicit path.

## Session decisions

**Session 4 — scope: Part D only.** Requested explicitly (`/implement-story clinic-subscription part D`). Same
branch, same explicit-path staging. Part D is `web/`-only in the plan's own layer table; in practice it carries
**three backend files**, all from the AC-1.3 decision below.

**Two questions asked and answered before any code was written** — both changed what was built:

1. **Where the banner mounts.** The plan says `app/layout.tsx`. `AppShell` is `flex h-dvh`, so a sibling above it
   makes the document taller than the viewport: the page scrolls as a whole, the phone's bottom bar is pushed off,
   and the spec's « ≤ 15 % of a 380 px-tall landscape viewport » budget becomes unmeetable. Answer: **mount it
   inside `AppShell`** (DEV-5). The provider stays in `layout.tsx` as planned.
2. **How the trial length is stated.** AC-1.3's sentence names « 30 jours » while the duration is
   `Subscription:TrialDays`, and the plan's own closing note already records a landing page saying « 2 semaines ».
   Answer: **serve the configured number** rather than write a literal in two places (DEV-6).

## Part D — steps

| # | Step | Status |
|---|---|---|
| D1 | `client.ts` — three codes, the 402 French fallback, `onSubscriptionRequired`; `errors.ts` | **done** |
| D2 | `SubscriptionProvider` + FR-15's three triggers; mounted in `app/layout.tsx` | **done** |
| D3 | `SubscriptionBanner` — one line, ≤ 15 % budget, dismissible only while valid | **done** |
| D4 | Confirm every refused save leaves its form open with input intact (AC-4.6) | **done** — audited, no fix needed |
| D5 | `HIDDEN_PATHS += /signup`; AC-1.3's sentence in the wizard **and** the verification e-mail | **done** |
| D6 | *(not a plan step)* Close Part C's interim rail row — AC-7.1/7.2 | **done** |
| D7 | *(not a plan step — the quality policy's)* Tests | **done** — 8 new tests |

## Deviations

### DEV-5: the banner mounts in `AppShell`, not `app/layout.tsx`
**Date:** 2026-08-10
**Story:** 1, Part D, step 3
**Category:** Technical
**Original Plan:** *Files to Create/Modify* — « `web/app/layout.tsx` | modify | `<SubscriptionProvider>` inside
`<SessionProvider>`; `<SubscriptionBanner/>` ».
**Actual Implementation:** The **provider** is in `app/layout.tsx` exactly as planned. The **banner** is one line in
`components/app-shell.tsx`, a flex sibling of `<main>` above `<DashboardHeader/>`.
**Justification:** `AppShell` is `flex h-dvh`. A banner sibling above it in the layout adds its height *on top of*
a full dynamic viewport, so the document scrolls as a whole and `BottomNav` — a flex child of the shell — leaves
the screen. It also makes the spec's « ≤ ~15 % of a 380 px-tall landscape viewport » budget meaningless, since the
app below still claims 100 dvh. The alternative that keeps the plan's file list (layout owns `h-dvh`, `AppShell`
drops to `h-full`) changes the height model **every** page inherits and puts the six chrome-less pages inside a
fixed-height box they were not written for. As a flex sibling the banner costs no height maths at all and `<main>`
shrinks around it, which is exactly what `BottomNav` already does and documents.
**Impact:** One extra line in a shared component. A **structural** gain rather than a cost: `AppShell` is used by
exactly the 27 chrome-ful pages and by **none** of `/login`, `/setup`, `/join`, `/change-password`, `/signup`,
`/signup/verifier` (verified by scanning every `app/**/page.tsx`), so « the banner is absent on the auth pages »
holds by construction instead of by a path list somebody has to remember to extend. The `isChromeLessPath` guard is
kept inside the banner as a belt to that braces.
**Approved:** Yes — asked with both options and their trade-offs before any code was written.

### DEV-6: the trial length is served, not written into the copy twice
**Date:** 2026-08-10
**Story:** 1, Part D, step 5
**Category:** Scope
**Original Plan:** « AC-1.3's trial sentence in `setup-wizard.tsx` **and** in `SignUpClinicCommand`'s verification
e-mail body » — i.e. the spec's literal « 30 jours d'essai gratuit, sans carte bancaire » in two places.
**Actual Implementation:** `GET /api/auth/mode` gains **`trialDays`** (`ISubscriptionPolicy.TrialDays` where
subscriptions are enforced, `null` otherwise) and the wizard renders `{trialDays} jours d'essai gratuit, sans carte
bancaire.`; `SignUpClinicCommandHandler` takes `ISubscriptionPolicy` and composes the same sentence from
`TrialDays`. The literal survives **only** as `DEFAULT_TRIAL_DAYS` in the wizard, for an API too old to answer.
**Justification:** The duration is operator configuration and `ISubscriptionPolicy.TrialDays` is its one authority —
a literal in the wizard and another in the e-mail would be a second and a third, and the plan's own *Deploy-time
values* note records that this product's landing copy **already** says « Essai accompagné — 2 semaines ». So the
drift is not hypothetical; it has happened once with nothing to catch it. This is the `fixes-dont-propagate` shape
in its other direction: one authority, three readers.
**Impact:** One optional DTO field (additive, read `?? 30`), one constructor parameter, and the two test fixtures it
forces. Three backend files in a part the plan called `web/`-only. Closed by
`The_email_quotes_the_configured_trial_length_not_a_literal` and
`The_reported_trial_length_follows_the_configured_value`, both of which a literal fails — proven red for the first.
**Approved:** Yes — asked with the alternative (hardcode + a go-live alignment note) before any code was written.

### DEV-7: Part C's interim « Abonnement » rail row is closed here
**Date:** 2026-08-10
**Story:** 1, Part D, step 6
**Category:** Scope
**Original Plan:** Part D's step list and file table do not mention `lib/nav.ts`'s `buildConfigItems`.
**Actual Implementation:** `buildConfigItems(isAdmin, showSubscription = true)` and
`buildNavSections(role, showSubscription = true)`; `dashboard-sidebar.tsx` feeds it `useSubscription().enforced`.
**Justification:** Not new scope — **deferred** scope, and this is the part it was deferred to. Part C's own
*Known interim state* says: « the row itself disappears in **Part D**, the part that introduces the client-side
`requiresSubscription` provider the rail would need. **AC-7.1/7.2 are therefore not tickable at Checkpoint C** ».
That provider is D2. Leaving it would carry an AC the story's own list already marks `[x]` while a clinic's own PC
still shows a rail row whose page says « cette installation ne fonctionne pas par abonnement », and would repeat the
deferral loop `no-deferring-in-scope-work` exists to stop.
**Impact:** Two default parameters, both defaulting to *showing* the row — deliberately, because the second caller
is `lib/zones.ts`, which builds the route→icon map and needs every destination that can render. A row that appears a
moment after load (the probe answering) rather than one that disappears is the safe direction, and the same one
`/join` and `/signup` take with their own probes.
**Approved:** Taken as closing a recorded deferral, not asked; stated here and in the session report.

## Auto-Approved Deviations

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| The provider listens to `visibilitychange` as well as `focus` | Trivial | Internal to one new file, same trigger by another name. A native shell returning from the background does not reliably raise `focus`, and the `inFlight` guard makes the overlap free. FR-15 names « window focus »; this is that event on the devices the product is used on. |
| The dismissal key is `endsOn\|daysRemaining`, not a computed date | Trivial | Internal storage detail, no API change. « The next clinic day » is a fact about Tunis and the browser is the one participant that cannot know it — the `todayLocalIso()` defect one layer over. `daysRemaining` decrements at Tunisian midnight, so the pair changes exactly when the banner should return, with no clock at all. |
| `HIDDEN_PATHS` gains **one** entry, not the plan's two | Trivial | `isChromeLessPath` matches a prefix, so `/signup` already covers `/signup/verifier`. A redundant second entry invites the next reader to think the prefix rule does not exist. |
| A failed subscription re-read keeps the last known state; only an explicit **404** turns the feature off | Trivial | Internal to the new provider, and EC-13's rule one layer down: a banner that vanishes on a network blip tells a cabinet three days from expiry that everything is fine. |
| The banner's dismiss **grows its own box** (`coarse:size-11`) rather than using `.touch-target` | Trivial | Found by the rule-by-rule re-read below, not by the plan. § 2: the overlay is for an *isolated* control, and this one sits 12 px from « Renouveler » in the same row — the later sibling paints last, so a 44 px pseudo-element would steal taps aimed at the one control that leads somewhere. |
| `countdown(null)` says « Abonnement bientôt à renouveler. » instead of `?? 0` | Trivial | Internal to one new function. `?? 0` renders « d'ici 0 jours » — « today is your last day » — to a cabinet the server declined to give a countdown for. The detail line carries the date either way. |
| `SubscriptionRefusals`' three codes are held in a module-level `Set` in `client.ts` | Trivial | Internal to one file; it is read once per non-OK response and keeps the branch a membership test rather than three `\|\|`s. |

## Part D gates — Checkpoint D

| Gate | Result |
|---|---|
| `dotnet build api/ClinicManagement.sln --no-incremental` | ✅ **0 errors**, **55 warnings — the identical Checkpoint A/B/C baseline**, and **zero** name a file this part added or edited (grepped the full list for `AuthController`, `SignUpClinic`, `SelfRegistrationGate`, `Subscription` → no match) |
| Unit suite | ✅ **2364 passed, 0 failed** (baseline 2356 → **8 new**) |
| `SelfRegistrationGateTests` | ✅ 10 (5 new: 3 trial-length rows + the configured-value fact + the AC-7.3 fact) |
| `SignUpClinicTrialCopyTests` | ✅ 3 — new class; the handler had **no** test before this |
| `SubscriptionGateMiddlewareTests` · `SubscriptionExemptionCoverageTests` · `SubscriptionControllerTests` | ✅ green, **all three unedited** — Part D adds no endpoint and changes no exemption |
| `ControllerAuthorizationCoverageTests` · `TenantScopeFilterTests` · `DeploymentProfileCoverageTests` · `MoneyReadConsistencyTests` | ✅ green, **all four unedited** (confirmed by name against `git status`) — the four derived guards that should have needed no edit needed none |
| `RealtimeResourceResolverTests` | ✅ green, **unedited** — no frontend key was added; the state is learned by a re-read (FR-15) |
| `verify-schema` | **not applicable — and the verb was re-confirmed to exist and run** (`Api/Maintenance/VerifySchemaCommand` + `SchemaVerificationService`, 49 passing tests). Part D adds no migration, column or index: `git status` shows nothing under `Infrastructure/` |
| `npx tsc --noEmit` | ✅ clean |
| `npm run check:responsive` | ✅ **15/15** |
| `npm run build` | ✅ compiled; 33/33 static pages, `/abonnement` still in the route table |
| Eye pass at 320 / 390 / 820 / 1180 / 1440 px + a 380 px-tall landscape | ⚠️ **not run — no browser automation on this machine.** `agent-browser` is not installed (`command -v` finds nothing; `npx` refuses to fetch it). See *The eye pass, and what stands in for it* |

### The one red-proof

`SignUpClinicTrialCopyTests.The_email_quotes_the_configured_trial_length_not_a_literal` was proven to fail. The
e-mail's `{_subscriptionPolicy.TrialDays}` was replaced with a literal `30`, the class was run, and **exactly one of
its three tests went red** — the other two (which assert the default figure) stayed green, which is precisely the
point: they would have passed on the hardcoded sentence for ever. Probe reverted, file confirmed to hold the
interpolation again, and the full suite re-run green (2364).

The other new assertions are behavioural. The one worth naming as able to fail on a plausible wrong implementation
is `No_subscription_setting_can_turn_enforcement_on` — it is the AC-7.3 guard in the direction nobody tests, and it
fails the moment somebody "helpfully" adds a `Subscription:Enabled` key.

### AC-4.6, audited rather than assumed

The plan's step 4 says « verify rather than assume, and fix any site that closes on error ». Three derived scans
over every `.tsx` under `app/` and `components/`, each walking the real brace depth of every `catch` block:

| Scan | Result |
|---|---|
| A `catch` that closes a dialog or reports success (`onOpenChange(false)`, `setOpen(false)`, `onClose()`, `onSuccess()`) | **none** |
| A `catch` that navigates away (`router.push/replace/refresh`, `window.location`) | **none** |
| A `catch` that resets the form (`reset()`, `resetForm()`, `clearForm`) | **none** |
| A `catch` following a **write** that reports nothing to the user | 8 candidates, **all false positives on inspection** — each sets a French error state (`setCustomError`, `setProcedureTypesError`, `setCreateError`, `setVoidError`, `setDeleteError`, `setCancelError`) and leaves its surface open; the ninth (`ai-chat.tsx:137`) is a **read**, not a write |

A dialog closing in a `finally` was checked for separately and found nowhere. So AC-4.6 needed **no** fix: every
refused write already leaves its dialog open with the typed input intact, and since each of those sites renders
`err.message` for an `ApiError`, what a 402 puts on screen is the gate's own French sentence naming the end date.

### The eye pass, and what stands in for it

No browser again (Part C's finding, unchanged). The widths were **not** looked at; recorded as owed. What was run
in its place: the mechanical gate (15/15) plus a deliberate re-read of both new surfaces against
`DEVICE-CONTRACT.md` § 1 and `.claude/rules/frontend-web.md`, item by item — which **found two real defects**, which
is the argument for doing it rather than writing « responsive ✓ »:

1. **The dismiss control used `.touch-target` in a row** — § 2's named wrong-action bug. Changed to
   `size-8 coarse:size-11`.
2. **The 44 px floor broke the height budget.** With both controls at 44 px, `py-2` gives 60 px against a ~57 px
   ceiling on a 380 px landscape viewport. `coarse:py-1` brings it to 52 px; a mouse keeps `py-2` and 48 px.

| Rule | How it is held in `subscription-banner.tsx` (+ the wizard's trial badge) |
|---|---|
| § 1 hinge | No hinge needed — one wrapping flex row. `px-4 md:px-6` matches `AppShell`'s own gutter so the strip lines up with the content under it |
| § 2 touch on the **pointer** | « Renouveler » `coarse:min-h-11` (`size="sm"` is 32 px); dismiss `size-8 coarse:size-11`, grown not overlaid — see above |
| § 3 16 px fields | No field on either surface |
| § 4 / § 5 dialogs & sheets | None — and the banner is deliberately **not** a modal (spec: it is met mid-consultation) |
| § 6 a `<Table>` never ships alone | No table |
| § 7 `dvh` / bottom inset | Nothing `fixed`. The banner is a flex sibling of `<main>`, so `AppShell`'s `h-dvh` keeps owning the viewport and the bottom bar keeps its edge — the whole reason for DEV-5 |
| § 8 11 px floor, no `text-[Npx]` | `text-sm` only (`type-scale` passes) |
| § 9 hover | `hover-hover:hover:opacity-100` on the dismiss — an opacity change, gated anyway, and the control is visible at `opacity-70` rather than hover-revealed |
| § 10 ungated grids | None. `flex-wrap` with `gap-x-3 gap-y-1`; the wizard badge is `inline-flex flex-wrap` |
| § 11 overflow | Nothing scrolls horizontally; long text wraps via `[overflow-wrap:anywhere]` |
| § 12 logical utilities | `-me-1`; `px-*`/`gap-*` are symmetric |
| § 13 UX floor | `role="status"` on the banner **and** on the wizard badge (an inline async result); `aria-label` on the icon-only dismiss; `aria-hidden` on every decorative glyph; no English string; the state's own French word is in the sentence, so « Expiré » is legible in greyscale and the tone only reinforces it |

### Verification steps — what is proven and what is still owed

| Step (story § *Verification Steps → Part D*) | Result |
|---|---|
| A grant reaches the browser within one interval, no sign-out, no reload (AC-5.8) | ✅ **structurally** — the 5-minute interval runs whenever `shouldWarn` or `!allowsWrites`, i.e. exactly while a cabinet is waiting to be unblocked, and nothing in the 402 path touches the session. The live walk needs Part F's grant verb — **owed, and blocked on Part F** |
| A refused save raises a French toast, leaves the form populated, and the banner appears with no reload (EC-1) | ✅ toast + form: audited in all four directions above. Banner-without-reload: `onSubscriptionRequired` → re-read → provider state → banner, wired and typechecked; **the live walk is owed** |
| The expired banner has no dismiss control and is not a modal; « Expiré » legible in greyscale | ✅ by construction — `dismissible: false` on both non-writable states, and the state's French word is in the text, not only in the colour |
| Dismissing while valid hides it for the rest of the clinic day and it returns the next day (AC-3.2) | ✅ by construction on the `endsOn`+`daysRemaining` key; **not** exercised across a real midnight — owed with the operator walk |
| Banner absent when `requiresSubscription` is not `true`, and on `/login` and `/signup` | ✅ **structurally, twice over**: the provider fetches nothing unless the flag is `=== true`, and `AppShell` — the only mount point — is used by none of the six chrome-less routes (verified by scanning every page file) |
| Frontend gate clean; eye pass incl. a 380 px-tall landscape viewport for the ≤ 15 % budget | ✅ gate clean · ⚠️ **eye pass owed** (no browser here). The budget was computed rather than measured: 52 px at the coarse floor, 48 px on a mouse, against ~57 px |

## Known interim state, deliberately

**Part C's interim rail row is now closed** (DEV-7) — `SelfHostedLan` and `CloudBrowser` show no « Abonnement » row.
What remains open after Part D:

- **No warning notifications** (AC-3.4–3.7). The banner appears seven days out; the bell does not badge. Part E.
- **No vendor verb** (US-5). A paid cabinet is still unlocked by editing the ledger directly, so AC-5.8's *live*
  walk cannot be performed yet — the re-read that observes a grant is built and the grant is not.
- **No outbox parking** (FR-8, EC-7). A reminder queued before expiry for a later appointment still sends. Part G.

## Learnings

- **A plan's file list can be right about the component and wrong about the parent.** « Put the banner in
  `layout.tsx` » is the natural sentence, and it is unimplementable here for a reason visible only in
  `app-shell.tsx`: that shell claims the whole dynamic viewport, so anything above it pushes the bottom bar off the
  screen. Reading the parent before writing the child cost one file open and turned a layout bug into a one-line
  mount — and the mount that works is also the one that makes an AC true by construction. Worth asking « what owns
  the height here? » before adding any element to a root layout.
- **« The spec's sentence, verbatim » and « one authority » can be the same instruction pointing two ways.** AC-1.3
  words the trial as « 30 jours », and the plan's own closing note says the landing copy says « 2 semaines » — i.e.
  the literal had already drifted before a line of this part was written. The tell was in the plan itself, in a
  section headed *Deploy-time values* that reads as housekeeping.
- **A rule-by-rule re-read is not a formality when it replaces an eye pass.** Part C's found one thing; this one
  found two, and the second (`coarse:py-1`) is a defect **no width would have revealed on this machine anyway** —
  it only appears on a coarse pointer, which a desktop browser at 380 px of height does not simulate. Where the eye
  pass cannot run, the arithmetic has to be done explicitly rather than deferred to looking.
- **A deferral recorded in `progress.md` is only closed if somebody re-reads `progress.md`.** Part C's *Known
  interim state* named Part D as the place the rail row disappears, and nothing in Part D's own step list or file
  table mentions `lib/nav.ts` — so following the plan alone would have shipped the whole feature with AC-7.1/7.2
  ticked and a visible row contradicting it. The story file's AC list already had it as `[x]`, which is exactly how
  such a gap survives a review.

---

# Part E — The cabinet is warned before it stops being able to work (⚠️ atomic)

**Working tree note (start of session 5).** Clean apart from the other author's untracked
`features/platform-console/`, left untouched and excluded. `git status` reviewed before any edit and again before
staging; every file below is staged by explicit path. `git diff HEAD --numstat` at the end: **5 deletions across
14 files** — three are the stale « written by nothing until Part E » paragraph on
`StaffNotification.SubscriptionThresholdDays`, now that Part E is what writes it, and two are the comma added after
each enum's previous last member. Everything else is additive.

## Session decisions

**Session 5 — scope: Part E only.** Requested explicitly (`/implement-story clinic-subscription part E`). Same
branch, same explicit-path staging. Part E is **atomic** by the plan's own split-point rule (R-9), so the four E1
items land in one commit with everything else.

**Two questions asked and answered before any code was written** — both changed what was built:

1. **The client half.** AC-3.4 requires the row to deep-link to « Abonnement », while the plan's layer table calls
   Part E `api/`-only. `dashboard-header.tsx`'s `handleNotificationClick` has no branch for a new `targetKind` and
   `notification-panel.tsx`'s two maps are loose `Record<string, …>` with a fallback — so backend-only would have
   shipped four rows that badge the bell, render a generic icon and **do nothing when clicked**. Answer: **include
   the client half** (DEV-8).
2. **What the daily pass does to an expired or suspended cabinet.** Answer: **leave both alone** — see DEV-9.

## Part E — steps

| # | Step | Status |
|---|---|---|
| E1 | The atomic four: `NotificationCategory.SubscriptionExpiring = 10`, `NotificationTargetKind.Subscription = 5`, `StaffNotification.ForSubscription(...)`, `StaffNotificationRules.ReachesALockedPhone → false` | **done** — one commit (R-9) |
| E2 | `EnsureSubscriptionWarningAsync` / `ClearSubscriptionWarningsAsync` inside `SafelyAsync`, deduped on (clinic, threshold), + the two `GetBackupStaleAsync` siblings | **done** |
| E3 | `SubscriptionWarningJob` (daily, `RequiresSubscription`-guarded, `UseSystemWide` + `RunAs`, try/catch per clinic, re-arm) + its `Program.cs` registration | **done** |
| E4 | `SubscriptionWarningTests` | **done** — 22 new tests, three executed red-proofs |

### What the wording came out as

Four rows, distinguishable **in the title** so a reader of the bell can tell « 3 jours » from the « 7 jours » they
read last week: « Abonnement — 7 jours restants » / « 3 jours restants » / « 1 jour restant » / « dernier jour ».
The message is one sentence naming the end date and following `SubscriptionRefusals`' own rule — what still works
before what will stop, because it is read chairside: « Votre abonnement se termine le JJ/MM/AAAA. Vous pourrez
toujours consulter et exporter vos données, mais plus enregistrer de nouveaux actes. Rendez-vous dans
« Abonnement » pour le renouveler. »

⚠️ **The message is derived from the THRESHOLD, not from the live countdown**, and that is load-bearing rather than
stylistic: a message rebuilt from « days remaining » differs every day, so the ensure would restate on every daily
pass and make every open browser refetch — the churn the dedupe exists to prevent. It is also what lets the
idempotency comparison be the whole message rather than a prefix, unlike its two ensure/clear neighbours which
carry a countdown inside their text.

## Deviations

### DEV-8: Part E carries two frontend files, in a part the plan calls `api/`-only
**Date:** 2026-08-10
**Story:** 1, Part E, step 1
**Category:** Scope
**Original Plan:** The *Files to Create/Modify → Part E* table lists nine `api/` files and no `web/` file; the
stories README's layer-weight table records Part E as `BE`.
**Actual Implementation:** Plus two small edits in existing files — `web/components/dashboard-header.tsx` gains the
`targetKind === "Subscription"` → `/abonnement` branch, and `web/components/notification-panel.tsx` gains one
`CATEGORY_ICON` entry (`CreditCard`) and one `CATEGORY_TONE` entry (amber `-wash`/`-ink`).
**Justification:** AC-3.4 says the notification « deep-links to « Abonnement » », and on this client that is not a
property of the backend. Both maps are `Record<string, …>` with a neutral fallback and `handleNotificationClick` is
an if/else chain over known `targetKind`s, so a backend-only Part E ships four rows that badge the bell, render an
uncoloured chip with a fallback glyph, and are **inert on click** — the `fixes-dont-propagate` shape, and an AC that
would have been ticked while being false. Neither file's `tsc` would have caught it: a loose `Record` accepts a
missing key by design.
**Impact:** Four added lines across two existing files, one new `lucide-react` import (already a dependency). No new
screen and no new layout surface, so the frontend gate is the mechanical one plus a re-read; there is nothing new to
eye-pass. The layer weight for Part E is `BE + FE` rather than `BE`.
**Approved:** Yes — asked with the backend-only alternative before any code was written.

### DEV-9: an expired or suspended cabinet is neither warned nor cleared
**Date:** 2026-08-10
**Story:** 1, Part E, step 3
**Category:** Technical
**Original Plan:** Step 3 says « an extension past the window clears outstanding warnings and **re-arms** the
thresholds » and is silent on what the pass does once the date has actually passed, or while a cabinet is suspended.
**Actual Implementation:** `SubscriptionWarningJob.ReviewClinicAsync` returns early for `SubscriptionState.Expired`
and `SubscriptionState.Suspended` — neither ensuring a row nor clearing the existing ones. The clear-and-re-arm
branch is reached only when no threshold is current, i.e. no end date or one beyond the window.
**Justification:** Both fall out of `SubscriptionStateReader` returning `DaysRemaining: null` for each, so *some*
decision was forced. **Expired:** clearing is the natural reading of « no threshold reached », and it is wrong — the
day after expiry the cabinet is meeting a refused save, and those four rows are the only thing in the feed that
explains it. Warning again is also wrong: AC-3.4's four warnings are the ones *before* it stops working, and a fifth
contradicts AC-3.5. **Suspended:** the reader surfaces no countdown for a suspended cabinet on purpose (EC-11 — it
reads « Suspendu », never « Expiré », because a payment will not fix it), so « votre abonnement se termine dans 3
jours » would send a practice suspended for another reason to pay for something that will not unblock it.
**Impact:** One early return in a new file, and both cases are pinned by named tests rather than left to read
correctly — `An_Expired_Cabinet_Keeps_The_Warnings_It_Was_Already_Given` is the one that would stay green on the
wrong implementation of the *other* branch, so it is asserted rather than assumed.
**Approved:** Yes — asked with the two alternatives (clear on expiry; write a fifth « expiré » row) before any code
was written.

## Auto-Approved Deviations

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| `WarnExpiringSubscriptions()` gains a `(DateTime clinicToday)` overload; the parameterless one resolves `ClinicClock.ClinicToday()` and is the sole production caller | Trivial | Internal to one new file, same production behaviour. It is `SubscriptionStateReader`'s own stated reason: the four thresholds and the midnight they turn on are otherwise untestable, and midnight is the only boundary that matters for a date that arrives by itself. The Hangfire registration points at the parameterless one, which carries the two attributes. |
| The warning wording lives as private members of `NotificationGenerator`, not in a new `Features/Subscriptions/` file | Trivial | Follows that file's own convention — all five existing categories' wording is private there. No second reader exists (Part F's report reads state, not the feed), and the tests assert through the generator, so no prose is retyped. |
| `NotificationGenerator` now imports `Features.Subscriptions` for `SubscriptionRefusals.DateFormat` | Trivial | One shared format string, same project. « The end date as it is written to a cabinet » is one statement; a `"dd/MM/yyyy"` literal here would be a second copy of it. |
| The whole message is compared for idempotency, not a prefix (unlike the stock-expiry and backup-stale ensures) | Trivial | Internal to the new method. Those two carry a countdown *inside* their message, so a whole-message comparison would differ daily; this one does not, which is what makes the simpler comparison correct here. |
| Both new repository reads call `IgnoreQueryFilters()` | Trivial | `GetBackupStaleAsync`'s precedent verbatim, comment included. The daily pass runs `UseSystemWide` so the filter is satisfied anyway; the `clinicId` parameter is the authoritative check either way, and Part F's grant may clear from inside a scoped command. |
| `StaffNotification.ForSubscription` is a static factory rather than a twelfth ctor parameter | Trivial | New member on an existing entity, no caller outside the generator. The threshold is meaningful for exactly one category; an optional ctor argument would let the other nine carry one. |
| The stale « ⚠️ Written by nothing until Part E » paragraph on `SubscriptionThresholdDays` is replaced by one line | Trivial | Comment correction in a file this part edits — Part E *is* what writes it now, so the note had become false. |

## Part E gates — Checkpoint E

| Gate | Result |
|---|---|
| `dotnet build api/ClinicManagement.sln --no-incremental` | ✅ **0 errors**, **55 warnings — the identical Checkpoint A/B/C/D baseline**, and **zero** name a file this part added or edited (the full list was grepped for all nine touched type names; the single `Program.cs` hit is the pre-existing Hangfire `CS0618` at **line 331**, ~400 lines above this part's edit) |
| Unit suite | ✅ **2386 passed, 0 failed** (baseline 2364 → **22 new**) |
| `SubscriptionWarningTests` | ✅ 22 |
| `SystemWideCallerCoverageTests` | ✅ green, **unedited** — its derived criterion **auto-enrolled the new job**, proven by probe (below) rather than assumed |
| `RealtimeResourceResolverTests` | ✅ green, **unedited** — the job writes through `INotificationGenerator`, which broadcasts the existing `"notifications"` key; no new key on either side |
| `SubscriptionGateMiddlewareTests` · `SubscriptionExemptionCoverageTests` · `SubscriptionControllerTests` | ✅ green, **all three unedited** — Part E adds no endpoint and changes no exemption |
| `ControllerAuthorizationCoverageTests` · `TenantScopeFilterTests` · `DeploymentProfileCoverageTests` · `MoneyReadConsistencyTests` | ✅ green, **all four unedited** (confirmed by name against `git status`) — the four derived guards that should have needed no edit needed none |
| `PushFanOutTests` | ✅ green, **unedited** — the new category is classified `false`, so the decorator passes it through and the fan-out never sees it |
| `verify-schema` | **not applicable — and the verb was re-confirmed to exist and run** (`Api/Maintenance/VerifySchemaCommand` + `SchemaVerificationService`, 49 passing tests). **Part E adds no migration**: the `StaffNotifications.SubscriptionThresholdDays` column it writes to landed with Part A's migration, by that part's own one-migration decision. `git status` shows nothing under `Infrastructure/Migrations/` |
| `npx tsc --noEmit` | ✅ clean |
| `npm run check:responsive` | ✅ **15/15** |
| `npm run build` | ✅ compiled; 33/33 static pages |
| Eye pass at the five widths | **not applicable, for the first time in this feature** — and checked rather than assumed: the two frontend edits are one `router.push` branch and two entries in two `Record` maps. No element, no class and no layout is added, so there is no surface to look at. The panel and the bell were eye-passed when they were built |

### The three red-proofs

All executed, because two of them guard properties that pass on a plausible wrong implementation.

1. **R-9, the split point.** Removed `NotificationCategory.SubscriptionExpiring => false` from
   `StaffNotificationRules`: `Every_Other_Category_Is_Still_Classified` **and**
   `The_Warning_Never_Reaches_A_Locked_Phone` both went **red**. Restored; green. This is the proof the part is
   genuinely atomic — the switch throws, so shipping E1 without that line breaks every notification write in the
   product, not only the new one.
2. **The dedupe key.** Replaced `threshold.Value` with `status.DaysRemaining!.Value` in the job's ensure call — i.e.
   keyed on the live countdown instead of the threshold. **Four** tests went red, including both headliners
   (`Simulating_The_Countdown_Produces_Exactly_Four_Rows_One_Per_Threshold` and
   `The_Wording_Does_Not_Change_While_The_Threshold_Holds`). Reverted; green. That defect compiles, produces
   plausible French, and writes a row **every day** of the countdown while restating the wording daily.
3. **The coverage guard's enrollment.** Removed the job's `_tenantScope.UseSystemWide(...)` declaration:
   `SystemWideCallerCoverageTests.Every_Path_Without_An_Http_Context_Declares_Its_Tenant_Scope` went **red naming
   `ClinicManagement.API\BackgroundJobs\SubscriptionWarningJob.cs`**. Restored; green. Worth doing rather than
   trusting the green run: a job with no scope reads **nothing** and logs a clean pass, which is US-2's own R-1 and
   is indistinguishable from « no cabinet is expiring ».

### The defect this part's own tests caught

**One, and it was in the test rather than the production code — but the failing run was still the finding.**
`A_Grant_Inside_The_Window_Restates_The_Row_Rather_Than_Adding_One` was written expecting a restate, and failed
with two rows. Moving the end date by two days while today stayed fixed moved the cabinet from threshold 1 to
threshold **3**, so a new row is correct — and restating *across* thresholds is precisely what AC-3.5's ⚠️ forbids,
since a restated row keeps the read markers of the warning already dismissed. It was replaced by the two cases that
actually exist: `A_Grant_That_Keeps_The_Threshold_Restates_The_Row_In_Place` (same threshold, moved date → same id,
new date) and `A_Grant_That_Moves_The_Threshold_Writes_A_New_Row_Rather_Than_Rewriting_The_Old_One`, which records
the behaviour that **looks like a bug and is not**.

### Verification steps — what is proven and what is still owed

| Step (story § *Verification Steps → Part E*) | Result |
|---|---|
| Simulating days −8 → 0 produces exactly **four** rows, each unread and each badging the bell | ✅ `Simulating_The_Countdown_Produces_Exactly_Four_Rows_One_Per_Threshold` (day −8 asserted to produce **none**, so « from 7 days » is a boundary rather than « soon ») + `Every_Threshold_Gets_Its_Own_Row_Rather_Than_A_Restatement`, asserted as four **distinct ids** — the property that makes « badges the bell » true |
| Running the job twice on the same day adds nothing (AC-3.5) | ✅ `Running_Twice_On_The_Same_Day_Adds_Nothing`, plus `A_Countdown_That_Sits_Inside_One_Threshold_For_Days_Yields_One_Row` (four consecutive days inside threshold 7 → one row) |
| **No push is queued** for the category (AC-3.6) | ✅ `The_Warning_Never_Reaches_A_Locked_Phone`, asserted against `StaffNotificationRules` — the single decision point the fan-out actually reads, not against the decorator |
| Extending past 7 days clears the rows; approaching again later warns again (FR-5) | ✅ `Extending_Past_The_Window_Clears_The_Rows_And_Re_Arms_Every_Threshold`, which walks the whole round trip: two rows → grant a year → empty → approach again → all four thresholds again |
| Every role receives the warning (AC-3.7) | ✅ `The_Warning_Is_Addressed_To_The_Whole_Practice` — `ActorUserId` and `TargetUserId` both null, which *is* the mechanism (an actor id would hide the row from whoever « caused » it, and nobody causes a date arriving) |
| Notification writes in **other** categories still work — proof the `StaffNotificationRules` half landed (R-9) | ✅ `Every_Other_Category_Is_Still_Classified`, with the executed red-proof above |
| A cabinet's warning row deep-links to « Abonnement » | ✅ backend: `The_Warning_Deep_Links_To_The_Subscription_Screen`. Client: the `handleNotificationClick` branch, typechecked and built (DEV-8). **The live click is owed with the operator walk** |
| The daily pass over simulated days (operator step, per the plan's *Manual verification*) | ⚠️ **owed** — done in the suite against a moving date, not against a real Hangfire schedule on a hosted deployment |

## Known interim state, deliberately

What remains open after Part E:

- **No vendor verb** (US-5). A paid cabinet is still unlocked by editing the ledger directly, so AC-5.8's *live* walk
  still cannot be performed. Part F.
- **No outbox parking** (FR-8, EC-7). A reminder queued before expiry for a later appointment still sends. Part G.
- **The `/hangfire` dashboard is loopback-only in both modes**, so on a hosted deployment the new job's runs are
  observable through the log rather than a screen. `GET /api/outbox` has **no section for it** and gains none — it is
  not a queue. Worth knowing before anyone goes looking for a « warnings sent » figure; there is none, deliberately
  (the rows in the feed are the record).

## Learnings

- **A plan's layer table can be right about the code and wrong about the acceptance criterion.** « Part E is
  `api/`-only » is true of every file the feature needs to *write* the notification, and false of AC-3.4, which says
  the row deep-links. The tell was not in the plan at all — it was in `dashboard-header.tsx` being an if/else chain
  over known `targetKind`s and both panel maps being loose `Record<string, …>` with a fallback, i.e. three places
  that accept a missing key **by design** and therefore cannot fail a build. Worth reading the *consumer* of any new
  enum value before believing a part is single-layer.
- **Two alerts in the same feed can need opposite dedupe rules, and the reason is read state, not wording.**
  `StockExpiringSoon` and `BackupStale` keep one row and reword it; this one must not, because rewording does not
  clear who has read it — so the escalation would land silently on a bell the owner had already cleared. The
  distinction is « is this the same fact restated, or a new fact? », and it is invisible if you only compare the two
  implementations.
- **Deriving a message from the live value is how an « ensure » becomes a daily broadcast.** Both existing ensures
  document this and work around it with a prefix comparison; the cleaner fix available here was to make the message a
  function of the **threshold**, so there is nothing volatile in it to compare. Same defect, one layer earlier — and
  the red-proof shows a row count alone would not have caught it.
- **A first-run test failure is worth diagnosing in both directions.** This part's one red was the *test* encoding a
  wrong expectation (a restate across thresholds), not the code — but only checking it against AC-3.5's own stated
  reasoning distinguished that from the opposite conclusion. The skill's guidance points at fixing the production
  code; the check that matters is which of the two the spec actually says.
