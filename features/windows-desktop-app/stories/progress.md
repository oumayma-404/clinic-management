# Phase 3 — Execution Progress (Connectivity Awareness & Offline UX)

**Plan:** [../plan.md](../plan.md) · **Spec:** [../spec.md](../spec.md) (FR-D + US-6)
**Phase:** 3 of 5 · Phases 1 & 2 COMPLETE (Phase 1 artifacts archived under `../phase-1/`)

## Story Status

| Story | Layer | Name | Status | Depends On |
|-------|-------|------|--------|------------|
| 1 | Full | Connectivity awareness & offline UX | implemented | - |

## Test Plan Coverage

| Test Type | Plan | Status |
|-----------|------|--------|
| E2E (`test-plan-e2e.md`) | not created | skipped (no APPROVED plan — matches Phases 1 & 2) |
| API (`test-plan-api.md`) | not created | skipped (Postman/Newman not run per user preference) |
| Integration (`test-plan-integration.md`) | not created | skipped (no APPROVED plan; backend covered by xUnit unit tests) |

## Log

- **Slice A (US-1) — connectivity signal.** Added `IInternetProbe` (Application), `InternetProbe`
  (Infrastructure, Singleton + `IMemoryCache` + `SemaphoreSlim` herd-guard), `ConnectivityConfig`
  (parallel static config helper — the plan's offered alternative to editing `LocalAuthConfig`),
  `GetConnectivityStatusQuery(+Handler)`, `ConnectivityStatusDto`, and `ConnectivityController`
  (`GET api/connectivity`, `[AllowAnonymous]`, 404 in Cloud). Registered probe + memory cache in
  `Extensions.cs`; documented `Connectivity` section in `appsettings.json`. Frontend
  `ConnectivityProvider` + `useConnectivity()` (Local-gated 15s poll, 3s debounce, toast on
  transition) and `<ConnectivityIndicator/>` in the header; provider wired into `layout.tsx`. [AC-6.1, AC-6.3, AC-6.4, FR-D1, FR-D3]
- **Slice B (US-2) — AI chat degradation.** Moved `<AIChat>` inside the provider tree; it consumes
  `useConnectivity()`, disables mic/textarea/send + shows a "connexion internet requise" banner when
  offline, short-circuits `handleSend`, and maps `ApiError.status === 0` to a clear "Connexion perdue"
  retry toast. Auto re-enables via the provider. [AC-6.2, AC-6.3, AC-6.5]
- **Slice C (US-3) — Google Calendar gating + sync badge + push.** Added additive
  `AppointmentDto.IsSyncedToGoogle` (derived from `GoogleCalendarEventId != null`), mapped in all four
  handlers (Create/Update/Get/GetAll — R-5). Mirrored the field in `types.ts`. Routed
  `syncAppointment` + `syncFromGoogle` through `client.ts` (so mid-request loss ⇒ `ApiError(0)` — R-7).
  Gated the two Google buttons on internet reachability. Added a "non synchronisé" badge + per-card
  "Push to Google" (via a shared `renderSyncControls` helper) in BOTH week- and day-view blocks;
  push success calls the new `onChanged` prop → `appointments/page.tsx` bumps `refreshKey` → badge
  clears. [AC-6.2, AC-6.5, AC-6.6, FR-D2, FR-D4]

## Quality Gate

- **Backend:** `dotnet build ClinicManagement.sln` — 0 errors, no new warnings (baseline 57 pre-existing; none from new files).
- **Backend unit tests (xUnit + Moq):** 14 new tests, all pass (SAC did not block this run).
  - `InternetProbeTests` — 2xx/3xx ⇒ reachable; request failure ⇒ not; result cached & probed once within TTL (R-1).
  - `GetConnectivityStatusQueryHandlerTests` — probe up ⇒ true; down ⇒ false; probe throws ⇒ false (no 500).
  - `AppointmentSyncMappingTests` — `IsSyncedToGoogle` null/non-null across Get, GetAll, Create, Update (R-5).
- **Frontend:** `npx tsc --noEmit` clean; `npm run build` succeeds.
- **Frontend lint fallback:** ESLint is **not installed** in `web/node_modules` and is disabled during
  `next build` (per `next.config.ts`). Per the skill's documented fallback, the frontend gate is
  `tsc --noEmit` + production build (both clean). Matches Phases 1 & 2.

## Manual Verification (deferred — no live network/Docker guaranteed in-session)

Documented for a later live pass (matches Phases 1 & 2):
- Local mode, internet ON → pulled: AI chat + Google controls disable with "requires internet";
  core features (patients/appointments/records/documents/files/dashboard) keep working; controls
  re-enable (debounced) when internet returns.
- Server stopped ⇒ header shows "Serveur injoignable".
- Create an appointment while offline ⇒ "non synchronisé" badge ⇒ "Push to Google" when online clears it.
- Cloud mode: no connectivity indicator; `/api/connectivity` 404s; AI + Google controls unchanged.

## Auto-Approved Deviations

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| Used a parallel `ConnectivityConfig` static helper instead of extending `LocalAuthConfig` | Trivial | Plan explicitly offered this as an alternative; keeps connectivity config separate from auth config. |
| Factored a shared `renderSyncControls(appointment)` helper inside `appointment-calendar.tsx` | Trivial | Internal helper; avoids divergence between the week- and day-view card blocks (plan required both). Same behavior in both. |
| Routed `syncFromGoogle` through `client.ts` too (not only `syncAppointment`) | Trivial | Plan said "syncAppointment (and the other sync calls it exposes)"; `ApiError` is a superset of the old `Error` so the one existing caller still works. |

## Notes for review (`/review-story`)

- **Sync badge/push hidden on `isVerySmall` cards** (appointment duration < ~24 min, card ≈ 14px tall):
  the "non synchronisé" badge + "Push" render only when `!isVerySmall`, consistent with the existing
  duration/procedure badges that also hide on tiny cards. Normal-length appointments show them. Flagged
  so review can decide if AC-6.6 needs coverage for very short appointments.
- **`<AIChat>` moved inside `SessionProvider`** (it was previously rendered outside, seeing the
  loading-default session). Verified non-regressive: `useAuthToken` no-ops while `isLoading`, and the
  chat call fetches its own token via `client.ts`; moving it in only makes session data more correct.
  Cloud yields the online default so AIChat behaves as before (R-3).
- **R-6 (Phase 4):** anonymous `/api/connectivity` is a deliberate Local-only exception (like
  `GET /api/auth/mode`) — flag it for Phase 4's "auth on all endpoints" release-gate review.
- **Deliberately unchanged:** `GoogleCalendarController.GetSyncStatus` network-down misclassification
  (shared/Cloud endpoint) — made inert by the connectivity signal disabling Google controls offline.

## Learnings

- The frontend has **no ESLint installed** and disables lint during `next build`; the effective FE
  quality gate is `tsc --noEmit` + `npm run build`. (Consistent across Phases 1–3.)
- Google-calendar client calls historically used raw `fetch` (plain `Error`); routing them through
  `client.ts` is the seam that unifies offline (`ApiError(status:0)`) handling with the AI path.
