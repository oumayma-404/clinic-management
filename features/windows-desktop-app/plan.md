# Implementation Plan: Windows Desktop / Offline-LAN — Phase 3 (Connectivity Awareness & Offline UX)

**Status:** APPROVED
**Challenged:** Yes
**Created:** 2026-07-08
**Spec:** [spec.md](./spec.md) (APPROVED · Challenged) — this plan covers **Phase 3 only** (FR-D + US-6). Phases 1 (pluggable auth) and 2 (local-disk storage) are **COMPLETE**; Phase 1 planning artifacts are archived under [phase-1/](./phase-1/). Phases 4–5 get their own plans via `/next`.

## Overview

Add **connectivity awareness** so the Local (offline-LAN) install degrades gracefully: the two internet-dependent features — **AI chat (HuggingFace)** and **Google Calendar** — are visibly disabled with a "requires internet" affordance when the internet is unreachable, and re-enable automatically (debounced) when it returns. Core features (patients, appointments, records, documents, files, dashboard) keep working fully offline. Appointments not yet pushed to Google (e.g. created while offline) show a **"not synced to Google"** badge with a manual **"Push to Google"** action.

**All behavior is additive and gated to Local mode** (`Auth:Mode = Local` server-side, `useSession().mode === 'local'` client-side). Cloud stays byte-for-byte unchanged — consistent with Phases 1 & 2.

### Central design decision — internet reachability is judged by the *server*

In the Local topology the **.NET server** (not the browser) makes the outbound AI + Google calls, and client PCs may only have LAN connectivity. So "internet reachable" must reflect the **server's** egress. A new **anonymous, Local-mode-only `GET /api/connectivity`** endpoint has the server probe a configurable URL (short timeout, briefly cached) and returns `{ internetReachable }`. The frontend polls it on an interval:

- **Server reachable** (FR-D1 (a)) = implicit — the poll got any HTTP response back (even an error status). A thrown `TypeError` / `ApiError(status: 0)` (the existing offline signal in `client.ts:41-54`) = server unreachable.
- **Internet reachable** (FR-D1 (b)) = the boolean in the response body.

These are **separately reflected** (FR-D1) and distinguishable from "feature not configured" (FR-D3): the UI combines the connectivity signal with each feature's existing config/status flag.

### What we deliberately do NOT change

- **`GoogleCalendarController.GetSyncStatus` network-down misclassification** (`catch { tokenValid = true }`, `GoogleCalendarController.cs:119-123`) is a **shared/Cloud endpoint**. Touching it risks a Cloud regression and would confuse the authorize-vs-sync button logic. It is made **inert** by the connectivity signal: when the internet is down, the Google controls are disabled by the signal regardless of what `/status` reports. Left unchanged (Cloud-unchanged principle).
- **No EF migration.** `Appointment.GoogleCalendarEventId` already exists (`Appointment.cs:22`); we only surface it through the DTO. No new schema.
- **No automatic backfill** of offline changes to Google — out of scope per spec; only the manual "Push to Google" action (AC-6.6).

### Mode-gating mechanism (reused from Phase 1)

- **Backend:** `LocalAuthConfig.IsLocalMode(IConfiguration)` (`Infrastructure/Auth/LocalAuthConfig.cs:29-30`). New config values follow the same helper idiom (static accessors, `const` defaults) — see Learnings.
- **Frontend:** `useSession().mode` (`web/lib/auth/session.tsx`). The `ConnectivityProvider` reads it and **arms polling only in Local mode**; in Cloud it supplies a static "everything online" default so all consumers behave exactly as today.

---

## Files to Modify / Create

