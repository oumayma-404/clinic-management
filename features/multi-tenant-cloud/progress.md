# Multi-Tenant Cloud — Implementation Progress

**Story:** [stories/story-1-full-hosted-multi-tenant.md](stories/story-1-full-hosted-multi-tenant.md)
**Branch:** `feature/audit-sections-3-to-10` (by decision — the story's entry criterion, not a new branch)
**Plan:** [plan.md](plan.md) — **APPROVED**, Challenged **Yes** (two passes)

## Part status

The story is one story with six ordered internal parts, and its own README names a **part** as the resumable
unit ( « 18 steps and ~35 files will not fit one session » ). This table is the checkpoint.

| Part | Plan | Steps | Status | Session |
|------|------|-------|--------|---------|
| A | US-1 | 1–4 | **implemented** (code gate) | 2026-08-05 |
| B | US-2 | 5–10 | **implemented** (code gate) | 2026-08-05 |
| C | US-3 | 11–13 | not-started | — |
| D | US-4 | 14 | not-started | — |
| E | US-5 | 15 | not-started | — |
| F | US-6 | 16–18 | not-started | — |

⚠️ **Part F's step 17 (`DataProtection:KeyRingPath` required) must land before Part D**, per the story's own
ordering: a PFX password protected by Data Protection makes e-invoice signing depend on the key ring.

## Working tree note (start of session)

The branch carried dirty files unrelated to this story; **none was staged**, per the repo's standing rule
(`git diff HEAD --numstat` before any `git add`, and stage by path — never `-A`):

- `features/mobile-native-shells/spec.md` — dirty at session start
- `features/mobile-native-shells/{blueprint,exploration}.md` + untracked `plan.md` — appeared **during** this
  session, i.e. parallel work by another session. Left untouched.

## Part A — what landed

Steps 1–4 of the story: `LocalAuthConfig.IsLocalMode` retired from every call site in favour of a resolved
**deployment profile** with a capability per question.

| Step | Deliverable |
|------|-------------|
| 1 | `Infrastructure/Deployment/DeploymentProfile.cs` — `DeploymentKind` (3 kinds) + **12** capabilities + `Resolve`/`For` |
| 2 | `Program.cs` — all 17 branches now ask a named capability; the migrate-and-backfill block split into two questions |
| 3 | `Extensions.cs`, `LocalDataProtection.cs`, `SecurityHeadersMiddleware.cs`, 4 controllers, 7 `Maintenance/*Command.cs` |
| 4 | `DeploymentProfileTests.cs` (matrix + R-2 back-compat) and `DeploymentProfileCoverageTests.cs` (derived guard) |

**30 `IsLocalMode` occurrences across 16 files → 2**, and those two are the only legitimate ones: the
declaration in `LocalAuthConfig`, and the single back-compat call inside `DeploymentProfile.Resolve`.

### Capability → call-site map (the answer to « which question was this? »)

| Capability | Call sites it now answers |
|---|---|
| `UsesLocalAccounts` | JWT bearer setup; `AuthController` login/refresh/setup/register guards + the `GET mode` **value**; `IAuth0ManagementService` vs the no-op |
| `FailClosedAuthz` | `AuthorizationPolicies.ConfigurePolicies` (keeps its `bool` — it lives in Application, which cannot reference Infrastructure) |
| `EnforcesTokenState` | `LocalAuthEnforcementMiddleware` |
| `UsesDiskStorage` | `IFileStorage` → disk vs MinIO |
| `SelfHostsFrontDoor` | YARP registration, `MapReverseProxy`, `UseHttpsRedirection` (inverted), the port-in-use outer catch |
| `SelfSignsCertificate` | the Kestrel cert block, the transport-posture log, **the HSTS default** |
| `RunsAsWindowsService` | `UseWindowsService`, the install-relative log path, the Data-Protection key-ring location + DPAPI, `harden-permissions`, `protect/read-credential` |
| `DefersMigrations` | `DeferredStartupService` registration + the inline migrate block |
| `RunsStartupBackfills` | the catalog seed + clinic-admin backfill inside that block |
| `ExposesTrustEndpoints` | `TrustController` (4 actions), the trust-port gate, **the connectivity probe** |
| `HasLocalDbTooling` | `verify-schema`, `reconcile-money`, `restore-backup`, `reset-admin-password` (with `UsesLocalAccounts`) |
| `ExposesMetaOnboarding` | `ClinicsController` WhatsApp connect/disconnect |

## Deviations

### DEV-1: a 12th capability, `ExposesMetaOnboarding`
**Date:** 2026-08-05 · **Story:** 1, Part A · **Category:** Technical · **Approved:** Yes (asked)
**Original Plan:** exactly eleven capabilities, with `HostedMultiTenant`'s value given for each.
**Actual Implementation:** twelve. `ClinicsController`'s two Meta/WhatsApp Embedded-Signup guards (today 404 in
Local) had **no capability among the eleven and no row in the plan's truth table**, while the plan does list
`ClinicsController` among the files to modify.
**Justification:** the alternatives were worse. Reusing `!UsesLocalAccounts` would tie WhatsApp onboarding to
the login provider — precisely the "one flag answering unrelated questions" defect Part A exists to remove.
`HostedMultiTenant` = ✓: the frontend is public, and per-clinic WhatsApp tokens are already stored encrypted.
**Impact:** `HostedMultiTenant` keeps Embedded Signup; the two shipped profiles are unchanged (R-2 holds).

