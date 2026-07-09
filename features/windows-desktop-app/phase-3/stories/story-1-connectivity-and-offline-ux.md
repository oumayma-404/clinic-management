# Story 1: [Full] Connectivity awareness & offline UX

**Status:** APPROVED
**Story Status:** implemented
**Layer:** Full
**Depends On:** None
**Blocks:** None

> Full-stack story covering the plan's US-1 (connectivity signal + indicator), US-2 (AI chat degradation), and US-3 (Google Calendar gating + sync badge + push). Delivered as one story per user's explicit choice. Steps are grouped by slice and ordered so backend + provider land before the features that consume them.

## Objective

In a Local (offline-LAN) install, the app becomes **connectivity-aware**: a header indicator shows whether the server and the internet are reachable, and the two internet-dependent features — **AI chat** and **Google Calendar** — visibly disable with a "requires internet" affordance when the internet is down and re-enable automatically (debounced) when it returns. All non-internet features keep working fully offline. Appointments not yet reflected in Google Calendar show a **"non synchronisé"** badge with a per-appointment **"Push to Google"** action available when online. Reachability is judged by the **.NET server** (which makes the outbound calls) via a new anonymous, Local-mode-only `GET /api/connectivity` endpoint the frontend polls. Everything is **additive and gated to Local mode**; Cloud behavior is unchanged.

## Acceptance Criteria

_From spec:_

- [ ] **AC-6.1** — App distinguishes (a) clinic server unreachable from (b) internet unreachable (core app fine).
- [ ] **AC-6.2** — With no internet, AI chat and Google Calendar controls are visibly disabled/greyed with a "requires internet" label — not clickable-then-error.
- [ ] **AC-6.3** — When internet returns, features re-enable automatically, with debouncing so a flapping connection doesn't thrash the UI.
- [ ] **AC-6.4** — All non-internet features (patients, appointments, records, documents, files, stock, dashboard) work fully offline.
- [ ] **AC-6.5** — An in-flight AI or calendar request that loses connectivity fails with a clear, retryable message rather than hanging.
- [ ] **AC-6.6** — Appointments not yet in Google Calendar show a "not synced to Google" indicator plus a manual "Push to Google" action; automatic backfill stays out of scope.
- [ ] **FR-D1** — Connectivity signal separately reflects server reachability and internet reachability.
- [ ] **FR-D2** — AI chat and Google Calendar key off internet reachability for enabled/disabled state.
- [ ] **FR-D3** — "Feature not configured" and "network unavailable" are distinguishable so the UI labels them correctly.
- [ ] **FR-D4** — Appointments not yet pushed to Google are trackable for the "not synced" indicator + manual push.

_Story-specific:_

- [ ] `GET /api/connectivity` exists, is `[AllowAnonymous]`, returns `{ internetReachable }` in Local mode, and **404s in Cloud mode**.
- [ ] The server probe caches its result for a short TTL and collapses concurrent polls to one outbound request (R-1).
- [ ] In Cloud mode the frontend provider yields a static online default and never polls; AI chat + Google controls behave exactly as today (R-3).
- [ ] No EF migration; `AppointmentDto` gains one additive field derived from the existing `GoogleCalendarEventId`.

## Entry Criteria

Before starting this story, ensure:

- [ ] Phases 1 & 2 are complete (auth mode switch + Local mode are functional).
- [ ] `dotnet build ClinicManagement.sln` is clean on the current branch.
- [ ] `web/` builds: `npm run build` succeeds and `npx tsc --noEmit` is clean.
- [ ] You can confirm current Local-mode gating idioms exist: `LocalAuthConfig.IsLocalMode(IConfiguration)` (backend) and `useSession().mode` (frontend).

## Steps

### Slice A — Connectivity signal (backend + provider) [US-1]

