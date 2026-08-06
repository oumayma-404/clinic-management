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
| D | US-4 | 14 | **implemented** (code gate) | 2026-08-06 |
| E | US-5 | 15 | **implemented** (code gate) | 2026-08-06 |
| F | US-6 | 16–18 | **implemented** (code gate) | 2026-08-05 |

✅ **Part F's step 17 landed before Part D, as the ordering required** — `DataProtection:KeyRingPath` is now required
in `HostedMultiTenant` and fails startup without it, so Part D's PFX password can safely depend on the key ring.

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

---

# Part F — operations, and the runtime nobody is watching (steps 16–18)

**Session:** 2026-08-05 · **Branch:** unchanged (`feature/audit-sections-3-to-10`).

Part F landed **out of order on purpose**: the story's own ordering says step 17's `DataProtection:KeyRingPath`
requirement must precede Part D, because a PFX password protected by Data Protection makes e-invoice signing depend
on the key ring. Parts D and E remain not-started.

## Working tree note (start of session)

⚠️ **A second session was again editing this working tree throughout**, and this time it was building a whole
feature: OS push notifications (`mobile-native-shells` Part 6 — `DeviceRegistration`, `PushDelivery`, four senders,
a Hangfire job, a migration, ~25 files). **Nothing of theirs is in this commit**, and one file needed real care:

| File | What happened |
|---|---|
| `Program.cs` | **shared.** Their hunk registers `dispatch-os-push`; my four are `AddConfiguredHealthChecks`, the auth-attempt capture, `HealthChecks.Register` and the `MigrationLock` wrap. Staged **hunk-selectively** (see below) |
| `appsettings.json` · `Extensions.cs` · `ApplicationDbContext.cs` · `DeploymentProfile.cs` · `NotificationGenerator.cs` · `ReminderSchedule.cs` · the three `SchemaVerification*` files | theirs entirely — **not staged**, not read as mine |

**How `Program.cs` was split**, since `git add -p` is unavailable here and a reconstructed patch was rejected
(`corrupt patch at line 88`): copy the working file aside → delete *their* contiguous block by its own text
(asserting the removed text contains `dispatch-os-push` and contains neither `HealthChecks` nor `MigrationLock`) →
`git add` → restore the copy. The staged version has **zero** occurrences of `dispatch-os-push`; the working tree
keeps their 20 lines unstaged. Verified both ways before committing.

## What landed

| Step | Deliverable |
|---|---|
| 16 | `deploy/Caddyfile` — **`/hub/*`** and **`/health`** routed to `api:5000` before the catch-all, plus the page security headers Caddy is the only thing that can set in this topology; `deploy/docker-compose.hosted.yml` + `deploy/.env.hosted.example` |
| 17 | `LocalDataProtection` — `KeyRingPath` **required** in `HostedMultiTenant`; `Maintenance/MaintenanceDatabase` + the three verbs re-gated (M3); `Startup/MigrationLock` around `MigrateAsync`; `Startup/HealthChecks` + `IFileStorage.ProbeAsync`; `Features/Outbox/GetOutboxDepthQuery` + `OutboxController`; `RateLimiting` re-keyed + `Startup/AuthAttemptAccount`; `SecurityHeadersMiddleware` CSP flag |
| 18 | `deploy/README.md` (new — the hosted operator view), the root guide's **topology table** + a Part F bullet, and the API / Application / Infrastructure / Domain / UnitTests guides |

Step 18's other half — the three `CLAUDE.md` files that described the query filter as fail-open — was **already
done in Part B**, deliberately, because leaving an inverted contract documented for one part longer is worse than
committing the doc with the code that changes it.

## Deviations

### DEV-13: `IFileStorage` gains `ProbeAsync`
**Category:** Technical · **Approved:** self (the story asks for a storage check and names no seam)
**Story:** « `MapHealthChecks("/health")` (DB + storage) ».
**Actual:** a new `ProbeAsync` on the interface, implemented in both backends.
**Why the alternatives were worse:** a round trip through the *existing* methods (upload → download → delete a
sentinel blob) proves more but writes into the clinic's own file store **every few seconds for the life of the
deployment**; and the check cannot live in Infrastructure, which has no ASP.NET framework reference. Reachability is
the failure this exists to catch. It **throws** rather than returning a bool because « storage: false » leaves the
operator exactly as blind as the 503 it produced.
**Impact:** two implementations; no other `IFileStorage` implementers exist (the tests mock it).

### DEV-14: the login limiter is re-keyed in **two** slots, not one
**Category:** Technical · **Approved:** self (the literal reading is a security regression)
**Story:** « re-key `AnonymousAuthPolicy` on the submitted email (+ address as a second dimension) ».
**Why not literally:** a compound `account+address` partition key hands one attacker a **fresh budget per address**
— it fixes the NAT lockout by opening credential stuffing. And .NET 8's `AddPolicy` yields exactly one partition, so
a named policy cannot chain two windows.
**Actual:** the named policy partitions on the **account**; the **global** limiter recognises the same `/api/auth`
prefix and partitions the same request on its **address** with its own ceiling
(`RateLimiting:Auth:AddressPermitLimit`, default 150 = 5 × the per-account limit, same window). Both apply. Without
the second slot the address ceiling on a login would have been the *API* window (600/min), i.e. none.
**Impact:** one new config key; `AuthAttemptAccount` is a new middleware registered immediately before
`UseRateLimiter()`.

