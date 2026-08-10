# Implementation Progress — Console éditeur (`platform-console`)

**Story:** [story-1-full-platform-console.md](./story-1-full-platform-console.md)
**Branch:** `feature/platform-console` — **local only**, no remote, nothing pushed
**Worktree:** `.claude/worktrees/platform-console/` (see the concurrency note below)

## Status

| Part | Increment | Blocked? | Status |
|------|-----------|----------|--------|
| 1 | Reach the console and sign in | No | **implemented** |
| 2 | The portfolio, and the counters behind it | No | not-started |
| 3 | One cabinet's detail | No | not-started |
| 4 | Record a payment and unlock the cabinet | Companion feature | not-started |
| 5 | Correct a mistake | Companion feature | not-started |
| 6 | Suspend for abuse | Companion feature | not-started |
| 7 | Verification, runbook and the promise | Follows 4–6 | not-started |

Part 1 was the agreed scope for this session; the user chose it over Parts 1–2 or 1–3 because each part is a
commit boundary and a part boundary is the resumption point.

## ⚠️ Working-tree note (start of session) — a second session on the same tree

The session opened on `feature/windows-desktop-app`, which carried **22 uncommitted files** of unrelated
branding/icon work (`web/public/*.png`, `web/branding/icon.svg`, `desktop/`, `mobile/`). None of it is this
story's and none of it was staged.

**Then a larger problem surfaced.** A **second agent session was actively writing `features/clinic-subscription/`
into that same working tree** — `LocalClinicProvisioning.cs` had been modified 37 seconds before it was noticed —
and the tree **did not compile**, because their refactor of `LocalClinicProvisioning.ProvisionAsync` had two call
sites not yet updated. Their errors, mid-edit. Concretely that meant:

- the quality gate could not be run at all, so nothing here could be verified or committed;
- five files are needed by **both** features (`DeploymentProfile.cs`, `Infrastructure/Extensions.cs`,
  `ApplicationDbContext.cs`, `DeploymentProfileTests.cs`, the EF model snapshot) — and a full-file write to
  `AuditActorProvider.cs` had already come close to clobbering their work;
- two hand-written migrations against one uncommitted snapshot would have duplicated each other.

