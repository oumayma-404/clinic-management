# Story 1 Review Report (Phase 3)

**Story:** 1 — [Full] Connectivity awareness & offline UX
**Phase:** 3 of 5 (Connectivity Awareness & Offline UX — FR-D + US-6)
**Review Date:** 2026-07-08
**Reviewer:** Claude

## Implementation Quality Score: 100/100

### Spec Coherence: 50/50

| Criteria | Score | Notes |
|----------|-------|-------|
| User stories implemented | 15/15 | Plan US-1 (signal + indicator), US-2 (AI chat), US-3 (Google gating + badge + push) all delivered in one full-stack slice per the user's breakdown choice. |
| Acceptance criteria met | 15/15 | AC-6.1..AC-6.6 + FR-D1..FR-D4 all addressed (see matrix). Server-vs-internet distinction is real (poll response = server up; body bit = internet up). |
| Functional requirements | 10/10 | FR-D1 (dual signal), FR-D2 (features key off internet), FR-D3 (config-vs-network distinguishable), FR-D4 (`IsSyncedToGoogle` trackable field). |
| Edge cases handled | 5/5 | Flapping (3s debounce + no-op skip), in-flight loss (`ApiError(0)` retry message on both AI and calendar paths), captive-portal false-positive (configurable probe URL, documented), herd of pollers (Singleton + `IMemoryCache` + `SemaphoreSlim`, TTL-collapsed). |
| No scope creep | 5/5 | No functionality outside FR-D/US-6. `GoogleCalendarController.GetSyncStatus` misclassification deliberately left unchanged (Cloud-shared) and made inert by the signal. |

### Code Quality: 50/50

| Criteria | Score | Notes |
|----------|-------|-------|
| Follows codebase patterns | 10/10 | Clean Architecture respected (interface in Application, impl in Infrastructure); `ConnectivityConfig` mirrors `LocalAuthConfig` static-accessor idiom; provider mirrors `session.tsx` non-throwing SSR default; controller mirrors the Phase-1 Local-only-404 pattern; MediatR `Result<T>` used throughout. |
| No dead code | 5/5 | No unused symbols; `serverReachable` is consumed by the indicator. |
| No duplication (DRY) | 10/10 | `renderSyncControls` helper shared across week+day blocks (avoids the divergence risk the plan flagged); both sync client calls routed through the single `client.ts` seam. |
| Clean solutions (no hacks) | 10/10 | Probe is timeout-bounded with a linked CTS; double-checked lock on cache miss; query handler swallows probe errors into a `false` (never a 500 for a poll). |
| Unit tests | 8/8 | 14 xUnit tests: probe classification (2xx/3xx/failure), cache-collapses-to-one-probe (R-1), handler up/down/throws, DTO mapping null/non-null across all four handlers (R-5). Meaningful assertions, isolated (mocked `HttpMessageHandler`, no network). |
| Integration tests | 7/7 | N/A for this phase (no APPROVED integration plan — matches Phases 1 & 2). Backend logic is covered by the unit layer; not counted as a gap. |

**Self-check:** the one finding below was fixed during review → final committed state is clean → 100/100.

## Test Traceability Matrix

Spec uses numbered ACs (AC-6.x) + FR IDs. Frontend has **no unit-test runner** (no vitest); the FE gate is `tsc --noEmit` + `npm run build`, so FE-behavior ACs are covered by implementation + **deferred manual verification** (documented in progress.md), consistent with Phases 1 & 2 — not counted as coverage gaps.

| AC / FR | Description | Backend unit | FE gate | Manual (deferred) | Status |
|---------|-------------|--------------|---------|-------------------|--------|
| FR-D1 | Signal reflects server AND internet separately | `InternetProbeTests`, `GetConnectivityStatusQueryHandlerTests` | provider poll logic | — | ✓ |
| FR-D2 | AI + Google key off internet reachability | — | ai-chat / appointments gating | ✓ | ✓ |
| FR-D3 | "not configured" vs "network unavailable" distinguishable | — | `ConnectivityIndicator` 3-state | ✓ | ✓ |
| FR-D4 | Unsynced appointments trackable | `AppointmentSyncMappingTests` (all 4 handlers) | `isSyncedToGoogle` in `types.ts` | — | ✓ |
| AC-6.1 | Distinguish server-unreachable vs internet-unreachable | — | 3-state indicator + provider | ✓ | ✓ |
| AC-6.2 | AI + Google visibly disabled with "requires internet" | — | disabled controls + labels/tooltips | ✓ | ✓ |
| AC-6.3 | Auto re-enable + debounce on flapping | — | 3s debounce, toast on debounced transition | ✓ | ✓ |
| AC-6.4 | Core features work fully offline | — | provider adds no gating to core features | ✓ | ✓ |
| AC-6.5 | In-flight loss fails clearly + retryable | — | `ApiError(0)` → retry toast (AI + calendar) | ✓ | ✓ |
| AC-6.6 | Not-synced badge + manual push; no auto-backfill | `AppointmentSyncMappingTests` | badge + "Push to Google" in both views | ✓ | ✓ |

