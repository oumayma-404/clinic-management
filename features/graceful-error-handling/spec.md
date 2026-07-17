# Feature Specification: Graceful Error Handling

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-17
**Scope:** Full
**Feature:** Standardize the API error contract and make every error surface to the user gracefully (readable, localized, non-blocking) across the stack.

## Overview
Error handling is fragmented. The backend emits at least four different error-body shapes; the frontend parser (`web/lib/api/client.ts`) reads `title`/`message`/bare-string but **never `error`**, so a large share of real messages are dropped and the user sees `"HTTP 400: Bad Request"`. On the UI side there are no React error boundaries (a render throw = blank screen), `alert()` calls in a toast-based app, errors swallowed to console-only or silently rendered as empty lists, mixed English/French text, and raw-`fetch` modules that bypass the shared client. This feature makes the error contract **one shape** end-to-end and ensures every failure path shows the user a clear, French, non-blocking message.

## What Changes

### Backend — one canonical error body
- Add a shared API base controller (`ApiControllerBase : ControllerBase`) with a `HandleFailure(Result)` / `Problem(...)` helper that maps every `Result.Failure` to `{ "error": "<message>" }` with the action's chosen status code. All 18 controllers extend it and route failures through it.
- Replace every ad-hoc failure return with the helper: bare `BadRequest(result.Error)`, `NotFound(result.Error)`, `BadRequest(result)` (raw `Result` envelope — Auth/Clinics), `{ error }` / `{ error, details }` anonymous objects (GoogleCalendar/Clinics/Auth), and loose raw-string returns (MedicalDocuments/PatientFiles) all become the canonical `{ error }` shape.
- Stop internal-detail leakage: `GoogleCalendarController` must no longer return `ex.ToString()` / raw Google error bodies in `details`; those become the generic `{ error }` (logged server-side instead).
- `ExceptionMiddleware` already emits `{ error }` — keep it as the canonical shape and add a `FluentValidation.ValidationException` case → `400 { error }` (today it would fall through to a generic 500).
- Status-code assignment is **preserved per action** (existing 400/401/403/404 choices stay); only the body shape is unified. No new error-code system, no 409/422 redesign.

### Frontend — parse the contract + surface gracefully
- `client.ts`: read `errorData.error` first (canonical), keep tolerance for the legacy shapes (bare string, ProblemDetails `title`/`errors`, `Result` envelope `error`) so nothing regresses mid-migration.
- `client.ts`: add a request **timeout** via `AbortController` (abort → localized `ApiError`), and localize the network/unexpected fallbacks to French (remove the English "…CORS is configured correctly" string).
- Normalize the raw-`fetch` modules (`patient-files.ts`, `medical-documents.ts` `generatePdfForDownload`, `clinics.ts` `getLogo`) to go through the shared response handler so they throw `ApiError` (fixes broken `instanceof ApiError` and `status === 0` offline detection).
- Add a shared error helper `getErrorMessage(err, fallback)` + `showErrorToast(err, fallback)` in `web/lib/` and route component error handling through it (single French-first formatting point).
- Add App Router error boundaries: `web/app/error.tsx` (segment-level) and `web/app/global-error.tsx`, both French, with a "Réessayer" action.
- Replace all `alert()` error/success calls (`procedure-types-table.tsx`, `app/appointments/page.tsx`) with sonner toasts.
- Replace user-facing console-only swallows and silent empty-on-error with a toast and/or an error state: notably `appointment-calendar.tsx` (currently ignores `error` from `useAppointments`), plus file preview/download and reload paths in `app/patients/[id]/page.tsx`.
- Localize all frontend-owned error/fallback strings to French (hooks `use-appointments`, `use-dashboard-stats`, `use-clinic-access`; `files/page.tsx`, `records/page.tsx`, `stock-table.tsx`, `patients-table.tsx`, `user-management.tsx`, `ai-chat.tsx`, dialogs, etc.).

## Acceptance Criteria
- **AC-1:** Every controller returns failures as `{ "error": "<message>" }` (single shape) through the shared base-controller helper; no action returns a bare string, a raw `Result` envelope, or an `{error, details}` object. A test pins the canonical shape.
- **AC-2:** `client.ts` surfaces the backend `error` message for all previously-broken paths (Google Calendar, clinic create/join/update, middleware 403/404/500) — the user sees the real reason, never `"HTTP 400: Bad Request"` when the server sent a message.
- **AC-3:** A thrown error during render is caught by an error boundary showing a French message + "Réessayer" — no blank screen.
- **AC-4:** No `alert()` remains for error/success feedback; those paths use toasts.
- **AC-5:** `appointment-calendar` (and other lists that previously rendered empty-on-error) show a distinct error state or toast when their load fails — not a silent empty view.
- **AC-6:** Raw-`fetch` modules throw `ApiError`; `instanceof ApiError` and `status === 0` offline handling work for file upload/download/PDF/logo paths.
- **AC-7:** A request that exceeds the timeout rejects with a localized `ApiError` instead of hanging forever.
- **AC-8:** No internal detail (stack trace, raw Google error body) is returned to the client from any endpoint; the client-facing body is the generic `{ error }` (details go to server logs).
- **AC-9:** All frontend-owned error/fallback/network strings are French; no English error text renders in the UI.
- **AC-10:** Cloud and Local auth modes are both unaffected in behavior (only error-body shape + messages change); existing controller authorization coverage tests still pass.

## API Contract
Canonical error body for all non-2xx responses (status codes unchanged per action):
```
4XX/5XX: { "error": "<human-readable message>" }
```
- `400` — validation / business-rule failures (default for `Result.Failure`)
- `401` — authentication failure (login)
- `403` — `ForbiddenAccessException`
- `404` — not-found (GET-by-id, delete, and the notification not-found convention)
- `500` — unhandled — always the generic message, never internals

## Data / Schema Changes
None. `Result`/`Result<T>` keep their current shape (single `Error` string); no error-code field is added.

## Out of Scope
- Adding a structured error-code / error-kind system, or redesigning HTTP status codes (no new 409/422).
- Translating **backend** business messages to French (they stay English for now; only the *shape* is standardized and FE-owned strings are localized). A future pass can add a code→FR table.
- 401 auto-reauth / token-refresh interceptor for mid-session expiry (behavior unchanged; still surfaces as an error).
- The dormant email/SMS `NotificationService`, SignalR/realtime "connection lost" UI (already silent by design), and RFC7807 ProblemDetails adoption.

## Edge Cases (Critical only)
- **Migration tolerance:** while backend controllers are being converted, `client.ts` must still read the legacy shapes so no path regresses (belt-and-suspenders parsing).
- **Non-JSON error body** (e.g. a proxy 502 HTML page): `client.ts` falls back to a localized generic message, never `[object Object]` or an empty toast.
- **Offline mid-request:** a `status === 0` failure shows the French offline/connection message, consistently, including from the newly-normalized raw-`fetch` paths.
- **AuthController partial-success:** `login`/`setup`/`register`/`change-password` currently return the whole `Result`; after standardizing to `{ error }`, the BFF/login routes that read `data.error` must keep working (verify `local-login`/`change-password` routes).