### Backend — create
- `api/ClinicManagement.API/Controllers/ConnectivityController.cs` — thin controller, `GET api/connectivity`, `[AllowAnonymous]`, **404 in Cloud mode** (mirrors the Phase 1 Local-only endpoint pattern). Returns the MediatR query result.
- `api/ClinicManagement.Application/Features/Connectivity/Queries/GetConnectivityStatusQuery.cs` (+ `GetConnectivityStatusQueryHandler`) — orchestrates the probe; returns `ConnectivityStatusDto { internetReachable }`. Handler calls `IInternetProbe`.
- `api/ClinicManagement.Application/Common/Interfaces/IInternetProbe.cs` — outbound-probe abstraction (Application owns the interface; Infrastructure implements — Clean Architecture).
- `api/ClinicManagement.Infrastructure/Services/InternetProbe.cs` — `IHttpClientFactory` HEAD/GET to a configurable probe URL with an **explicit short timeout + CancellationToken**; any 2xx/3xx ⇒ reachable; caches the last result for a short TTL so N polling clients don't hammer the external URL. **Registered as a Singleton** and holds the cache in `IMemoryCache` (add `services.AddMemoryCache()` if not already present) so the cache is genuinely shared across all requests; concurrent polls collapse to one outbound probe per TTL (a `SemaphoreSlim`/lock guards against a thundering-herd on cache miss). `IHttpClientFactory` is safe to inject into a singleton. *(Do NOT register scoped — a per-request instance's cache would never be shared and R-1 would be defeated.)*
- `api/ClinicManagement.Application/DTOs/ConnectivityStatusDto.cs` — `{ bool InternetReachable }`.

### Backend — modify
- `api/ClinicManagement.Infrastructure/Auth/LocalAuthConfig.cs` **or** a small parallel static config helper — add `ProbeUrl` (default e.g. `https://www.google.com/generate_204`), `ProbeTimeoutSeconds` (default 3), `ProbeCacheSeconds` (default 5), read via `configuration["Connectivity:..."]` with baked-in `const` defaults.
- `api/ClinicManagement.Infrastructure/Extensions.cs` — register `IInternetProbe` **as a Singleton** (`services.AddSingleton<IInternetProbe, InternetProbe>()`) + `services.AddMemoryCache()` if absent (only meaningful in Local; safe to register unconditionally). Singleton is required for the shared probe cache (see R-1).
- `api/ClinicManagement.API/appsettings.json` — add an optional `Connectivity` section (documented; defaults apply if absent). **No secrets.**
- `api/ClinicManagement.Application/DTOs/AppointmentDto.cs` — add `bool IsSyncedToGoogle` (derived from `GoogleCalendarEventId != null`) **or** expose `string? GoogleCalendarEventId`. (US-3.)
- `.../Features/Appointments/{Commands/CreateAppointmentCommand.cs, Commands/UpdateAppointmentCommand.cs, Queries/GetAppointmentQuery.cs, Queries/GetAppointmentsQuery.cs}` — map the new DTO field in all four manual mappers. (US-3.)

### Frontend — create
- `web/lib/connectivity/connectivity.tsx` — `ConnectivityContext` + SSR-tolerant `useConnectivity()` (non-throwing default, mirrors `session.tsx:38-40`) + `ConnectivityProvider`. Reads `useSession().mode`; in Local: `setInterval` poll (pattern `appointment-calendar.tsx:84-90`) of `/api/connectivity`, debounced transitions via a `useRef` timer (pattern `session.tsx:123-140`), toast on debounced online/offline change. Exposes `{ serverReachable, internetReachable, isLocal }`. In Cloud: static `{ serverReachable: true, internetReachable: true, isLocal: false }`.
- `web/components/connectivity-indicator.tsx` — small header status affordance (shadcn `Badge`) showing online / "no internet" / "server unreachable" (AC-6.1, FR-D3), with a tooltip explaining the state.

