# Implementation Progress — Abonnement du cabinet

**Feature:** `features/clinic-subscription/`
**Story:** [Story 1 — Abonnement du cabinet](./story-1-full-clinic-subscription.md) (`Layer: Full`, seven parts)
**Branch:** `feature/windows-desktop-app` (user decision — see *Session decisions*)

## Status Tracker

| Story | Status |
|---|---|
| 1 — Abonnement du cabinet | in-progress (Parts A + B done) |

### Parts inside Story 1

| Part | Focus | Status |
|---|---|---|
| A | Every cabinet has an entitlement, at every door and for all of history | **done** (Checkpoint A green) |
| B | An expired cabinet keeps its records and loses only recording | **done** (Checkpoint B green) |
| C | The cabinet can see where it stands and how to pay | not-started |
| D | The banner, the refusal toast, and the live re-read | not-started |
| E | The cabinet is warned before it stops being able to work (⚠️ atomic) | not-started |
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
