# E2E Test Plan: In-App Staff Notification Center

**Status:** APPROVED
**Decision:** SKIPPED — no new E2E scenarios planned
**Created:** 2026-07-14

## Decision

No E2E test scenarios are planned for this feature.

**Rationale:**
- The repository has **no E2E test harness** — there is no Cucumber/Playwright setup, no `.feature` files, and no `components/e2e-tests/` (or equivalent) test project. The tooling this skill assumes does not exist here.
- Every prior feature in this repo (`real-time-updates`, `live-dashboard`, `stock-persistence`, `harden-existing-features`, etc.) was `Type: Small` and shipped without any test plan; there is no established E2E convention to extend.
- The user explicitly chose to skip E2E test planning for this feature (2026-07-14).

The notification center *does* have meaningful user-facing behavior (bell + unread badge, notification panel, mark-read / mark-all-read, deep-link navigation, live updates). Should an E2E harness be introduced later, the acceptance criteria in `spec.md` (US-1 through US-6) are the source of truth for the flows that would warrant coverage:
- Bell badge visibility + `99+` cap; panel open, newest-first, 50-row cap, empty/loading/error states (US-1).
- Appointment created/cancelled/rescheduled feed entries with actor exclusion (US-2, US-3).
- Reminder surfacing when due (US-4).
- Low-stock crossing notification (US-5).
- Per-user read/unread, mark-all-read, and deep-link navigation to appointment/stock (US-6).

## Coverage

Verification of this feature is deferred to the integration/API layers (see `test-plan-api.md` / `test-plan-integration.md`) and manual verification, consistent with this repo's practice.

## Out of Scope

- All E2E/browser automation (no harness present).
