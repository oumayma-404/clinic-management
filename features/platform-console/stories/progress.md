# Implementation Progress — Console éditeur (`platform-console`)

**Story:** [story-1-full-platform-console.md](./story-1-full-platform-console.md)
**Branch:** `feature/platform-console` — **local only**, no remote, nothing pushed
**Worktree:** `.claude/worktrees/platform-console/` (see the concurrency note below)

## Status

| Part | Increment | Blocked? | Status |
|------|-----------|----------|--------|
| 1 | Reach the console and sign in | No | **implemented** |
| 2 | The portfolio, and the counters behind it | No | **implemented** |
| 3 | One cabinet's detail | No | **implemented** |
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

---

# Part 2 — The portfolio, and the counters behind it

**Session:** 2026-08-10 (second session on this branch) · steps 16–25 of the story.

## Working-tree note (start of session)

The worktree was **clean**. The *main* checkout (`feature/windows-desktop-app`) meanwhile has moved on: it now
carries `clinic-subscription` **Part A committed** (`c541897`) plus its Part B uncommitted. None of it is on this
branch, which is still based on `50b6f1c` — so the placeholder below is still the correct answer here, and the
merge points listed in Part 1's note are unchanged and have grown by one (`ApplicationDbContext`'s new `DbSet`s).

## What Part 2 delivers

**Domain** — `ClinicActivityDay` (one cabinet, one clinic-local day) and `ClinicActivitySnapshot` (one cabinet,
the row the list JOINs), `IClinicActivityRepository` with the portfolio projection, `ClinicActivityAuditRow`, and
`ClinicStaffSummary` on `IUserRepository`.

**Application** — `PlatformCounterPass` (pure; the two AC-2.2 exclusions), `PlatformCollectedReader` (the fifth
money read), `PlatformReadShape` (the closed returned-field set), `PlatformSubscriptionPlaceholder`,
`ListPlatformClinicsQuery`, `GetPlatformSummaryQuery`, the portfolio DTOs, and three new `verify-schema` checks.

**Infrastructure** — both configurations and the `AddClinicActivityCounters` migration, `ClinicActivityRepository`
(the one bounded `Clinics ⋈ snapshot` LEFT JOIN with `unaccent` search over name · city · **administrators'
addresses**), the two audit/user projections, DI.

**API** — `PlatformPortfolioController` (`GET /api/platform/clinics`, `GET /api/platform/summary`) and the daily
`ClinicActivityCounterJob` (`count-clinic-activity`, 03:00 UTC).

**`console/`** — the portfolio screen: summary strip, `Table` above `lg:` / `CardList` below it, a filter sheet on
phone with removable active-filter chips, a link pager, and the freshness line on every width.

## Gate results

| Gate | Command | Result |
|------|---------|--------|
| Backend build | `dotnet build --no-incremental` | **0 errors, 55 warnings — the identical pre-existing baseline, and 0 in any file this part touched** (verified by grepping the warning list for every new/changed filename) |
| Backend unit suite | `OutDir=api/.testrun` + `dotnet vstest` | **2322 passed, 0 failed** (Part 1 left it at 2291; +31) |
| Schema | `verify-schema` before/after, diffed | **before: 5 DRIFT — exactly the 3 indexes and 2 FKs this migration creates, with both new data checks reporting « not applicable »; after: « schema matches the model », exit 0.** Run against a throwaway `clinic_p2_verify` database, dropped afterwards |
| Schema checks proven able to fail | hand-inserted rows | **yes, both**: a snapshot with `Writes7d > Writes30d` turned `clinic-activity-snapshot-is-internally-consistent` red; deleting it while its clinic remained turned `clinic-activity-snapshot-covers-every-clinic` red. Neither passes vacuously on an empty database, which is what an unproven check would have done |
| Console typecheck | `npx tsc --noEmit` | clean |
| Console device gate | `npm run check:responsive` | **14/14 pass**, and **proven able to fail**: a throwaway probe rendering a `<Table>` with no `<CardList>` (plus `min-h-screen` and `text-[9px]`) turned 3 checks red, then green again once deleted. That probe specifically confirms the new `Table`/`CardList` primitives are **visible** to `card-fallback` — a raw `<table>` would have left the rule silently inert over this part's only table |
| Console build | `npm run build` | clean, 8 routes; `/cabinets` is `ƒ` (server-rendered on demand), as a token-bearing read must be |
| CI | `.github/workflows/ci.yml` | unchanged — the `console` job already runs all three console gates |

## Owed, and honestly outstanding

