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
| 4 | Record a payment and unlock the cabinet | No | **implemented** |
| 5 | Correct a mistake | No — the companion has shipped | **implemented** |
| 6 | Suspend for abuse | No — the companion has shipped | **implemented** |
| 7 | Verification, runbook and the promise | No | **implemented** |

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

---

# Part 4 — Record a payment and unlock the cabinet

**Session:** 2026-08-10/11 (fourth session on this branch) · steps 32–39 of the story.
**Status: implemented.**

## Working-tree note (start of session)

The worktree was **clean** (`git status` empty). Part 4's step-32 pre-flight had already been run and committed in
the previous session, and `feature/windows-desktop-app` was merged in at `25b252d`, so
`features/clinic-subscription/` Parts A–F are on this branch. Nothing else was in flight.

## What Part 4 delivers

**Q-1 (companion-side, done first — the console read half depends on it).** `SubscriptionLedgerEntry` gains
**`Kind`** (deliberately **not** defaulted); `FoldWithSpans` now returns a named **`SubscriptionFold`** carrying
**`LatestCoverKind`** — the kind of the last non-cancelled entry in fold order — beside `EndsOn` and the spans;
`ClinicSubscription.LatestCoverKind` is written by `RecomputeFrom` and by nothing else, from **one** fold;
`SchemaVerificationReader` projects both columns and `verify-schema` gains
**`subscription-cover-kind-matches-ledger`**, which calls the **real** fold rather than re-expressing it in SQL.
**`IsOnTrial` was MOVED** out of `GetSubscriptionQuery` into `Features/Subscriptions/SubscriptionTrial` — the
console is its second caller, and a correct helper wired to one call site is this repo's dominant defect shape.
`SubscriptionStateReader.Read` gained a **primitive overload** (`endsOn`, `isSuspended`) so a projected row can be
read by the one FR-1 rule without materialising an aggregate; the entity form delegates to it.

**Read half.** `PlatformSubscriptionPlaceholder` is **deleted** and the compiler listed its callers, as it was
designed to. The portfolio JOIN is now `Clinics ⋈ snapshot ⋈ ClinicSubscriptions` (LEFT on both), carrying
`HasEntitlement`/`Plan`/`SubscriptionEndsOn`/`SubscriptionIsSuspended`/`LatestCoverKind`;
**`PlatformClinicRowMapper`** is the one place a row becomes a DTO, deriving the state through
`SubscriptionStateReader`. AC-2.3's five filters and AC-2.4's **end-date sort** are SQL predicates
(`PlatformSubscriptionFilter`), and the summary strip counts through the **same** predicates. AC-3.2's payment
history is the companion's ledger read back, with each entry's « période couverte » taken from the **fold**.
`GET /api/platform/summary` gains the vendor's own revenue via
`IClinicSubscriptionRepository.GetVendorCollectedBetweenAsync`, over the **clinic's** month.

**Write half.** `RecordSubscriptionPeriodCommand` +
`POST /api/platform/clinics/{clinicId}/subscription-periods` on a new **`PlatformSubscriptionsController`**,
carrying `[AllowsWithoutSubscription]`. `PlatformAccessEntry` gains `SubscriptionPeriodId` and a **unique,
partial-indexed `IdempotencyKey`**; `PlatformAccessAction.GrantedPeriod` arrives with the write that produces it.
Migration **`AddPlatformConsoleWrites`** (three columns, one index, one backfill below every DDL statement).
`Platform` joins `RealtimeResourceResolver.ExcludedAreas` (DEV-12).

**`console/`** — `components/record-payment-sheet.tsx` (full-screen `dvh` sheet below `lg:`, dialog above, pinned
action, confirm-before-discard), `app/bff/paiements/route.ts`, the state badge, the five filter chips, the end-date
sort, the real summary strip and the payment history on the detail.

## Gate results