**Coverage:** 10/10 criteria addressed (backend-testable ACs have automated tests; FE-behavior ACs covered by implementation + deferred manual, per phase policy). 0 unaddressed.

## Auto-Approved Deviations

| Deviation | Reason | User Feedback |
|-----------|--------|---------------|
| Parallel `ConnectivityConfig` static helper instead of extending `LocalAuthConfig` | Plan explicitly offered this alternative; keeps connectivity config separate from auth config | Accepted (correct classification) |
| Shared `renderSyncControls(appointment)` helper inside `appointment-calendar.tsx` | Internal helper; avoids week/day divergence the plan required in both blocks | Accepted (correct classification) |
| Routed `syncFromGoogle` through `client.ts` too (not only `syncAppointment`) | Plan said "syncAppointment (and the other sync calls it exposes)"; `ApiError` is a superset of the old `Error` so the one existing caller still works | Accepted (correct classification) |

**Total:** 3 auto-approved deviations (3 accepted, 0 flagged as miscategorized). All trivial (internal / no behavior or API impact); classification is working as intended.

## Significant Deviations

| ID | Title | Justified | Approved | Score Impact |
|----|-------|-----------|----------|--------------|
| — | None | — | — | 0 |

## Scope Creep Review

No scope creep detected. Every change traces to FR-D / US-6.

## Findings Summary

| Category | Count |
|----------|-------|
| Critical | 0 |
| Major | 0 |
| Minor | 1 |
| Suggestions | 0 |
| **Total** | 1 |

## Fixed Issues

1. **[Minor] Sync badge + "Push to Google" shown on cancelled/completed appointments** — When an appointment is cancelled/completed, `GoogleCalendarSyncService` deletes its Google event and clears `GoogleCalendarEventId`, so `isSyncedToGoogle` becomes `false`. With the Cancelled/Completed filter toggled on, such cards then rendered the "non synchronisé" badge and an enabled "Push" button; pushing routed to the sync service (a no-op delete) yet toasted "synchronisé", which is misleading and outside AC-6.6's intent (the badge is for appointments *not yet* in Google, e.g. created offline). **Fix:** `renderSyncControls` now returns `null` for `cancelled`/`completed` statuses (`web/components/appointment-calendar.tsx`). Re-verified: `tsc --noEmit` clean, `npm run build` succeeds.

## Skipped Issues

None.

## Learnings & Observations

- **Server-judged reachability is the right call.** In the Local topology the .NET server (not the browser) makes the outbound AI/Google calls, so probing the server's egress and letting clients poll it is the only signal that reflects what the features actually depend on. Clean seam for Phase 4.
- **`client.ts` unifies offline handling.** Routing the historically-raw-`fetch` Google-calendar calls through the wrapper is what lets both the AI and calendar paths share the `ApiError(status:0)` retryable-message treatment (AC-6.5). Cloud-safe because `getAccessToken()` swallows token-fetch errors and the endpoints are anonymous.
- **FE test-runner gap persists (Phases 1–3).** No vitest in `web/`; the effective FE gate remains `tsc --noEmit` + `npm run build`. If a later phase wants automated FE coverage of the connectivity/gating behavior, standing up a runner is a prerequisite.
- **R-6 carry-forward:** anonymous `GET /api/connectivity` is a deliberate Local-only exception (like `GET /api/auth/mode`). Flag it for Phase 4's "auth on all endpoints" release-gate review.
- Spec ACs are numbered (AC-6.x); the matrix uses spec IDs directly (no story renumbering occurred).

## Quality Check Results

| Check | Result |
|-------|--------|
| Backend build (`dotnet build ClinicManagement.sln`) | Pass — 0 errors, 57 warnings (all pre-existing baseline; none from new files) |
| Type checking (`npx tsc --noEmit`) | Pass — clean |
| Frontend build (`npm run build`) | Pass |
| Unit tests (xUnit, story filter) | Pass — 14/14 |
| Integration tests | N/A (no APPROVED plan this phase) |
| E2E tests | Deferred (handled by `/story-e2e`; auto-skips — no APPROVED E2E plan) |
