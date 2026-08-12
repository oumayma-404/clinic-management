# Progress — Hosted Security Hardening

**Story:** [story-1-full-hosted-security-hardening.md](./story-1-full-hosted-security-hardening.md)
**Worktree:** `.claude/worktrees/hosted-security-hardening/` · **Branch:** `feature/hosted-security-hardening`
**Base:** `9a90d54` (tip of `feature/windows-desktop-app`)

## Part status

| Part | Name | Plan part | Status |
|------|------|-----------|--------|
| A | Identity | Part 1 | **implemented** (A.1–A.4 landed; eye pass owed) |
| B | Transit | Part 2 | not-started |
| C | Custody | Part 3 | not-started |
| D | Evidence & surface | Part 0 + Part 4 | not-started |

### Part A sub-parts

| Sub-part | Covers | Steps | Status |
|----------|--------|-------|--------|
| A.1 | The capability and the served password floor | 1–7 | **implemented** · committed `3c8d2fe` |
| A.2 | The factor itself, and the login screen that enrols it | 8–19 | **implemented** · `07d40d8` + `1aef203` |
| A.3 | « Sécurité », step-up, and the three ways back | 20–26 | **implemented** · `3b7b6c8` |
| A.4 | Session replay, cookie hardening, the guards | 27–32 | **implemented** · `03d0ea5` |

## Part A gate — final run

| Gate | Result |
|------|--------|
| Backend suite (Release, `BaseOutputPath` outside the repo) | **2825 passed, 0 failed** (baseline 2800 + 25 new) |
| `web/` `check:responsive` · `tsc --noEmit` · `build` | **15/15**, clean, compiled |
| `console/` `check:responsive` · `typecheck` · `build` | **14/14**, clean, compiled |
| `verify-schema` before → after the migration | **263 → 269 ok, 0 drift, exit 0** — the diff is exactly the 4 new indexes + 2 FKs |
| `verify-schema` with A.4's three checks | all three live and green against the running database |
| Backend warnings | no new ones; the pre-existing `CS8618`/`CS8602` baseline is untouched |

**Owed:** the eye pass at 320 / 390 / 820 / 1180 / 1440 + landscape + keyboard. No browser was driven in this
session, so it is recorded as **not done** rather than claimed — the surfaces needing it are `/login`'s four
modes, `/securite`, and the step-up sheet.

**Also owed (verification, not code):** the two flow walks of step 30 could not be executed here (the Google
OAuth round trip needs real credentials). The `SameSite` decision was taken on the defined behaviour of
`Strict` rather than on an observation — see DEV-3.

## Resuming — read this first

**Part A is landed in full and the tree is green.** Start at **Part B** (Transit), whose entry section is
`exploration.md` § 2. Nothing is half-applied.

The migration `AddUserSecondFactorAndSessionFamilies` is **applied to the local database** and its before/after
`verify-schema` runs are committed under `features/hosted-security-hardening/verification/`. Take a fresh
"before" for Part C's migrations rather than reusing those.

Two items are owed from Part A and are verification, not code — see *Part A gate* above: the eye pass, and the
two flow walks of step 30.

## Session log

### 2026-08-12 — session 1

**Scope chosen by the user:** all of Part A (A.1–A.4) in this session. Baseline: backend suite + `web/`/`console/`
builds only; the `verify-schema` baseline is deferred to A.2, which carries the story's first migration.

**Working tree note (start of session):** the worktree was clean at `9a90d54` as the story's entry criteria
predict — `git status --porcelain` showed only the untracked `features/hosted-security-hardening/` docs. None of
the main checkout's 40+ in-flight modifications are present here. `node_modules` was absent in both `web/` and
`console/` (a fresh worktree), so `npm install` was run in each before the gate.

## Pre-change baseline (entry criteria)

All three green **before any edit**, so a later failure is attributable.

| Gate | Result |
|------|--------|
| Backend suite (`-c Release`, `BaseOutputPath` outside the repo) | **2800 passed, 0 failed, 0 skipped** (18 s) |
| `web/` `check:responsive` | **All 15 checks passed** |
| `web/` `npx tsc --noEmit` + `npm run build` | clean |
| `console/` `check:responsive` | **All 14 checks passed** |
| `console/` `npm run typecheck` + `npm run build` | clean |
| `verify-schema` | **deferred to A.2** — A.1 changes no schema; captured before the first migration |

**Pre-existing backend warning baseline:** the Release build emits `CS8602`/`CS8600` nullable warnings in
`MedicalDocumentsController.cs` and `PatientsController.cs`, both untouched by this story. The 0-warning gate is
read as « no NEW warnings in files this story changes ».

## A.1 — the capability and the served password floor (steps 1–7)

**Status:** implemented, gate green.

