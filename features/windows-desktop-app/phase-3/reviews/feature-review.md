# Feature Review: windows-desktop-app — Phase 3 (Connectivity Awareness & Offline UX)

**Status:** COMPLETE
**Challenged:** Yes
**Date:** 2026-07-08
**Challenged Date:** 2026-07-08
**Parent Branch:** main
**Merge Base:** 9798b95d31f55ee07f2ad5e0af5550c4c2831022
**Scope:** Phase 3 only (FR-D + US-6). Reviewed the Phase 3 commits `e382572..HEAD` (961e77c + b7b81b9): **25 changed files, +887 / -63 lines** (excludes `features/**`, `*.md`, and lock files). The full merge-base diff is 143 files / +9859 lines, but that is Phases 1 & 2 (already reviewed and COMPLETE) — out of scope here.
**Review method:** 5 stack-adapted agents in parallel (Code Quality, Error-Handling/CQRS [not ROP — this repo uses MediatR + `Result<T>`], Business-Logic/spec-alignment, Breaking-Changes, Frontend React/Next), plus manual source verification of the two Major findings. Phase-4 concerns (HTTPS/CORS, auth-on-all-endpoints, anonymous `/api/connectivity`) and inline-validation-vs-FluentValidation were treated as out of scope per the plan.

## Challenge Summary

| Metric | Count |
|--------|-------|
| Original findings | 8 |
| Confirmed | 8 |
| Confirmed (adjusted) | 0 |
| Dismissed (false positive) | 0 |
| Dismissed (pre-existing) | 0 |
| **Final findings** | 8 |

Every finding was re-verified against the **full source** (not just the diff), including reading the cited precedents (`client.ts` `handleResponse`, `GoogleCalendarController` response shapes, the `GetConnectivityStatusQueryHandler` return paths). All 8 held up as real; no false positives and no severity adjustments. The two Major findings (F1 debounce, F2 short-card gating) are the actionable ones and both fail an explicit spec AC.

## Findings

### Finding 1
- **Severity:** Major
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** web/lib/connectivity/connectivity.tsx
- **Line:** 37
- **Anchor:** `ConnectivityProvider.applyDebounced` / `DEBOUNCE_MS`
- **Comment:** The debounce does not actually suppress flapping (spec edge case "Connectivity flapping: debounce so online-only features don't rapidly toggle", AC-6.3/AC-6.4). `DEBOUNCE_MS` (3s) is far shorter than `POLL_INTERVAL_MS` (15s), and `applyDebounced` only clears/re-arms the timer when a *different* reading arrives before the pending 3s timer fires. Since polls are 15s apart, the 3s timer always fires before the next poll, so consecutive differing polls are never coalesced — the "debounce" just delays every genuine transition by a fixed 3s and provides zero flap suppression. A server whose internet alternates reachable/unreachable each poll will toggle the UI and fire a `notifyTransition` toast every 15s. Fix: require N consecutive stable readings before applying a transition, or use a debounce/confirmation window ≥ the poll interval.
- **Challenge verification:** Read the full provider. `poll()` runs once immediately then on a 15s `setInterval`; two differing readings can never arrive within the 3s window, so `applyDebounced`'s clear/re-arm branch is unreachable in practice and the timer only adds latency. Confirmed exactly as described.

### Finding 2
- **Severity:** Major
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** web/components/appointment-calendar.tsx
- **Line:** 586
- **Anchor:** `{!isVerySmall && renderSyncControls(appointment)}` (week view; day view at ~line 653)
- **Comment:** AC-6.6 / FR-D4 require that *any* appointment not yet pushed to Google shows the "non synchronisé" badge **and** a manual "Push to Google" action. But `renderSyncControls` is gated behind `!isVerySmall` (`isVerySmall = heightPercent < 40`, and `heightPercent = (durationMinutes/60)*100`), so every appointment shorter than ~24 min (common 15-/20-min slots → 25%/33%) renders neither the badge nor the Push button, in both week (586) and day (653) views. The edit dialog was not modified in this diff, so there is no fallback push affordance — a short appointment created while offline can never be flagged as unsynced nor manually pushed. Fix: keep at least the Push control (an icon-only variant) for small cards, or expose "Push to Google" in the edit-appointment dialog so AC-6.6 holds regardless of duration. (The team already flagged this in `stories/progress.md` → "Notes for review".)
- **Challenge verification:** Confirmed at both call sites (`appointment-calendar.tsx:586` week, `:653` day) with `isVerySmall = heightPercent < 40` at lines 552 and 610. `renderSyncControls` (line 83) is otherwise correct (skips synced, busy-slot, cancelled/completed) — the only defect is that it's unreachable for short cards.

