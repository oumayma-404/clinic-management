# Feature Review: graceful-error-handling

**Status:** INCOMPLETE
**Challenged:** No
**Date:** 2026-07-17
**Parent Branch:** feature/windows-desktop-app (feature is uncommitted working-tree work; no merge-base commit isolates it)
**Merge Base:** n/a — reviewed the working tree scoped to this feature's files (per `progress.md` "Files Changed"), excluding files owned by other in-flight features (dental-records, post-visit-review): `dental-records.ts`, `patient-record-modal.tsx`, `post-visit-review-popup.tsx`, `types.ts`, `use-clinic-realtime.ts`, and the dental migrations.
**Files Reviewed:** 47 changed files (~+445 / −385) + 4 new files (`ApiControllerBase.cs`, `errors.ts`, `error.tsx`, `global-error.tsx`). Test files (`ApiControllerBaseTests.cs`, `ExceptionMiddlewareTests.cs`) excluded from review scope.
**Review method:** 3 parallel agents adapted to the stack (backend C# code-quality; backend error-contract correctness + breaking-change; frontend Next.js/TS) — ROP agent dropped (no `Extensions.ROP` in this repo). Highest-value findings re-verified inline against source.

## Findings

### Finding 1
- **Severity:** Major
- **Category:** Business Logic
- **File:** web/lib/api/client.ts
- **Line:** 103
- **Anchor:** `handleRequest`
- **Comment:** The new unconditional 30s `AbortController` timeout (`REQUEST_TIMEOUT_MS = 30000`) wraps **every** helper — `apiGet/apiPost/apiPut/apiDelete` and the two FormData helpers (`apiPostFormData` line 218, `apiPutFormData` line 237). Several legitimate operations routinely exceed 30s and will now abort mid-flight, surfacing a false "La requête a expiré" while the server actually completes: (a) `backupApi.backupNow` → `apiPost('/backup')` — a `pg_dump` custom-format dump + file-storage copy of a real clinic; (b) `aiChatApi.sendMessage` → `apiPost('/ai/chat')` — HuggingFace inference, especially cold-start; (c) large PDF/logo uploads via `apiPostFormData`/`apiPutFormData` over a slow LAN. (Note the raw-`fetch` upload/download in `patient-files.ts` are NOT timed out, so upload behavior is also inconsistent.) Fix: make the timeout opt-out / configurable per call (thread an options arg through the helpers) and disable or greatly raise it for backup, AI chat, and the FormData helpers.

### Finding 2
- **Severity:** Minor
- **Category:** Business Logic
- **File:** web/lib/api/client.ts
- **Line:** 83
- **Anchor:** `toApiError`
- **Comment:** A timeout is normalized to `ApiError(0, TIMEOUT_ERROR_MESSAGE)` — the same `status === 0` used for genuine network/offline failures. Consumers that branch on `status === 0` as the "connection lost / offline" signal (e.g. `ai-chat.tsx` shows "Connexion perdue … pendant l'envoi") cannot distinguish a 30s timeout from an offline drop, so a timeout is mislabeled as offline. Fix: give timeouts a distinguishable marker (HTTP-style `408`, or an `isTimeout`/`kind` field on `ApiError`) while keeping the localized message, so callers branch correctly. (Interacts with Finding 1 — less pressing if the timeout is scoped down.)

### Finding 3
- **Severity:** Minor
- **Category:** Code Quality
- **File:** api/ClinicManagement.API/Controllers/ClinicsController.cs
- **Line:** 93
- **Anchor:** `ClinicsController` (DoctorInfo JSON-parse catch)
- **Comment:** The AC-8 fix correctly stopped leaking `ex.Message` to the client, but `catch (Exception ex)` became `catch (Exception)` with **no server-side logging** — the deserialization failure is now swallowed with zero trace. This is inconsistent with how the same feature handled the analogous cases (`GoogleCalendarController` and `MedicalDocumentsController` both `_logger.LogError(ex, ...)` before returning the generic message). Fix: inject `ILogger<ClinicsController>` and `_logger.LogError(ex, "Invalid DoctorInfo JSON on clinic creation")` before `return Failure("Invalid DoctorInfo format.")`, matching the log-server-side/return-generic pattern used elsewhere in this feature.

### Finding 4
- **Severity:** Minor
- **Category:** Code Quality
- **File:** web/components/appointment-calendar.tsx
- **Line:** 133
- **Anchor:** `AppointmentCalendar` (error `useEffect`)
- **Comment:** `useAppointments.fetchAppointments` calls `setError(null)` at the start of every fetch, so each date/view navigation transitions `error` null→message. The new `useEffect(() => { if (error) toast.error(error) }, [error])` therefore re-fires on every failed refetch — navigating day/week/month or changing the date while the server is down spams a fresh toast each time. Fix: give the toast a stable id (`toast.error(error, { id: "appointments-load" })`) so repeats dedupe, or only toast on the first failure.

### Finding 5
- **Severity:** Suggestion
- **Category:** Code Quality
- **File:** api/ClinicManagement.API/Controllers/ApiControllerBase.cs
- **Line:** 17
- **Anchor:** `ApiControllerBase.GenericErrorMessage`
- **Comment:** The canonical generic fallback string "An error occurred while processing your request." is duplicated as a literal in `ExceptionMiddleware`'s 500 `default` case (`ExceptionMiddleware.cs:56`) — the other half of this feature's `{ error }` contract. Two independent copies of the same canonical message will drift over time. Consider a single shared constant (or a cross-referencing comment) so the API-helper fallback and the middleware fallback stay identical.

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 1 |
| Minor | 3 |
| Suggestion | 1 |
| **Total** | 5 |

**Verified clean (no findings):** status-code preservation across all 18 controllers (401/403/404/500 all passed into the helper, none downgraded to the default 400); AuthController failure-shape change does not break the BFF consumers (`local-login`/`change-password` read `data.error`, still present; success paths still return the full `Result` envelope unchanged); AC-8 internal-detail leakage resolved in GoogleCalendar/MedicalDocuments; canonical `{ error }` shape complete (no remaining bare-string / raw-`Result` / `{error,details}` failure returns); `ExceptionMiddleware` `ValidationException`→400 case correct and independent of the 403/404 cases; timeout timer cleared in `finally` (no leak); `ensureSuccess` reads the body only on the non-OK path (no double-consume / blob-as-JSON); `extractErrorMessage` reads canonical then legacy shapes with a French generic fallback for non-JSON bodies; both error boundaries correct.