### DEV-15: `/health` is routed through Caddy, and there is no container `healthcheck:`
**Category:** Technical · **Approved:** self
The endpoint is mapped at the host root, so behind Caddy it fell through to the catch-all, reached the Next
container and **404'd** — an endpoint nothing in the topology it was built for could reach, which is the
dead-capability shape this repo keeps flagging. A `handle /health` block fixes that. A compose-level `healthcheck:`
was **not** added: `mcr.microsoft.com/dotnet/aspnet:8.0` ships no HTTP client, so it would mean installing `curl`
into the image the `CloudBrowser` profile also builds from. Stated in the compose file rather than left to be
discovered.

### DEV-16: `GET /api/outbox` reports three named sections and an *age*, not five scalars
**Category:** Scope (additive) · **Approved:** self
**Story:** « pending / **blocked** / failed reminder rows, queued e-invoices, queued document emails ».
**Actual:** those five, **plus** a `Due` figure and the oldest waiting row's instant per queue, and
`FailedSinceUtc` beside `FailedRecent`.
**Why:** a depth alone cannot distinguish « three reminders enqueued a second ago » from « three stuck since
yesterday », and R-1's failure mode is precisely the second. A reminder scheduled for next Tuesday is *supposed* to
be waiting, so « pending » is not a backlog — « pending **and due** » is. Three named sections rather than one
uniform row per queue because only reminders can be `Blocked` and only a document email has no scheduled instant; a
`0` standing in for a concept that does not exist is a field the operator must learn to ignore.

### Auto-approved (trivial)

| Deviation | Classification | Reason |
|---|---|---|
| `MigrationLock.{Acquire,Release}Sql` are `static readonly`, not `const` | Trivial | An interpolated string is only a compile-time constant when every hole is a string constant, and `LockKey` is a `long`. It **did not compile** as left by the previous session — two `CS0133`s |
| Ten members widened `internal` → `public` for the tests | Trivial | The test project has no `InternalsVisibleTo`, and the repo's precedent (`StartupDiagnostics`, `TrustPortGate`, `ClientIp`) is public static helpers precisely so they are testable |
| `/health` listed in `ExemptPathPrefixes` although the not-`/api` rule already covers it | Trivial | The exemption must hold because of what the endpoint **is**, not where it happens to be mounted |
| `Security:EnforceCsp` read **once** in the constructor | Trivial | A per-request read would let a mid-session config reload change the header a page's assets are already loading under |
| `AuthAttemptAccount` scoped by the `/api/auth` **prefix**, not by a list of the four `[EnableRateLimiting]` actions | Trivial | A list is a second place to remember; the fifth auth endpoint somebody adds would silently get the API ceiling. Shared with `RateLimiting.IsAnonymousAuthPath` so the two halves cannot disagree |
| `RateLimiting:Auth:AddressPermitLimit` shares `Auth:WindowSeconds` | Trivial | « the address may spend five accounts' worth » is only a comparison while both cover the same period; a fourth knob invites them to diverge |

## Quality gate — Part F