### Frontend — modify
- `web/app/layout.tsx` — insert `<ConnectivityProvider>` **inside** `SessionProvider`, around `SidebarProvider`; **move `<AIChat>` inside** the provider tree so it can consume `useConnectivity()`.
- `web/components/ai-chat.tsx` — read `useConnectivity()`; when internet unreachable disable send/textarea/mic (lines 654/675/681) + a "requires internet" label/tooltip (AC-6.2); short-circuit `handleSend` (line 167); an in-flight request that fails with `ApiError.status === 0` shows a clear "lost connection — retry" message rather than the generic toast (AC-6.5).
- `web/components/dashboard-header.tsx` — mount `<ConnectivityIndicator />`.
- `web/app/appointments/page.tsx` — gate the two Google buttons (lines 112-133) on internet reachability (disabled + "requires internet" tooltip, AC-6.2).
- `web/lib/api/types.ts` — mirror the `AppointmentDto` field addition.
- `web/lib/api/google-calendar.ts` — route `syncAppointment` (and the other sync calls it exposes) through the shared `client.ts` request wrapper instead of raw `fetch`, so a mid-request connectivity loss surfaces as `ApiError(status === 0)` — unifying the calendar path's in-flight failure handling with the AI path (AC-6.5). Verify existing callers (`appointments/page.tsx`, calendar) still get the `{ message }` shape / handle the new `ApiError`. *(Behavior-preserving for the happy path; only the error type changes.)*
- `web/components/appointment-calendar.tsx` — render a "non synchronisé" badge on cards whose appointment is not synced + a per-card **"Push to Google"** action calling the **existing** `googleCalendarApi.syncAppointment(id)` (`web/lib/api/google-calendar.ts:59-74`); enabled only when internet reachable. **Badge/action must be added in BOTH card render blocks** — week view (~lines 491-521) and day view (~lines 548-587). **Badge-clear mechanism:** the calendar has no `refetch` (it destructures only `{ appointments, loading }` from `useAppointments`); refresh today is driven by the parent bumping `key={refreshKey}` to remount. So add an `onChanged?: () => void` prop; on push success call `onChanged()`, and `appointments/page.tsx` bumps `refreshKey` (its existing state) to remount → data refetches → badge clears (AC-6.6). No change to the shared `useAppointments` hook.

*(Note: `web/components/appointment-list.tsx` renders hardcoded sample data — not API-wired — so it is out of scope for the sync badge.)*

---

## Implementation Stories

Vertical slices, strict dependency order. US-2 and US-3 both depend on US-1; they are independent of each other.

### US-1: Live connectivity signal + status indicator
**Value:** A user in a Local install sees a live status affordance in the header that correctly distinguishes "online", "no internet" (core app fine), and "server unreachable" — and the app has a single seam every internet feature can consume.

- **Backend:** `IInternetProbe` + `InternetProbe` (configurable URL, short timeout, short-TTL cache); `GetConnectivityStatusQuery(+Handler)` + `ConnectivityStatusDto`; `ConnectivityController` (`GET api/connectivity`, `[AllowAnonymous]`, 404 in Cloud); config keys + DI registration.
- **Frontend:** `ConnectivityProvider` + `useConnectivity()` (Local-gated polling, debounced, toast on transition, AC-6.3); `<ConnectivityIndicator/>` in the header; wire the provider into `layout.tsx`.
- **AC covered:** FR-D1, FR-D3, AC-6.1, AC-6.3 (debounce), AC-6.4 (core app unaffected — provider adds no gating to core features).
- **Depends on:** — (first slice).

### US-2: AI chat degrades gracefully offline
**Value:** When the server has no internet, the AI chat widget is visibly disabled with a "requires internet" label instead of failing with a generic error; it re-enables automatically when internet returns; an in-flight request that loses connectivity fails clearly and is retryable.

- **Frontend:** move `<AIChat>` inside the provider tree; consume `useConnectivity()`; disable controls + label/tooltip when offline (AC-6.2); short-circuit `handleSend`; distinguish `ApiError.status === 0` for a clear retryable message (AC-6.5); auto re-enable via the provider (AC-6.3).
- **AC covered:** AC-6.2, AC-6.3, AC-6.5 (AI path).
- **Depends on:** US-1.

### US-3: Google Calendar offline gating + appointment sync indicator + manual push
**Value:** Staff can see which appointments are not yet in Google Calendar (e.g. created while offline) and push them one-by-one when internet is available; the Google connect/sync controls are disabled with a "requires internet" affordance when offline.

- **Backend:** add the sync field to `AppointmentDto`; map it in the Create/Update/Get/GetAll appointment handlers (no migration).
- **Frontend:** mirror the type in `types.ts`; gate the two Google buttons on internet reachability; render the "not synced" badge + per-card "Push to Google" action **in both the week- and day-view card blocks** (reusing the existing `syncAppointment` client + `POST /googlecalendar/sync-appointment/{id}`), enabled only when online; on success invoke a new `onChanged` prop so `appointments/page.tsx` bumps its existing `refreshKey` and the calendar remounts/refetches to clear the badge.
- **AC covered:** AC-6.2 (Google controls), AC-6.5 (calendar path — via routing `syncAppointment` through `client.ts` so it yields `ApiError(status:0)` like the AI path), AC-6.6, FR-D2, FR-D4.
- **Depends on:** US-1.

---

## Testing Strategy