1. **Define the probe abstraction (Application)**
   - Create `api/ClinicManagement.Application/Common/Interfaces/IInternetProbe.cs` — outbound-probe interface (Application owns it; Infrastructure implements).
   - Create `api/ClinicManagement.Application/DTOs/ConnectivityStatusDto.cs` — `{ bool InternetReachable }`.

2. **Implement the probe (Infrastructure)**
   - Create `api/ClinicManagement.Infrastructure/Services/InternetProbe.cs` — `IHttpClientFactory` HEAD/GET to a configurable probe URL with an explicit short timeout + `CancellationToken`; any 2xx/3xx ⇒ reachable.
   - Cache last result in `IMemoryCache` for a short TTL; guard the cache-miss path with a `SemaphoreSlim`/lock so simultaneous polls trigger one outbound probe (R-1, R-2).
   - Register **as a Singleton** so the cache is genuinely shared (a scoped instance would defeat R-1).

3. **Add config keys + DI registration**
   - Modify `api/ClinicManagement.Infrastructure/Auth/LocalAuthConfig.cs` (or a small parallel static helper) — add `ProbeUrl` (default `https://www.google.com/generate_204`), `ProbeTimeoutSeconds` (default 3), `ProbeCacheSeconds` (default 5), read via `configuration["Connectivity:..."]` with baked-in `const` defaults.
   - Modify `api/ClinicManagement.Infrastructure/Extensions.cs` — `services.AddSingleton<IInternetProbe, InternetProbe>()` + `services.AddMemoryCache()` if absent.
   - Modify `api/ClinicManagement.API/appsettings.json` — add an optional documented `Connectivity` section (no secrets).

4. **Add the query + controller (Local-only)**
   - Create `api/ClinicManagement.Application/Features/Connectivity/Queries/GetConnectivityStatusQuery.cs` (+ handler) — calls `IInternetProbe`, returns `ConnectivityStatusDto`.
   - Create `api/ClinicManagement.API/Controllers/ConnectivityController.cs` — `GET api/connectivity`, `[AllowAnonymous]`, **404 in Cloud mode** (mirror Phase 1 Local-only endpoint pattern), returns the MediatR result.

5. **Frontend connectivity provider + indicator**
   - Create `web/lib/connectivity/connectivity.tsx` — `ConnectivityContext` + SSR-tolerant non-throwing `useConnectivity()` + `ConnectivityProvider`. Reads `useSession().mode`; in Local: `setInterval` poll of `/api/connectivity`, debounced transitions via a `useRef` timer, toast on debounced online/offline change (AC-6.3). Exposes `{ serverReachable, internetReachable, isLocal }`. In Cloud: static `{ serverReachable: true, internetReachable: true, isLocal: false }` (R-3, R-4).
   - Create `web/components/connectivity-indicator.tsx` — shadcn `Badge` showing online / "no internet" / "server unreachable" with an explanatory tooltip (AC-6.1, FR-D3).
   - Modify `web/app/layout.tsx` — insert `<ConnectivityProvider>` inside `SessionProvider`, around `SidebarProvider`.
   - Modify `web/components/dashboard-header.tsx` — mount `<ConnectivityIndicator />`.

### Slice B — AI chat degrades gracefully [US-2]

6. **Gate AI chat on internet reachability**
   - Modify `web/app/layout.tsx` — move `<AIChat>` **inside** the provider tree so it can consume `useConnectivity()`.
   - Modify `web/components/ai-chat.tsx` — when internet unreachable, disable send/textarea/mic + show a "requires internet" label/tooltip (AC-6.2); short-circuit `handleSend`; on an in-flight request failing with `ApiError.status === 0` show a clear "lost connection — retry" message instead of the generic toast (AC-6.5); auto re-enable via the provider (AC-6.3).

### Slice C — Google Calendar gating + sync badge + manual push [US-3]