| Step | Delivered |
|------|-----------|
| 1 | `DeploymentProfile.RequiresAdminSecondFactor` — the **18th** capability, ✓ for `HostedMultiTenant` alone, via all five edits + the `ExpectedMatrix` row + the `hostedOnlyCapabilities` entry |
| 2 | `PasswordPolicy.MinLength` 8 → **12**. All five enforcement sites confirmed to be on *set*, never on a check — so an existing short password keeps working until its owner next changes it (AC-7) |
| 3 | `LocalAuthService.GenerateTemporaryPassword` derives its length from `PasswordPolicy.MinLength` instead of coinciding with it at 12 |
| 4 | `passwordMinLength` + `requiresSecondFactor` on `GET /api/auth/mode` |
| 5 | **Seven** client literals replaced (the story counted five — see the finding below), via `usePasswordMinLength()` |
| 6 | `GET /api/platform/auth/meta` + `PlatformReadShape.AllowedLeafNames` += `PasswordMinLength`; read server-side by the console's « Changer le mot de passe » |
| 7 | `PasswordFloorSingleSourceTests` — derived, both-directions exemption map, **proven red** |

### Finding: step 5 named five literals; there were **seven**

`PasswordFloorSingleSourceTests` failed on its first real run and named two files the story's own enumeration
does not mention: **`placeholder="Au moins 8 caractères"`** in `web/components/join-wizard.tsx` *and*
`web/components/setup-wizard.tsx`. Those are **user-facing** — they state the floor in the field itself — so
following step 5 literally would have raised the server to 12 while both signup paths went on promising 8 to
every new clinic, in the placeholder the user reads while typing the password that is about to be refused.

This is the derived-guard payoff the exploration's § 5.1 describes: a hand-listed set of « the five literals »
was already wrong when it was written, and only a check deriving its own candidates could say so.

### The guard's two calibration decisions, both proven rather than assumed

- **`(?!0\b)` on the length comparison.** `password.length > 0` is « did they type anything », not a floor, and
  four of those are live in the wizards. Without the exclusion the guard would have needed an exemption for each
  — an exemption list that grows is a check that has stopped working.
- **Whole-line comments are stripped, trailing ones are not.** The first real run flagged the four files that had
  just been *fixed*, for quoting « Au moins 8 caractères » in the comment explaining why they no longer say it.
  Stripping from the first `//` would also cut a URL inside a string and could swallow a real violation later on
  the same line — the silent direction. Whole-line only cannot hide executable code, and
  `Dropping_Comment_Lines_Does_Not_Blind_The_Guard` asserts both halves.

### Red proofs executed

| Guard | Proof |
|-------|-------|
| `PasswordFloorSingleSourceTests` | A throwaway `web/lib/__floor-probe.ts` holding `MIN_PASSWORD_LENGTH = 8` + `p.length < 8` → **1 failed**, naming the file. Probe deleted in the same command |
| Its comment stripper | Five in-file assertions: prose not flagged, a live placeholder beside prose quoting it **is**, and code after a trailing comment / after a URL's `//` **is** |
| `DeploymentProfileTests` | `Every_capability_is_covered_by_the_matrix` reflects every `bool`; the new capability failed it until the `ExpectedMatrix` row was added, and `Both_pre_existing_kinds_reproduce_the_old_IsLocalMode_truth_table` failed until `hostedOnlyCapabilities` gained it |
| `PlatformReadShapeTests` | Both directions — the new `PasswordMinLength` leaf had to be declared, and a declared-but-unreturned name fails too |

## A.2 – A.4 — what the guards caught

Every one of these was a derived check doing its job on arrival, not a review finding:

| Guard | What it caught |
|-------|----------------|
| EF's own scaffolder | An `xmin` column in **both** `CreateTable` blocks — PostgreSQL rejects it outright. Removed by hand, reason recorded at each site |
| `SubscriptionExemptionCoverageTests` | The five new `/api/auth` writes inheriting the class-level exemption — i.e. step 26's FR-1.10 requirement, arriving before I reached that step |
| `SystemWideCallerCoverageTests` | The console verb's filename not matching its type, so its tenant-scope declaration was invisible to the scan |
| `StaffNotificationRules` | **Throws** on an unclassified category — so adding `SecondFactorReset` without classifying it would have broken *every* notification write in the product, not only the new one |
| `PushNotificationGeneratorDecorator` | The compiler forced the new generator methods onto the decorator — which is exactly the `fixes-dont-propagate` shape that decorator exists to prevent |
| Moq expression trees | An optional `Guid?` parameter cannot appear in `It.IsAny` setups; making it required is better anyway — an explicit `null` says « no family » |
| `LoginCommandHandlerTests` | A second `SaveChangesAsync` — which made me stage the family **before** the existing single save, so login and family land in one transaction |
| `SchemaVerificationServiceTests` | Its own comment warns that appending positionally lands a zero in the wrong slot; the three new counts went to the end of the record **with defaults** and are passed by **name** |