### Finding 3
- **Severity:** Minor
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Services/InternetProbe.cs
- **Line:** 85
- **Anchor:** `InternetProbe.ProbeAsync` (catch) + `IsInternetReachableAsync` caching; also `GetConnectivityStatusQueryHandler.Handle` catch (GetConnectivityStatusQuery.cs:28)
- **Comment:** `ProbeAsync` wraps the outbound call in a blanket `catch (Exception)` that returns `false`, and `IsInternetReachableAsync` then caches that `false` for the full TTL. Two problems: (a) a genuine programming/config fault — e.g. a bad `Connectivity:ProbeUrl` throwing `UriFormatException`/`InvalidOperationException` (the config reads at lines 67-68 are *outside* the try, but any client-construction/send fault is inside) — is silently reported as "no internet" and only logged at `Debug`, masking a real bug behind a plausible offline state. (b) The method threads a caller `CancellationToken` into the linked timeout token; if a caller token is ever wired (the controller currently passes none via `_mediator.Send(new GetConnectivityStatusQuery())`, so this is latent, not yet live), a caller-abort would surface as `OperationCanceledException`, be swallowed as `false`, and **cache the false for the whole TTL — poisoning the shared singleton cache so every LAN client shows "offline" until it expires**. The handler's `catch (Exception)` at GetConnectivityStatusQuery.cs:28 likewise swallows with no log (inconsistent with other handlers in this repo). Fix: narrow the catch to `HttpRequestException`/`TaskCanceledException`/`TimeoutException`; re-throw when the caller's own token requested cancellation (don't cache that); and log the handler catch at least at Debug.
- **Challenge verification:** Confirmed. `new HttpRequestMessage(HttpMethod.Get, url)` (line 76, inside the try) parses `url` into a `Uri` and throws `UriFormatException` on a malformed `Connectivity:ProbeUrl`, caught by the blanket handler → masked as offline (part (a) is *live*). Part (b) is correctly flagged by the reviewer as *latent* — `ConnectivityController` sends the query with no token today — but the design smell (caching a cancellation as `false` in the shared singleton `IMemoryCache`) is real. `GetConnectivityStatusQuery.cs:28-33` swallows with no logging.

### Finding 4
- **Severity:** Minor
- **Category:** Breaking Change
- **Verdict:** Confirmed
- **File:** web/lib/api/google-calendar.ts
- **Line:** 40
- **Anchor:** `googleCalendarApi.syncFromGoogle` / `syncAppointment` (`apiPost` calls)
- **Comment:** Routing `syncFromGoogle`/`syncAppointment` through `client.ts` (`apiPost`) silently changes the error message surfaced to the **existing Cloud caller**. `GoogleCalendarController` returns failures as `{ error: "..." }` (e.g. `BadRequest(new { error = "Google Calendar is not configured..." })`, `StatusCode(500, new { error = $"Error syncing...: {ex.Message}" })`), and the old raw-fetch code read exactly that: `error.error || 'Failed to sync...'`. But `client.ts` `handleResponse` only inspects `errorData.title || errorData.message || errorData.errors` — it never reads `.error` — so the specific server message is dropped and `ApiError.message` falls back to `HTTP 400: Bad Request` / `HTTP 500: ...`. This degrades the non-status-0 branch of `handlePushToGoogle` (`appointment-calendar.tsx`) and the `alert(...)` in `appointments/page.tsx:92`, which previously showed the actionable "not configured" text. This is a Cloud-mode behavior change (against the byte-for-byte-unchanged principle), though functionally the sync and `.message` access still work (ApiError extends Error). Fix: have `client.ts` `handleResponse` also fall back to `errorData.error`, or return ProblemDetails (`title`/`message`) from the controller.
- **Challenge verification:** Cited precedent verified verbatim — `GoogleCalendarController.cs` returns `new { error = ... }` at lines 44, 158, and 163; `client.ts` `handleResponse` (lines 19-27) reads only `.title`/`.message`/`.errors`, never `.error`. The specific server message is genuinely dropped to `HTTP 4xx/5xx: <statusText>`. Confirmed.

### Finding 5
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** web/app/appointments/page.tsx
- **Line:** 91
- **Anchor:** `handleSyncFromGoogle` (catch)
- **Comment:** AC-6.5 requires an in-flight AI *or calendar* request that loses connectivity to show a clear, retryable "connection lost" message rather than a generic error. `handleSyncFromGoogle` (a calendar request) does not special-case `ApiError.status === 0`; on mid-request loss it shows a generic `alert("Failed to sync: Network error: Unable to connect...")`. This is inconsistent with the polished handling added to `ai-chat.tsx` and `handlePushToGoogle` for the same failure. Fix: mirror the `error instanceof ApiError && error.status === 0` branch here with the "Connexion perdue / réessayez" message, and prefer a toast over a raw `alert`.
- **Challenge verification:** Confirmed. `appointments/page.tsx:91-92` is a bare `alert(\`Failed to sync: ${error.message}\`)` with no `status === 0` branch, whereas `ai-chat.tsx:221` and `appointment-calendar.tsx:67` (`handlePushToGoogle`) both special-case it. The inconsistency is real; the button is internet-gated but a *mid-request* drop still hits this path.

### Finding 6
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** web/components/ai-chat.tsx
- **Line:** 175
- **Anchor:** `handleSend` internet-gate (`if (!internetReachable) toast.warning(...)`)
- **Comment:** FR-D3 / AC-6.3 require "feature not configured" to be distinguishable from "network unavailable". For AI chat this distinction does not exist: `ConnectivityStatusDto` carries only `internetReachable`, no AI-config status is exposed to the frontend, and the widget gates purely on `internetReachable`. When internet is up but HuggingFace is unconfigured, the widget appears fully enabled and a send produces the generic "Failed to get AI response" — the two states are conflated. (Google Calendar *does* distinguish this via the pre-existing `getStatus().isConfigured`, so the gap is AI-specific.) Fix: surface a distinct "Assistant IA non configuré" affordance so online-but-unconfigured is not presented identically to a working online state. (Lower priority than the calendar path since AI config is server-side and static per install.)
- **Challenge verification:** Confirmed. `ConnectivityStatusDto` exposes only `InternetReachable`; `ai-chat.tsx` gates on `internetReachable` alone (lines 176, 681, 702, 708) with no AI-config flag, so online-but-unconfigured falls through to the generic `catch` at line 226. Google Calendar's `getStatus().isConfigured` gives it the distinction AI lacks. Real FR-D3 gap, AI-specific; low priority as the reviewer notes (config is static per install).

### Finding 7
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/Controllers/ConnectivityController.cs
- **Line:** 41
- **Anchor:** `ConnectivityController.Get`
- **Comment:** The fallback `result.IsSuccess && result.Value is not null ? result.Value : new ConnectivityStatusDto { InternetReachable = false }` is unreachable: `GetConnectivityStatusQueryHandler.Handle` returns `Result.Success` with a non-null `Value` on *every* path (both the try and the catch), so the else branch is dead. This is a third redundant layer guarding the same `false` default (probe → handler → controller). Simplify to `return Ok(result.Value);` and let the handler own the fallback.
- **Challenge verification:** Confirmed. Both paths in `GetConnectivityStatusQuery.cs` (line 26 try, line 32 catch) return `Result<...>.Success(new ConnectivityStatusDto {...})` — never `Failure`, never null `Value` — so the controller's `else` branch (line 43) is dead code. Valid cleanup.

### Finding 8
- **Severity:** Suggestion
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/components/ai-chat.tsx
- **Line:** 700
- **Anchor:** mic toggle button `disabled={isLoading || !internetReachable}`
- **Comment:** The mic toggle is disabled on `!internetReachable`. If internet drops while the user is actively dictating (`isListening === true`), the button becomes disabled mid-capture — the user can no longer press it to stop, and any dictated text lands in a textarea that is itself disabled and cannot be sent. Consider keeping the toggle usable while listening (`disabled={isLoading || (!internetReachable && !isListening)}`) or proactively stopping recognition when `internetReachable` flips to false.
- **Challenge verification:** Confirmed. The mic button (line 681) is `disabled={isLoading || !internetReachable}` with no `isListening` exception, and the textarea (line 702) is also disabled on `!internetReachable`, so a mid-dictation internet drop strands both the stop control and the captured text. Valid UX suggestion.

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 2 |
| Minor | 4 |
| Suggestion | 2 |
| **Total** | 8 |

**Notes on what was verified clean (no findings):** the four `AppointmentDto.IsSyncedToGoogle` mappings (Create/Update/Get/GetAll) are all present and consistently derived from `GoogleCalendarEventId != null`, with a matching `types.ts` field (purely additive DTO change); `InternetProbe` is correctly a Singleton over a shared `IMemoryCache` with a double-checked `SemaphoreSlim` herd-guard and a disposed linked-timeout token; `IHttpClientFactory` is safe in the singleton and `AddMemoryCache()` is idempotent; Clean Architecture layering holds (`IInternetProbe` in Application, impl in Infrastructure); the connectivity endpoint 404s in Cloud and the provider never polls in Cloud (static online default), so Cloud behavior is preserved; the `setInterval`/debounce timers are cleared on unmount with an `active` guard against setState-after-unmount; the context default is SSR/no-provider tolerant; and `"use client"` is present on both new client files.
