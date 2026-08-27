# Feature Review: real-time-updates (Appointments slice)

**Status:** INCOMPLETE
**Challenged:** No
**Date:** 2026-07-10
**Parent Branch:** feature/windows-desktop-app (feature reuses this branch; work is uncommitted, interleaved with Phase 5 windows-desktop-app changes)
**Merge Base:** n/a — feature is uncommitted in the working tree; reviewed the working-tree diff scoped to the real-time-updates files listed in `progress.md`
**Files Reviewed:** 16 real-time files (10 new, 6 modified) — backend hub/notifier/interface + appointment handlers + Program.cs SignalR wiring; frontend hub factory/hook + appointments page + package.json; plus 4 new test classes.

**Review method:** Reviewed inline (not via the 4 parallel agents) — small, single-feature diff (~300 lines of production code across small files, fits in working memory). Applied all mandates adapted to this stack: Code Quality & Architecture, Business Logic Correctness, Breaking Changes / Regression Risk, plus a **Security** lens (hub JWT auth + multi-tenant isolation) and a **Frontend** lens (React hook lifecycle, SSR-safety). ROP mandate N/A — this repo uses MediatR + `Result<T>`, not `Extensions.ROP`.

**Scope note (out of this review):** Program.cs also carries two Phase-5 / windows-desktop-app edits that are uncommitted in the same file — the `if (!isLocalAuthMode) app.UseHttpsRedirection()` guard and `app.MapReverseProxy().AllowAnonymous()`. These belong to windows-desktop-app (which has its own feature-review) and are **not** real-time-updates changes; they were excluded from this review.

## Findings

### Finding 1
- **Severity:** Suggestion
- **Category:** Security
- **File:** api/ClinicManagement.API/Program.cs
- **Line:** 68
- **Anchor:** `CreateHubJwtEvents` (OnMessageReceived)
- **Comment:** The hub reads the bearer JWT from the `access_token` **query-string** parameter on the WebSocket handshake. This is the standard (and only) way a browser WebSocket can pass a bearer token, so the approach is correct and the path is correctly narrowed to `/hub` requests. The residual hardening concern is that query strings are commonly captured verbatim in HTTP access logs / reverse-proxy logs, so the token could land in logs. Consider ensuring the request-logging pipeline (Serilog request logging, and any front-door/YARP access log) scrubs or omits the query string for `/hub/*`, or documents that these logs must be treated as sensitive. Not blocking — it's an accepted SignalR tradeoff — but worth an explicit note given this is the one real-time behavior added to Cloud as well.

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 0 |
| Minor | 0 |
| Suggestion | 1 |
| **Total** | 1 |

### Reviewer notes (verified clean — no findings)
- **Multi-tenant isolation (AC-2):** group name is `clinic-{clinicId}` from the single-source `ClinicGroups.Name`; the clinic id is always resolved server-side from the authenticated principal (`user.ClinicId` on create, `appointment.ClinicId` on update) — never from client input. No cross-clinic leak path found.
- **Broadcast-after-commit (edge case):** both handlers broadcast strictly after `SaveChangesAsync`, inside the try, so a rolled-back/failed save never broadcasts. Cancellation is an update to `Cancelled`, covered by the update path.
- **Additive / fail-safe (AC-5):** `SignalRRealtimeNotifier` catches and **logs** (not silently swallows) any broadcast failure, honoring the `IRealtimeNotifier` contract that a failed broadcast must never fail the committed use case. The interface lives in Application; the SignalR impl lives in the API/presentation layer — Clean Architecture dependency direction respected.
- **Auth (AC-3):** `ClinicHub` is `[Authorize]`; in Local mode the fail-closed `FallbackPolicy` also gates it. `options.Events` is set only in the two existing JWT branches with no pre-existing events clobbered.
- **Frontend:** the hook connects once per mount (deps `[]`, latest callback held in a ref), retries the first connect until unmount, relies on `withAutomaticReconnect` + `onreconnected` refetch for catch-up (AC-4), and is SSR-safe (`resolveHubUrl`/`createClinicHubConnection` return null off-browser). Cleanup clears the retry timer and stops the connection.
- **Regression risk:** no API contract, DB schema, or public DTO changes; the only breaking-at-compile change is the added `IRealtimeNotifier` ctor param on the two appointment handlers, already reconciled in `AppointmentSyncMappingTests`.
