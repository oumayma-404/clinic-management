# Story 3 Review Report

**Story:** 3 — Nothing in the demo misbehaves around the read-only rule
**Review Date:** 2026-11-03
**Reviewer:** Claude

## Implementation Quality Score: 100/100

### Spec Coherence: 50/50

| Criteria | Score | Notes |
|----------|-------|-------|
| User stories implemented | 15/15 | US-3 residue (AC-3.3c) + every FR-4 row this story owns |
| Acceptance criteria met | 15/15 | AC-3.3c, EC-3, EC-6 (by construction), FR-4 rows verified in `qa/run-s3-2.md` |
| Functional requirements | 10/10 | FR-4 table; « writes on its own fail silently » (bell, chat, push, folders skip the request) |
| Edge cases handled | 5/5 | mode not yet known (`isDemo !== false`), two visitors on one account, session-cap logout |
| No scope creep | 5/5 | install suppression left to story 7 per plan D9 |

### Code Quality: 50/50

| Criteria | Score | Notes |
|----------|-------|-------|
| Follows codebase patterns | 10/10 | one helper (`refuseInDemo`), one notice primitive (`DemoRefusalNotice`), capability read through `IDemoPolicy` |
| No dead code | 5/5 | — |
| No duplication (DRY) | 10/10 | 13 call sites share one refusal; the step-up dialog covers its 5 callers at once |
| Clean solutions (no hacks) | 10/10 | refused before any request or file read; no per-screen branches on the sentence |
| Unit tests | 8/8 | `GetMySessionsDemoTests` (3), `ClinicHubTests.On_The_Demo_…` + dependency review |
| Integration tests | 7/7 | none in repo; isolated-stack walk with a network log (B1) stands in |

## Test Traceability Matrix

| AC / FR | Description | Unit / guard | Browser | Status |
|---------|-------------|--------------|---------|--------|
| AC-3.3c | optimistic controls return to stored state | — | A9 (dashboard), source read (agenda, status, stock); odontogram refused before requests | ✓ |
| EC-3 | an arrival reloads nobody | `ClinicHubTests.On_The_Demo_A_Visitor_Arriving_Or_Leaving_Signals_Nobody` | — | ✓ |
| EC-6 | booking prompts not shown | (gate refuses before the handler — story 2 tests) | — | ✓ |
| FR-4 popup | never appears | — | B2 | ✓ |
| FR-4 Sécurité | own session, no « Déconnecter », actions refused | `GetMySessionsDemoTests` ×3 | A1, A2 | ✓ |
| FR-4 code-gated actions | refused before the prompt | — | A4 | ✓ |
| FR-4 mot de passe | form visible, save refused | — | A3 | ✓ |
| FR-4 Mode discret | visible, refused | — | A8 | ✓ |
| FR-4 exports | refused before a code prompt | — | A4 | ✓ |
| FR-4 uploads | refused at pick | `check:responsive` `demo-upload-guard`, `demo-guard-reads-live-flag` | A5, A6, A7 | ✓ |
| FR-4 Personnaliser | toggle refused, layout restored | — | A9 | ✓ |
| FR-4 bell / chat | read markers silent; send refused | — | B1 (no read/send requests); send ⏭ (no conversation until story 4) | ✓ / ⏭ |
| FR-4 push | never asked | — | B1 (no push-devices request) | ✓ |
| FR-4 Fichiers | folders not created | — | ⏭ (no patient until story 4) | ⏭ |

**Coverage:** every FR-4 row this story owns is covered; two rows wait for story 4's data (recorded in progress.md).

## Auto-Approved Deviations

| Deviation | Reason | User Feedback |
|-----------|--------|---------------|
| `/change-password` off the closed doors | FR-4 wants the form visible | not asked |
| `DemoRefusalNotice` + `refuseInDemo(isDemo, showInForm?)` | one notice in a form, one toast otherwise | not asked |
| Chat send refused in composer and hook | a failed bubble reads as an error | not asked |
| App install left to story 7 | plan D9 | not asked |

## Significant Deviations

None.

## Scope Creep Review

No scope creep detected.

## Findings Summary

| Category | Count |
|----------|-------|
| Critical | 0 |
| Major | 1 |
| Minor | 0 |
| Suggestions | 0 |
| **Total** | 1 |

## Fixed Issues

1. **Major — the letterhead picker let a file through on the demo.** Its guard sat in a `useCallback` whose
   dependencies were `[conflict]`, so it kept the first `isDemo` (null, mode not yet read) and `refuseInDemo(null)`
   lets the action through. Added `isDemo`, and `check:responsive` **`demo-guard-reads-live-flag`**: every memoized
   handler that refuses on the demo or tests `isDemo` must list it. Red-proved on a probe **and** on the original
   defect (removing the fix turns the check red at `letterhead-import-dialog.tsx:109`).

## Skipped Issues

None. (Chat send and Fichiers are re-walked in story 4, where a patient and colleagues exist.)

## Learnings & Observations

- A guard reading a flag that loads asynchronously is only as good as the dependency array around it; the QA walk
  could not see this one because it never opened that dialog.
- Backend warning baseline unchanged (72); the review touched no API file.
- `/change-password` is a standalone page, so the band is absent there (off-plan observation in `run-s3-2.md`).

## Quality Check Results

| Check | Result |
|-------|--------|
| Linting | N/A (no ESLint in `web/`) |
| Type checking | Pass (`tsc --noEmit`) |
| Unit tests | Pass — 6 113 passed, 0 failed, 6 skipped (unfiltered, before the review; the review changed web only) |
| Integration tests | N/A (none in repo) |
| E2E tests | Skipped at story review |
| Device check (mechanical) | Pass — `check:responsive` 85/85 |
| Device review (static, 12a) | Done — 0 findings (notice reuses `FormErrorBanner`; no new table, dialog width or fixed element) |
| Widths verified in browser | 320×640 · 390×844 · 1536×730 + story-1/2 walks' widths (QA run-s3-2) |