| Gate | Result |
|---|---|
| Backend build (`--no-incremental`, scratch `BaseOutputPath`) | **0 errors, 60 warnings.** Parts B and C left it at 57; the **+3 are the parallel session's** `Features/PushDevices/*` (untracked), confirmed by file name |
| New warnings in changed files | **0.** The warning list was filtered against every file Part F touched — **empty intersection**. The only warning in a shared file is `Program.cs` `CS0618` (Hangfire obsolete overload), pre-existing since Part A |
| Part F's own classes | **126/126 green** — `AuthAttemptAccountTests` · `RateLimitingTests` (+9 US-6 cases) · `HealthCheckTests` · `SecurityHeadersMiddlewareTests` · `MigrationLockTests` · `MaintenanceDatabaseTests` · `LocalDataProtectionTests` (+6) · `LocalDiskFileStorageTests` (+3) · `GetOutboxDepthQueryHandlerTests` |
| Full unit suite | **2 078 passed / 0 failed** (Part C left it at 1 963; +115 here and the parallel session's own) |
| Frontend gate | **Not applicable** — Part F changes no `web/` file. Both scripts re-confirmed present in `web/package.json` |
| `verify-schema` / `reconcile-money` | **Not applicable** — Part F adds no migration. Both verbs were *edited* here (M3) and their new gate is covered by tests |

### Two red tests, and they were Part F's own debt

The full suite came back **2 red** before this session's own classes were added to it:
`{ReconcileMoney,VerifySchema}CommandTests.Run_refuses_and_returns_nonzero_when_not_in_local_mode` still asserted
the refusal message names `CloudBrowser` — which is exactly what **M3 retired**, since those verbs now gate on the
connection string. Part B had *strengthened* those two assertions (`nameof(DeploymentKind.CloudBrowser)`), so the
test was right until this part deliberately changed the contract. Both were rewritten as a `[Theory]` over
`Cloud`/`Local`, asserting the refusal happens in **every** profile and that it names the config key an operator
must set — a stronger claim than the one it replaced, and the one M3 actually makes.

### ⚠️ The test runner works — and where the output goes is what decides it

Every recipe recorded in the `smart-app-control-blocks-tests` memory failed this session: `dotnet vstest` over a
scratchpad `OutDir` **and** over a scratchpad `BaseOutputPath`, then a throwaway reflection-based mini-runner (its
generated `.exe` was blocked, and running the `.dll` through `dotnet` then blocked each referenced project DLL in
turn) — all `0x800711C7`. What worked was building the **same assembly into the repository tree**:

```
dotnet build api/ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj -p:OutDir=<repo>/api/.testrun/
dotnet vstest api/.testrun/ClinicManagement.UnitTests.dll
```

Whole suite, SAC on, dev API running. **Smart App Control's verdict depends on the output *location*, not on the
runner** — which no prior session had isolated, because the two blockers look like one: SAC is `0x800711C7` at
**load**, while the running dev API holding `bin/Debug` is `MSB3021 … locked by ClinicManagement.API (PID 9364)` at
**build**, and a redirected output fixes only the second. Recorded in `UnitTests/CLAUDE.md` and in the memory file.
⚠️ `api/.testrun/` is **not** covered by `.gitignore` — delete it after a run.

## Findings recorded, not fixed (out of Part F's scope)

1. **The hosted profile served page responses with no security headers at all** — `SecurityHeadersMiddleware` covers
   what Kestrel serves (behind Caddy: `/api/*` only) and `web/next.config.ts` emits headers only when
   `AUTH_MODE != local`, which is false here. Already closed in the Caddyfile; recorded because the *reason* is a
   structural gap in a two-sided condition, and that shape can reappear anywhere the two sides are set separately.
2. **`restore-backup` has no hosted path at all.** Its gate is honest, but « restore one clinic, or restore at all,
   in a container topology » remains unanswered — the story names it as a hazard belonging to the topology rather
   than to a part.
3. **No `/api/outbox` frontend surface.** Deliberate here (the consumer is the operator, like `/health`), but if a
   « files d'attente » admin screen is ever wanted, the read already returns everything it would need.
4. **The three verbs are now reachable in `CloudBrowser` too.** M3 gates them on the connection string, so an Auth0
   operator running `verify-schema` gets past the gate — harmless and arguably correct, just wider than before, and
   worth knowing before someone reads the old « Local-only console verbs » framing.

## Learnings

- **« Re-key X on Y » can be a security regression when read literally.** A compound `account+address` key fixes a
  lockout by opening credential stuffing, and the framework's one-partition-per-policy shape hides that from you
  until you try to chain two windows. The fix was to use the two slots that already existed rather than to invent a
  third.
- **An endpoint mapped at the host root does not exist behind a reverse proxy until the proxy is told.** `/health`
  would have 404'd in the exact topology it was written for — the same defect, in the same file, as the `/hub/*`
  route this part opened with. Two occurrences in one part is a pattern: **every root-mounted route needs a Caddy
  block**, and the Caddyfile's header comment now lists all three.
- **A guard can only assert what is reachable — so assert what a mistake would *look like*.** No test here can take
  a real advisory lock, but two things a wrong implementation would show are reachable: both statements naming one
  fixed key, and `pg_advisory_lock` rather than the `xact` variant. The third property — that the migration is
  *inside* the wrap — is only visible in `Program.cs`'s own source, so that is where it is asserted.
- **Sharing a tree with a session that is mid-feature is different from sharing with one that is finishing.** Part C
  shared a tree with docs edits; this part shared it with a 25-file feature touching `Program.cs`,
  `ApplicationDbContext` and `Extensions.cs`. `git diff HEAD --numstat` is not enough when the overlap is *inside* a
  file — the answer was hunk-level staging with an assertion that the removed text was theirs and not mine, checked
  in both directions before the commit.
- **A doc gate can be « already done » for a good reason, and should say so.** Step 18's filter-contract half landed
  in Part B on purpose: a `CLAUDE.md` asserting the inverted contract is worse than no doc, so it could not wait for
  the part that owns the docs step. Recording *that* is what stops a later reader treating it as skipped.

## Next

`/review-story` for Part F, then **Part D** (US-4, per-clinic TTN e-invoicing identity — now unblocked: the key ring
is required, so a PFX password protected by Data Protection is safe) and **Part E** (US-5, per-clinic storage key
prefix). Both are additive; A–C + F are the story's whole security and operations thesis.

---

# Part D — per-clinic secrets, and the certificate that was one practice's for everybody (step 14)

**Session:** 2026-08-06 · **Plan:** US-4 · **Status:** implemented (code gate)

Part D landed **after** Part F, as the ordering required: `DataProtection:KeyRingPath` is required in
`HostedMultiTenant` since US-6, so a PFX password protected by Data Protection can now depend on a key ring that
survives a redeploy. Landing D first would have meant a rotated key silently breaking signing for every clinic.

## Working tree note (start of session)

The branch opened carrying **~30 files of another session's in-flight work** (`mobile-native-shells` Part 6, OS
push): `Program.cs`, `Extensions.cs`, `ApplicationDbContext.cs`, `DeploymentProfile.cs`, the four
schema-verification files and an unapplied migration. None was staged.

