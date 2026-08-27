# Retrospective: Windows Desktop App — Phase 3 (Connectivity Awareness & Offline UX)

**Feature:** windows-desktop-app (Phase 3 of 5 — FR-D + US-6)
**Date:** 2026-07-08
**Duration:** Single-session phase (1 full-stack story)
**Score:** Story review 100/100 · Feature review 8 findings (0 Critical, 2 Major, 4 Minor, 2 Suggestion), all confirmed on challenge

## Summary

Added server-judged **connectivity awareness** so the Local (offline-LAN) install degrades gracefully: the two internet-dependent features (AI chat + Google Calendar) visibly disable with a "requires internet" affordance when the internet is unreachable and auto re-enable (debounced) when it returns; core features keep working fully offline. Appointments not yet pushed to Google show a "non synchronisé" badge with a manual "Push to Google" action. All behavior is additive and gated to Local mode — Cloud stays byte-for-byte unchanged (consistent with Phases 1 & 2).

## What Went Well

- **Clean architectural seam.** `IInternetProbe` (Application) + `InternetProbe` (Infrastructure, Singleton + `IMemoryCache` + `SemaphoreSlim` herd-guard) + a mode-gated `GET /api/connectivity` endpoint that 404s in Cloud — layering held, Cloud verified unchanged.
- **Server-vs-internet distinction is real** (poll response = server up; body bit = internet up), and the frontend provider mirrors the existing `session.tsx` SSR-tolerant non-throwing default.
- **Unified offline handling** by routing the historically raw-`fetch` Google-calendar calls through `client.ts`, so AI and calendar paths share the `ApiError(status: 0)` retryable treatment.
- **Story review scored 100/100**; the 1 Minor found during review (badge/push shown on cancelled/completed appointments) was fixed in-review with the quality gate re-verified clean.
- **Deviations stayed trivial** — 3 auto-approved, all internal/no-behavior-impact; 0 significant.

## What Could Be Improved

- **Debounce logic didn't do what it claimed** (Major, Finding 1): a 3s window under a 15s poll suppresses zero flapping. Design timing against the actual event cadence.
- **A required affordance was hidden by a size heuristic** (Major, Finding 2): the sync badge/push disappeared on short appointment cards, so AC-6.6 failed for a whole class of items — the team flagged it pre-review but it shipped into the review diff.
- **Defensive error handling was too blanket** (Finding 3): `catch (Exception) → cache false` can mask config bugs and (latently) poison the shared singleton cache.
- **Unifying calls onto `client.ts` regressed an error message** (Finding 4): the wrapper doesn't parse the `{ error }` shape the Google controller returns.
- **FE has no automated test/lint runner** (Phases 1–3): gate remains `tsc --noEmit` + `npm run build`; connectivity/gating behavior is covered only by deferred manual verification.

## Learnings

10 learnings captured/added in `features/LEARNINGS.md`:

1. **Pattern:** Judge internet reachability at the server, not the browser, in offline-LAN topologies.
2. **Pattern:** Route every HTTP call through one client wrapper to unify offline/error handling.
3. **Pitfall:** A debounce window shorter than the poll interval suppresses nothing.
4. **Pitfall:** Space-based UI gating can hide a required affordance entirely.
5. **Pitfall:** Blanket `catch → return/cache false` masks real faults and can poison a shared cache.
6. **Convention:** The central error parser must cover every server error shape (or standardize on ProblemDetails).
7. **Convention:** Handle in-flight connectivity loss uniformly across every network call site.
8. **Convention:** Distinguish "not configured" from "network unavailable" for each gated feature.
9. **Tools:** `web/` has no unit-test runner and no ESLint; the FE gate is `tsc --noEmit` + `npm run build`.
10. **Carry-forward (Phase 4):** anonymous `GET /api/connectivity` is a deliberate Local-only exception (like `GET /api/auth/mode`) — flag it for the "auth on all endpoints" release-gate review.
