# Retrospective: In-App Staff Notification Center

**Feature:** notification-center
**Date:** 2026-07-14
**Duration:** 1 day (2 sessions: implementation + story review)
**Score:** Story review 100/100; feature review 8 confirmed findings (1 Major, 6 Minor, 1 Suggestion)

## Summary

Turned the inert header bell + removed hardcoded notifications list into a real, clinic-scoped in-app notification feed: a bell with an unread badge opening a newest-first panel, per-user read/unread state, mark-all-read, live SignalR updates, and deep-links to the related record. Notifications are generated best-effort (post-commit) by clinic events — appointment created/cancelled/rescheduled, a due ~24h reminder, and not-low→low stock crossings. In-app only; the disabled email/SMS pipeline stays untouched.

## What Went Well

- **Clean vertical slice** — domain + persistence (migration, EF configs, global filter) → read side (DTO, queries, controller) → generation seam → frontend (types, api module, realtime key, hook, panel, header, deep-links) all delivered in one story, scoring 100/100 at story review.
- **Convention fit** — CQRS + `Result<T>`, aggregate with private setters + intention-revealing `MoveReminder`, repo-stages/UoW-commits, and the `*Api`+hook+component frontend layering all matched existing patterns; zero new backend warnings against a 57-warning baseline.
- **Strong backend test coverage** — 22 notification unit tests (generation rules, query/read semantics, tenant isolation, idempotency) plus a resolver contract test pinning the `"notifications"` realtime key; all passing (full suite 229/229 at commit).
- **Best-effort generation decoupled and post-commit** — the single hard constraint (never fail/roll back the core op; never reactivate email/SMS) was honored, verified by `Generator_Swallows_Persistence_Failure`.

## What Could Be Improved

- **The DEV-1 deep-link endpoint (`GET /api/appointments/{id}`) shipped leaning on the fail-open EF global query filter for tenant isolation** — the feature review's one Major finding. A by-id read must DB-resolve the clinic and scope explicitly, as its sibling paths already do. Story review accepted "tenant-safe via the global filter" at face value; it holds only when the `clinic_id` claim is present.
- **State-transition gaps** — appointment reactivation (Cancelled→Scheduled, no date change) fell through both notification branches, leaving a reactivated far-out appointment with no re-created reminder (Finding 3).
- **Realtime over-broadcasting** — the notifications key is broadcast after every generator write, including no-op/future-dated ones, causing needless clinic-wide refetches (Finding 4).
- **Frontend deep-link polish** — the stock highlight is never cleared (stuck-selection look) and the scroll effect re-fires on every list reload (Findings 6 & 7).
- **No integration/E2E/FE-test harness in the repo** — repository LINQ predicates (due-gating, 50-row cap, actor-exclusion, late-joiner) and all UI ACs remain the documented untested surface, verified manually. (Consistent with the standing `windows-desktop-app` learning.)

## Learnings

8 learnings captured in `features/LEARNINGS.md`:

1. **Pattern:** Best-effort side-effects run after the core commit and must never fail it (broad-catch + log at Error).
2. **Pattern:** Same-page deep-link via a `window` `CustomEvent` (`clinic:deeplink`), not `useSearchParams` — preserves static prerendering.
3. **Pitfall:** A by-id read that leans only on the fail-open EF global query filter can leak cross-tenant data (the Major finding) — DB-resolve the clinic and scope explicitly.
4. **Pitfall:** A state transition matching neither obvious branch silently skips a required side-effect (reactivation → no reminder).
5. **Pitfall:** Broadcasting a realtime refresh key on every write causes needless clinic-wide refetches — signal visible-change first.
6. **Pitfall:** A never-cleared deep-link highlight + a scroll effect keyed on a fresh-reference list yank and stick the viewport.
7. **Convention:** Map a tenant-scoped not-found `Result.Failure` to 404 (not 400) and keep sibling endpoints consistent.
8. **Convention:** Bulk operations that consume only ids should fetch an id-only projection.