⚠️ **Mid-session that session committed as `999b877`, and its commit contains one of Part D's changes.** My
`DeploymentProfile.cs` edit (the 14th capability, `SharesInstallWideTtnIdentity`) was already in the working tree
when they staged that file, and their addition (`PermitsOsPush`) and mine land in overlapping hunks — so they took
both and added the matching `DeploymentProfileTests` row to keep their build green, saying so in a comment at the
row. **Recorded rather than corrected**: the code is right, the test row is right, and unpicking one capability
out of a landed commit to re-land it here would be churn with a chance of breaking their build. Part D's own
commit therefore does **not** contain the capability it introduced — read `999b877` for it.

The lesson generalises. Part F learned that hunk-level staging is needed when two sessions share a *file*. This
part learned the other half: **when you share a file with a session that is about to commit, your uncommitted
edit is theirs to sweep up, and the only defence is to notice and record it.**

## What landed

| Deliverable | Where |
|---|---|
| Four `Clinic` columns + `SetTtnIdentity` | `Domain/Entities/Clinic.cs`, `Persistence/Configurations/ClinicConfiguration.cs` |
| The migration | `Migrations/20260806075521_AddPerClinicTtnIdentity` — four nullable columns, no backfill, no index |
| The precedence rule, once | `Application/Common/Interfaces/ITtnIdentityProvider.cs` + `Infrastructure/Services/TtnIdentityProvider.cs` |
| The resolved identity | `ResolvedTtnIdentity` + `TtnIdentitySource` in `Common/Models/EInvoiceModels.cs` |
| Its own Data-Protection purpose | `ITtnSecretProtector` / `TtnSecretProtector` (`ClinicManagement.TtnSecrets.v1`) |
| Signer takes the identity | `IEInvoiceSigner.Sign(xml, identity)`, `XadesEInvoiceSigner` (no longer reads config or disk) |
| TTN client takes the identity | `ITtnClient.SubmitAsync(…, identity, …)`, `HttpTtnClient` authenticates as the clinic |
| Resolved **once** per dispatch | `EInvoiceService.DispatchAsync` |
| The 14th capability | `SharesInstallWideTtnIdentity` — ⚠️ landed in `999b877`, see above |
| The schema gate | `verify-schema` → `ttn-identity-is-complete` |

**The defect it closes was documented in the code long before it was fixed.** `XadesEInvoiceSigner`'s own
docstring read: « KNOWN CONSTRAINT (single-cert-per-install): … in a multi-clinic install every clinic's
e-invoices are signed with the same qualified identity … before Production multi-tenant use, key the
cert/password lookup by clinic id ». A TEIF signature attests **who issued** the invoice and TTN validation is
irreversible, so on a hosted backend that is a false legal declaration that cannot be withdrawn — not a
configuration inconvenience.

### Why a provider and not two `if`s

The plan names `XadesEInvoiceSigner` + `TtnConfig`. Taken literally that is **two** copies of the precedence
rule, because the identity has two consumers — the signer wants the certificate, the production client wants the
credentials — and `fixes-dont-propagate` is this repository's dominant defect shape. One provider, and the two
consumers are handed the **same** resolved object: signing with clinic A's certificate while submitting under
clinic B's account is now not a state the code can reach.

⚠️ **The certificate arrives as bytes, already fetched.** The I/O (a DB row, a blob download or a file read)
belongs to the resolver, which must be async for the DB read anyway; signing stays a synchronous pure transform.
That is also what made the signer's happy path testable for the first time — see the quality gate below.

## Deviations

### DEV-17: the two ciphertext columns carry the `Encrypted` suffix
**Date:** 2026-08-06 · **Story:** 1, Part D · **Category:** Technical · **Approved:** Yes (asked)
**Original Plan:** `TtnApiSecret`, `TtnCertificatePassword`.
**Actual Implementation:** `TtnApiSecretEncrypted`, `TtnCertificatePasswordEncrypted`.
**Justification:** all three existing Data-Protection columns are suffixed (`SmsApiKeyEncrypted`,
`WhatsAppAccessTokenEncrypted`, `SmtpPasswordEncrypted`) and the suffix is load-bearing — it is what stops a later
reader treating the column as plaintext. The plan's names would have made these the only two ciphertext columns
in the schema whose names do not say so.
**Impact:** naming only.