| Gate | Command | Result |
|------|---------|--------|
| Backend build | `dotnet build --no-incremental` | **0 errors, 55 warnings — the identical pre-existing baseline, and 0 in any file this part touched** (verified by extracting every warning's filename; the only `Designer.cs` hit is the 2025 `addclinics` migration) |
| Backend unit suite | `OutDir=api/.testrun` + `dotnet vstest` | **2612 passed, 0 failed** (the post-merge baseline was 2570; +42) |
| Schema | `verify-schema` before/after, diffed | **before: 1 DRIFT — exactly the index this migration creates — with the cover-kind check « not applicable »; after: that index « present (unique) » and « 2 entitlement(s), each naming the cover its ledger actually folds to ». The diff is three lines plus the timestamp.** Run against a throwaway `clinic_p4_verify`, dropped afterwards |
| Backfill proven on real rows | two hand-seeded cabinets | **yes, and not vacuously**: cabinet A (trial → paid) backfilled to `Paid`, cabinet B (trial + a **cancelled** payment) to `Trial` — so the « last **non-cancelled** entry » rule is what ran, not « the last row ». An empty database would have proven neither |
| New check proven able to fail | hand-corrupted one row | **yes** — flipping cabinet B's `LatestCoverKind` to `Paid` turned `subscription-cover-kind-matches-ledger` red naming « 1 of 2 » |
| Console typecheck | `npx tsc --noEmit` | clean |
| Console device gate | `npm run check:responsive` | **14/14 pass**, and **proven able to fail**: a throwaway probe carrying `min-h-screen`, `text-[9px]`, `hover:scale-105`, `max-h-[90vh]` and a `<Table>` with no `<CardList>` turned **5** checks red, then green again once deleted |
| Console build | `npm run build` | clean, **10 routes** (was 9); `/bff/paiements` is `ƒ`, as a token-bearing write must be |
| CI | `.github/workflows/ci.yml` | unchanged — the `console` job already runs all three console gates |
| `web/` untouched | `git status` | verified: this part changes no file under `web/` |

## Deviations — Part 4

### Auto-approved (trivial)

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| `SubscriptionFold` as a named record rather than a 3-tuple | Trivial | `FoldWithSpans` had to grow a third result; a tuple would have made every call site's `var (_, spans)` positional and silently re-orderable. Three call sites updated to `.Spans` |
| `SubscriptionStateReader.Read` primitive overload | Trivial | The console holds a projected row, not an aggregate. An **overload delegating to one body** rather than a second reader, so « is this cabinet expired? » still has one answer |
| `PlatformClinicRowMapper` extracted | Trivial | The list and the detail held byte-identical mappers; Part 4 gave them something to disagree about (a *derived* state), and AC-3.1 is « the same figures » |
| `FakeAccessLedger`/`FakePlatformSession` moved to `PlatformConsoleFakes.cs` | Trivial | Part 4's tests are their second caller. The fake also reproduces the **partial unique index**, without which the EC-5 race test would pass over an implementation that has no index behind it |
| `PlatformSubscriptionsController` as its own controller | Trivial | `PlatformPortfolioController`'s docstring claims « read-only by construction », which stops being checkable the moment one action on it writes |

### DEV-13: the console cannot grant open-ended cover, so EC-14 is met on the read side

**Date:** 2026-08-11 · **Category:** Scope · **Approved:** yes (recorded — forced by the companion)

- **Plan:** step 33 — « Supports « offert » with no amount (AC-4.8) **and a never-expiring cabinet (EC-14)** ».
- **Implemented:** « offert » in full; a grant with **no duration at all is refused**, by the console and by the
  companion alike.
- **Justification:** `GrantSubscriptionPeriodCommandHandler` refuses it in the companion's own code, with its
  reasoning stated there: open-ended cover is « reachable by forgetting one flag and unnoticeable afterwards », so a
  cabinet that should never expire is **grandfathered by a migration**, not granted from a console. Re-opening that
  door here would be the console contradicting the feature it delegates to (R-2 says adapt, never re-implement).
- **Impact:** EC-14 is satisfied where it is actually about display — a cabinet whose `EndsOn` is null reads
  « Sans échéance » **in words** on the portfolio, on the detail and in the payment sheet's header, and
  `A_Never_Expiring_Cabinet_Is_Active_With_No_End_Date` pins it.

### DEV-14: the command reuses the companion's write half rather than sending its grant command

**Date:** 2026-08-11 · **Category:** Technical · **Approved:** yes (recorded)

- **Plan:** step 33 — « delegate to the companion's grant handler ».
- **Implemented:** it delegates to the companion's **pieces** — `SubscriptionCabinetLookup`,
  `SubscriptionPeriod.Create`, `SubscriptionRefold.SaveAsync` — and stages the `PlatformAccessEntry` **before**
  the refold's single save, rather than `_mediator.Send`-ing the companion's grant command.
- **Justification:** **atomicity, and it is not a preference.** That command commits on its own, so a ledger row
  written after it would be a second transaction — and a payment recorded with no FR-5 row behind it is exactly
  the « an unattributable action must not aboutir » Part 3 settled for reads (DEV-11). An explicit transaction
  around it was rejected too: `SubscriptionRefold` retries on `ConflictException`, and in PostgreSQL a failed
  statement aborts the ambient transaction, so the retry could not run. **No date is computed here** —
  `ClinicSubscription.RecomputeFrom` remains the only writer of `EndsOn`, which is what AC-4.2 actually forbids.
- **Impact:** the two paths share every rule and differ only in pipeline. Parts 5–6 inherit the shape. It also
  keeps `Controllers/` free of the three forbidden type names, which `SubscriptionVendorCommandReachabilityTests`
  source-scans for.

### DEV-15: idempotency lives on the access ledger, not in a table of its own

**Date:** 2026-08-11 · **Category:** Technical · **Approved:** yes (recorded)

- **Plan:** step 34 — « idempotency on `idempotencyKey` ». It does not say where the key is stored.
- **Implemented:** a nullable, **partial-unique** `IdempotencyKey` column on `PlatformAccessEntry`, beside a
  nullable `SubscriptionPeriodId`.
- **Justification:** every console write already produces **exactly one** ledger row, in the same transaction as
  the write itself — so the ledger already *is* the « one row per console action » table an idempotency store
  would duplicate, and a second table would be a second thing able to disagree with it. It also makes the replay
  answerable rather than approximate: the row names the entry that was created, so a repeated submission returns
  the **first** outcome instead of guessing at it.
- **Impact:** ⚠️ **The enforcement is the index, never this handler's read-first check**, which two simultaneous
  submissions both pass. `A_Repeated_Submission_That_Loses_The_Race_Replays_Rather_Than_Failing` drives that path
  deliberately (by blinding one read) so the guard under test is the database's.

### DEV-16: `PlatformSummaryDto` gained a tenth figure, « sans abonnement »

**Date:** 2026-08-11 · **Category:** Scope · **Approved:** yes (recorded)

- **Plan:** AC-2.7 lists the summary's figures and does not include it.
- **Implemented:** `NoEntitlement`, counted and shown only when non-zero.
- **Justification:** without it a cabinet in FR-13's failure state is counted in **none** of the five state
  figures, so the lines stop summing to « Cabinets » — the one property that makes a strip readable at a glance,
  and the same reason la caisse prints « Espèces 0,000 ». `The_Five_State_Counts_Sum_To_The_Portfolio` pins it.
- **Impact:** one field, one chip, one test. « En essai » is subtracted out of « Actifs » for the same reason:
  in SQL both branches match a covered, unsuspended cabinet, so leaving them overlapping would over-count.

## Owed, and honestly outstanding

- **The eye pass has not been done, for the fourth time.** There is still no browser tooling in this repository.
  What was done instead: the mechanical gate (14/14, proven live against a five-violation probe) and a re-read of
  the diff against `DEVICE-CONTRACT.md` § 1. The structural claims for the new surface are: the payment sheet is
  **full height below `lg:`** (`h-dvh`, never `vh`) with the body scrolling inside `flex-1` and the primary action
  a `shrink-0` sibling, so it stays on screen with the keyboard open and at a 380 px landscape height; it becomes a
  centred `lg:max-w-lg` dialog above that boundary; it is dismissible by a visible control **and** `Escape` **and**
  the overlay, all three routed through one handler that confirms before discarding typed input; every control is
  disabled in flight; the method picker is a **native** `<select>` (the platform's own picker on a phone, keyboard
  reachable for free) and every field is ≥ 16 px. The state badge is **text and shape, never colour alone**
  (AC-6.3). **Structurally sound is not looked at.** Widths still owed: 320 / 390 / 820 / 1180 / 1440 px + a
  landscape phone + a keyboard walk.
- **The write has not been exercised over the wire.** The command, its idempotency, its refusals and its
  attribution are unit-tested against an in-memory ledger, and the schema behind them is verified — but no request
  has travelled `console/` → `/bff/paiements` → Kestrel's console listener → the handler on a running deployment.
  That is the same operator-verified boundary Part 1's tunnel walk sits on.
- **The counter job still has not been run against real data** (unchanged from Parts 2–3).

## Next

**Part 5 — correct a mistake** (steps 40–43). It reuses everything this part built: `PlatformAccessLedger` gains
its third caller, `PlatformAccessAction` gains `CancelledPeriod`, and the confirmation's « from which date the
cabinet becomes read-only » comes from the companion's **own fold**, never a console-side estimate. The shape
DEV-14 settled — reuse the companion's pieces, stage the ledger row before the single save — is what Part 5's
cancel command should follow.

---

# Part 5 — Correct a mistake

**Session:** 2026-08-11 (fifth session on this branch) · steps 40–43 of the story.
**Status: implemented.**

## Working-tree note (start of session)

The worktree was **clean** (`git status` empty, `git diff HEAD --numstat` empty) at `394b248`. The *main* checkout
(`feature/windows-desktop-app`) has moved on again — it now carries `clinic-subscription` **Part G** committed
(`e379f09`) plus uncommitted work in `Features/Subscriptions/` and `Domain/Services/SubscriptionLedger.cs`. **None of
it was merged in**, and nothing in Part 5 needs it: Part G is the outbox-parking half (SMS/WhatsApp/push), which this
part does not touch. That merge belongs at Part 7's own boundary, and `SubscriptionLedger.Fold` is read here
**unchanged** — a fold edited in the main tree meeting this part's preview mid-session is exactly the situation the
worktree exists to avoid.

## What Part 5 delivers

**Domain** — `PlatformAccessAction.CancelledPeriod`, arriving with the write that produces it (DEV-8's terms) and
leaving only Part 6's two members outstanding. **No migration and no model change**: the column is
`HasConversion<int>()`, so a new member is an int the schema already accepts.

**Application** — **`CancelSubscriptionPeriodFromConsoleCommand`**: mandatory motif (AC-5.1), the entry located
**within the cabinet's own ledger** so another practice's is structurally unreachable, `entry.Cancel(motif,
console|{accountId}, now)` (AC-5.2), the `PlatformAccessEntry` staged **before** `SubscriptionRefold`'s single save,
and the result read back through `SubscriptionStateReader`. Three refusal **codes** (`clinic_not_found` ·
`period_not_found` · `period_already_cancelled`). Plus **`PlatformCancellationPreviewDto`** and
**`PlatformSubscriptionCancelledDto`**, and `IfCancelled` on every live row of the payment history.

**API** — `POST /api/platform/clinics/{clinicId}/subscription-periods/{entryId}/cancellation` on
`PlatformSubscriptionsController`, carrying `[AllowsWithoutSubscription]` with its own reason, plus
`Models/CancelSubscriptionPeriodRequest`.

**`console/`** — `components/cancel-period-dialog.tsx` (sheet below `lg:`, dialog above, motif mandatory, the
consequence stated before committing, confirm-before-discard), `app/bff/annulations/route.ts`, and the per-entry
control on the fiche.

## Step 41 — where the consequence is computed, and why there

`GetPlatformClinicDetailQuery` re-folds the cabinet's **real** ledger with one entry marked cancelled, through
`SubscriptionLedger.FoldWithSpans`, and reads the result with `SubscriptionStateReader`. So the confirmation's
sentence is the server's own arithmetic in both halves, and the dialog **cannot exist without it** — see DEV-17 for
why that beat a preview endpoint.

⚠️ **The naive client-side version is wrong in the case a correction is actually for.** « The current end date minus
this entry's duration » is only right when the entry is the *latest* one; the fold advances on an exclusive cursor, so
removing a **middle** entry shortens every stretch after it. Re-folding is also what makes the preview and the write
agree by construction rather than by review, since `SubscriptionRefold` runs the same fold over the same rows a
moment later.

⚠️ **`isTrial` comes from the *previewed* fold, not from the cabinet's current cover.** Cancelling a paid entry can
hand the cover back to the trial, and labelling that « Actif » would describe the state the cabinet is *leaving*.

## ⚠️ The trap this part found: cancelling a cabinet's ONLY entry yields « sans échéance », not « expiré »

`FoldWithSpans` starts `endsOn` at null and returns `openEnded ? null : endsOn`, so a ledger whose every entry is
cancelled folds to **`EndsOn = null`** — which `SubscriptionStateReader` reads as *no end date*, i.e. **Active,
writes allowed, for ever**. It is the companion's own semantics and was left alone (FR-4: no second arithmetic here),
and in practice it is unreachable, because every cabinet is provisioned with an opening entry (FR-13) that the fold
falls back to.

But it is why **EC-7's fixture needs two entries**: `GivenTrialThenGrant` seeds a lapsed trial *and* the grant, so
cancelling the grant lands on the trial's expired date. A one-entry fixture would have made
`Cancelling_A_Three_Week_Old_Grant_Puts_The_Cabinet_Back_Into_Read_Only` assert the opposite of EC-7 and pass, which
is the shape of a test that certifies a bug. Worth knowing before Part 6 writes its own fixtures.

## Decisions worth recording (not deviations — the plan is silent on all three)

- **The cancel carries no idempotency key, unlike the grant.** AC-4.6's replay exists because a double-click on
  « Enregistrer le paiement » is the vendor's *own* repeated action and the first outcome is what they wanted. An
  entry already struck through was struck through by **somebody**, and which colleague and for what motif is a fact
  the vendor needs — so « déjà annulée » is a refusal carrying `period_already_cancelled`, and the dialog re-reads
  the fiche so that motif and author appear beside the refusal. Replaying silently would hide a colleague's action.
- **A POST, not a DELETE.** Nothing is deleted (AC-5.2), and `DELETE` would advertise the opposite to every future
  reader of the controller and of any client generated from it.
- **409 for the already-cancelled case**, matching `ConflictException`'s status: it is a state of the world, not a
  malformed request.

## Gate results

| Gate | Command | Result |
|------|---------|--------|
| Backend build | `dotnet build --no-incremental` | **0 errors, 55 warnings — the identical pre-existing baseline, and 0 in any file this part touched** (verified by extracting every warning's filename: the 29 files named are all pre-existing, none of them this part's) |
| Backend unit suite | `OutDir=<temp>` + `dotnet vstest` | **2627 passed, 0 failed** (Part 4 left it at 2612; +15) |
| Preview guard proven able to fail | broke `PreviewCancellation` by hand | **yes** — dropping the `IsCancelled = true` marking turned `The_Preview_On_The_Fiche_Is_Exactly_What_Cancelling_Then_Does` red and **only** it (1 of 15), then green once restored. A preview's failure mode is silent, so this is the one case that had to be seen red |
| Schema | — | **not applicable, and verified so rather than assumed**: this part adds no migration and no model change. `PlatformAccessEntryConfiguration` maps `Action` with `HasConversion<int>()`, so a new enum member is an int the existing column already accepts; `git status` shows no migration and no snapshot change. (`verify-schema` exists and was last run in Part 4 — this row is « nothing to verify », not « the verb is missing ») |
| Console typecheck | `npx tsc --noEmit` | clean |
| Console device gate | `npm run check:responsive` | **14/14 pass**, and **proven able to fail**: a throwaway probe carrying `min-h-screen`, `max-h-[90vh]`, `text-[9px]` and `hover:scale-105` turned **4** checks red, then green again once deleted. ⚠️ `card-fallback` stayed green against that probe's raw `<table>` — it matches the `<Table>` **primitive**, as Part 2 recorded; this part adds no table |
| Console build | `npm run build` | clean, **11 routes** (was 10); `/bff/annulations` is `ƒ`, as a token-bearing write must be |
| CI | `.github/workflows/ci.yml` | unchanged — the `console` job already runs all three console gates |
| `web/` untouched | `git status` | verified: this part changes no file under `web/` |

### Derived guards that had something to say

- **`SubscriptionExemptionCoverageTests`** — the new write needed its reviewed entry (`PlatformSubscriptions.CancelPeriod`) with a reason, in **both** directions. Resolved by adding it, not by an exemption.
- **`SubscriptionVendorCommandReachabilityTests`** — green, and it was the name to be careful about:
  `CancelSubscriptionPeriodFromConsoleCommand` does **not** contain the substring `CancelSubscriptionPeriodCommand`
  (`…PeriodFromConsole…`), so the source scan over `Controllers/` stays clean. A console command named
  `CancelSubscriptionPeriodCommandForConsole` would have failed it.
- **`PlatformReadShapeTests`** — green with two new names. It asserts in **both** directions, so `IfCancelled` and
  `MakesReadOnly` had to be genuinely reached; `EndsOn`/`State`/`StateLabel` are **reused verbatim** inside the
  preview rather than duplicated under prefixed names, because they mean exactly what they mean elsewhere.

## Deviations — Part 5

### Auto-approved (trivial)

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| The preview is a nested `PlatformCancellationPreviewDto`, not two flat fields | Trivial | A bare nullable date cannot distinguish « this row is already cancelled, there is nothing to preview » from « cancelling would leave the cabinet *sans échéance* » — and both are real states. A null object says the first; a null `EndsOn` **inside** it says the second |
| A native `<textarea>` with the `Input`'s classes rather than a new `Textarea` primitive | Trivial | The payment sheet's native `<select>` precedent, for its reason: it is keyboard-reachable for free and `text-base` keeps it at 16 px so a phone does not zoom on focus. One primitive fewer to keep in step with `web/`'s |
| `/bff/annulations` as its own route rather than a mode flag on `/bff/paiements` | Trivial | Recording money and striking an entry through are different actions with different refusals, and one handler branching on a body field is how the second inherits the first's **idempotency** semantics — which here would replay a correction the vendor may have meant to repeat against another entry |
| `CancelPeriodDialog` reuses the `Sheet` primitive at `max-h-[85dvh]` rather than the payment sheet's full `h-dvh` | Trivial | The plan says « bottom sheet on phone, dialog on desktop ». One field does not need a full-screen takeover; the `flex` + `min-h-0 flex-1 overflow-y-auto` body + `shrink-0` footer is kept, so the primary action stays on screen at whatever height the panel has — including a 380 px landscape one with the keyboard open |

### DEV-17: AC-5.3's consequence travels on the detail read, not behind a preview endpoint

**Date:** 2026-08-11 · **Category:** Technical · **Approved:** **yes — chosen by the user** (`AskUserQuestion`, « On the detail read, per entry »)

- **Plan:** step 41 — « Compute that consequence **from the companion's own fold**, not from a console-side
  estimate. » It does not say *where*.
- **Implemented:** `IfCancelled` on every live row of `GET /api/platform/clinics/{id}`, computed by re-folding the
  real ledger with that entry marked cancelled. The alternative considered was a
  `PreviewSubscriptionPeriodCancellationQuery` fetched when the dialog opens.
- **Justification:** a preview that can **fail** leaves the confirmation with AC-5.3's sentence missing, and the
  fallback is then either to block a legitimate correction or to open the dialog without the consequence stated —
  both worse than the staleness this avoids. Carrying it on the read makes « the dialog cannot exist without the
  figure » **structural**, and the write re-folds and reports the true outcome regardless, so a ledger that moved
  between render and click is answered honestly by `PlatformSubscriptionCancelledDto` rather than by the preview.
- **Impact:** N small folds per detail read (a cabinet's ledger is a handful of entries, and the fold was already run
  once for « période couverte »); two names on `PlatformReadShape`. Part 6's suspension confirmation can follow the
  same shape or not — suspension needs no fold, so it will not need to.

## Owed, and honestly outstanding

- **The eye pass has not been done, for the fifth time.** There is still no browser tooling in this repository. What
  was done instead: the mechanical gate (14/14, proven live against a four-violation probe) and a re-read of the diff
  against `DEVICE-CONTRACT.md` § 1. The structural claims for the new surface are: the confirmation is a **bottom
  sheet below `lg:`** sized in `dvh` (never `vh`) with the body scrolling inside `flex-1` and the destructive action a
  `shrink-0` sibling, so it stays on screen with the keyboard open; it becomes a centred `lg:max-w-lg` dialog above
  that boundary; it is dismissible by a visible control **and** `Escape` **and** the overlay, all three routed through
  one handler that confirms before discarding a typed motif; every control is disabled in flight; the motif field is
  `text-base` (16 px); the trigger is a real `<button>` carrying `touch-target`'s 44 px coarse-pointer floor and a
  row-naming `aria-label`, present at **every** width and never revealed by hover; and the consequence is carried by
  **text**, not by the border colour that accompanies it (AC-6.3's rule one field over). **Structurally sound is not
  looked at.** Widths still owed: 320 / 390 / 820 / 1180 / 1440 px + a landscape phone + a keyboard walk.
- **The cancellation has not been exercised over the wire**, exactly as Part 4's grant has not: the command, its
  refusals, its attribution and the preview↔write agreement are unit-tested, but no request has travelled
  `console/` → `/bff/annulations` → Kestrel's console listener → the handler on a running deployment.
- **The counter job still has not been run against real data** (unchanged from Parts 2–4).

## Next

**Part 6 — suspend for abuse** (steps 44–47). ⚠️ Two things this part settled that Part 6 inherits: the companion
shipped suspension as **one** `SetSubscriptionSuspensionCommand` (`bool Suspend` + `Reason` + `ActedBy`), so per
**R-2** steps 44/45 adapt one call site rather than wrapping two — the step-32 pre-flight recorded that — and
`SetSubscriptionSuspensionCommand` deliberately does **not** use `SubscriptionRefold` (it touches no ledger, so a
lost update there is an ordinary 409). `PlatformAccessAction` gains its last two members, `Suspended` and
`Unsuspended`, each with the write that produces it. AC-6.3's « text and shape, never colour alone » is the one to
hold on the client, and the fiche's state badge already reads « Suspendu » distinctly.

---

# Part 6 — Suspend for abuse

**Session:** 2026-08-11 (sixth session on this branch) · steps 44–47 of the story.
**Status: implemented.**

## Working-tree note (start of session)

The worktree was **clean** (`git status` empty) at `20c221e`. The *main* checkout
(`feature/windows-desktop-app`) carries `clinic-subscription` Part G plus its 52-finding review commit (`58e43ba`)
and an untracked `features/platform-console/`. **None of it was merged in**, and nothing in Part 6 needs it: Part G
is the outbox-parking half and the review pass corrected the fold, which this part reads through
`SubscriptionStateReader` without folding anything. That merge belongs at Part 7's boundary — as Part 5 already
recorded.

⚠️ **A `dotnet run` API was live in the main checkout and was stopped at the user's instruction** partway through the
gate run. It was not the cause of the test failures below (see the SAC note), but it would have been the cause of an
`MSB3021` had the build gone to the default `bin/`.

## Session decision — one command, and the fiche reads the trail back

Two forks were settled by `AskUserQuestion` **before any code was written**, because each changes the shape of the
deliverable rather than a detail:

1. **One command with a `bool Suspend`** rather than the story's `SuspendClinicFromConsoleCommand` /
   `UnsuspendClinicFromConsoleCommand` pair — see DEV-18.
2. **The fiche shows the motif, the moment and the author** of a live suspension, which meant three new names on
   `PlatformReadShape` — see DEV-19.

## What Part 6 delivers

**Domain** — `PlatformAccessAction.Suspended` and `.Unsuspended`, closing the enum on DEV-8's terms. **No migration
and no model change**: the column is `HasConversion<int>()`, and the entitlement has carried
`SuspensionReason`/`SuspendedAtUtc`/`SuspendedBy` since the companion's Part A.

**Application** — **`SetClinicSuspensionFromConsoleCommand`**: mandatory motif when suspending (AC-6.1),
`console|{accountId}` as the author through `AuditActor`'s own constant, the `PlatformAccessEntry` staged **before**
the single save (Part 4's shape, DEV-14), and the outcome read back through `SubscriptionStateReader`. Three refusal
**codes** (`clinic_not_found` · `clinic_already_suspended` · `clinic_not_suspended`). Plus
**`PlatformSuspensionChangedDto`**, **`PlatformSuspensionDto`**, `Suspension` on `PlatformClinicDetailDto`, and
`GetPlatformClinicDetailQuery.ReadSuspensionAsync`.

**API** — `POST /api/platform/clinics/{clinicId}/suspension` and `…/suspension/lifting` on
`PlatformSubscriptionsController`, each with its own `[AllowsWithoutSubscription]` reason, plus
`Models/SuspendClinicRequest`.

**`console/`** — `components/suspend-dialog.tsx` (one component, both directions), `app/bff/suspensions/route.ts`,
and a **« Suspension » section of its own** on the fiche carrying the motif, the moment and the author.

## Step 45 — where « distinct from expiry » actually lives

AC-6.3 is carried in four places, and only the first is code the compiler sees:

1. **`SubscriptionStateReader` already ranks suspension above expiry** (EC-11), so a cabinet that is both reads
   « Suspendu ». Part 6 adds no arithmetic — it reads that rule and reports it.
2. **The journal wording is « Cabinet suspendu » / « Suspension levée »**, never « Abonnement … ». A test asserts the
   labels contain neither « abonnement » nor « paiement », because the failure mode is a plausible French sentence.
3. **The fiche puts suspension in its own section**, below « Abonnement et paiements » — never as a control inside
   it. A « Suspendre » button under the payment history presents the measure as a billing lever, and a vendor who
   reads it that way reaches for a **cancellation**, which is not reversible.
4. **Every statement is text.** « Ce cabinet est suspendu », the quoted motif, and the outcome sentence all say it in
   words; the `border-destructive/40` beside them adds nothing a greyscale printout or a screen reader would miss.

## ⚠️ The trap this part is built around: a lift is not a fix

`Unsuspend` clears a flag. It grants nothing and restores nothing — AC-6.4's « unsuspending restores whatever
entitlement the cabinet had » is a property of **never having spent anything**, not a step this command performs.
So a cabinet suspended in March whose cover ran out in April is **still read-only** when released, for expiry.

The naive handler reports the direction it was asked for (`MakesReadOnly = IsSuspended`). It compiles, it produces
plausible French, and it passes **16 of the 17** new tests — the console then tells the vendor that a practice can
work again when its very next save will be refused. Hence the read-back through `SubscriptionStateReader`, and hence
`Lifting_A_Suspension_Off_A_Lapsed_Cabinet_Leaves_It_Read_Only_For_Expiry` being the class's load-bearing case. It
was **proven red by writing that exact line**, and it was the only test that failed.

## Decisions worth recording (not deviations — the plan is silent on all four)

- **Re-suspending is a 409, not a re-statement.** The entitlement holds exactly one motif, one author and one moment,
  so a second `Suspend` would overwrite a colleague's reasoning with no trace of it on the row. Changing a motif is
  therefore lift-then-suspend, and both halves land in the journal. This is Part 5's « déjà annulée » argument, and
  it lands the same way.
- **Lifting a cabinet that is not suspended is also a 409.** `Unsuspend` clears nothing there, so a silent success
  would write an `Unsuspended` journal row for an action that never happened — and on the fiche it would read as
  having released a read-only cabinet whose real problem is its end date. The refusal says exactly that instead.
- **A POST for the lift, not a DELETE.** What is cleared is a flag on a live entitlement, and the two journal rows
  stay for ever; `DELETE` would advertise a removal to every future reader of the controller.
- **Neither journal row names a `SubscriptionPeriodId`.** Suspension touches no entry, and pointing at one would
  assert that a payment was involved — the same AC-6.3 line, in the data.

## Gate results

| Gate | Command | Result |
|------|---------|--------|
| Backend build | `dotnet build --no-incremental` | **0 errors, 55 warnings — the identical pre-existing baseline, and 0 in any file this part touched** (verified by extracting every warning's filename and grepping it for each of the ten new/changed files: no hit) |
| Backend unit suite | `dotnet test -c Release` | **2644 passed, 0 failed** (Part 5 left it at 2627; +17) |
| Load-bearing case proven able to fail | wrote the naive handler by hand | **yes, and precisely**: `MakesReadOnly: !status.AllowsWrites` → `MakesReadOnly: subscription.IsSuspended` turned **exactly one** test red — `Lifting_A_Suspension_Off_A_Lapsed_Cabinet_Leaves_It_Read_Only_For_Expiry`, 1 of 17 — then green once restored. That 16 of 17 pass over the naive version is the measurement that justifies the case existing |
| Schema | — | **not applicable, and verified so rather than assumed**: no migration, no model change, no snapshot change (`git status`). `PlatformAccessEntryConfiguration` maps `Action` with `HasConversion<int>()` and `ClinicSubscription`'s three suspension columns have existed since the companion's Part A. ⚠️ `verify-schema` **exists** (`api/ClinicManagement.API/Maintenance/VerifySchemaCommand.cs`, dispatched by `Program.cs`) and was last run in Part 4 — this row is « nothing to verify », not « the verb is missing » |
| Console typecheck | `npx tsc --noEmit` | clean |
| Console device gate | `npm run check:responsive` | **14/14 pass**, and **proven able to fail**: a throwaway probe carrying `min-h-screen`, `max-h-[90vh]`, `text-[9px]` and `hover:scale-105` turned **4** checks red, then green again once deleted |
| Console build | `npm run build` | clean, **12 routes** (was 11); `/bff/suspensions` is `ƒ`, as a token-bearing write must be |
| CI | `.github/workflows/ci.yml` | unchanged — the `console` job already runs all three console gates |
| `web/` untouched | `git status` | verified: this part changes no file under `web/` |

### Derived guards that had something to say

- **`SubscriptionExemptionCoverageTests`** — the two new writes needed their reviewed entries
  (`PlatformSubscriptions.Suspend`, `PlatformSubscriptions.LiftSuspension`) with reasons, in **both** directions.
  Resolved by adding them, not by an exemption.
- **`SubscriptionVendorCommandReachabilityTests`** — green, and again the name to be careful about:
  `SetClinicSuspensionFromConsoleCommand` does **not** contain the substring `SetSubscriptionSuspensionCommand`. A
  wrapper named `SetSubscriptionSuspensionCommandForConsole` would have failed the source scan over `Controllers/`.
- **`PlatformReadShapeTests`** — green with five new names, asserted in **both** directions, so `Suspension`,
  `SuspensionReason`, `SuspendedAt`, `SuspendedBy` and `IsSuspended` all had to be genuinely reached.

## ⚠️ Environment: Smart App Control blocked the suite, and Release-in-place is what cleared it

Worth writing down, because it cost several rounds and the suite guide's advice was not sufficient this time.

`dotnet vstest` over a Debug build **failed 841 of 2589 tests**, every one of them
`FileLoadException … An Application Control policy has blocked this file (0x800711C7)` on
`ClinicManagement.Infrastructure.dll` and `ClinicManagement.API.dll`. Three retries of the same command, a fresh
`%TEMP%` path and the in-repo `api/.testrun/` path all reproduced it **identically** (841 every time) — so the
guide's « treat a block as transient and retry » did not apply here, and neither did its location rule.

**What worked: `dotnet test -c Release` in place** — 2644 passed, 0 failed, first attempt. A Release build emits
different bytes, so SAC judges a different file; the blocked assemblies were the *Debug* ones. Also note the count:
the Debug run reported `Total: 2589` against Release's `2644`, i.e. the block **suppresses discovery** too, so a
blocked run under-reports the suite and « 2589 total » must not be read as the baseline.

Stopping the user's running API was a separate matter (it would have locked `bin/` for an in-place Debug build) and
changed nothing about the SAC failures.

## Deviations — Part 6

### Auto-approved (trivial)

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| `SetSuspension` private helper behind the two actions | Trivial | The two endpoints differ only in a boolean and a body; the mediator send, the success branch and the three-code status map would otherwise be written twice, and the second copy is where a new refusal code fails to be mapped |
| `SuspendDialog` serves both directions rather than two components | Trivial | The panel, the confirm-before-discard, the refusal handling and the outcome are identical — only the question changes. Two components would be the same `fixes-dont-propagate` shape one layer up, and the direction is a **prop** off the server's own state, never a toggle |
| One `/bff/suspensions` route with a required `suspend` boolean | Trivial | Part 5 split `/bff/annulations` off `/bff/paiements` because those are different actions with different **idempotency** semantics. These two are one decision with a sign, served by one command; the flag is `typeof === "boolean"`-checked rather than defaulted, so a truncated body is refused instead of silently suspending |
| A native `<textarea>` with the `Input`'s classes | Trivial | The cancel dialog's own precedent, for its reason: keyboard-reachable for free, and `text-base` keeps it at 16 px so a phone does not zoom on focus |

### DEV-18: one command with a flag, not the story's suspend/unsuspend pair

**Date:** 2026-08-11 · **Category:** Technical · **Approved:** **yes — chosen by the user** (`AskUserQuestion`,
« One command with a bool »)

- **Plan:** step 44 and the story's file table name `SuspendClinicFromConsoleCommand` **and**
  `UnsuspendClinicFromConsoleCommand`, « delegating to the companion's handlers ».
- **Implemented:** one `SetClinicSuspensionFromConsoleCommand` with `bool Suspend`, reached by **two** endpoints.
- **Justification:** the companion shipped suspension as one `SetSubscriptionSuspensionCommand` (the step-32
  pre-flight recorded this under **R-2**: adapt the call site, never re-implement), and two console handlers would be
  two copies of « resolve the cabinet · mutate · stage the access row · save » differing in one boolean — this
  repository's dominant defect shape, and the place a later refusal gets added to one and not the other. Keeping
  **two endpoints** preserves what a pair would have bought: the direction is in the URL, so no truncated or
  mis-serialised body can flip « suspendre » into « lever », and which journal action is recorded is decided once.
- **Impact:** the story's two file names do not exist; one does. Part 7's runbook names the endpoints, not the
  commands, so nothing downstream refers to the pair.

### DEV-19: the fiche shows the motif, which put free text on a closed read shape

**Date:** 2026-08-11 · **Category:** Scope · **Approved:** **yes — chosen by the user** (`AskUserQuestion`,
« Show motif, moment and author »)

- **Plan:** AC-6.1 says a suspension « is recorded with its author and moment » and AC-6.3 that suspension is
  distinct from expiry « throughout the console ». Neither says the console *reads* the trail back, and Part 3's
  detail showed only `stateLabel`.
- **Implemented:** `PlatformSuspensionDto` on the detail read, and five new names on `PlatformReadShape`.
- **Justification:** without it the motif exists only in PostgreSQL and in `subscription-report`, so the screen that
  can lift a suspension cannot say why it exists — and « suspendu pourquoi ? » is the question the companion made the
  motif mandatory for. ⚠️ **`SuspensionReason` is the first free text this surface returns**, which is the reason this
  is a recorded deviation and not a trivial one: it is admissible because of who writes it and about whom — the
  *vendor* types it about a *practice*, no clinic user can reach the field, and the entitlement is written only by
  the five vendor verbs and this console. `SuspendedBy` is a `console|…` account id, never a person at the practice.
- **Impact:** `PlatformReadShapeTests` asserts in both directions, so all five names are genuinely reached. The trail
  is **withdrawn** when the suspension is lifted (`Unsuspend` clears it, by design), which is why the fiche links to
  `/journal` — and why the two new journal actions are the durable record rather than a convenience.

## Owed, and honestly outstanding

- **The eye pass has not been done, for the sixth time.** There is still no browser tooling in this repository. What
  was done instead: the mechanical gate (14/14, proven live against a four-violation probe) and a re-read of the diff
  against `DEVICE-CONTRACT.md` § 1. The structural claims for the new surface are: the confirmation is a **bottom
  sheet below `lg:`** sized in `dvh` (never `vh`) with the body scrolling inside `flex-1` and the action a `shrink-0`
  sibling, so it stays on screen with the keyboard open; it becomes a centred `lg:max-w-lg` dialog above that
  boundary; it is dismissible by a visible control **and** `Escape` **and** the overlay, all three routed through one
  handler that confirms before discarding a typed motif; every control is disabled in flight; the motif field is
  `text-base` (16 px); the trigger is a real `<button>` carrying `touch-target`'s 44 px coarse-pointer floor and a
  cabinet-naming `aria-label`, present at **every** width and never revealed by hover; the new section's figure list
  is a single-column `<dl>`, so nothing reflows; and every state is carried by **text** rather than by the border
  colour beside it. **Structurally sound is not looked at.** Widths still owed: 320 / 390 / 820 / 1180 / 1440 px + a
  landscape phone + a keyboard walk.
- **Neither write has been exercised over the wire**, exactly as Parts 4–5 have not: the command, its three refusals,
  its attribution and the read-back are unit-tested, but no request has travelled `console/` → `/bff/suspensions` →
  Kestrel's console listener → the handler on a running deployment.
- **The counter job still has not been run against real data** (unchanged from Parts 2–5).
- **AC-6.2's cabinet-side half is the companion's and was not re-verified here.** « A suspended cabinet is read-only
  exactly as an expired one is, and is told it is suspended » is `SubscriptionGateMiddleware` +
  `SubscriptionRefusals.Suspended` + the « Abonnement » screen, all shipped and tested in
  `features/clinic-subscription/`. This part asserts the console's half — that the state it reports is `Suspended`
  and never `Expired` — and takes the cabinet's on trust from those tests, which is what FR-4 asks for.

## Next

**Part 7 — verification, operator runbook and the promise** (steps 48–52). ⚠️ Three things this part leaves it:
the `clinic-subscription` merge is now **due at that boundary** (Part G + the 52-finding review are on the main
checkout and this branch is behind them); step 48's `verify-schema` before/after diff has had **no migration since
Part 4**, so the batch to run it over is Parts 1–4's; and step 50's « what the vendor sees » sentence must now
include the **suspension motif** — free text the vendor wrote about the practice, which is new since the sentence
was drafted, and the one item on that list a cabinet might be surprised to learn is readable.

---

# Part 7 — verification, operator runbook and the promise (steps 48–52)

**Date:** 2026-08-11 · **Status: implemented** — and it is the part that found the feature's one real security
defect, which is what step 51's « by trying them » is for.

## Working-tree note (start of session)

Clean. `git status` showed only the untracked `features/platform-console/` in the **main** checkout, which belongs
to a different branch and was not touched. Nothing under `web/` changed in this part (`git status web` → empty).

## Step 0 — the merge that was due at this boundary

`feature/windows-desktop-app`'s `clinic-subscription` Part G **and its 52-finding review pass** were merged in at
`0b97d09` — the third and last merge of the companion. Parts 4–6 were built against Parts A–F, so without this the
verification would have been run against code that will not ship.

Six conflicts, all in files both features edit, plus two compile fallouts the merge could not see. The two worth
knowing:

- **`ClinicSubscription.RecomputeFrom` had been changed on both sides.** Part 4 replaced `Fold` with `FoldWithSpans`
  (to write `LatestCoverKind`); the review made the method take `whenUtc` instead of reading the clock. Kept **both**,
  and the four console test fixtures now pass the instant.
- **`SubscriptionTrial.IsOnTrial` needed the review's fix *ported*, not merged.** Part 4 **moved** that method out of
  `GetSubscriptionQuery` into its own class, so the review's correction — « any live non-trial entry ends the trial
  label, even one whose cover starts later » — landed on the copy that no longer exists. Git resolved that as « HEAD
  deleted it », i.e. silently dropping a real fix. This is the shape to watch for whenever two branches touch code one
  of them has relocated: **the conflict is not where the fix is missing.**

`PlatformAccountCommand` also had to switch from `ProvisionClinicCommand.ReadOption` (now private) to the review's new
`ConsoleArgs`.

## Step 48 — the schema gate, before and after, diffed

Run as the verb's own workflow prescribes, against a scratch database **cloned from the dev one** so the « before »
state was a real pre-batch schema and the user's data was never migrated. Batch = Parts 1–4's four migrations
(`AddPlatformConsole` · `AddClinicActivityCounters` · `AddPlatformAccessLedger` · `AddPlatformConsoleWrites`); there
has been no migration since Part 4, so this is the batch the whole feature owes.

| | Result |
|---|---|
| **Before** | **11 checks found drift** — every one a `MISSING` index or FK in the four new tables, plus `platform-account-has-totp-or-unenrolled`, both counter checks and `subscription-cover-kind-matches-ledger` reading « not applicable — what it measures does not exist yet » |
| **After** | **1 check found drift**, and it is the *expected* one: `clinic-activity-snapshot-covers-every-clinic` — « 4 cabinet(s) have no activity snapshot … either the nightly pass has not run yet on this deployment, or it has been failing for those cabinets while logging a clean run » |
| **Diff** | Only the intended objects. All 11 `MISSING` lines → `[ ok ] … present`, `ClinicActivitySnapshots.CollectedThisMonth: (18,3)` appears, and the four « does not exist yet » lines become real assertions |

⚠️ **The most valuable line in the diff is one nothing else in this repo can produce**:
`subscription-cover-kind-matches-ledger` went from « not applicable » to « 4 entitlement(s), each naming the cover its
ledger actually folds to » — i.e. Part 4's denormalised `LatestCoverKind` was independently re-derived through the
**post-review** fold, over real rows. That is the merge's fold resolution verified by something other than the code
that performed it.

⚠️ **`dotnet ef` needs `--configuration Release` on this machine.** `dotnet ef database update` failed with
`FileLoadException … An Application Control policy has blocked this file (0x800711C7)` on the **Debug**
`ClinicManagement.API.dll` — Part 6's lesson applies to `dotnet ef` as much as to `dotnet test`. Also worth knowing:
`dotnet ef` reads its connection string from the **startup project's own config chain**, and `launchSettings.json`
overrides an exported `ASPNETCORE_URLS` / `ConnectionStrings__*` — so it pointed at the dev database and said
« already up to date » while the scratch one stayed empty. The batch was applied as SQL instead
(`dotnet ef migrations script … --idempotent` → `psql -f`), which is deterministic and needs no design-time host.

## Step 51 — the two « cannot look the same » requirements, tried rather than reasoned about

The first live end-to-end walk this feature has had: API booted in `HostedMultiTenant` with `Console__Port=5443`,
a console account created with the verb, TOTP enrolled with a code computed from the printed secret, signed in, and
the console endpoints called with the real token. What that established, in order:

| Checked | Result |
|---|---|
| Both listeners bound in one call | startup log: « Bound the public API on port 5100 and the vendor console on port 5443 » |
| The public API survived the second listener | `GET :5100/api/auth/mode` → **200** with the real payload |
| A console path on the public port | `GET :5100/api/platform/summary` → **404** — absent, not present-and-refusing |
| A clinic path on the console port | `GET :5443/api/auth/mode` → **404** |
| Password-only sign-in on an unenrolled account (EC-2) | **403** `totp_enrolment_required`, no secret and no session in the body |
| Enrolment returns the recovery codes once | **200**, eight codes |
| **EC-12** — the portfolio with the database frozen (`docker pause`) | **500 + « Une erreur est survenue… »**, and the page's `catch → <ReadFailure>` renders « je n'ai pas pu lire ». **Never** an empty table |
| **EC-15** — a deployment whose counter pass has never run | `summary` answers `neverMeasured: 4`, `dormant: 0` — reported as unmeasured, **not** as four dormant cabinets. Same fact from `verify-schema`, independently |

⚠️ **EC-12 was tested by freezing the container, not by stopping it, and the first attempt was invalid.**
`ALTER DATABASE … CONNECTION LIMIT 0` looked like the polite way to make the database unreadable without disturbing
the user's dev stack — but `clinic_user` is the container's superuser and **the limit does not apply to superusers**,
so new connections kept succeeding and only the in-flight one failed. The 500 it produced was real, but for the wrong
reason. `docker pause` is the honest version, and reversible in a second.

## ⚠️ What step 51 actually found: `PlatformAccountStateMiddleware` was inert in production

Committed separately as `3d348a1`, ahead of this part's documentation, because it is a security fix and not
housekeeping.

**The experiment:** signed in, ran `platform-account --deactivate --email ops@editeur.tn` (which succeeded, set
`IsActive=false` and bumped `TokenVersion` to 2), then called `GET /api/platform/summary` again with the **same
token**. Expected 401 on the very next request (AC-1.6). Got **HTTP 200 and the whole portfolio.** The one-time
bootstrap password was equally unenforced: `MustChangePassword` was `true` in the row and every console route
answered normally, so AC-8.1's « one-time » was true of nothing.

**Why:** the middleware read `context.User`. For a **console** token that is never populated where it runs —
`UseAuthentication` authenticates only the *default* (clinic) scheme, a console token fails that scheme **by design**
(that being AC-1.4, the feature's own guarantee), and the console's own scheme is authenticated inside
`AuthorizationMiddleware` because `AuthorizationPolicies.PlatformConsole` **pins** it, which runs *after* this
middleware. So `ConsoleAccountId` saw an unauthenticated principal on every request, returned null, and took the
pass-through branch. Both of AC-1.6's revocations and AC-8.1 were absent for the whole life of the feature.

**Why nothing caught it:** `PlatformAccountStateTests` set `context.User` by hand — the one thing production does not
do. Its ordering guard against `Program.cs`'s source passed too, because the *position* was right; it is the
*mechanism* that was wrong. The generalisable rule, now in the test-suite guide: **a middleware whose subject is
established by a pinned authentication scheme cannot be unit-tested through `DefaultHttpContext.User`** — assigning
the principal asserts the very arrangement that is broken.

**The fix:** the middleware authenticates the console scheme itself and every check reads *that* principal.
⚠️ **Two-layer defect**: after routing the account-id lookup through the resolved principal, `HasCurrentTokenVersion`
was **still** reading `context.User`, so a stale token version passed — caught by the second new test, which is why
there are two rather than one. ⚠️ Moving the middleware after `UseAuthorization` would also have worked (that *does*
write the combined principal back) and was rejected: it lets a revoked token be authorized before being refused, and
rests on a framework detail rather than on this file.

**Re-verified over the wire after the fix:** one-time password → **403** `must_change_password` on the summary and
**200** on the password change; a stale `TokenVersion` → **401**; deactivation → **401 on the very next request**.

## Steps 49–50 — the runbook and the promise

`deploy/README.md`'s console section gains three subsections, and the two existing ones are corrected:

- **« Recording a payment, correcting one, stopping a cabinet »** — the console's four writes mapped to the verbs that
  do the same thing, plus the three properties an operator will otherwise discover by accident (a repeated payment
  replays rather than erring; nothing is ever deleted; a lift restores the entitlement and leaves an *expired* cabinet
  still read-only).
- **« When the console is unavailable »** — AC-8.3, stated as the fallback being undegraded rather than as a list. With
  the two asymmetries: the verbs are the only place a period id is printed and the only way to pass an explicit
  `--until`, while the console is the only one that records a *person* (`console|…` rather than
  `job|subscription-grant`).
- **« What the console can and cannot see »** — rewritten as a **French block quote meant to be sent to a clinic**,
  because the operator's paraphrase is exactly where AC-7.4 fails. It names the administrator's contact details, the
  counts, the subscription and its payment history, **the cabinet's own monthly collected total**, and — new since the
  sentence was first drafted — **the suspension motif**, which is free text the *vendor* wrote about the practice and
  the one item a clinic might be surprised is readable. It then says what is not visible, and why that is structural
  (a closed field list that fails the build) rather than a promise.
- **« Failures worth recognising »** grew from two entries to five: EC-12's « je n'ai pas pu lire », EC-15's « jamais
  mesuré » on a deployment whose first night has not passed, and « a deactivated console account still works », which
  is now stated as the defect above rather than as a hypothetical.

**Decision recorded:** the promise is a **document only** — chosen by the user (`AskUserQuestion`, « Document only »)
over also rendering it on the clinic's « Abonnement » screen. The plan's own verb is « write », and a new clinic-facing
UI block would pull in `web/`'s device gate and an eye pass this repo still has no browser for.

## Step 52 — the maps

| File | Change |
|---|---|
| `CLAUDE.md` | A **Part 7** bullet (the defect, the two EC verifications, the schema diff), and the Part 1 bullet's claim about `PlatformAccountStateMiddleware` annotated — it described behaviour that did not exist until now |
| `api/ClinicManagement.API/CLAUDE.md` | The middleware-order line was **stale**: it named neither console middleware, both added in Part 1. Now correct, with a paragraph on `PlatformAccountStateMiddleware` including why it was inert |
| `api/ClinicManagement.UnitTests/CLAUDE.md` | `PlatformAccountStateTests` as the suite's clearest example of a class that was green while the thing it guards did nothing, and the pinned-scheme rule that generalises from it |
| `api/ClinicManagement.Application/CLAUDE.md` · `Domain/CLAUDE.md` | Unchanged by Part 7 itself; both were **merged** at `0b97d09` (the Domain one needed a hand-merge of two rows whose ⚠️ notes had both grown) |

## Gate results

| Gate | Command | Result |
|------|---------|--------|
| Backend build | `dotnet build --no-incremental` | **0 errors, 55 warnings** — the identical pre-existing baseline, and **0 in any file this part touched** (verified by extracting every warning's filename and comparing against the changed set) |
| Backend unit suite | `dotnet test -c Release` | **2662 passed, 0 failed.** Part 6 left 2644; the merge brought +16, this part's two new cases +2 |
| Load-bearing case proven able to fail | the wire, before the fix | **yes, and it is the strongest proof in this feature**: the defect was found by the check rather than the check being validated against a known defect — deactivate, re-call, **200**. After the fix, **401** |
| Schema | `dotnet run -- verify-schema`, before/after, diffed | **clean after, and the diff shows only the intended objects** — see step 48. The one remaining DRIFT is EC-15's own signal on a deployment whose counter pass has never run |
| Console typecheck | `npx tsc --noEmit` | clean |
| Console device gate | `npm run check:responsive` | **14/14 pass** |
| Console build | `npm run build` | clean, **12 routes** |
| `web/` untouched | `git status web` | empty |
| CI | `.github/workflows/ci.yml` | unchanged — the `console` job already runs all three console gates |
| Live walk | see step 51 | both listeners, the two-way port gate, EC-2, enrolment, EC-12, EC-15, and AC-1.6/AC-8.1 before **and** after the fix |

## Owed, and honestly outstanding

- **The eye pass has still not been done, for the seventh and last time.** There is no browser tooling in this
  repository. Part 7 changed **no** rendering file, so nothing new is owed by this part — but the widths owed by
  Parts 1–6 are unchanged: **320 / 390 / 820 / 1180 / 1440 px + a landscape phone + a keyboard walk**, on login, the
  list, the detail, the journal, the payment sheet and both confirmations. Everything mechanical passes (14/14) and
  every part re-read its diff against `DEVICE-CONTRACT.md` § 1; **structurally sound is not looked at.**
- **The four writes still have not been exercised over the wire.** This session drove the *reads* and the whole auth
  surface end to end, which is what caught the middleware defect — but no payment, cancellation or suspension has
  travelled `console/` → `/bff/*` → the console listener → the handler against a running deployment. Given what the
  first live walk found in the auth path, this is the highest-value thing left in the feature.
- **The counter job has still never been run against real data.** `count-clinic-activity` runs at 03:00 UTC; the walk
  used a database whose snapshots were never written, which is exactly why EC-15 was verifiable and the portfolio's
  activity figures were not.
- **`ClinicActivityCounterJob`'s output is therefore unverified end to end**: `verify-schema`'s
  `clinic-activity-snapshot-is-internally-consistent` passed over **zero** rows, which is a true statement about an
  empty table and not evidence about the pass.
- **The `console/` app itself was never opened in a browser.** EC-12's client half is a code reading
  (`catch → <ReadFailure>`) plus the server's 500, not a screenshot.

## Next

**The story is `implemented` — all seven parts.** What follows is `/review-story`, and the two items worth putting in
front of it are the last two bullets above: the four writes over the wire, and the eye pass. Neither is a gap in the
code; both are gaps in what has been *observed*.
