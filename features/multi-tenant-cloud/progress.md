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
| C | US-3 | 11–13 | **implemented** (code gate) | 2026-08-05 |
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
names three times.

**Decision: fixed in this session, in a separate commit after Part B's** — because a 24-red baseline means Parts
C–F could not tell new breakage from old, which is exactly how this went unnoticed for two sessions. The suite is
now **1 914 passed / 0 failed**. What the fixes actually were, since the first diagnosis was only half right:

| Area | Real root cause | Fix |
|---|---|---|
| Stock (2) · `CreditNoteReadTests` (3) · `InvoiceTenantIsolationTests` list (1) | **Not** filter drift — an **unstubbed dependency**. The handlers grew reads returning *collections* (`GetDistinctCategoriesAsync`, `IPatientRepository.GetByIdsAsync`, `GetTreatmentPlanLinksAsync`); Moq's default for those is **null**, the handler dereferences it, and the swallowed `NullReferenceException` surfaces as a French `Result.Failure`. Every one failed on `Assert.True(result.IsSuccess)` — which points nowhere near a missing stub | added the three stubs |
| CNAM (7) · Medications (7) | genuine drift: the handlers are now **pass-throughs**, so a mocked repository applies no predicate and the old « hand it the whole catalogue, assert it narrows » cases tested a capability the handler had correctly lost | rewritten to assert each argument reaches the repository **verbatim** (including untrimmed — normalisation is `SearchTerm`'s job *inside* the repository) plus the mapping. The matching itself is SQL and is stated to be outside this project's reach |
| `ProcedureTypeTenantIsolationTests` list (1) | same drift, on an *isolation* test whose premise (« the repository might hand back a foreign row ») was retired twice over: `list-pagination` put the clinic predicate in SQL, and US-2 stopped the filter failing open | asserts the read is **issued with the caller's clinic id** and never with another's — the shape `InvoiceTenantIsolationTests` already used |
| `LiaisonRenderContentTests` (3) | two are a reworded heading (« Motif » → « Motif de la liaison »). ⚠️ The third looked like a **regression** — Part E's rule that free-text `content` was a legacy fallback suppressed when guided fields exist had been deleted — but the frontend's `liaisonSections()` states the new rule in as many words (« a first-class unlabelled section, NOT a legacy fallback ») in the same order with the same headings, so both sides were changed deliberately | headings updated; the precedence case rewritten as `Free_Text_Prose_Coexists_With_Guided_Sections` |

⚠️ **Finding, not fixed:** `LiaisonContent` (server, renders the PDF) and `document-editor-content.tsx`'s
`liaisonSections()` (preview + Word export) carry the **same ten headings in the same order in two places**, with
only a comment asking future editors to keep them identical. That is the shape `CnamClosedSetContractTests` and
`RealtimeResourceResolverTests` exist to prevent, and it is what let the heading rename go unnoticed here. A
parse-the-frontend contract test would close it; out of scope for this story.

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
- **`Assert.True(result.IsSuccess)` is where a missing Moq stub goes to hide.** Six of the 24 looked like filter
  drift and were not: a handler that grew a read returning a *collection* gets **null** from an unstubbed mock,
  dereferences it, and the repo-wide `catch → Result.Failure` convention converts the `NullReferenceException` into
  a French business error. The test then fails on the success assertion, pointing at nothing. Rule of thumb: a
  handler test failing on `IsSuccess` rather than on a value is almost always a fixture that has not kept up with
  the handler's dependencies — check what the handler calls before theorising about behaviour.
- **When filtering moves from a handler into SQL, its handler tests do not become wrong — they become vacuous, and
  they must be rewritten rather than deleted.** What is still worth asserting is that every argument arrives at the
  repository *verbatim*: a silently dropped `category` or a term the handler "helpfully" trims is a real defect that
  nothing else in this project can see, and the old cases did not check it.

---

# Part C — provisioning and onboarding over the internet (steps 11–13)

**Session:** 2026-08-05 · **Branch:** unchanged (`feature/audit-sections-3-to-10`). The first part that touches
`web/`.

## Working tree note (start of session)

⚠️ **A second session was editing this same working tree, on this branch, throughout.** It committed
`web/CLAUDE.md`, `api/…/Application/CLAUDE.md` and `.claude/rules/frontend-web.md` mid-session and left these
dirty, **none of which is staged here**:

| Theirs | |
|---|---|
| modified | `api/ClinicManagement.API/appsettings.json` · `UnitTests/Api/ControllerAuthorizationCoverageTests.cs` · `web/lib/realtime/clinic-hub.ts` · nine `web/lib/api/*.ts` · `web/scripts/check-responsive.mjs` |
| untracked | `API/Controllers/MetaController.cs` · `API/Middleware/ClientVersionMiddleware.cs` · `API/Models/ClientRequirements.cs` · `UnitTests/Api/ClientVersionMiddlewareTests.cs` |

Every `git add` named paths explicitly. **`web/lib/api/users.ts` is in both sets** — it was diffed before staging
and contains only this part's two additions.

Two consequences for the numbers below, stated rather than glossed: the full-suite and full-rebuild figures
**include their code**, so they are joint results; and one `tsc` error seen mid-session
(`clinic-hub.ts` — `shellVersionHeader` undefined) was their in-flight edit and had cleared by the final run.

## What landed

| Step | Deliverable |
|---|---|
| 11 | `Application/Features/Clinics/LocalClinicProvisioning.cs` (+ `LocalClinicRequest`/`ProvisionedClinic`) — **moved** out of `CreateClinicCommandHandler`; `API/Maintenance/ProvisionClinicCommand.cs` + its `Program.cs` registration |
| 12 | 13th capability `AllowsSelfRegistration`; `AuthController.Register` re-gated on it; `GET auth/mode` gains `selfRegistrationEnabled`; `Features/Users/Commands/CreateClinicUserCommand.cs` + `CreatedClinicUserDto` + `POST /api/users` |
| 13 | `web/components/join-unavailable.tsx`, `app/join/page.tsx`, `join-wizard.tsx`, `lib/api/auth.ts`, `lib/api/users.ts`, `components/user-management.tsx` |

Plus the five `CLAUDE.md` files this part falsifies (API, Application, Infrastructure, `web/`, `web/lib/`,
`web/components/`).

## Deviations

Four asked, all confirmed. The first is the one that matters: **the plan's step 11 could not be implemented as
written.**

### DEV-9: `provision-clinic` cannot wrap `CreateClinicCommand` + `ResetUserPasswordCommand`
**Category:** Technical · **Approved:** Yes (asked)
**Plan:** « a new `API/Maintenance/ProvisionClinicCommand.cs`, wrapping `CreateClinicCommand` +
`ResetUserPasswordCommand` ».
**Why neither works.** `ResetUserPasswordCommand` begins at `IClinicContext.GetUserId()` and needs an *existing*
target — in a console verb there is no HTTP context, so it returns « Session invalide ». And
`CreateClinicCommand`'s Local branch refuses outright once **any** user exists (`AnyUserExistsAsync`, AC-1.2a),
so it can create an install's first clinic and never its second — which is the entire purpose of the verb. The
verb's container is also `AddInfrastructure` alone: no mediator.
**Actual:** the clinic + admin construction was **moved** into `Application/Features/Clinics/LocalClinicProvisioning`
and both callers use it — the `PatientFromRequest.Build` precedent, and the repo's own answer to
`fixes-dont-propagate`. Setup keeps the two rules that are genuinely *its* (the bootstrap gate, the password-length
policy); the helper does construction only.
**Impact:** `CreateLocalFirstRunAsync` is 73 lines shorter and gained a duplicate-email check it did not have
(the partial unique index used to surface as a `DbUpdateException`). The clinic id is now minted by the *caller*
so the verb can declare `UseClinic(id)` **before** the writes it covers — a scope declared afterwards covers
nothing.

### DEV-10: a 13th capability, `AllowsSelfRegistration`
**Category:** Technical · **Approved:** Yes (asked)
**Plan:** « `AuthController.Register` → 404 in `HostedMultiTenant` », with no capability named.
**Actual:** a new capability rather than a `Kind == HostedMultiTenant` test at the call site — the exact shape
Part A spent 30 occurrences removing. **R-2 holds**: `SelfHostedLan` ✓, `CloudBrowser` ✗, both exactly as
`UsesLocalAccounts` answered before.
**Impact:** `DeploymentProfileTests`' derived drift guard failed until the matrix row was added, which is the
guard doing its job.

### DEV-11: `GET /api/auth/mode` reports `selfRegistrationEnabled`
**Category:** Technical (API surface) · **Approved:** Yes (asked)
**Plan:** silent on how the browser learns this.
**Why it was needed:** `useSession().mode` comes from the Next server's `AUTH_MODE`, which reads `local` in
**both** account-owning profiles — so `/join` could not distinguish a LAN install from a hosted one. The endpoint
had **zero** frontend callers before this.
**Rejected alternatives:** a sixth `NEXT_PUBLIC_*` deploy key (R-6: fails quietly if omitted, and a second source
of truth the server never checks) and interpreting the 404 after the user has typed an account (§ 0).
`SelfRegistrationGateTests.Self_registration_is_not_derivable_from_the_reported_mode` pins the reason.

### DEV-12: the « Créer un compte » dialog ships with the command
**Category:** Scope · **Approved:** Yes (asked)
**Plan/story:** the frontend surface is « two files (`join-wizard.tsx`, `app/join/page.tsx`) ».
**Actual:** plus `lib/api/auth.ts`, `lib/api/users.ts`, `components/join-unavailable.tsx` and
`components/user-management.tsx`.
**Justification:** with self-registration closed and no create-user UI, a hosted clinic has **no way at all** to
add a colleague — the endpoint would ship with zero callers, the dead-capability shape this repo has flagged
repeatedly (`SetMaterials`, per-doctor working hours, `RecallController`).

### Auto-approved (trivial)

| Deviation | Classification | Reason |
|---|---|---|
| `provision-clinic` requires `--admin-name` | Trivial | `User.CreateLocalUser` throws without a full name, and it is printed on documents — deriving « owner » from `owner@cabinet.tn` would put a fabricated identity on a clinical record |
| Gated on `UsesLocalAccounts` **only**, not `HasLocalDbTooling` | Trivial | That capability is about `pg_dump`/`pg_restore` and is **false** in `HostedMultiTenant`, the one profile this verb exists for. Pinned by a test so the sibling verbs' gate is not copied here later |
| The clinic-code card re-captions itself when self-registration is closed | Trivial | Presentation only. Left alone it instructs an admin to hand out a code that creates nothing — a control that lies |
| `coarse:h-11` on three buttons | Trivial | The device pass measured them at 40/36 px; see the finding below |
| `CreatedClinicUserDto` is its own shape, not `ClinicUserDto` + a field | Trivial | The password is returned once; putting it on the list's type leaves a future `GET` one property away from serving it |

## Quality gate — Part C

| Gate | Result |
|---|---|
| Backend build (`--no-incremental`, scratch `BaseOutputPath`) | **0 errors, 57 warnings** — identical to Part B's post-change baseline |
| New warnings in changed files | **0.** The only warning in a file this part touched is `Program.cs` CS0618 (Hangfire obsolete overload), pre-existing — Part A recorded it at line 316, it is now 326 because the verb block pushed it down |
| Full unit suite | **1 963 passed / 0 failed** (Part B left it at 1 914; +49 here and the parallel session's own) |
| Part C's own classes | **49/49 green** — `LocalClinicProvisioningTests` (12) · `CreateClinicUserCommandHandlerTests` (14) · `ProvisionClinicCommandTests` (7) · `SelfRegistrationGateTests` (8) · `DeploymentProfileTests` (+ the new matrix row, 8) |
| Frontend `npx tsc --noEmit` | **clean** |
| Frontend `npm run check:responsive` | **14/14** — including the parallel session's new `api-headers` check |
| Frontend `npm run build` | **succeeds** |
| Device eye pass | **done, measured** — see below |
| `verify-schema` / `reconcile-money` | **Not applicable** — Part C adds no migration. Both verbs confirmed to **exist** (`API/Maintenance/{VerifySchema,ReconcileMoney}Command.cs`); neither was edited here |

### The device pass was measured, not eyeballed — and `--window-size` lies on Windows

Widths driven: **320 · 390 · 820 · 1180 · 1440**, plus a **844 × 380 landscape phone**, on both surfaces
(`/join`'s closed card and the « Créer un compte » dialog). No `agent-browser` on this machine, so it was done
over **CDP** (`Emulation.setDeviceMetricsOverride` + `Page.captureScreenshot`) from a throwaway Node script,
against `next dev --turbopack` on port 3010 with `AUTH_MODE=local` and a stub `/api/auth/mode` returning
`selfRegistrationEnabled: false`.

⚠️ **Chrome's `--headless --window-size=320,720` does not give a 320 px viewport on Windows** — the window is
clamped to its ~500 px minimum and the screenshot is *cropped* to 320. Every element then appears cut off at the
right edge with a ~26 px left offset, which reads exactly like a real horizontal-overflow defect. Two rounds were
spent on that phantom before `Emulation.setDeviceMetricsOverride` (which sets the real layout viewport) showed
`scrollWidth === innerWidth` at every width. **If a screenshot suggests overflow, measure it before believing it.**

The same run asserted numbers rather than impressions: page `scrollWidth` vs `innerWidth`, every element whose
right edge exceeds the viewport, every control under 44 px, whether the dialog fits, and `<label>`s with no
`for`. Results: **no horizontal overflow at any width**, the dialog fits the viewport at all of them
(320 → full-bleed 320, ≥ 640 → 512 px centred), and **0 labels without `for`**.

**It found one real defect in this part's own code:** « Aller à la connexion » measured **40 px** on a coarse
pointer — `Button size="lg"` is `h-10`. Fixed with `coarse:h-11` (grown rather than `.touch-target`: it is the
page's only control and already full-width, so there is nothing for an invisible overlay to avoid disturbing).
The dialog's two footer buttons were **36 px** (`size="default"` is `h-9`) and got the same treatment. Re-measured
after: 44 px on every coarse viewport, and still 36/40 px at 1180/1440 with a mouse — density preserved, which is
the whole point of gating on the pointer.

**And `check:responsive` caught one before the browser did**: `failed-read-as-empty` flagged
`authApi.getMode().catch(() => {})` in `user-management.tsx`. The check was right — an empty catch is a silent
swallow — so the *code* was fixed (a real body that logs and leaves the caption at its pre-US-3 wording), not the
check.

## Findings recorded, not fixed (out of Part C's scope)

1. **`Button` is under the 44 px coarse floor app-wide**: `default` = `h-9` (36 px), `lg` = `h-10` (40 px), and
   `globals.css`'s coarse floor covers only `input`/`textarea`/`select`/`[data-slot="select-trigger"]` — not
   buttons. Every `size="lg"` submit in the product (login, setup wizard, join) and every dialog footer is
   therefore 36–40 px on a tablet. Fixing it in `ui/button.tsx` is one line and would shift layout on 24 pages
   with no way to eye-check them here, so this part fixed **only its own three controls** and records the rest.
2. **`DialogContent`'s built-in « Fermer » ✕ measures 16 px** on every dialog in the app — a shadcn primitive,
   present long before this part.
3. **`reset-admin-password` refuses in `HostedMultiTenant`**: it gates on `UsesLocalAccounts && HasLocalDbTooling`,
   and the second is false there. Part F's step 17 (amendment M3) is exactly this fix; noted so it is not
   forgotten, since the hosted profile now has clinics whose admin could get locked out.
4. **`POST /api/clinics/join` was left alone.** The plan names only `auth/register`. In `HostedMultiTenant` the
   Cloud join path is unreachable anyway (no Auth0, and an account already has a clinic), so closing it would be
   speculative — but it is the one adjacent door nobody has audited.

## Learnings

- **« Wrap command X » is unimplementable whenever X's guard is the thing you need to skip.** Part B's lesson was
  « set the scope from X » failing because X is behind the filter; this is its sibling. Both were found by reading
  the call site before writing code, and both would have been discovered by the first compile *only* in the sense
  that the code would have compiled and silently refused at runtime.
- **A screenshot is not a measurement.** The Windows minimum-window-width clamp produced a picture of a defect
  that did not exist, twice. `scrollWidth === innerWidth` settled it in one run. Any future device pass on this
  machine should use `Emulation.setDeviceMetricsOverride`, never `--window-size`.
- **A derived guard earns its keep at the moment it is inconvenient.** `DeploymentProfileTests`'
  `Every_capability_is_covered_by_the_matrix` went red the instant a 13th capability was added with no row — which
  is precisely the case a hand-written `[InlineData]` table cannot fail on.
- **Sharing a working tree with another session changes what a green suite means.** 1 963 passing includes their
  code; the honest claim is « nothing here broke » (diffed by name against Part B's 1 914), not « my change is the
  reason it is green ».

## Next

`/review-story` for Part C, then Part D — ⚠️ **but Part F's step 17 (`DataProtection:KeyRingPath` required) must
land first**, per the story's ordering: a PFX password protected by Data Protection makes e-invoice signing depend
on the key ring. Finding #3 above belongs to that same step.