Manual-testing-first, matching Phases 1 & 2 (no Docker/live-network guaranteed in-session; `test-plan-*.md` are all skipped for this phase — no APPROVED E2E/API/integration plans).

- **Backend unit tests (xUnit + Moq)** — the natural automated layer:
  - `GetConnectivityStatusQueryHandler`: internet-up ⇒ `internetReachable: true`; probe throws/timeout ⇒ `false`; cached result reused within TTL.
  - `InternetProbe`: 2xx/3xx ⇒ reachable; non-response/timeout ⇒ not reachable (mock `HttpMessageHandler`).
  - `ConnectivityController` / query gated to Local (404/short-circuit in Cloud).
  - Appointment DTO mapping: `GoogleCalendarEventId == null ⇒ IsSyncedToGoogle == false`, non-null ⇒ true, across all four handlers.
- **Frontend:** no unit-test runner in the repo (no vitest); `npx tsc --noEmit` + `npm run build` must be clean. E2E deferred (auto-skips).
- **Manual (deferred, documented in progress.md):** Local mode with internet ON then pulled — AI + Google controls disable with "requires internet", core features keep working, controls re-enable (debounced) when internet returns; server stopped ⇒ header shows "server unreachable"; create an appointment (offline) ⇒ "not synced" badge ⇒ "Push to Google" when online clears it. Cloud mode: no connectivity indicator, AI + Google unchanged.
- **Cloud parity check** (every slice): provider returns the online default in Cloud; `/api/connectivity` 404s in Cloud; no new gating on core features.

Quality gate (per project policy): `dotnet build ClinicManagement.sln` 0 errors / 0 new warnings; `tsc --noEmit` clean; `npm run build` succeeds; all unit tests pass.

---

## Risk Register

| ID | Risk | Likelihood | Impact | Story | Mitigation |
|----|------|------------|--------|-------|------------|
| R-1 | N polling clients hammer the external probe URL / add server egress load | Med | Med | US-1 | Cache the probe result server-side with a short TTL (default 5s); one outbound probe serves all clients within the window; short timeout so a poll never hangs. **Cache correctness depends on a Singleton `InternetProbe` + `IMemoryCache`** — a scoped instance's in-memory cache would never be shared and every poll would probe the URL. A `SemaphoreSlim` guards the cache-miss path so a burst of simultaneous polls triggers only one outbound probe. |
| R-2 | Probe URL blocked / captive portal ⇒ false "no internet" | Med | Med | US-1 | Probe URL configurable; default to a reliable 204 endpoint; treat any 2xx/3xx as up; document how to change it. |
| R-3 | Moving `<AIChat>` inside the provider tree regresses Cloud behavior | Low | High | US-2 | In Cloud the provider yields the online default ⇒ AIChat identical to today; verify Cloud `npm run build` + smoke that the widget still mounts/works. |
| R-4 | Connectivity flapping spams toasts / thrashes controls | Med | Low | US-1 | Debounce state transitions with a `useRef` timer (AC-6.3); only toast on the debounced change, not each poll. |
| R-5 | Appointment DTO field missed in one of four mappers ⇒ inconsistent badge | Med | Med | US-3 | Add + map in all four handlers in the same slice; unit-test each mapper's null/non-null case. |
| R-6 | Anonymous `/api/connectivity` conflicts with Phase 4's "auth on all endpoints" gate | Low | Low | US-1 | Endpoint returns only a non-sensitive boolean, Local-mode-only; deliberate exception like the existing anonymous `GET /api/auth/mode`. Note it for the Phase 4 release-gate review. |
| R-7 | In-flight AI/calendar request that loses connectivity hangs instead of failing cleanly | Low | Med | US-2/US-3 | Rely on the existing `client.ts` `TypeError → ApiError(0)` mapping; surface a clear retryable message on `status === 0` (AC-6.5). **The Google-calendar client currently uses raw `fetch` (plain `Error`, not `ApiError`)** — US-3 routes `syncAppointment` through `client.ts` so the calendar path also produces `ApiError(status:0)` and shares the retryable-message handling. |

## Breaking Changes
None. All changes are additive and Local-mode-gated; Cloud behavior is unchanged. The `AppointmentDto` gains one optional field (additive; existing consumers ignore it).

## Migrations
None. `Appointment.GoogleCalendarEventId` already exists; the sync indicator derives from it. No schema change.