- **The eye pass has not been done, again.** There is still no browser tooling in this repository. What was done
  instead: the mechanical gate (14/14, proven live) and a re-read of the diff against the device contract. The
  structural claims are that the portfolio is **two trees** (`Table` above `lg:`, `CardList` below — never a
  reflow), filters move into a `dvh`-sized bottom sheet below `lg:` with the active filters as chips **outside**
  it, the card grid drops to one column below 380 px, the pager's disabled steps are text rather than dead links,
  and the freshness line is unconditional. Structurally sound is not looked at.
- **The counter job has not been run against real data.** It is unit-tested through its pure pass and its money
  reader, and the tables it writes are verified by `verify-schema`, but nothing here has executed
  `count-clinic-activity` end to end. First run on a real deployment is where a slow per-cabinet loop would show.

## Deviations — Part 2

### Auto-approved (trivial)

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| `PlatformCollectedReader` extracted from the job | Trivial | The plan puts the money figure inside `ClinicActivityCounterJob`. A private method there cannot be held equal to la caisse by `MoneyReadConsistencyTests`, which is the entire point of reusing those predicates — so the four calls moved to a shared reader the job and the test both use |
| `IUserRepository.GetStaffSummaryAsync` | Trivial | `users` + `lastLoginAt` per cabinet. The alternative was `GetByClinicIdAsync(paging: null)` — every colleague's row materialised, per cabinet, per night, to produce two scalars |
| `IAuditEntryRepository.GetActivityRowsAsync` | Trivial | A four-column projection. `GetFilteredAsync` with no paging would drag `ChangedFields` (unbounded text) through the job for nothing |
| `console/components/ui/{table,card-list}.tsx` written compact rather than copied | Trivial | `web/`'s are 231 + 304 lines, most of it the per-row action menu and primary-action slot this part has no actions for. The **component names are kept identical** on purpose — that is what `check:responsive`'s `card-fallback` rule matches on, and it was verified live |
| Only `@radix-ui/react-dialog` added, not `react-dropdown-menu` | Trivial | Both were approved; only the sheet is used, because Part 2 has no row action (see DEV-7). An unused dependency is noise in a container image |

### DEV-3: `PlatformTenantScope` moved from `API/Startup/` to `Application/Features/Platform/`

**Date:** 2026-08-10 · **Category:** Technical · **Approved:** forced by the plan's own wording

- **Plan:** Part 1 step 7 places it in `API/Startup/PlatformTenantScope.cs`, and its own docstring says
  `EnsureDeclared` is « called by the console handlers' entry point rather than trusted from the middleware ».
- **Implemented:** the file moved to Application. Part 2 introduces the first console *handlers*, and they are in
  Application, which cannot reference API — so the backstop was unreachable from the only place the plan says it
  should run.
- **Impact:** two `using` lines. `PlatformTenantScopeMiddleware` stays in the API and still calls `Declare`; both
  new query handlers call `EnsureDeclared`, and `PlatformPortfolioQueryTests` pins that an undeclared scope
  **throws** rather than reading zero rows (EC-12).

### DEV-4: the second schema check is not the one the plan names

**Date:** 2026-08-10 · **Category:** Technical · **Approved:** yes (recorded)

- **Plan:** step 25 asks for `clinic-activity-day-unique-per-clinic-day`.
- **Implemented:** `clinic-activity-snapshot-is-internally-consistent` instead.
- **Justification:** the unique index on `(ClinicId, Day)` makes the planned check **unfalsifiable** — the database
  refuses the row it would look for — and the index itself is already diffed against the catalog for free by
  reading the EF model. A check that cannot fail reports « ✓ » for ever about something it never looked at, which
  is the rot this verb exists to avoid. The replacement holds the relations one `Restate` call makes true
  (7 j ≤ 30 j, ≤ 30 active days, no active day without a save, no saves without a last-write instant), so a second
  writer or a half-applied refactor is visible — and it was **proven red** by hand.
- **Impact:** none on the count (three checks, as planned); `platform-account-has-totp-or-unenrolled` and
  `clinic-activity-snapshot-covers-every-clinic` are exactly as specified.

### DEV-5: the counter pass rewrites the whole 30-day window, not only yesterday

**Date:** 2026-08-10 · **Category:** Technical · **Approved:** yes (recorded)