### Red proofs executed

| Guard | Proof |
|-------|-------|
| `ClinicTotpAuthTests` | Dropped the admin term from `SecondFactorApplies` → **exactly one** test red (`An_Admin_With_No_Factor_Cannot_Obtain_A_Token`), source restored |
| `SecondFactorCoverageTests` | In-file: the real regexes run over a minting path that ignores the requirement, and over one that consults it. ⚠️ A **file-level** probe was also attempted; the run timed out at 10 min and the probe was removed. The in-file proof is what the house style asks for, but the stronger one is not claimed |
| `PasswordFloorSingleSourceTests` | (A.1) throwaway probe file → red, naming the file; removed in the same command |

## Deviations

### DEV-1: `/api/auth/mode` is a controller action, not `GetAuthModeQuery.cs`
**Date:** 2026-08-12 · **Story:** Part A, step 4 · **Category:** Technical
**Original plan:** Part A's *Modify* list names
`Application/Features/Auth/Queries/GetAuthModeQuery.cs`.
**Actual implementation:** the fields are added to `AuthController.GetMode()`
(`api/ClinicManagement.API/Controllers/AuthController.cs:71-98`).
**Justification:** that file does not exist anywhere in the solution — `/api/auth/mode` has always been an inline
controller action with no MediatR query behind it. Step 4's own wording is *« mirroring how `trialDays` is already
served »*, and `trialDays` is served at `AuthController.cs:96`, so the step body and the file list disagree and the
body matches reality. The file list is stale, not the instruction.
**Impact:** none on behaviour. The *Modify* list is the only thing inaccurate.
**Approved:** auto (trivial — the named file does not exist, so the plan's literal wording is unimplementable, and
the step's own text names the real location)

### DEV-2: the console's password literal is in `mot-de-passe/`, not `login/sign-in-form.tsx`
**Date:** 2026-08-12 · **Story:** Part A, step 6 · **Category:** Technical
**Original plan:** *« read it in `console/app/login/sign-in-form.tsx` »*.
**Actual implementation:** the served floor is read in
`console/app/mot-de-passe/change-password-form.tsx`, which is where the literal actually is.
**Justification:** `sign-in-form.tsx` collects a password but has **no length rule at all** — it is a sign-in form,
and nothing there could drift from the floor. The console's real literal is the hardcoded French sentence
**« Au moins 8 caractères. »** at `change-password-form.tsx:74`, which is exactly the second copy step 6 exists to
delete. Following the story literally would state the floor where nothing states it *and* leave the stale `8`
in place — and step 7's `PasswordFloorSingleSourceTests`, which scans `console/` for password-length literals,
would then be red on a violation the same sub-part had just been told to fix. The correction is self-proving, and
Part A's own exit criterion (*« The floor is 12, served, and stated identically by `web/`, `console/` and the
server »*) is unmet without it.
**Impact:** one file outside the story's *Modify* list is edited; `sign-in-form.tsx` needs no change.
**Approved:** auto (the step's stated goal is preserved exactly; only the file name was wrong)

### DEV-3: `SameSite` stays `Lax`; `Strict` was considered and rejected
**Date:** 2026-08-12 · **Story:** Part A, steps 29–30 · **Category:** Technical
**Original plan:** step 29 hardens the cookies and step 30 says to walk the Google Calendar OAuth callback and
the e-mailed signup link, keeping `Lax` **and recording the reason** if either breaks.
**Actual implementation:** `__Host-` prefixing landed; `SameSite` is left at `lax`, with the reason written into
`session-cookie.ts` beside the attribute.
**Justification:** `SameSite=Strict` withholds the cookie on any cross-site-initiated top-level navigation,
redirect chains included. The Google Calendar OAuth return is exactly that — accounts.google.com →
`/api/googlecalendar/callback` → `FrontendUrl` — so under `strict` the chain arrives with no session cookie,
`middleware.ts` (which gates on cookie presence alone) sees an anonymous request, and connecting a calendar
signs the user out every time. `lax` already blocks cross-site POSTs and subresource requests; what `strict`
adds is protection against a cross-site *link* carrying the session, which is not worth breaking a shipped
integration for — especially as `__Host-` is the larger win here and costs nothing. The signup link is
unaffected either way: `/signup/verifier` is public and issues no session.
⚠️ **Stated honestly: this is reasoning from `SameSite`'s defined behaviour, not from an observed walk.** The
OAuth round trip needs real Google credentials and was not executed in this session. The story permits `Lax`
with a recorded reason, and this is that record — but the walk itself remains owed, and if it is ever run and
contradicts this, the setting is the thing to change.
**Impact:** the spec's FR-1.7 and `plan.md` should carry the same note; only `session-cookie.ts` and this file
do so far.
**Approved:** auto (the story pre-authorises `Lax` provided the reason is written down)