**Resolution (user's call): a git worktree, and the main checkout left alone.** `feature/platform-console` was
branched from `50b6f1c` into `.claude/worktrees/platform-console/`, this story's files were moved across, and
**every edit of mine was reverted out of the main tree** — `git checkout --` for the six files only this story had
touched, and the specific hunks removed by hand from the three genuinely shared ones. The main tree was then
verified to carry **only** the other session's work (`grep` for every symbol of this feature: none).

Consequences to know when these two branches meet:

- **Merge conflicts are expected and small**: `DeploymentProfile`'s constructor and matrix gain a capability from
  each feature (`RequiresSubscription` theirs, `ServesPlatformConsole` mine), and `DeploymentProfileTests`'
  `ExpectedMatrix` + `hostedOnlyCapabilities` gain a row each. Both are mechanical.
- **`Infrastructure/Extensions.cs` and `ApplicationDbContext.cs`** gain an independent registration block each.
- **The migrations do not conflict** — different tables, and this one was scaffolded against a model that contains
  none of theirs. Whichever lands second must be re-scaffolded or hand-checked against the merged snapshot.
- This branch is based on `50b6f1c` and therefore does **not** contain `clinic-subscription`. Parts 1–3 have no
  dependency on it, which is what made the split safe.

## Environment notes

- **Smart App Control is intermittent and location-sensitive.** `dotnet ef` refused to load
  `ClinicManagement.Infrastructure.dll` with `0x800711C7` while the worktree sat at
  `Desktop/clinic-console-wt`; **moving the worktree under `.claude/worktrees/` fixed it immediately** and every
  `dotnet ef` command has worked since. That is consistent with the suite guide's note that SAC's verdict depends
  on where the assembly is — and the repo's eleven existing worktrees all live there.
- **The design-time factory ignores `ConnectionStrings__DefaultConnection`.** `ApplicationDbContextFactory` reads
  `appsettings.json` + `appsettings.Development.json` only, so a `dotnet ef database update` with the env var set
  went to the **shared dev database** instead of the intended one. It was **rolled back immediately** (both tables
  dropped, the `__EFMigrationsHistory` row deleted, absence verified) and the verification was redone against a
  throwaway `clinic_console_verify` database by pointing the worktree's own `appsettings.Development.json` at it,
  restored afterwards. Worth knowing before the next migration: set the database in that file, not the environment.

## What Part 1 delivers

Backend: the 16th capability; `PlatformAccount` + `PlatformRecoveryCode` with their configurations and the
`AddPlatformConsole` migration; `TotpService` (Otp.NET) + `PlatformSecretProtector`; `PlatformAuthConfig` +
`PlatformAuthService` + the second JWT bearer scheme; `AuthorizationPolicies.PlatformConsole` with its pinned
scheme; `ConsolePortGate` + `ConsoleListenerPlanning`; the dual-port `ConfigureKestrel` bind;
`PlatformAccountStateMiddleware` + `PlatformTenantScopeMiddleware` and the three middleware skips;
`AuditActor.Console` + the provider's reordering; the rate-limit widening; `PlatformAuthController`; the
`platform-account` verb.

Frontend: `console/` — a second Next 15 application with its own `check:responsive`, sign-in (password + code +
recovery + enrolment), `mot-de-passe`, a `cabinets` shell, an HttpOnly-cookie session written in one place, and
no clinic chrome.

Deploy + CI: the second Caddy site on `127.0.0.1:9443`, the `console` service, `Console__Port`/`Console__SigningKey`,
the loopback-only publication, the operator runbook, and the fifth CI job.

## Gate results

| Gate | Command | Result |
|------|---------|--------|
| Backend build | `dotnet build --no-incremental` | **0 errors, 55 warnings — all pre-existing, 0 in files this story touched** (verified by filename) |
| Backend unit suite | `BaseOutputPath=<temp> dotnet test` | **2291 passed, 0 failed** (baseline was 2203; +88) |
| Schema | `dotnet run -- verify-schema` before/after, diffed | **before: 3 DRIFT (exactly the objects this migration creates) → after: « schema matches the model », exit 0.** The diff shows only those three lines and the timestamp |
| Console typecheck | `npx tsc --noEmit` | clean |
| Console device gate | `npm run check:responsive` | **14/14 pass**, and **proven able to fail**: a throwaway probe with `min-h-screen`, `hover:scale-105` and `text-[9px]` turned 3 checks red, then green again once deleted |
| Console build | `npm run build` | clean, 7 routes |
| `web/` untouched | — | not rebuilt: this story changes no file under `web/` |
| CI | `.github/workflows/ci.yml` | parses; jobs are now `api · web · console · desktop · android` |
| Compose | `docker compose config` | parses; `9443` resolves with **`host_ip: 127.0.0.1`** |

### Derived guards that fired, and were resolved by review rather than by exemption

All five were the guards doing their job on arrival — worth listing because each is a decision, not a fix:

1. `DeploymentProfileTests.Every_capability_is_covered_by_the_matrix` — the new capability needed a matrix row.
2. `…Both_pre_existing_kinds_reproduce_the_old_IsLocalMode_truth_table` — `ServesPlatformConsole` is the **second**
   capability true of `HostedMultiTenant` alone, so it joins `hostedOnlyCapabilities` beside
   `AllowsPublicClinicSignup`.
3. `AuditInterceptorTests.The_Exclusion_List_Is_Still_Only_The_Documented_Types` — the two new tables were argued
   into the list with their reason (a console sign-in belongs to no cabinet's « Journal d'activité »; what the
   console does *to* a cabinet is still audited).
4. `ControllerAuthorizationCoverageTests.{Every_Defined_Policy_Is_Applied_Somewhere, Defined_And_Applied_…}` —
   resolved by the controller existing, not by an exemption.
5. `…No_unexpected_anonymous_endpoints_exist` — the three anonymous console actions were reviewed onto
   `ExpectedAnonymous` with the reasoning; `PlatformAuth.ChangePassword` deliberately stays off it.

## Deviations

### Auto-approved (trivial)

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| `Startup/ConsoleListenerPlan.cs` split out of `Program.cs` | Trivial | The plan puts the port resolution and EC-4's collision check in `Program.cs`. Extracted to a pure static so both are unit-testable — `ConsoleListenerPlanTests` is what proves the check fires where `Hosting:HttpPort` is unset, which is the whole of EC-4 and is unassertable inside a composition root |
| `Middleware/PlatformTenantScopeMiddleware.cs` as its own type | Trivial | The plan names `Startup/PlatformTenantScope.cs`. Both exist: the static holds the reason string and the `EnsureDeclared` guard, the middleware applies it — mirrors `TenantScopeMiddleware`'s shape rather than inventing a second one |
| `PlatformConsoleScheme` in its own file | Trivial | Forced, not chosen: `ControllerAuthorizationCoverageTests` derives the policy vocabulary from **every public string constant** on `AuthorizationPolicies`, so a scheme name parked there is read as a fifth policy — applied nowhere, registered by nothing, failing two assertions |
| `lib/refusal.ts` in `console/` | Trivial | `response.json().catch(() => ({}))` is what `check:responsive`'s `failed-read-as-empty` fails on, and the rule is right: it discards the status, the only fact left. Reading text and parsing under a guard yields a status-derived French sentence instead |

### DEV-1: `PlatformAccount.MustChangePassword` and its enforcement

**Date:** 2026-08-10 · **Category:** Scope · **Approved:** proceeding under a stated assumption

- **Plan:** the bootstrap verb "prints the enrolment secret and a one-time password" (AC-8.1). Nothing in the plan
  says what makes the password one-time.
- **Implemented:** `MustChangePassword`, set at creation, cleared by `ChangePlatformPasswordCommand`, and enforced
  by `PlatformAccountStateMiddleware` with a **403 + `must_change_password`** on every console route but the
  password change — `LocalAuthEnforcementMiddleware`'s shape for the clinic side.
- **Justification:** without it "one-time" is true of nothing. The operator reads that password to somebody and it
  stays a valid credential indefinitely. Reading this as implementing AC-8.1 rather than extending it.
- **Impact:** ~15 lines and one field. Part 4's writes are unaffected. If the intent was genuinely a password with
  no forced change, the field and the middleware branch come out together.

### DEV-2: the console's `check:responsive` is an adapted copy

**Date:** 2026-08-10 · **Category:** Technical · **Approved:** yes (recorded, mechanical)

- **Plan:** `console/scripts/check-responsive.mjs` — "the same responsive gate `web/` runs".
- **Implemented:** a copy with two documented differences, both stated in its own header. `agenda-scroll` is
  **deleted** (all three of its invariants are about `components/appointment-calendar.tsx`, which this app has
  not got and never will), and `CARD_FALLBACK_EXEMPT` is **emptied** (its four entries argue about `web/`
  components).
- **Justification:** carried over verbatim, both report failures forever — and a gate that is red from birth gets
  ignored, which that script's own header warns about. ⚠️ Emptying an inherited allow-list makes the check apply
  to **more** of this codebase, not less: the first table `console/` grows must have a card list.
- **Impact:** 14 checks run here against `web/`'s 15. Proven able to fail (see the gate table).

## Owed, and honestly outstanding

- **The eye pass has not been done.** `.claude/rules/frontend-web.md` § 14 requires 320 / 390 / 820 / 1180 /
  1440 px plus a landscape phone and a keyboard walk, and this repository has **no browser tooling** — the rule
  itself says the manual walk is the load-bearing half. What was done instead: the mechanical gate (14/14, proven
  live) and a re-read of the diff against the device contract. The sign-in screen is a single-column `max-w-md`
  card with full-width controls, `min-h-dvh`, a 44 px coarse-pointer floor applied in `globals.css` and a real
  `<button>` for the recovery link, so 320 px is structurally sound — but structurally sound is not looked at.
- **The tunnel walk** (Part 1's first two validation rows: `https://{DOMAIN}/api/platform/summary` → 404,
  a clinic token on `/api/platform/*` → 401 over the wire) is **operator-verified, not run here**. The behaviour is
  unit-tested as a predicate; the deployment is not runnable on this machine.

## Next

`/review-story`, then Part 2 (the portfolio and its counters) — which is buildable now and has no dependency on
`features/clinic-subscription/`.