### DEV-18: `ITtnIdentityProvider`, and two interfaces gain a parameter
**Date:** 2026-08-06 · **Story:** 1, Part D · **Category:** Technical · **Approved:** self (forced by the plan's own goal)
**Original Plan:** « `XadesEInvoiceSigner` + `TtnConfig` — take the clinic's cert ».
**Actual Implementation:** a provider holding the rule; `IEInvoiceSigner.Sign` and `ITtnClient.SubmitAsync` take a
`ResolvedTtnIdentity`. Neither interface carried a clinic at all, so the plan's sentence was not implementable
without *some* signature change; the choice was where the rule lives, and two copies was the alternative.
**Impact:** three implementations and four test classes updated. `TtnConfig` keeps its accessors as the
*per-install* half and its docstring now says so.

### DEV-19: `CloudBrowser` loses the per-install fall-back — a shipped profile changes behaviour
**Date:** 2026-08-06 · **Story:** 1, Part D · **Category:** Scope · **Approved:** Yes (asked)
**Original Plan:** « fall back to the per-install one **only** in `SelfHostedLan` » — silent on what that means
for `CloudBrowser`, which is also multi-clinic and has been leaning on the per-install certificate all along.
**Actual Implementation:** the literal reading. `SharesInstallWideTtnIdentity` is true for `SelfHostedLan` only.
**Justification:** the fall-back is correct exactly where « per install » and « per clinic » name the same thing.
Keeping it in `CloudBrowser` would perpetuate the defect Part D exists to close, in the profile most likely to
hit it. ⚠️ **R-2 still holds** — the capability's truth table (`true, false, false`) is `IsLocalMode`'s, so
`DeploymentProfileTests`' derived matrix passes untouched; what changes is that a *new* question is now asked
where previously there was no branch.
**Impact:** a `CloudBrowser` clinic with no certificate of its own is now refused. The refusal is **loud**: an
`InvalidOperationException` with a French message naming what to provide, which `EInvoiceService`'s existing
catch turns into a queued retry with the reason on the row and a visible backlog in `GET /api/outbox`. Nothing is
signed with the wrong key, and nothing is silently dropped.

### DEV-20: a second secret protector rather than reusing the reminder one
**Date:** 2026-08-06 · **Story:** 1, Part D · **Category:** Technical · **Approved:** self
`IReminderSecretProtector` would have worked mechanically, but a Data-Protection **purpose** exists precisely to
stop ciphertext written for one subsystem being read by another; decrypting a signing-certificate password with
the reminder purpose is the single misuse that model is designed to prevent. `TtnSecretProtector` is 20 lines and
mirrors it exactly. The *rule* that must not be duplicated is the precedence rule, and it is not.

### DEV-21: `verify-schema` gains `ttn-identity-is-complete`
**Date:** 2026-08-06 · **Story:** 1, Part D · **Category:** Technical · **Approved:** self
Not in the plan. Added because of what the write-path decision below leaves behind: `Clinic.SetTtnIdentity`
refuses half an identity, but **nothing calls it yet**, so rows arrive by hand. Unlike its siblings on that list
this check is not a backstop behind an application guard — it is the only guard there is. It follows
`cheque-details-only-on-cheques` exactly: the columns' *shape* is diffed against the catalog for free, so only
the model-inexpressible invariant is named.

### Auto-approved (trivial)

| Deviation | Classification | Reason |
|---|---|---|
| `Clinic.SetTtnIdentity` added with no production caller yet | Trivial | The entity's own API and the only place the half-identity rule can live; exercised by tests, and the write path's first caller. Same history as `ClearGoogleCalendarConnection` |
| `TtnConfig` docstring rewritten | Trivial | Its four identity accessors are now a fall-back, not the answer; the endpoint/outbox accessors are unaffected |
| `TtnIdentityProvider` registered plainly rather than via a factory | Trivial | It was written with an explicit factory to avoid depending on the parallel session's uncommitted `AddSingleton(profile)`; once `999b877` landed that line, the plain registration is what the file already does everywhere else |

## The write path is deliberately absent, and this is the honest statement of it

**Nothing in the product writes these four columns.** That was asked and confirmed: the plan scopes Part D to the
columns plus the signer, the story's file list agrees, and a certificate *upload* is a storage-key concern that
belongs with Part E. Until an admin surface exists an identity is installed by hand.

That is the `Invoice.AppointmentId` shape — « a column nobody writes is a column nobody validates » — so it is
mitigated rather than merely noted:

- the **resolver validates everything it reads** (the blob must download, each secret must decrypt, a certificate
  key with no blob behind it refuses) with a distinct French reason per failure, and
- **`verify-schema` checks the invariant** (DEV-21), which is what catches a half-filled row *before* an invoice
  is queued rather than hours later at dispatch.

Owed follow-up: the admin surface (username + secret on the existing admin-only TTN settings, and a certificate
upload). ⚠️ When it lands it must go through `Clinic.SetTtnIdentity` and `ITtnSecretProtector` — writing the
columns directly would reintroduce exactly the plaintext-at-rest and half-identity states those two exist to
prevent.

## Quality gate — Part D

| Gate | Result |
|---|---|
| Backend build (`--no-incremental`, scratch `BaseOutputPath`) | **0 errors.** 30 warning-bearing files; the only one Part D touches is `Clinic.cs`'s pre-existing `CS8618` on `Name`'s private EF ctor — same warning, line number shifted by the added properties. **0 new warnings** (all four new properties are nullable) |
| Part D's own classes | **109/109 green** — `TtnIdentityProviderTests` (new) · `XadesEInvoiceSignerTests` (rewritten) · `ClinicEInvoiceSettingsTests` (+7) · `SchemaVerificationServiceTests` (+2) · `SandboxTtnClientTests` · `CancelledInvoiceIsNotDispatchedTests` · `DeploymentProfileTests` |
| Full unit suite | **2 143 passed / 0 failed** (Part F left it at 2 078; the delta is Part D's own plus `999b877`'s Part 6 classes) |
| `verify-schema` before/after, diffed | **Ran.** `ttn-identity-is-complete` flips *not applicable* → « 0 clinic(s) hold half a TTN identity », proving the columns landed and the check reads them. **The four columns produce no drift of their own** — plain nullable columns declare no index or FK, so the model↔catalog diff has nothing to report. Drift went 9 → 1; the 8 cleared are `999b877`'s push tables (its migration was unapplied on this dev DB), and the surviving one, `overlapping-appointment-pairs: 3`, is **byte-identical in the before snapshot** — pre-existing dev data |
| `reconcile-money` before/after, diffed | **Ran. Empty diff**, which is the story's stated requirement: US-4 touches nothing financial |
| Frontend gate | **Not applicable** — Part D changes no `web/` file (`git status -- web/` empty). Both scripts re-confirmed present in `web/package.json` |

### The signer's happy path is covered for the first time

`XadesEInvoiceSignerTests` used to say « the positive signing path needs a real qualified PFX
(integration/manual) » and asserted only fail-fast. That was true *because* the signer read a configured path.
Now that it takes bytes, a self-signed pair generated in-process exercises the whole signature — and the case
US-4 actually rests on is assertable: **`Sign_Embeds_The_Certificate_It_Was_Given`** signs the same TEIF under two
identities and compares the thumbprints embedded in the two `KeyInfo` blocks. A signer that quietly went on
reading one configured path would pass every other test in the file and fail that one.

⚠️ `TtnIdentityProviderTests` pins `Ttn:CertPath` to a path that cannot exist. Left at its default it resolves
`.local/teif-signing.pfx` under the **test assembly's own directory**, so the fall-back cases would pass or fail
depending on what happens to be on the machine.

## Findings recorded, not fixed (out of Part D's scope)

1. **`HttpTtnClient`'s endpoint is still per install, and that is correct** — TTN is one national platform, so
   every clinic posts to the same URL. Only the *account* is per clinic. Worth knowing before someone "completes"
   the per-clinic move by adding a URL column.
2. **`TtnCertificateKey` is a flat storage key today.** Part E prefixes *new* keys with `clinics/{clinicId}/`;
   since nothing writes this column yet, every certificate ever stored in it will already be prefixed, so it
   needs no backfill consideration.
3. **The e-invoice artifacts (`{clinicId}/e-invoices/…`) were already clinic-prefixed** by `EInvoiceService`, and
   by a different convention from the one Part E introduces (no `clinics/` segment). Not touched here; noted so
   Part E decides deliberately rather than discovering it.

## Learnings

- **A defect the code documents is still a defect.** The single-cert constraint had a precise, correct paragraph
  in `XadesEInvoiceSigner`'s docstring naming the exact fix, and it survived every review for the life of the
  feature. Writing down « this is wrong for multi-tenant » does not make it less wrong — it makes it *cheaper to
  fix later*, which is only worth something if somebody does.
- **« Take the clinic's cert » had no implementable literal form.** Neither `IEInvoiceSigner.Sign` nor
  `ITtnClient.SubmitAsync` carried a clinic, so the plan's sentence forced a signature change no matter what; the
  only real choice was whether the precedence rule lived in one place or two. Reading a plan step for what it
  *cannot* mean is faster than discovering it at the first compile error.
- **A capability whose truth table matches `IsLocalMode` can still change behaviour.** `SharesInstallWideTtnIdentity`
  is `(true, false, false)` — the old boolean exactly — so R-2's derived matrix passes untouched. What changed is
  that a *new question* is asked where there was previously no branch at all. R-2 constrains the answers, not the
  number of questions, and it is worth being explicit about which of the two a part is doing.
- **Sharing a tree with a session that is about to commit is different again.** Part F's lesson was hunk-level
  staging for a shared file. The new one: an uncommitted edit of yours in a file *they* stage goes into *their*
  commit, and there is no defence except checking `git log` when the diff you expected shrinks. `git diff` quietly
  shrinking from 143 lines to 14 was the tell.

## Next

`/review-story` for Part D, then **Part E** (US-5, clinic-prefixed storage keys) — the last unbuilt part. Its
`DoctorCachetKey` pitfall and finding 3 above are its two known traps.

---

# Part E — clinic-prefixed storage keys, and the second convention nobody had chosen (step 15)

**Session:** 2026-08-06 · **Plan:** US-5 · **Status:** implemented (code gate) — **the story's last unbuilt part**

## Working tree note (start of session)

The tree was **clean** at branch level when the session opened. Four `web/` files went dirty **during** it —
`components/connectivity-indicator.tsx`, `components/document-editor-content.tsx`, `lib/api/client.ts`,
`lib/connectivity/connectivity.tsx` — carrying `mobile-native-shells` AC-64 wording changes from a parallel
session. **None was staged**; Part E touches no frontend file at all.

## What landed

| Deliverable | Where |
|---|---|
| The single key composer | `Infrastructure/Storage/ClinicStorageKey.cs` — `clinics/{clinicId}/` + relative path or generated leaf |
| Both upload overloads require the clinic | `Application/Common/Interfaces/IFileStorage.cs` |
| Both backends compose through it | `MinioFileStorage`, `LocalDiskFileStorage` |
| The four flat-key callers | `UploadPatientFileCommand`, `Create`/`UpdateMedicalDocumentCommand`, `QueueDocumentEmailCommand` |
| The four already-prefixed callers, unified | `Create`/`UpdateClinicCommand` (logo), `UpdateDoctorProfileCommand` (cachet), `EInvoiceService` (e-invoice artifacts) |
| Superseded-cachet cleanup | `UpdateDoctorProfileCommand` — post-commit, best-effort |
| The derived guard + the composer's rules | `UnitTests/Infrastructure/Storage/ClinicStorageKeyTests.cs` (12 cases) |
| Prefix + legacy-key cases on the real filesystem | `LocalDiskFileStorageTests` (+2) |

### The defect was two conventions, not only the flat keys

The plan names « the default key becomes `clinics/{clinicId}/…` », which reads as four call sites. What the code
actually held was **two** answers to « which clinic owns this blob »: four sites wrote a flat
`{guid}-{timestamp}` with no clinic in it (patient files, the two medical-document PDF paths, the document-email
attachment) and four prefixed a path of their own with a bare `{clinicId}/` (logo, cachet, e-invoice artifacts) —
the convention Part D's finding 3 flagged and deliberately left for this part to settle. Prefixing only the first
group would have shipped the second convention permanently.

So the composition **moved** into `ClinicStorageKey` rather than being added beside what was there, and the four
custom-path callers now pass a path **relative to their clinic** (`logo`, `doctors/{id}/cachet`,
`e-invoices/{id}-signed.xml`). Same shape as Part D's `ITtnIdentityProvider` and, before it,
`PatientDuplicateIndex`: this repository's dominant defect is a correct rule wired to some of its call sites.

### The enforcement is the signature

Both `UploadAsync` overloads take a required `Guid clinicId`, so **an unprefixed key is not something a caller can
write** — the compiler is the guard, and every existing call site had to be revisited to build. What a test can
still add is the case the compiler cannot see: a *third* overload added later without one.
`Every_Upload_Overload_Requires_A_Clinic_Id` reflects over `IFileStorage` for that, and it was **proved red** by
temporarily adding such an overload (it failed naming the offending parameter list; the probe was reverted and the
suite re-run green).

## Deviations

### DEV-22: one convention, not one prefix — the four custom-path callers change too
**Date:** 2026-08-06 · **Story:** 1, Part E · **Category:** Technical · **Approved:** Yes (asked)
**Original Plan:** « `MinioFileStorage` — default key becomes `clinics/{clinicId}/{guid}-{timestamp}` », plus
« pass the clinic id to every custom-path caller ».
**Actual Implementation:** every new key is `clinics/{clinicId}/…`, composed in one place for both backends; the
four callers that supplied their own path drop their `{clinicId}/` segment and pass a clinic-relative one.
**Justification:** the literal reading leaves `{clinicId}/logo` beside `clinics/{clinicId}/{guid}` — two spellings
of the same fact, which is what the part exists to remove, and it makes the acceptance criterion (« new storage
keys are `clinics/{clinicId}/…` ») only half-true. It also keeps the customPath overload clinic-agnostic, so a
future upload site could still write an unprefixed key.
**Impact:** the logo, cachet and e-invoice artifact keys change format for **new** uploads. Readers are untouched
and take stored keys verbatim, so nothing existing breaks. ⚠️ The logo and cachet keys were **deterministic** and
overwrote in place; a changed key would leave the old blob behind, so `UpdateDoctorProfileCommand` gained a
post-commit best-effort delete of the superseded one (the logo path already deleted the old key before uploading,
and a new clinic has nothing to orphan).
⚠️ The plan's parenthetical « (`patient-files`, cachet upload) » is **wrong about patient files** — that site uses
the *default* overload and supplies no path. Every `IFileStorage` call site was enumerated rather than trusted.

### DEV-23: the clinic is a parameter, not the ambient tenant scope
**Date:** 2026-08-06 · **Story:** 1, Part E · **Category:** Technical · **Approved:** self
The obvious implementation reads the clinic off `ITenantScope` inside the storage: zero call-site churn, and it
works for every HTTP path. It fails **silently** for the one that matters — `EInvoiceService` uploads its signed
XML and TTN receipt from the outbox job, which runs `UseSystemWide` and has **no clinic in scope at all** — so
e-invoice artifacts, the one class of blob with legal weight, would be the class written unattributed.
`PdfGenerationJob` would have worked (it declares `UseClinic`), which is what makes the trap plausible: three of
the four no-request paths do the right thing.

### DEV-24: `UpdateMedicalDocumentCommand` takes the clinic from the document's patient
**Date:** 2026-08-06 · **Story:** 1, Part E · **Category:** Technical · **Approved:** self
Not `user.ClinicId`: on this handler `user` is legitimately **null**, because the unauthenticated
`PdfGenerationJob` feeds a stored document back through it to attach the rendered PDF. `document.Patient.ClinicId`
is the value the job itself resolved to set its scope, and in the authenticated path the handler has already
asserted the two are equal. A null patient (unreachable — the job throws first) refuses rather than composing
`clinics/00000000-…/`.

### Auto-approved (trivial)

| Deviation | Classification | Reason |
|---|---|---|
| Traversal refusal moved into `ClinicStorageKey` | Trivial | Same exception type and the local-disk `ResolveWithinBase` guard is untouched; MinIO now refuses the identical path instead of storing the literal name, so the two backends cannot disagree about what a key means |
| `ClinicStorageKey` is `public`, not `internal` | Trivial | Infrastructure has no `InternalsVisibleTo`, and both `DoctorCachetTests`' upload echo and `ClinicStorageKeyTests` compose through the real thing rather than retyping the format |
| `Guid.Empty` refused in the composer | Trivial | Internal invariant with no caller that can reach it today; the alternative is a folder nothing ever looks in, discovered months later with no way back to the write |

## Quality gate — Part E

| Gate | Result |
|---|---|
| Backend build (`--no-incremental`, scratch `BaseOutputPath`) | **0 errors, 0 new warnings.** 57 distinct pre-existing warnings across **30 files** (the same 30 Part D measured) — **none** of them in a file this part touched |
| Full unit suite | **2 157 passed / 0 failed** (Part D left it at 2 143; the delta is exactly Part E's 12 + 2 new cases) |
| The derived guard is proved red | **Yes** — a third `UploadAsync` overload with no `Guid` was added temporarily; `Every_Upload_Overload_Requires_A_Clinic_Id` failed naming its parameter list, the probe was reverted and the suite re-run green |
| `verify-schema` before/after | **Not applicable, and the verb exists** (Part D ran it this week): US-5 adds **no migration** and touches no entity, configuration or model snapshot — `git status` over `Domain/`, `Persistence/Configurations/` and `Migrations/` is empty |
| `reconcile-money` before/after | **Not applicable, same verb, same reason**: no money path is touched. The one financial file changed is `EInvoiceService`'s artifact *path* |
| Frontend gate | **Not applicable** — Part E changes no `web/` file. (The four dirty ones are another session's; see the working-tree note) |

## Findings recorded, not fixed (out of Part E's scope)

1. **There is no backfill and there will not be one** (amendment M2). A hosted deployment's object store will hold
   both shapes indefinitely: flat pre-Part-E keys and `clinics/…` ones. That is deliberate — the rows point at
   their own keys — but anyone reasoning about the bucket by *listing* it (a retention sweep, a per-clinic export,
   a future per-clinic restore, which is owed decision #2) must handle both, and cannot infer a blob's clinic from
   its key alone.
2. **`Clinic.TtnCertificateKey` needs no consideration after all.** Part D's finding 2 flagged it; since nothing
   writes that column yet, every certificate ever stored in it will already be prefixed by the caller that
   eventually uploads one — which must go through `IFileStorage.UploadAsync` and therefore cannot produce a flat
   key.
3. **`ProbeAsync` writes outside any clinic.** `LocalDiskFileStorage`'s health probe creates `.health-{guid}` at
   the base of the store, not under `clinics/`. Correct — it is an infrastructure check, not a tenant's blob — but
   worth knowing before someone writes a sweep that assumes everything under the root belongs to a clinic.

## Learnings

- **A plan step that names one file can still be about a convention.** « `MinioFileStorage` — default key becomes
  `clinics/{clinicId}/…` » reads as a one-line change; enumerating the call sites first turned it into « this
  question has two answers and the plan only addresses one of them ». Part D's finding 3 had already spotted it
  and wrote it down for whoever came next, which is the only reason it was a decision rather than a discovery.
- **The strongest guard here was a signature, not a test.** Requiring `Guid clinicId` on both overloads made every
  call site a compile error and made an unprefixed key unwritable. A test can only add what the compiler cannot
  see — a *future* overload — which is exactly what the reflection guard covers, and nothing more.
- **« Read it off the ambient scope » is the shape that works everywhere except where it matters.** Three of the
  four no-HTTP-context upload paths carry a `UseClinic`; the fourth is the e-invoice outbox under `UseSystemWide`.
  Convenience implementations tend to fail on the *one* caller with a different shape, and the failure here would
  have been silent and on the blobs with legal weight.
- **The plan's own parenthetical was wrong, and enumerating cost less than trusting it.** « pass the clinic id to
  every custom-path caller (`patient-files`, cachet upload) » names a call site that supplies no custom path and
  omits two that do. A grep of `UploadAsync(` took a minute.
