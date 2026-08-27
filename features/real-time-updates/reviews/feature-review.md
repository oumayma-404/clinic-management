# Feature Review: real-time-updates (Slice 2 — broadcast any edit via pipeline behavior)

**Status:** INCOMPLETE
**Challenged:** No
**Date:** 2026-07-10
**Parent Branch:** feature/windows-desktop-app
**Merge Base / Scope base:** `73e8a0a` (Phase-5 leftovers commit; slice-1 real-time is `3e4315b`, already reviewed — archived as `feature-review-slice1.md`)
**Files Reviewed:** 21 tracked changes (+162 / −258, the −198 being a removed test) + 2 new files (`RealtimeBroadcastBehavior.cs`, `RealtimeBroadcastBehaviorTests.cs`); `features/**` docs and the deleted test excluded.

**Review method:** Reviewed **inline** (agents skipped) — small, single-intent diff (~470 lines of new reviewable code, fits in working memory). Applied all four mandates adapted to this stack: Code Quality & Architecture; **`Result`/CQRS error-handling** (in place of the ROP mandate — this repo uses MediatR + `Result<T>`, not `Extensions.ROP`); Business Logic Correctness; Breaking Changes / Regression Risk; plus a **Frontend** lens (React hook lifecycle, Rules of Hooks, connection management). Scope note: HTTPS/CORS/front-door and slice-1 hub auth were out of scope (slice 1 / windows-desktop-app, separately reviewed).

## Findings

### Finding 1
- **Severity:** Minor
- **Category:** Business Logic
- **File:** web/app/patients/[id]/page.tsx
- **Line:** 143
- **Anchor:** `PatientDetailsPage` → `useClinicRealtime([Patients, Appointments, Files], …)`
- **Comment:** The patient-detail page re-runs its full loader (6+ API calls: patient, appointments, medical/family history, dental records, files, folders) on **any** clinic-wide change to `patients`, `appointments`, or `files` — not just changes to the patient currently open. On a busy clinic, one secretary creating an appointment for patient B triggers a full 6-call reload of patient A's open detail on every other client. This is inherent to the no-payload "signal → refetch" design (the event carries no entity id), so it is a deliberate trade-off, but it is the heaviest refetch wired. Options: accept (documented), or have the loader split so the realtime path reloads only the changed sub-resource, or add an optional entity-id argument to the broadcast so pages can ignore unrelated ids. Not blocking.

### Finding 2
- **Severity:** Suggestion
- **Category:** Code Quality
- **File:** api/ClinicManagement.Application/Common/Behaviors/RealtimeBroadcastBehavior.cs
- **Line:** 63
- **Anchor:** `RealtimeBroadcastBehavior.Handle` / `ResolveClinicIdAsync`
- **Comment:** For every successful mutating command the behavior re-resolves the clinic with `IUserRepository.GetByAuth0SubAsync`, even though the handler that just ran typically already performed the exact same lookup (e.g. `CreateAppointmentCommand`). That is one extra DB round-trip per mutation. It is the accepted cost of keeping broadcasting zero-wiring/cross-cutting (there is no clean channel to hand the already-resolved clinic id from the handler to the behavior), and mutations are far rarer than reads, so it is acceptable — but worth noting. If it ever shows up in profiling, expose the resolved clinic id via a scoped `IClinicContext` cache so both handler and behavior share one lookup.

### Finding 3
- **Severity:** Suggestion
- **Category:** Business Logic
- **File:** web/lib/realtime/clinic-hub.ts
- **Line:** 19
- **Anchor:** `RealtimeResource`
- **Comment:** The frontend `RealtimeResource` keys (and the backend behavior's `ExcludedAreas`) are string-coupled to the API's feature-folder names (`Features/<Area>/Commands` lowercased) with **no test guarding the contract**. Renaming a feature folder — e.g. `ProcedureTypes` → `Procedures` — would silently change the broadcast key to `procedures` while the frontend still listens for `proceduretypes`, so live refresh for that view stops working with no compile error and no test failure. Add a backend test that reflects over the real command types and asserts the derived key for each area (also documents the exclusion list), and/or a shared constants source. Low risk today (keys verified to match), but silent-failure-prone.

### Finding 4
- **Severity:** Suggestion
- **Category:** Code Quality
- **File:** web/lib/realtime/use-clinic-realtime.ts
- **Line:** 30
- **Anchor:** `useClinicRealtime` (useEffect)
- **Comment:** Each `useClinicRealtime` call opens its **own** SignalR connection. This is fine for the current one-page-at-a-time navigation (only the active route's page/components are mounted, so ~1 socket), and the multi-resource callback was added precisely so a page watches several resources over one connection. But if a future surface mounts two subscribing components at once (e.g. a dashboard with several live widgets), it will open N sockets. If that happens, promote the connection to a shared module-level singleton / React context that fans `entityChanged` out to per-resource subscribers. Note only; no change needed now.

### Finding 5
- **Severity:** Suggestion
- **Category:** Business Logic
- **File:** web/components/clinic-settings.tsx
- **Line:** 251
- **Anchor:** `ClinicSettings` → `useClinicRealtime(Clinics, …)`
- **Comment:** The guarded reload (skip while `isEditingClinicInfo || isEditingDoctors || isEditingHours`) correctly avoids clobbering an in-progress edit — good call. The side effect is that an admin editing settings while another admin saves a change never sees that their form is now stale, and on save will overwrite it (last-write-wins). This is a **pre-existing** characteristic (there is no optimistic-concurrency token on clinic update), not introduced by this slice, but the new guard makes the stale window slightly longer. If concurrent admin edits are a real scenario, consider surfacing a "settings changed elsewhere — reload?" prompt instead of silently skipping. Out-of-scope-adjacent; note only.

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 0 |
| Minor | 1 |
| Suggestion | 4 |
| **Total** | 5 |

### Reviewer notes (verified clean — no findings)
- **Rules of Hooks:** all inserted `useClinicRealtime` calls sit above the first component-level JSX `return` (verified in clinic-settings/user-management/patient-files-manager/patient-detail); no conditional-hook violation.
- **Broadcast-after-commit:** the behavior awaits `next()` (the handler, which owns its `SaveChangesAsync`) before broadcasting; a failed command returns a failure `Result` → no broadcast. Pipeline order Validation → Logging → RealtimeBroadcast → Handler is correct.
- **Multi-tenant isolation:** resource key is derived from the command **type** (never client input); clinic id is resolved server-side from the authenticated principal and broadcast only to `clinic-{id}`. No cross-tenant path.
- **Fail-safe / additive:** behavior swallows resolution/broadcast errors and always returns the handler's response; `SignalRRealtimeNotifier` swallows transport errors. A broadcast can never fail a committed command.
- **Excluded areas:** `Auth`/`AI`/`Backup`/`Connectivity` correctly excluded (login, chat, backup are not clinic list data); `Stock`/`Documents` broadcast but have no subscriber yet (harmless — documented in spec Out of Scope).
- **Breaking change (event/interface rename `appointmentsChanged`→`entityChanged`, `NotifyAppointmentsChangedAsync`→`NotifyEntityChangedAsync`):** the only consumers are this app's own frontend and the notifier impl, all updated in the same change; slice 1 was never released. No external subscriber at risk.
- **Quality gates:** backend `dotnet build` 0/0, 19 unit tests pass; frontend `tsc --noEmit` clean, `npm run build` succeeds (17 routes).
