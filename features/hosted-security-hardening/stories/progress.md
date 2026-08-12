# Progress — Hosted Security Hardening

**Story:** [story-1-full-hosted-security-hardening.md](./story-1-full-hosted-security-hardening.md)
**Worktree:** `.claude/worktrees/hosted-security-hardening/` · **Branch:** `feature/hosted-security-hardening`
**Base:** `9a90d54` (tip of `feature/windows-desktop-app`)

## Part status

| Part | Name | Plan part | Status |
|------|------|-----------|--------|
| A | Identity | Part 1 | in-progress |
| B | Transit | Part 2 | not-started |
| C | Custody | Part 3 | not-started |
| D | Evidence & surface | Part 0 + Part 4 | not-started |

### Part A sub-parts

| Sub-part | Covers | Steps | Status |
|----------|--------|-------|--------|
| A.1 | The capability and the served password floor | 1–7 | in-progress |
| A.2 | The factor itself, and the login screen that enrols it | 8–19 | not-started |
| A.3 | « Sécurité », step-up, and the three ways back | 20–26 | not-started |
| A.4 | Session replay, cookie hardening, the guards | 27–32 | not-started |

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