7. **Expose the appointment sync field (backend, no migration)**
   - Modify `api/ClinicManagement.Application/DTOs/AppointmentDto.cs` — add `bool IsSyncedToGoogle` derived from `GoogleCalendarEventId != null` (US-3).
   - Modify all four manual mappers: `CreateAppointmentCommand`, `UpdateAppointmentCommand`, `GetAppointmentQuery`, `GetAppointmentsQuery` — map the new field (R-5).

8. **Gate Google controls + route sync through `client.ts`**
   - Modify `web/lib/api/types.ts` — mirror the new `AppointmentDto` field.
   - Modify `web/app/appointments/page.tsx` — gate the two Google buttons on internet reachability (disabled + "requires internet" tooltip, AC-6.2).
   - Modify `web/lib/api/google-calendar.ts` — route `syncAppointment` (and its sibling sync calls) through the shared `client.ts` request wrapper instead of raw `fetch`, so a mid-request connectivity loss surfaces as `ApiError(status === 0)` like the AI path (AC-6.5, R-7). Verify existing callers still handle the response shape.

9. **Sync badge + per-card "Push to Google"**
   - Modify `web/components/appointment-calendar.tsx` — render a "non synchronisé" badge on cards whose appointment is not synced + a per-card "Push to Google" action calling the existing `googleCalendarApi.syncAppointment(id)`, enabled only when internet reachable. **Add in BOTH card render blocks** — week view and day view.
   - Add an `onChanged?: () => void` prop; on push success call `onChanged()`; `appointments/page.tsx` bumps its existing `refreshKey` to remount the calendar → refetch → badge clears (AC-6.6, FR-D4). No change to the shared `useAppointments` hook.

## Files to Create/Modify

### Files to Create

| File | Purpose |
|------|---------|
| `api/ClinicManagement.Application/Common/Interfaces/IInternetProbe.cs` | Outbound-probe abstraction (Application owns interface) |
| `api/ClinicManagement.Application/DTOs/ConnectivityStatusDto.cs` | `{ bool InternetReachable }` |
| `api/ClinicManagement.Application/Features/Connectivity/Queries/GetConnectivityStatusQuery.cs` | Query + handler orchestrating the probe |
| `api/ClinicManagement.Infrastructure/Services/InternetProbe.cs` | Singleton probe: cached, timeout-bounded, herd-safe |
| `api/ClinicManagement.API/Controllers/ConnectivityController.cs` | `GET api/connectivity`, anonymous, 404 in Cloud |
| `web/lib/connectivity/connectivity.tsx` | `ConnectivityProvider` + `useConnectivity()` (Local-gated polling) |
| `web/components/connectivity-indicator.tsx` | Header status badge + tooltip |

### Files to Modify

| File | Changes |
|------|---------|
| `api/ClinicManagement.Infrastructure/Auth/LocalAuthConfig.cs` | Add `ProbeUrl`/`ProbeTimeoutSeconds`/`ProbeCacheSeconds` config accessors + const defaults |
| `api/ClinicManagement.Infrastructure/Extensions.cs` | Register `IInternetProbe` Singleton + `AddMemoryCache()` |
| `api/ClinicManagement.API/appsettings.json` | Optional documented `Connectivity` section (no secrets) |
| `api/ClinicManagement.Application/DTOs/AppointmentDto.cs` | Add additive `IsSyncedToGoogle` field |
| `.../Features/Appointments/Commands/CreateAppointmentCommand.cs` | Map new DTO field |
| `.../Features/Appointments/Commands/UpdateAppointmentCommand.cs` | Map new DTO field |
| `.../Features/Appointments/Queries/GetAppointmentQuery.cs` | Map new DTO field |
| `.../Features/Appointments/Queries/GetAppointmentsQuery.cs` | Map new DTO field |
| `web/app/layout.tsx` | Add `<ConnectivityProvider>`; move `<AIChat>` inside provider tree |
| `web/components/ai-chat.tsx` | Disable controls + label when offline; short-circuit send; `ApiError(0)` retry message |
| `web/components/dashboard-header.tsx` | Mount `<ConnectivityIndicator />` |
| `web/app/appointments/page.tsx` | Gate two Google buttons on internet; bump `refreshKey` on push success |
| `web/lib/api/types.ts` | Mirror `AppointmentDto` field |
| `web/lib/api/google-calendar.ts` | Route `syncAppointment` through `client.ts` (yields `ApiError(0)`) |
| `web/components/appointment-calendar.tsx` | "non synchronisé" badge + "Push to Google" in week+day blocks; add `onChanged` prop |

