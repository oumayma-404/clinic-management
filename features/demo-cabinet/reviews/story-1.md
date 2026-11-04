# Story 1 Review Report

**Story:** 1 — A visitor enters the demo with one click
**Review Date:** 2026-11-03
**Reviewer:** Claude

## Implementation Quality Score: 100/100

### Spec Coherence: 50/50

| Criteria | Score | Notes |
|----------|-------|-------|
| User stories implemented | 15/15 | US-1 complete; US-7's AC-7.2 (separate key) in place |
| Acceptance criteria met | 15/15 | AC-1.1–1.6 verified in browser (run-2) |
| Functional requirements | 10/10 | FR-1 capabilities, FR-3 session rules |
| Edge cases handled | 5/5 | EC-1 expiry (after F1), EC-9 preview metadata |
| No scope creep | 5/5 | `demo_visit` cookie and `Demo:SessionMinutes` serve FR-3 directly (DEV-2, auto-approved) |

### Code Quality: 50/50

| Criteria | Score | Notes |
|----------|-------|-------|
| Follows codebase patterns | 10/10 | capability matrix, `IDemoPolicy` seam (`ISubscriptionPolicy` shape), `TradeRelayAssertionCommand` mint, BFF via `writeSessionCookies` |
| No dead code | 5/5 | no-op `useMemo` removed in review |
| No duplication (DRY) | 10/10 | one locator, one refusal module, one cookie constant shared server/client |
| Clean solutions (no hacks) | 10/10 | session cap from `SessionFamily.CreatedAt`, no migration |
| Unit tests | 8/8 | profile matrix, mint, cap (refresh + issuance), policy parsing, controller door, coverage guards |
| Integration tests | 7/7 | N/A in repo (no DB tests); replaced by the isolated-stack QA walk + SQL probes |

## Test Traceability Matrix

| AC ID | Description | Unit Test | Browser / probe | Status |
|-------|-------------|-----------|-----------------|--------|
| AC-1.1 | entry screen | — | QA A1 | ✓ |
| AC-1.2 | lands on dashboard, no password/code | `StartDemoSessionCommandTests.The_Session_Ends_At_A_Fixed_Time_And_Writes_Only_Itself` | QA A2 | ✓ |
| AC-1.3 | « Créer mon cabinet » link | `SelfRegistrationGateTests.GetMode_reports_whether_this_is_the_public_demo` | QA A1 | ✓ |
| AC-1.4 | two visitors, two sessions | `Each_Visitor_Gets_A_Family_Of_Their_Own` | QA A3 + SQL | ✓ |
| AC-1.5 | auth doors → entry | — | QA B2 | ✓ |
| AC-1.6 | signed-in visitor → dashboard | — | QA B1 | ✓ |
| AC-7.2 | demo session refused by production | — | manual: demo signing key ≠ main key | ✓ |
| FR-1 | every capability answered | `DeploymentProfileTests` (4-tuple matrix) | — | ✓ |
| FR-3 | 2 h cap, untrusted, no User write | `RefreshTokenCommandHandlerTests` (3 demo cases), `DemoSessionLifetimeTests` | QA B4 | ✓ |
| EC-1 | session ends mid-use → « La démo a expiré » | — | QA B4 | ✓ |
| EC-9 | link preview title | — | QA C4 | ✓ |

**Coverage:** 11/11 story criteria covered (100 %).

## Auto-Approved Deviations

| Deviation | Reason | User Feedback |
|-----------|--------|---------------|
| Team + letterhead move to story 4 | story 1 needs only the dentist | not asked (owner prefers decisions) |
| `Demo:SessionMinutes` | QA can expire a session in one run | not asked |

## Significant Deviations

| ID | Title | Justified | Approved | Score Impact |
|----|-------|-----------|----------|--------------|
| DEV-1 | `demoBuiltOn` moves to story 6 | Yes | auto (within plan) | 0 |
| DEV-2 | `demo_visit` cookie | Yes (QA F1, spec FR-3) | auto | 0 |
| DEV-3 | date-bomb fix in `FreeSlotFinderTests` | Yes (pre-existing red) | auto | 0 |

## Scope Creep Review

No scope creep detected.

## Findings Summary

| Category | Count |
|----------|-------|
| Critical | 0 |
| Major | 1 |
| Minor | 2 |
| Suggestions | 0 |
| **Total** | 3 |

## Fixed Issues

1. **Major — access token outlived the session cap.** A token minted near the end of a 2 h session stayed valid up to
   30 more minutes. `ILocalAuthService.GenerateToken(user, family, notAfterUtc)` added; the mint and the refresh both
   cap it at the session end. Tests assert both.
2. **Minor — no-op `useMemo` in `DemoProvider`.** Removed.
3. **Minor — `DemoPolicy` parsing and the token caps had no direct test.** `DemoSessionLifetimeTests` added
   (5 parse cases, cap/no-cap, kind isolation).

## Skipped Issues

None.

## Learnings & Observations

- Backend warning baseline unchanged; 0 warnings in this story's files (`LocalDataProtection.cs:189` CA1416 is pre-existing).
- Every credential minted for a capped session needs the cap — refresh **and** access — or the shorter one is not the limit.
- Spec ACs are numbered; matrix uses spec IDs.

## Quality Check Results

| Check | Result |
|-------|--------|
| Linting | N/A (no ESLint in `web/`) |
| Type checking | Pass (`tsc --noEmit`) |
| Unit tests | Pass — 6 087 passed, 0 failed, 6 skipped (unfiltered) |
| Integration tests | N/A (none in repo) |
| E2E tests | Skipped at story review |
| Device check (mechanical) | Pass — `check:responsive` 81/81 |
| Device review (static, 12a) | Done — 0 findings (entry page: no table, no dialog, `min-h-dvh`, 44 px coarse, no hover-only affordance) |
| Widths verified in browser | 320×640 · 390×844 · 820×1180 · 1180×820 · 1440×900 · 1536×730 (QA run-2, before the review fixes — which change no rendering) |