### DEV-2: `RunsStartupBackfills` means « the *inline* block owes them »
**Date:** 2026-08-05 · **Story:** 1, Part A · **Category:** Technical · **Approved:** self (R-2-preserving)
**Original Plan:** the split is justified as « the backfills are data obligations **every profile owes on every
boot** », which read literally would make the capability `true` for all three kinds.
**Actual Implementation:** `SelfHostedLan` = **false**, because there the work is *deferred*, not skipped —
`DeferredStartupService` performs it.
**Justification:** the ✓✓✓ reading would require adding the clinic-admin backfill to `DeferredStartupService`,
changing `SelfHostedLan` behaviour — which R-2 forbids for Part A. **Both readings give the plan's stated
`HostedMultiTenant` ✓**, so no observable behaviour differs; only the reason does.
**Impact:** none on behaviour. See the standing finding below.

### DEV-3: HSTS mapped to `SelfSignsCertificate`
**Date:** 2026-08-05 · **Story:** 1, Part A · **Category:** Technical · **Approved:** self (mapping the plan left open)
The plan names « HSTS default » as one of the questions but gives it no capability. `SelfSignsCertificate` is the
code's own stated reason ( « HSTS on a device that never imported our CA turns a bypassable warning into a
permanent hard failure » ), so hosted — served over a publicly-trusted certificate — gets HSTS **on**.

### DEV-4: unknown `Deployment:Profile` throws
**Date:** 2026-08-05 · **Story:** 1, Part A · **Category:** Technical · **Approved:** self
The plan specifies back-compat when the key is *absent*, and is silent on a value that is present but
unrecognised. `Resolve` **throws**: falling back would hand a hosted deployment Auth0 login and no local
accounts, silently, on a typo. It throws on the early startup config, so startup fails before anything binds.

### Auto-approved (trivial)

| Deviation | Classification | Reason |
|---|---|---|
| Per-verb capabilities rather than one gate for all seven console verbs | Trivial | Each verb names its real dependency; every one still refuses in both non-`SelfHostedLan` profiles, so the truth table is unchanged |
| `LocalDataProtection.KeyRingPathKey` const extracted | Trivial | The key name was a literal; Part F needs to name it too |
| `Deployment` private property on `Auth`/`Trust` controllers | Trivial | Internal; mirrors the old one-call-per-guard shape |
| Comment/docstring rewording from "Local/Cloud mode" to profile language | Trivial | Same behaviour; the two-mode wording is now wrong |

## Findings recorded, not fixed (out of Part A's scope)

1. **`DeferredStartupService` never runs `IClinicAdminBackfill`**, while the inline block does — so a
   `SelfHostedLan` install has never had the clinic-admin backfill. Pre-existing, deliberately preserved here
   (R-2); named in `DeploymentProfile.For` at the `runsStartupBackfills: false` line. Worth a follow-up.
2. **`AuthController.Refresh` is a 6th `[AllowAnonymous]` action** while `API/CLAUDE.md` documents four Auth
   actions on the coverage guard's allow-list. Noted by the plan as an aside; untouched.

## Quality gate — Part A