- **Plan:** step 18 — « writes yesterday's `ClinicActivityDay` for every cabinet ».
- **Implemented:** it restates every day of the 30-day window it already read audit rows for.
- **Justification:** the snapshot needs those rows anyway, so the day rows cost one extra loop and nothing extra
  from the database — and it makes the history **self-healing**. Yesterday-only means a container down for three
  days, a first deployment, or any failed run leaves permanent holes in a trend nothing can reconstruct
  afterwards, because the audit window the pass reads is itself only 30 days. Idempotent by the unique index: the
  pass loads each day and restates it.
- **Impact:** ≤ 30 upserts per cabinet per night. Part 3's six-month trend becomes real within 30 days of
  deployment instead of six months.

### DEV-6: the list declares that subscription data is unavailable

**Date:** 2026-08-10 · **Category:** Scope · **Approved:** **yes — chosen by the user** (`AskUserQuestion`, « Placeholder, as planned »)

- **Plan:** step 20 — the state column reads « — » from one clearly-named placeholder resolver.
- **Implemented:** that, plus a `subscriptionDataAvailable: false` field on both responses, off which the screen
  hides the « en essai / expire sous N j / expiré / suspendu » chips and states the gap in one sentence. The
  « par date de fin » sort is likewise **not offered** — `PlatformPortfolioSort` has no such member.
- **Justification:** a filter that silently matches nothing is worse than a filter that is not offered, and an
  option that quietly sorted by something else is a screen answering a different question. The four row members
  stay **null** rather than defaulted, so « pas encore géré ici » can never render as « Actif ».
- **Impact:** Part 4 deletes `PlatformSubscriptionPlaceholder`; the compiler then lists every caller. The DTO
  field and the enum member arrive with the data behind them.

### DEV-7: no row-actions menu in Part 2

**Date:** 2026-08-10 · **Category:** Scope · **Approved:** yes (recorded)

- **Plan:** step 23 — « row actions in an explicit menu on **every** width, nothing hover-only ».
- **Implemented:** no menu, and no clickable row.
- **Justification:** the cabinet detail page is Part 3 and the three writes are Part 4, so there is nothing to put
  in the menu today. A menu that opens onto nothing, or a row that looks clickable and is not, is a dead control —
  which the device contract forbids in the same breath as it forbids hover-only affordances.
- **Impact:** Part 3 adds the menu with its first real action. The « nothing hover-only » half is already honoured:
  nothing on this screen is revealed by hover.

## Next

