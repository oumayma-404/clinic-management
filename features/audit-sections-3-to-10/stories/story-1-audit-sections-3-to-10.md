# Story 1: Close every finding in audit §§ 3–10

**Status:** in-progress — **P2 complete** (13 / 57 bullets, 45 / 301 ACs). See [progress.md](./progress.md).
**Spec:** [../spec.md](../spec.md) · **Plan:** [../plan.md](../plan.md) · **Design:** [../design.md](../design.md)
**Branch:** `feature/audit-sections-3-to-10`

> **One story by explicit user decision.** `/break-plan` was deliberately skipped — with a single story there is
> nothing to decompose, and the story body already lives in the plan. This file is a pointer plus the entry criteria;
> the resume state lives in [progress.md](./progress.md).

## Objective

Close all **57 audit bullets** of `CODEBASE_AUDIT_2026-07.md` §§ 3–10 plus the **26 adjacent defects**, across
**301 acceptance criteria** — the operations that report success while doing nothing, the finished features no UI can
reach, the app being unusable on a phone, the English leaking through a French product, the unindexed queries, the
broken lint, and the two genuine product builds (audit trail, CNAM reconciliation).

## The steps live in the plan

This story is worked through in **eight ordered parts**. Do not duplicate the steps here — read them from the plan so
there is one source of truth:

| Part | Plan section | Bullets |
|---|---|:--:|
| **P1** Appointment lifecycle & booking | [plan.md § Part P1](../plan.md#part-p1--appointment-lifecycle--booking-integrity) | 8 |
| **P2** Finish what's built | [plan.md § Part P2](../plan.md#part-p2--finish-whats-built) | 13 |
| **P3** UX, accessibility & French | [plan.md § Part P3](../plan.md#part-p3--ux-accessibility--french) | 13 |
| **P4** Stock, realtime & schema | [plan.md § Part P4](../plan.md#part-p4--stock-realtime--schema) | 9 |
| **P5** Build & tooling | [plan.md § Part P5](../plan.md#part-p5--build--tooling) | 5 |
| **P6** Money truth & timezone | [plan.md § Part P6](../plan.md#part-p6--money-truth--timezone) | 7 |
| **P7** Audit trail, duplicate prevention, anonymize | [plan.md § Part P7](../plan.md#part-p7--audit-trail-duplicate-prevention--anonymize) | 1 |
| **P8** CNAM claims & reconciliation | [plan.md § Part P8](../plan.md#part-p8--cnam-claims-bordereau--reconciliation) | 1 |

**A part boundary is the commit point and the resume point** (plan risk **R-1**). Never stop mid-part in **P1**
(the `Type:` migration + the exclusion constraint), **P4** (the precision normalization + the batch backfill), or
**P7** (the audit-trail retrofit across ~38 handlers) — the schema or the handler set is briefly half-migrated inside
each.

## Entry criteria

- [x] `spec.md` APPROVED (301 ACs, 57/57 bullets traced)
- [x] `plan.md` APPROVED (8 parts, 18 risks, 15 migrations)
- [x] `design.md` APPROVED (2 novel screens mocked)
- [x] Branch `feature/audit-sections-3-to-10` exists, off `feature/windows-desktop-app` @ `1932acf`
- [x] Base contains the merged audit § 1 **and** § 2 work
- [ ] **P8 only:** Q-1 … Q-6 answered (see [plan.md § Part P8](../plan.md#part-p8--cnam-claims-bordereau--reconciliation))

## Ordering rules that cross part boundaries

1. **P2's US-P2a before US-P2b** — the un-mark lands before the delete-fiche button, because adding the button first
   is what makes § 6.11's orphaned links *reachable*.
2. **P7a before the rest of P7 and all of P8** — the audit trail is what the other parts record into.
3. **P5's `TreatWarningsAsErrors` step is the very last thing in the whole story** — it can only flip once the count
   is zero, and every other part adds code.
4. **P8 cannot start** until Q-1 … Q-6 are answered. Q-3 may retire P8 entirely.

## Quality gate (every part)

| Check | Command | Requirement |
|---|---|---|
| Backend build | `dotnet build api/ClinicManagement.sln --no-incremental` | 0 errors; **0 new** warnings in changed files |
| Frontend types | `cd web && npx tsc --noEmit` | 0 errors |
| Frontend build | `cd web && npm run build` | clean — expect 27/27 static pages |
| Tests | `dotnet build -p:OutDir=<scratch>/utbuild/` then `dotnet vstest` | pass |
| Schema | `verify-schema` console verb | exit 0 |
| Lint | `cd web && npm run lint` | **P5 onward only** — it cannot run before AC-P5.1 |

**Baselines:** 58 backend warnings, suite **941 passed / 3 failed** (the 3 are `ReminderSchedulerTests`, pure-Moq and
unrelated). Re-measure with `--no-incremental` before asserting — an incremental build reports "0 warnings" by
recompiling nothing.

**`dotnet test` is blocked on this machine** by Smart App Control (`0x800711C7`). Use
`dotnet build -p:OutDir=<scratch>/utbuild/` then `dotnet vstest`. **Never `--no-build` after changing production
code** — it runs stale DLLs and produced a false negative during § 2.

**Nothing in the test project touches a database.** The 15 migrations, the exclusion constraint, every index and the
`xmin` behaviour are operator-verified plus `verify-schema` only — see
[plan.md § Testing Strategy](../plan.md#testing-strategy).