| Gate | Result |
|---|---|
| Backend build (`--no-incremental`, scratch `BaseOutputPath`) | **0 errors, 58 warnings** — byte-identical to the pre-change baseline captured this session |
| New warnings in changed files | **0.** The two warnings landing in touched files (`Program.cs:316` CS0618 Hangfire obsolete overload, `Extensions.cs:48` CS8604 nullable audit arg) are pre-existing and on lines this story did not edit |
| `IsLocalMode` retirement | **2 occurrences left**, both permitted (`LocalAuthConfig` declaration, `DeploymentProfile.Resolve`) |
| `DeploymentProfileTests` / `DeploymentProfileCoverageTests` | **compile clean; could not be executed** — see below |
| Frontend gate (`tsc` / `check:responsive` / `build`) | **Not applicable** — Part A changes no `web/` file. Both scripts verified to exist in `web/package.json`; Part C step 13 is the first part that touches the frontend |
| `verify-schema` / `reconcile-money` before/after | **Deferred to Part D** by decision — Part A adds no migration. Both verbs verified present |

### ⚠️ The test runner is environmentally blocked, and that is not a green tick

`dotnet vstest` **cannot load** the freshly-built `ClinicManagement.UnitTests.dll`: Windows **Smart App Control**
refuses it with `0x800711C7`. The documented workaround (clear `bin/`+`obj/`, `dotnet build-server shutdown`,
build to a scratch `OutDir`) was applied and **three** output locations were tried — all blocked. `dotnet test`
in-place additionally fails to link, because a running `ClinicManagement.API` (PID 9364) holds `bin/Debug`.