`/review-story`, then Part 3 (one cabinet's detail) — buildable now, and the last part before the companion
feature gates the rest.

---

# Part 3 — One cabinet's detail

**Session:** 2026-08-10 (third session on this branch) · steps 26–31 of the story.

## Working-tree note (start of session)

The worktree was **clean** (`git status` empty, `git diff HEAD --numstat` empty). The *main* checkout
(`feature/windows-desktop-app`) has moved on again: it now carries `clinic-subscription` **Parts A + B + C
committed** (`c541897`, `8545a4d`, `f92fb6e`) plus an untracked copy of `features/platform-console/`. None of it is
on this branch, which is still based on `50b6f1c`. The merge points listed in Part 1's note are unchanged.

⚠️ **The main checkout's untracked `features/platform-console/` is an older copy of this feature's own docs** (it has
no `progress.md`). It was left untouched; the authoritative copy is the one on this branch.

## Session decision — scope, and the one question the companion raised

**Scope: Part 3 only**, as asked (`/implement-story feature/platform-console part 3`).

**The companion feature has partly shipped since Part 2, and the decision was to ignore it here.** Asked and
answered at session start (`AskUserQuestion`, « Keep the placeholder »). `features/clinic-subscription/` Parts A–C
are now on the main checkout, so a merge would have made the state column and AC-3.2's payment history real inside
Part 3 — at the cost of pulling Part 4's scope forward and merging `DeploymentProfile`, `Infrastructure/Extensions`,
`ApplicationDbContext` and the EF model snapshot mid-part, with two hand-written migrations meeting one snapshot.
Part 4's step 32 **is** the pre-flight for that work, and it is where it belongs.

## What Part 3 delivers

**Domain** — `PlatformAccessEntry` (append-only; no FK to `Clinics` or `PlatformAccounts`, with `ClinicName` and
`AccountEmail` denormalised), `PlatformAccessAction` (`ViewedClinic` only — DEV-8), `IPlatformAccessEntryRepository`
+ `PlatformAccessActor`, `IClinicActivityRepository.GetClinicRowAsync`, and
`IUserRepository.GetPrimaryAdminContactAsync` + `ClinicAdminContact`.

**Application** — `GetPlatformClinicDetailQuery` (row + admin contact + six-month trend + the stated subscription
gap), `GetPlatformAccessLogQuery`, `PlatformAccessLedger` (the shared writer Parts 4–6 will call),
`PlatformAccessLabels`, the detail and access-log DTOs, `PlatformSubscriptionPlaceholder.DetailExplanation`, and
the new names on `PlatformReadShape`.

**Infrastructure** — `PlatformAccessEntryRepository`, `PlatformAccessEntryConfiguration`, the
`AddPlatformAccessLedger` migration (scaffolded — `dotnet ef` works in this worktree), the shared portfolio
projection now used by list *and* detail, DI, the `DbSet`, and the two derived-guard entries this table lands in.

**API** — `GET /api/platform/clinics/{clinicId}` on `PlatformPortfolioController` (404 + `clinic_not_found`) and the
new `PlatformAccessLogController` (`GET /api/platform/access-log`).

**`console/`** — `app/cabinets/[clinicId]/page.tsx`, `app/journal/page.tsx`, `components/activity-trend.tsx`,
`access-log-list.tsx`, `access-log-filters.tsx`, the extracted `components/ui/pager.tsx`, and the portfolio's first
row action.

## Gate results

| Gate | Command | Result |
|------|---------|--------|
| Backend build | `dotnet build --no-incremental` | **0 errors, 55 warnings — the identical pre-existing baseline, 0 in any file this part touched.** ⚠️ It was **56** on the first full rebuild: `g.Max(e => e.AccountEmail)` in the new repository produced a genuine `CS8604`. Fixed by rewriting the read as `SELECT DISTINCT` (see the file), **not** by a `!` — a new file's warnings are fully in scope |
| Backend unit suite | `OutDir=api/.testrun` + `dotnet vstest` | **2334 passed, 0 failed** (Part 2 left it at 2322; +12) |
| Schema | `verify-schema` before/after, diffed | **before: 2 DRIFT — exactly the two indexes this migration creates, exit 2; after: « schema matches the model », exit 0.** The diff shows only those two lines and the timestamp. Run against a throwaway `clinic_p3_verify` database, dropped afterwards |
| Read-shape guard proven able to fail | a `PatientName` added to `PlatformActivityMonthDto` | **yes** — `No_Console_Read_Returns_A_Field_Outside_The_Declared_Shape` went red naming `PatientName`, then green once reverted. This is the story's own exit criterion (« verified by trying it »), and it was verified against a **Part 3** DTO rather than only against the test's built-in `SmuggledPatientRow` |
| Console typecheck | `npx tsc --noEmit` | clean |
| Console device gate | `npm run check:responsive` | **14/14 pass**, and **proven able to fail**: a throwaway probe with `min-h-screen`, `text-[9px]`, `hover:scale-105` and a `<Table>` with no `<CardList>` turned **4** checks red, then green again once deleted. The `card-fallback` hit specifically confirms the journal's new table is visible to that rule |
| Console build | `npm run build` | clean, **9 routes** (was 8); `/cabinets/[clinicId]` and `/journal` are both `ƒ` (server-rendered on demand), as token-bearing reads must be |
| CI | `.github/workflows/ci.yml` | unchanged — the `console` job already runs all three console gates |
| `web/` untouched | `git status` | verified: this part changes no file under `web/` |

## Owed, and honestly outstanding

- **The eye pass has not been done, for the third time.** There is still no browser tooling in this repository.
  What was done instead: the mechanical gate (14/14, proven live) and a re-read of the diff against
  `DEVICE-CONTRACT.md` § 1. The structural claims for the two new screens are: the detail is **one column up to
  `lg:`** and two above it (a tablet in portrait is past `md:`, the same boundary the portfolio's table uses); every
  figure grid is `1 → min-[380px]:2 → lg:3`, so a label never shares a line with a value that is not its own; the
  trend scrolls in **its own** `overflow-x-auto` container with a `min-w-[20rem]` track and states every value as
  text below the bars, so nothing is reachable only by reading a column's height; the journal is **two trees**
  (`Table` above `lg:`, `CardList` below — never a reflow); every link carries `min-h-11` and the coarse-pointer
  44 px floor from `globals.css` applies; and there is no hover-only affordance and no dead control (the pager's
  disabled steps are text, and the journal's filters are links with no client state to desynchronise).
  **Structurally sound is not looked at.** Widths still owed: 320 / 390 / 820 / 1180 / 1440 px + a landscape phone
  + a keyboard walk.
- **The counter job still has not been run against real data** (unchanged from Part 2), so the six-month trend has
  not been seen with rows behind it. Its bucketing and its measured/unmeasured distinction are unit-tested; what is
  unverified is how it looks when five of six months are genuinely empty on a young deployment, which is the
  ordinary case for months after release.
- **The tunnel walk** remains operator-verified rather than run here (unchanged from Part 1).

## Deviations — Part 3

### Auto-approved (trivial)

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| `PlatformAccessLabels` as its own file | Trivial | `AuditLabels`' shape. The French wording of an action and of a month bucket has to be server-side (the values are CLR enum names and month numbers), and Parts 4–6 add members to the same map |
| `PlatformAccessLedger` as a shared static writer | Trivial | Parts 4–6 add three more callers, and « who was acting » must resolve identically in all four. A copy per write site is the shape in which the fourth one forgets — and it is where the « an unattributable action does not happen » decision lives, once |
| `components/ui/pager.tsx` extracted out of `portfolio-pager.tsx` | Trivial | Part 3 adds a second paged screen. Two pagers with independently written disabled-step handling is this repo's dominant defect shape; `PortfolioPager` is now a thin wrapper and nothing else changed |
| `PortfolioJoin` named class + a shared projection expression in `ClinicActivityRepository` | Trivial | The detail needs the list's row and AC-3.1 says « the same figures ». An anonymous type cannot be the parameter of a shared `Expression<Func<…>>`, so the join got a name |
| `IUserRepository.GetPrimaryAdminContactAsync` + `ClinicAdminContact` | Trivial | AC-3.3 needs the admin's name and address. Extending `ClinicStaffSummary` would have made the nightly counter pass read two more fields it never stores; returning a `User` would hand a cross-cabinet surface the whole account row including its password hash |
| `SELECT DISTINCT` instead of `GroupBy` in `GetRecordedActorsAsync` | Trivial | Forced, not chosen: `g.Max(e => e.AccountEmail)` is a real `CS8604` and the fix must not be a `!`. It is also the better answer — see the file |

### DEV-8: `PlatformAccessAction` declares one member, not the plan's five

**Date:** 2026-08-10 · **Category:** Scope · **Approved:** **yes — chosen by the user** (`AskUserQuestion`,
« `ViewedClinic` only »)

- **Plan:** the story's file table lists `ViewedClinic`, `GrantedPeriod`, `CancelledPeriod`, `Suspended`,
  `Unsuspended`.
- **Implemented:** `ViewedClinic` alone. Parts 4–6 add each member in the commit that adds the write producing it.
- **Justification:** this feature's own Part-2 precedent (DEV-6): `PlatformPortfolioSort` omitted « par date de
  fin » because « the member arrives with the data behind it ». A member nothing can produce is a value the journal
  can never show, and a reader has no way to tell « jamais fait » from « pas encore possible ».
- **Impact:** three one-line additions across Parts 4–6, each beside its own write. `PlatformAccessLabels.Action`
  falls through to the CLR name for an unmapped member, so a member added without its label degrades rather than
  disappearing.

### DEV-9: AC-3.2's payment history is stated as unavailable rather than rendered empty

**Date:** 2026-08-10 · **Category:** Scope · **Approved:** **yes — chosen by the user** (`AskUserQuestion`,
« Keep the placeholder »)

- **Plan:** step 26 — the detail includes « the payment ledger (cancelled entries included, with reason, canceller
  and moment) ».
- **Implemented:** `PlatformSubscriptionPlaceholder.DetailExplanation`, one French sentence saying that neither the
  state, nor the end date, nor the payment history is readable from this console yet — and **no**
  « Historique des paiements » section at all.
- **Justification:** the subscription ledger is `features/clinic-subscription/`'s (FR-4) and is not on this branch.
  An empty table asserts « ce cabinet n'a jamais payé » — a claim about the cabinet — where the truth is a claim
  about the console. The same reasoning covers **EC-14**: no end date is shown, because until the entitlement ledger
  exists « sans échéance » and « nous ne pouvons pas le lire » are indistinguishable and the second is what is true.
- **Impact:** AC-3.2 and EC-14 are **not met by Part 3** and are recorded here as deferred to Part 4, which deletes
  `PlatformSubscriptionPlaceholder` and gets the compiler to list every caller.

### DEV-10: the portfolio's row action is a link, not a menu

**Date:** 2026-08-10 · **Category:** Technical · **Approved:** yes (recorded)

- **Plan:** step 23 — « row actions in an explicit menu on **every** width, nothing hover-only ». Part 2 deferred it
  (DEV-7) because there was nothing to put in a menu; Part 3 is where that debt falls due.
- **Implemented:** one always-visible « Ouvrir » link per row, in the table **and** in the card list, each with a
  row-naming `aria-label`. No dropdown, and the row itself is still not clickable.
- **Justification:** with exactly one action a dropdown is a control whose only purpose is to hide one link behind a
  tap. What the requirement is about — nothing revealed by hover, the same affordance at every width — is honoured
  in full. `@radix-ui/react-dropdown-menu` stays unadded (Part 2's trivial deviation holds), so the container image
  gains nothing. A `<tr onClick>` was rejected outright: no keyboard path, no accessible role.
- **Impact:** Part 4 adds the menu when there are three writes to present, and the link becomes its first item.

### DEV-11: the detail read is a Query that writes, and its ledger row is not best-effort

**Date:** 2026-08-10 · **Category:** Technical · **Approved:** yes (recorded — the plan asks for the write; this
records the two decisions it does not settle)

- **Plan:** step 27 — « Write a `PlatformAccessEntry` for the detail (AC-7.3) », placed on a **query** (step 26).
- **Implemented:** as planned, plus two decisions the plan leaves open. (a) It stays a `Query` — a `Command` would
  broadcast into a clinic group on every page load, because `RealtimeBroadcastBehavior` derives its key from the
  namespace. (b) A failed or unattributable ledger write **fails the read** rather than being swallowed.
- **Justification:** (b) departs from this codebase's otherwise-universal « post-commit side effects are
  best-effort » rule, and deliberately: those swallow because the operation they follow has already committed and
  must not be undone by a secondary failure, whereas here the operation *is* what is being recorded. « Every detail
  read is recorded » is false the moment an unrecorded read succeeds.
- **Impact:** Parts 4–6 inherit `PlatformAccessLedger`'s throw-on-unattributable behaviour, which is correct for a
  write too. `PlatformAccessLedgerTests` pins both halves.

## Part 4 — step 32 pre-flight (run 2026-08-10) · **Part 4 is UNBLOCKED**

`features/clinic-subscription/` Parts **A–F** have now shipped. `feature/windows-desktop-app` was merged into this
branch at **`25b252d`** (bringing Parts E–F; A–D came in at `3553396`). Two doc-only conflicts
(`api/ClinicManagement.API/CLAUDE.md`, `api/ClinicManagement.Domain/CLAUDE.md`), both resolved by keeping **both**
sides — the verb lists and the entity rows are additive. `Program.cs` auto-merged. **Gate after the merge:
`dotnet build` 0 errors / 55 pre-existing baseline warnings, `dotnet test` 2570 passed / 0 failed.**

### The six *Assumed dependency surface* rows

| # | Assumed | Result |
|---|---|---|
| 1 | `ClinicSubscription` (plan, derived end date, suspension flag) + `SubscriptionPeriod` ledger, both aggregate roots | ✅ **verbatim** — `EndsOn`, `IsSuspended`, `RecomputeFrom`, `SetPlan`, `Suspend`/`Unsuspend`; `SubscriptionPeriod.Create`/`Trial`/`OpenEnded`/`Cancel` |
| 2 | One authority for Essai/Actif/Expiré/Suspendu + `EndsOn` + `DaysRemaining` | ✅ `SubscriptionStateReader.Read(entitlement, clinicToday, isTrial)` + `SubscriptionState` + `Domain/Services/SubscriptionLedger.Fold` |
| 3 | `GrantSubscriptionPeriodCommand`, `CancelSubscriptionPeriodCommand`, `SuspendClinicCommand`, `UnsuspendClinicCommand` | ✅ **two verbatim, one adaptation.** `GrantSubscriptionPeriodCommand` → `Result<SubscriptionGrantResult>` and `CancelSubscriptionPeriodCommand` → `Result<SubscriptionCancelResult>` exist as named. Suspend + unsuspend arrived as **one** `SetSubscriptionSuspensionCommand` (`bool Suspend` + `Reason` + `ActedBy`), so per **R-2** steps 44/45 adapt the call site — nothing is re-implemented. ⚠️ Each carries its own actor string (`RecordedBy`/`CancelledBy`/`ActedBy`): that is where `console\|{accountId}` goes |
| 4 | The write-refusal gate with an explicit allow-list (FR-3) | ✅ **adapt.** It is the `[AllowsWithoutSubscription("<reason>")]` **endpoint attribute**, not a path list, so step 35 becomes « carry the attribute on the console's write endpoints ». ⚠️ `SubscriptionGateMiddleware.cs:58` already passes a caller whose `ITenantScope` is not `Clinic`, which a console account never is — so the endpoint is not refused today either way, and step 35's test pins that pass-through rather than adding an entry |
| 5 | The subscription re-read (FR-15) — how a clinic learns of a console write | ✅ `web/lib/subscription/subscription-context.tsx` + `subscription-banner.tsx`, three re-read triggers |
| 6 | A declared realtime resource key for subscription | ❌ **absent, and deliberately.** `Subscriptions` is on `RealtimeResourceResolver.ExcludedAreas` — the companion decided state is learned by **re-read, never broadcast**, because neither moment that changes it can push one. See DEV-12 |

Nothing was improvised: no console-side grant handler, end-date computation, state fold or period entity was
written as a stand-in. That is the FR-4 violation this feature is defined around.

### DEV-12: AC-4.4a cannot be implemented as written — there is no realtime key to reuse

**Date:** 2026-08-10 · **Category:** Scope · **Approved:** pending

- **Plan:** step 37 — « Optionally (AC-4.4a) notify the **target** cabinet via `IRealtimeNotifier` with the
  companion's **existing** declared key. »
- **Reality:** the companion declared **no** key. `Subscriptions` is on `RealtimeResourceResolver.ExcludedAreas`,
  and its stated reason is the same one AC-4.4a would have to defeat: a vendor grant runs in a process with no
  caller's token to derive a clinic from, and an entitlement ending at midnight has no actor at all.
- **Proposal:** **drop AC-4.4a.** AC-4.4 (« the clinic's app reflects it without signing out ») is satisfied by
  FR-15's re-read, which the companion states clears the banner within one 5-minute cycle. Inventing a key here
  would fail `RealtimeResourceResolverTests` in both directions *and* re-open the decision the companion made.
- **Impact:** AC-4.4a is recorded as out of scope with a reason (the story's own exit criteria permit that);
  AC-4.4 is unaffected.

### OPEN QUESTION — Q-1: how the portfolio filters and sorts on a state that needs the ledger

**Raised 2026-08-10, before any Part 4 code was written. This blocks the read half of step 32 (deleting
`PlatformSubscriptionPlaceholder`), not the write half.**

Deleting the placeholder means the list, the summary and the detail must carry real `Plan`/`State`/`EndsOn`/
`DaysRemaining`, and AC-2.3/AC-2.4 want the portfolio **filterable** by « en essai · expire sous N jours · expiré ·
suspendu » and **sortable** by end date. AC-2.4a and EC-11 require every such figure to exist **before a page is
cut**, in one bounded query.

Three of the four filters are plain SQL over the entitlement row — `suspendu` is `IsSuspended`, `expiré` is
`EndsOn < today`, `expire sous N` is a `BETWEEN`. **« En essai » is not.** `SubscriptionStateReader.Read` takes
`isTrial` as a *parameter* precisely because Trial-vs-Active is a fact about the **ledger**, not about the
entitlement row — and folding N cabinets' ledgers before paging is exactly the unbounded read EC-11 forbids.

Candidate resolutions, none yet chosen:

1. **Express « en essai » as a SQL predicate over the ledger** — a cabinet is on its trial iff it has no
   non-cancelled entry whose `Kind != Trial` and its cover is still in force. Bounded, joinable, no fold. Risk: it
   is a *second* statement of what a trial is, in SQL, where no compiler checks it against `SubscriptionLedger` —
   the FR-4 shape this feature exists to avoid, in miniature.
2. **Offer the other three filters and not « en essai »**, the way Part 2 already declines to offer a sort it
   cannot honour. Honest and cheap; leaves an AC-2.3 filter unshipped.
3. **Denormalise a `CoverKind` onto `ClinicSubscription`**, written by the same `RecomputeFrom` that is already the
   only writer of `EndsOn`. One authority, one write path, filterable and sortable. Costs a migration and a
   backfill in the companion's table — i.e. an edit to `features/clinic-subscription/`, not to this feature.

Option **3** looks right and option **1** is the one to avoid, but it is a change to the companion's schema and so
is the user's call. **No code was written against any of them.**

### Q-1 — RESOLVED (2026-08-10): denormalise, but **not** the field the option named

**Decision: option 3.** A kind is denormalised onto `ClinicSubscription`, written by `RecomputeFrom` — already the
only writer of `EndsOn`, so there is one write path and `verify-schema` can hold it the way it holds
`clinical-child-clinic-matches-patient`.

⚠️ **The obvious column is unstorable, and this is the trap to write down.** « En essai » as
`GetSubscriptionQuery.IsOnTrial` defines it is *« is the cover in force **today** the trial? »* — the **last
covering span**, which is a function of the ledger **and of today**. `RecomputeFrom` is deliberately **clock-free**
(`SubscriptionLedger`'s own remarks say why: a fold that reads a clock makes a lapsed entry restart from today and
flaps `subscription-end-date-matches-ledger` daily). So a stored « what covers today » would be correct only until
the next midnight, and would need a daily pass to stay true — reintroducing exactly the staleness the fold was
designed to avoid.

**The storable form is `LatestCoverKind`** — the `SubscriptionPeriodKind` of the **last non-cancelled entry in fold
order**. That is a pure function of the ledger with no clock in it, so `RecomputeFrom` can write it and
`verify-schema` can re-derive it.

It answers the filter because the filter **ANDs with the state SQL already computes** from `EndsOn`/`IsSuspended`:

| Ledger | `IsOnTrial` (today) | `LatestCoverKind` | State | « en essai » filter |
|---|---|---|---|---|
| trial only, in force | true | `Trial` | Trial/Active | ✅ both |
| trial only, lapsed | false (no covering span) | `Trial` | **Expired** | ✅ excluded by the state term |
| trial then paid | false | `Paid` | Active | ✅ both |
| grandfathered then paid | false | `Paid` | Active | ✅ both |

So the two agree everywhere the filter can select, and the disagreement (a lapsed trial) is excluded by the state
term regardless.

**Implementation shape, so the ordering has one authority:** add `Kind` to `SubscriptionLedgerEntry` and let
`FoldWithSpans` return the latest cover kind alongside `EndsOn`, rather than re-ordering the entities inside
`RecomputeFrom` — the fold's `OrderBy(RecordedAtUtc).ThenBy(Id)` must not exist twice. That also means
`SchemaVerificationReader`'s raw ADO projection gains the column, which is what lets `verify-schema` check the
denormalisation rather than trust it.

⚠️ **`IsOnTrial` must MOVE, not be copied.** It is a private static on `GetSubscriptionQuery` today; the console is
its second caller, and this repo's dominant defect shape is a correct helper wired to one call site. It goes beside
`SubscriptionStateReader` — whose `isTrial` parameter exists for it — and `GetSubscriptionQuery` becomes a caller.

**Scope note:** this edits `features/clinic-subscription/`'s table (a migration + a backfill + a `verify-schema`
check), not just this feature. That is the cost the option was chosen with.

### DEV-12 — RESOLVED: AC-4.4a dropped

**Approved 2026-08-10.** Recorded out of scope with the reasoning above; AC-4.4 is unaffected. No realtime key is
added and `Subscriptions` stays on `ExcludedAreas`.

## Next

**Part 4, resuming at step 32.** The merge and the pre-flight are done and committed, and **Q-1 and DEV-12 are both
settled** (above) — no open questions remain. Order to resume in:

1. **Q-1's implementation** (companion-side, do it first — the console read half depends on it):
   `SubscriptionLedgerEntry` gains `Kind`; `FoldWithSpans` returns the latest cover kind; `ClinicSubscription`
   gains `LatestCoverKind` written by `RecomputeFrom`; migration + backfill; `SchemaVerificationReader` projects
   the column and `verify-schema` gains `subscription-cover-kind-matches-ledger`; `IsOnTrial` **moves** out of
   `GetSubscriptionQuery` beside `SubscriptionStateReader`. Then delete `PlatformSubscriptionPlaceholder` and let
   the compiler list its nine callers.
2. Steps 33–34 — `RecordSubscriptionPeriodCommand` + idempotency. ⚠️ `PlatformPortfolioController` must **never
   name** `GrantSubscriptionPeriodCommand` (or the other two): Part F shipped
   `SubscriptionVendorCommandReachabilityTests`, which **source-scans every `Controllers/` file** for those type
   names and fails on a substring hit — a `using` or a comment is enough. The plan's own
   `RecordSubscriptionPeriodCommand` indirection is what keeps this green, and it is load-bearing now rather than
   stylistic. Verified green at `25b252d` with `Controllers/Platform/` present.
3. Steps 35–37 — the `[AllowsWithoutSubscription]` attribute + its test, the `PlatformAccessEntry`, and DEV-12.
4. Step 38 — the payment sheet. 5. Step 39 — tests and the two gates.
