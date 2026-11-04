# Story 2 Review Report

**Story:** 2 — A save is refused kindly, and the visitor always knows it is a demo
**Review Date:** 2026-11-03
**Reviewer:** Claude

## Implementation Quality Score: 100/100

### Spec Coherence: 50/50

| Criteria | Score | Notes |
|----------|-------|-------|
| User stories implemented | 15/15 | US-3 (refusal) and US-4 (band) complete |
| Acceptance criteria met | 15/15 | AC-3.1–3.5 (except 3.3c, story 3), AC-4.1–4.4 verified in browser (`qa/run-s2-1.md`) |
| Functional requirements | 10/10 | FR-2 per-action classification, refusal before idempotency; FR-5 band |
| Edge cases handled | 5/5 | repeated refusal (one message), dialog + toast never both, unroutable path, non-API path, other deployments |
| No scope creep | 5/5 | the toast sweep (DEV-4) is needed for AC-3.3 |

### Code Quality: 50/50

| Criteria | Score | Notes |
|----------|-------|-------|
| Follows codebase patterns | 10/10 | gate mirrors `SubscriptionGateMiddleware`; attributes mirror `AllowsWithoutSubscription`; message swap mirrors `must_change_password` |
| No dead code | 5/5 | unused imports left by the sweep removed |
| No duplication (DRY) | 10/10 | one sentence (`demo-refusal.ts`), recognised in two primitives, held by `demo-refusal-one-owner` |
| Clean solutions (no hacks) | 10/10 | no per-screen branches; a derived guard for each rule |
| Unit tests | 8/8 | `DemoReadOnlyMiddlewareTests` (verbs, attributes, other kinds, paths, order), `DemoExemptionCoverageTests` (both sets, both directions, source scan, red-proofs) |
| Integration tests | 7/7 | none in repo; replaced by the isolated-stack walk + SQL (0 idempotency rows) |

## Test Traceability Matrix

| AC ID | Description | Unit Test | Browser / probe | Status |
|-------|-------------|-----------|-----------------|--------|
| AC-3.1 | one sentence + « Créer mon cabinet » | `DemoReadOnlyMiddlewareTests.A_Write_Is_Refused_With_The_Demo_Answer` | QA A3, A5, B1 | ✓ |
| AC-3.2 | dialog stays open, input kept | — | QA A3 | ✓ |
| AC-3.3 | never looks like an error | `check:responsive` `server-error-toast-has-one-owner` | QA A3 (no `role=alert`), A5 (not `error`) | ✓ |
| AC-3.3b | in-dialog notice / toast outside | — | QA A3, A5 | ✓ |
| AC-3.3c | optimistic controls roll back | — | story 3 | — (story 3) |
| AC-3.3d | one message at a time | — | QA A4, A6 | ✓ |
| AC-3.4 | opening forms allowed | `An_Ordinary_Read_Passes` | QA B3 | ✓ |
| AC-3.5 | nothing changes another's view | `A_Write_Is_Refused…` (every verb) | QA D3 (0 rows) | ✓ |
| AC-4.1 | band text + link | — | QA A1 | ✓ |
| AC-4.2 | short at phone, ≤ 2 lines, covers nothing | — | QA A2, C1 | ✓ |
| AC-4.3 | not closable | — | no control rendered (source) | ✓ |
| AC-4.4 | not printed | — | QA C2 | ✓ |
| FR-2 | per-action classification, before idempotency | `DemoExemptionCoverageTests` (5 facts), `The_Gate_Runs_After_Token_State_And_Before_Idempotency` | QA B2 | ✓ |

**Coverage:** 12/12 story criteria covered (AC-3.3c belongs to story 3).

## Auto-Approved Deviations

| Deviation | Reason | User Feedback |
|-----------|--------|---------------|
| Refusal recognised by message identity, `useConflict` untouched | `must_change_password` precedent | not asked (owner prefers decisions) |
| PDF + aperçu allowed; 9 more exports + file manifest refused | spec FR-2 wording beats the story's short list | not asked |
| Band is an `aside` landmark, no live role | shell remounts per navigation | not asked |

## Significant Deviations

| ID | Title | Justified | Approved | Score Impact |
|----|-------|-----------|----------|--------------|
| DEV-4 | 42 hand-rolled error toasts → `showErrorToast` + guard | Yes (AC-3.3) | auto | 0 |

## Scope Creep Review

No scope creep detected.

## Findings Summary

| Category | Count |
|----------|-------|
| Critical | 0 |
| Major | 0 |
| Minor | 1 |
| Suggestions | 0 |
| **Total** | 1 |

## Fixed Issues

1. **Minor — the hand-out source scan failed open.** It matched a controller's source by file name and a signature on
   one line only, so a controller in another file or a wrapped `public async Task<…>\n Export(` signature was silently
   skipped — exactly the new case the guard exists for. Now: every file declaring the class is read, wrapped signatures
   match, and an action whose source is not found fails the test. Red-proof added for the wrapped case.

## Skipped Issues

None.

## Learnings & Observations

- Backend warning baseline: 72, none in this story's files.
- A source-scan guard needs « could not find it » to be a failure, not a skip — same lesson as the non-zero count.
- Spec ACs are numbered; matrix uses spec IDs.

## Quality Check Results

| Check | Result |
|-------|--------|
| Linting | N/A (no ESLint in `web/`) |
| Type checking | Pass (`tsc --noEmit`) |
| Unit tests | Pass — 6 109 passed, 0 failed, 6 skipped (unfiltered) |
| Integration tests | N/A (none in repo) |
| E2E tests | Skipped at story review |
| Device check (mechanical) | Pass — `check:responsive` 83/83 |
| Device review (static, 12a) | Done — 0 findings (band: `shrink-0`, `print:hidden`, 44 px coarse CTA, no hover-only affordance; notice link 44 px coarse) |
| Widths verified in browser | 320×640 · 390×844 · 820×1180 · 1180×820 · 1440×900 · 1536×730 · coarse 390 (QA run-s2-1) |