So the two new classes are **written and compiling but unrun**. To get real evidence anyway, their assertions
were re-executed through a throwaway console harness in the scratchpad (`dotnet run` on a fresh assembly is
*not* blocked, which is why the user's API runs at all). **All 66 checks passed**, covering:

- the **R-2 truth table** for both shipped kinds, asserted against `LocalAuthConfig.IsLocalMode` itself
- the 12 × 3 capability matrix, plus the reflection guard that the matrix covers every declared capability
- `Resolve`: absent key ⇒ derived from `Auth:Mode`; explicit key wins; case/whitespace tolerant; three
  unrecognised values fail loud with a message naming the key and the valid values
- the coverage scan finding **exactly** the three permitted files, the single production call being inside
  `Resolve`, and — planting a deliberate violation — **the guard actually going red**

**Still owed:** one clean `dotnet test` run of the two classes on a machine where the runner loads.

### Two real defects the harness caught (both in the test I had just written)

Recorded because they are the case for running a check rather than reading it:

1. `Directory.EnumerateFiles(root, "*.cs", AllDirectories)` **threw** `UnauthorizedAccessException` on
   `ClinicManagement.API/bin/.../Backups/clinic-backup-*` — that legacy overload uses `IgnoreInaccessible =
   false`, and filtering `bin`/`obj` *after* enumeration is too late. The guard now **never descends** into
   them. As first written it would have been red on this machine for a reason having nothing to do with what it
   guards.
2. The « the call sits inside `Resolve` » range check anchored on `" For("`, which matches the **call** to
   `For` on the very line being located — so it passed by coincidence (`hit@129 … For@129`). Re-anchored on the
   declarations (`DeploymentProfile For(`), giving `Resolve@123 … For@150`, and two probes now prove the
   predicate rejects a hit above `Resolve` and inside `For`.

## Learnings

- **The plan's capability list was one short, and the gap was invisible until the call sites were counted.**
  Eleven capabilities, ~30 call sites, and two of them (`ClinicsController`'s Meta guards) had no home. Mapping
  every site *before* writing the type is what surfaced it — a capability-per-question refactor is only as
  complete as the enumeration behind it.
- **A "not applicable" gate and a missing one look identical in a table.** Both frontend scripts and both
  console verbs were confirmed to exist before being recorded as not-applicable this part.
- **Changing a check's pattern demands re-proving it, not re-reading it.** Both defects above were in code that
  looked obviously right; one crashed, the other passed for the wrong reason.

---

# Part B — `ITenantScope`, and the query filter starts refusing (steps 5–10)

**Session:** 2026-08-05 · **Branch:** unchanged (`feature/audit-sections-3-to-10`, per the story's decision — the
branch already matched the feature, so no re-prompt).

## Working tree note (start of session)

Same as Part A's, and still nobody else's business: `features/mobile-native-shells/{spec,blueprint,exploration}.md`
dirty + untracked `plan.md` and `stories/` — a parallel session's work. **Not staged.** Every `git add` in this
session named paths explicitly (`git diff HEAD --numstat` first), per the standing rule.

## What landed

| Step | Deliverable |
|---|---|
| 5 | `Application/Common/Interfaces/ITenantScope.cs` (3 states + `SystemWideReason`) · `Common/Services/TenantScope.cs` — single-assignment **in both directions**, reason required and logged |
| 6 | `ICurrentClinicProvider` gains `IsSystemWide`; `CurrentClinicProvider` projects the **scope** instead of the JWT claim; all **21** filters become `IsSystemWide \|\| ClinicId == ScopedClinicId` |
| 7 | `API/Middleware/TenantScopeMiddleware.cs` + `RequestAccount.cs`; registered unconditionally after `UseAuthorization`, before the token-state middleware |
| 8 | `UseSystemWide` on the 5 recurring jobs, both startup scopes and the 3 DB-touching verbs; `UseClinic` in `PdfGenerationJob` + the Google dispatcher's child scope |
| 9 | `TenantScopeTests`, `TenantScopeFilterTests`, `SystemWideCallerCoverageTests`, `ClinicalRecordTenantIsolationTests`; `CurrentClinicProviderTests` rewritten |
| 10 | Onboarding-under-`Unset` assertion, `AuditEntries` left alone, `ClinicHub` note + `ClinicHubTenantScopeTests` |

Plus the docs the change falsifies — root `CLAUDE.md`, Application, Infrastructure, API and UnitTests guides — because
after step 6 all three said « fail-open is deliberate » about code that refuses. (Step 18's *other* halves — the
topology table row, the `Deployment:Profile` config list, the `deploy/` operator note — stay Part F.)

## The scope call sites

| Caller | Scope | Reason it is that one |
|---|---|---|
| `NotificationJob` · `EInvoiceOutboxJob` · `DocumentEmailJob` · `StockExpiryJob` · `BackupJob` | `UseSystemWide` | genuinely drain/scan every clinic |
| `Program.cs` startup scope · `DeferredStartupService` | `UseSystemWide` | migrations + per-clinic backfills |
| `reset-admin-password` · `reconcile-money` · `verify-schema` | `UseSystemWide` | no clinic to be in scope of |
| `PdfGenerationJob` | **`UseClinic`** | renders **one** document |
| `AppointmentGoogleSyncDispatcher` child scope | **`UseClinic`** | pushes **one** appointment |
| `provision-cert` · `harden-permissions` · `protect/read-credential` | *exempt* | no `DbContext` at all |
| `restore-backup` | *exempt* | raw ADO (`NpgsqlCommand`) — no filter in play |
| `AuditSaveChangesInterceptor`'s child scope | *exempt* | writes only `AuditEntry`, unfiltered by design |
| `ClinicCatalogSeeder` | *not listed* | every read is `IgnoreQueryFilters()` — structurally immune, per the plan |

## Deviations

Four asked, all confirmed. Each exists because the plan's wording could not be implemented literally — in every
case because **the thing needed to set the scope is itself behind the filter**.

### DEV-5: the Google dispatcher takes the clinic id from its caller
**Category:** Technical · **Approved:** Yes (asked)
**Plan:** « the App→Google dispatcher's child scope → `UseClinic(appointment.ClinicId)` … the clinic is already
loaded to resolve the connection ».
**Actual:** `IAppointmentGoogleSyncDispatcher.Dispatch(Guid appointmentId, Guid clinicId)`; the three callers pass
the clinic they already resolved.
**Justification:** the appointment is only loaded *inside* `SyncAppointmentToGoogleCalendarAsync`, and
`Appointment` is filtered — so under `Unset` that load returns null and the push dies with « appointment not
found » on every write. The clinic cannot be read before the scope exists. Passing it costs one signature change
and adds no filter-bypassing read.
**Impact:** three call sites; no test needed updating (the dispatcher is mocked loosely everywhere).

### DEV-6: `PdfGenerationJob` resolves its clinic through an `IgnoreQueryFilters` projection
**Category:** Technical · **Approved:** Yes (asked)
**Plan:** « `PdfGenerationJob` → `UseClinic(document's clinic)` ».
**Actual:** new `IMedicalDocumentRepository.GetOwningClinicIdAsync(id)` — `IgnoreQueryFilters()`, projecting one
`Guid?`.
**Justification:** a `MedicalDocument` has **no `ClinicId`** and its owning `Patient` *is* filtered, so under
`Unset` the `Include` comes back null and the clinic is unreachable. The alternatives were leaving it `Unset`
(works today only by accident — nothing it reads happens to be filtered) or `UseSystemWide` for one PDF, which the
plan's own table names as spending the widest scope on the narrowest work. The job now **fails loud** when the
clinic cannot be resolved rather than rendering unscoped.
**Impact:** one new repository method, explicitly documented as the one scope-independent read.

### DEV-7: the middleware resolves the account itself, through a shared per-request accessor
**Category:** Technical · **Approved:** Yes (asked)
**Plan:** the *Modify* line says « resolves the clinic through `ICurrentClinicResolver` »; the very next sentence
says « the two should share one lookup rather than issue two ».
**Actual:** `RequestAccount.ResolveAsync(context, users)` — reads the `sub` claim, queries once, caches on
`HttpContext.Items`; both middlewares call it.
**Justification:** the two halves of that bullet conflict. `ICurrentClinicResolver` returns only a `Guid`, so it
cannot hand `LocalAuthEnforcementMiddleware` the `User` entity it needs (`TokenVersion`/`IsActive`/
`MustChangePassword`) — using it literally means two `User` queries per authenticated request in every profile
that enforces token state. The clinic id is **the same DB-resolved `User.ClinicId`** either way, so C3′ is
honoured exactly; and the duplicated `sub`-claim reading collapses from two copies to one.
**Impact:** neither middleware assumes ordering (the cache is keyed, not positional).

### DEV-8: `AddInfrastructure` gets `ITenantScope` and a floor `ICurrentClinicProvider`
**Category:** Technical · **Approved:** Yes (asked)
**Plan:** silent on where these are registered; the story's file table says « register `ITenantScope` » in
`Infrastructure/Extensions.cs`.
**Actual:** `AddScoped<ITenantScope, TenantScope>()` + `TryAddScoped<ICurrentClinicProvider, CurrentClinicProvider>()`
there, beside the existing `IAuditActorProvider` floor.
**Justification:** without the *provider* floor the three DB-touching verbs still have **no provider at all**, so
the context's optional provider stays null, the filters stay inactive, and their `UseSystemWide` calls are
decoration — Part B would change nothing for the CLI and R-1's mitigation would be theatre for three of the
listed callers. With it, a verb that forgets reads nothing and the guard catches it. Behaviour for the two shipped
profiles is unchanged: the verbs still read every clinic, now because they said so.
**Impact:** `AuditSaveChangesInterceptor`'s provider moved from `GetService` to `GetRequiredService` (see below).

### Auto-approved (trivial)

| Deviation | Classification | Reason |
|---|---|---|
| `TenantScope.UseClinic` refuses `Guid.Empty` | Trivial | It is the value the filter compares against when unset; accepting it would make « scoped to the empty clinic » and « unscoped » produce identical SQL |
| Same-value `UseClinic`/`UseSystemWide` is idempotent rather than throwing | Trivial | The plan says « a second call **with a different id** throws »; restating the same answer lets a handler assert what the middleware established |
| `SolutionSources` extracted; `DeploymentProfileCoverageTests` now uses it | Trivial | Test-only, behaviour identical. The alternative was a second copy of the `bin`/`obj` non-descent lesson — the repo's own `fixes-dont-propagate` shape |
| `AuditSaveChangesInterceptor`'s provider → `GetRequiredService` | Trivial | Made honest by DEV-8's floor: the parameter is non-nullable and can no longer be null. Clears one pre-existing `CS8604` (58 → 57 warnings) |
| The `Unset`-per-aggregate case lives in `TenantScopeFilterTests`, derived, not copied into nine `*TenantIsolationTests` | Trivial | Those are Moq-based handler tests with no `DbContext`, so an `Unset` case there would assert the mock, not the filter. The derived version covers **every** filtered root instead of the nine that have a suite |

## Quality gate — Part B

| Gate | Result |
|---|---|
| Backend build (`--no-incremental`, scratch `BaseOutputPath`) | **0 errors, 57 warnings.** Baseline captured this session on untouched `HEAD`: **0 / 58** |
| New warnings in changed files | **0** — the changed-file list was diffed against the warning list; empty intersection. The one warning that *disappeared* is `Extensions.cs` `CS8604`, cleared deliberately by DEV-8 |
| **The test runner worked this session** | Smart App Control did **not** block it (its verdict is time-varying). `dotnet vstest` over a scratch `OutDir`: **1 887 passed / 24 failed / 1 911 total** |
| Part B's own classes | **31/31 green** — `TenantScopeTests` (11), `TenantScopeFilterTests` (6), `SystemWideCallerCoverageTests` (3), `CurrentClinicProviderTests` (3), `ClinicalRecordTenantIsolationTests` (8) |
| `SystemWideCallerCoverageTests` proven to go **red** | Yes — `The_Guard_Rejects_A_Job_Whose_Declaration_Is_Removed` feeds the predicate the real `StockExpiryJob` source with its declaration stripped |
| Frontend gate | **Not applicable** — Part B changes no `web/` file (Part C step 13 is the first that does). Both scripts re-confirmed present in `web/package.json` |
| `verify-schema` / `reconcile-money` | **Not applicable** — Part B adds no migration. Both verbs confirmed to **exist** (`API/Maintenance/{VerifySchema,ReconcileMoney}Command.cs`) and both were *edited* by step 8 |

### ⚠️ The 24 remaining failures are a pre-existing red baseline — and it was invisible until now

Part A recorded that the runner could not load the test assembly, so **the suite has not actually been run on this
branch for at least two sessions**. It runs now, and it is 24 red. Proven not to be Part B's:

- The identical suite was built from untouched `HEAD` (`base-build`) and run: **27 failures**.
- After Part B: **24**, and `comm` of the two sorted name lists shows **zero new** failures and exactly **three
  fixed**.

The three fixed are this story's own debt and were repaired here: `ProvisionCertCommandTests`,
`ReconcileMoneyCommandTests` and `VerifySchemaCommandTests` each asserted the refusal message contains
`"Local"`, which **Part A** changed to name the resolved profile. They now assert
`nameof(DeploymentKind.CloudBrowser)` — a stronger claim, and `nameof` so a rename cannot leave them green
against a string that no longer exists.

The other **24 belong to earlier features**, in six areas, with two root causes:

| Count | Tests | Root cause |
|---|---|---|
| 16 | `GetCnamNomenclatureQueryHandlerTests` (7) · `GetMedicationsQueryHandlerTests` (7) · `GetStockItemsQueryHandlerTests` (2) | `list-pagination` moved free-text/category filtering **into SQL**; a mocked repository applies none, so the handler correctly returns everything the mock hands it while the fixture still asserts in-memory filtering |
| 5 | `CreditNoteReadTests` (3) · `InvoiceTenantIsolationTests.List_Is_Scoped_To_Caller_Clinic` · `ProcedureTypeTenantIsolationTests.List_Should_Only_Return_Own_Clinic` | same shape — list scoping is now the repository's SQL job. ⚠️ The **by-id** cases in both isolation files pass, so the per-handler guard is intact; only the list assertions drifted |
| 3 | `LiaisonRenderContentTests` | a section heading was reworded « Motif » → « Motif de la liaison » |

None is a production defect; all are the « stale fixture, not a defect » pattern `UnitTests/CLAUDE.md` already
names three times. **Left for a decision** rather than fixed here: it is six unrelated feature areas and would
swamp Part B's review. ⚠️ But it must not be left long — a 24-red baseline means the next session cannot tell new
breakage from old, which is exactly how this went unnoticed.

## Learnings

- **« Set the scope from X » is unimplementable whenever X is behind the filter.** Three of the four deviations
  are one shape: the appointment, the document's clinic and the user's clinic all had to be read *before* the scope
  existed. Worth checking first on any future scope/context work — the fix is either « the caller already knows,
  pass it » or « one explicitly-`IgnoreQueryFilters` projection », never « widen the scope ».
- **A guard that reflects over a namespace must exclude compiler-generated nested types.** In a **Debug** build an
  async state machine is a *class*, not a struct, so `<FlagExpiringStock>d__3` arrived as a candidate whose source
  file does not exist — the guard failed on its own machinery. Found by running it; unreadable from the source.
- **« Could not run the tests » compounds silently.** Part A's blocked runner hid 27 red tests, three of which it
  had caused itself. The cost was not the three assertions — it was that nobody could see them. When the runner
  works, run the **whole** suite and diff against a baseline build; that diff is what made « Part B broke nothing »
  a fact rather than a claim.
- **A doc that describes the inverted contract is worse than no doc.** Three `CLAUDE.md` files asserted the filter
  was fail-open « deliberately ». Left one part longer, the next session would have read them and believed them.

## Next

`/review-story` for Part B, then Part C (steps 11–13 — `provision-clinic`, self-registration closed, the `/join`
explanation; the first part that touches `web/`). ⚠️ Part F's step 17 (`DataProtection:KeyRingPath` required) still
must land **before** Part D.