## Verification Steps

After completing this story, verify:

- [ ] `dotnet build ClinicManagement.sln` — 0 errors / 0 new warnings.
- [ ] Backend unit tests pass (xUnit + Moq):
  - [ ] `GetConnectivityStatusQueryHandler`: internet-up ⇒ `true`; probe throws/timeout ⇒ `false`; cached result reused within TTL.
  - [ ] `InternetProbe`: 2xx/3xx ⇒ reachable; non-response/timeout ⇒ not (mock `HttpMessageHandler`).
  - [ ] Connectivity query/controller gated to Local (404/short-circuit in Cloud).
  - [ ] Appointment DTO mapping null/non-null across all four handlers (R-5).
- [ ] `npx tsc --noEmit` clean; `npm run build` succeeds.
- [ ] **Cloud parity:** provider returns online default; `/api/connectivity` 404s; AI + Google controls unchanged; no gating on core features (R-3).
- [ ] **Manual (deferred, documented in progress.md):** Local mode, internet ON→pulled — AI + Google controls disable with "requires internet", core features keep working, controls re-enable (debounced) when internet returns; server stopped ⇒ "server unreachable"; create appointment offline ⇒ badge ⇒ "Push to Google" when online clears it.

**Verification commands:**
```bash
# Backend build + unit tests
dotnet build api/ClinicManagement.sln
dotnet test api/ClinicManagement.sln

# Frontend types + build
cd web && npx tsc --noEmit && npm run build
```

> Note (from MEMORY): Smart App Control may block `dotnet test` at DLL load (0x800711C7) — environmental, not a defect. If it blocks, record the unit tests as author-verified-by-build and note in progress.md.

## Exit Criteria

This story is complete when:

- [ ] `GET /api/connectivity` returns `{ internetReachable }` in Local, 404 in Cloud, and is anonymous.
- [ ] The header shows a live indicator distinguishing online / no-internet / server-unreachable in Local mode; nothing new appears in Cloud.
- [ ] AI chat and Google Calendar controls disable with a "requires internet" affordance when the internet is down and re-enable (debounced) when it returns.
- [ ] Core features remain fully usable while the internet is down (AC-6.4).
- [ ] Unsynced appointments show a "non synchronisé" badge with a working "Push to Google" that clears it when online (AC-6.6).
- [ ] In-flight AI/calendar loss yields a clear retryable message, not a hang (AC-6.5).
- [ ] All verification steps pass; quality gate met (0 errors / 0 new warnings; tsc clean; build succeeds).

## Notes

- **Deliberately unchanged (Cloud-safety):** `GoogleCalendarController.GetSyncStatus` network-down misclassification (`catch { tokenValid = true }`) is a shared/Cloud endpoint — left as-is; the connectivity signal makes it inert by disabling Google controls when the internet is down regardless of what `/status` reports.
- **No migration:** `Appointment.GoogleCalendarEventId` already exists; the sync indicator derives from it.
- **R-6 (Phase 4 note):** the anonymous `/api/connectivity` is a deliberate Local-only exception (like `GET /api/auth/mode`); flag it for Phase 4's "auth on all endpoints" release-gate review.
- **Test plans:** all `test-plan-*.md` are skipped for this phase (no APPROVED E2E/API/integration plans) — matching Phases 1 & 2. Automated coverage is backend unit tests; frontend is type-check + build; E2E deferred.
