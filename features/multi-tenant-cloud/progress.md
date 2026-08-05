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
| B | US-2 | 5–10 | not-started | — |
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

## Next

`/review-story` for Part A, then Part B (steps 5–10 — `ITenantScope`, and the query filter starts refusing).
Part B is the plan's whole security thesis and is the largest part.
