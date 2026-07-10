# Feature Specification: Harden & Fix Existing Features (Cleanup Pass)

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-10
**Scope:** Full (BE-heavy)
**Feature:** A single hardening/bug-fix pass — no new user-facing features. Close cross-tenant data leaks, an auth gap, an OAuth CSRF gap, data-corruption/leak bugs, and small frontend/doc defects so everything that exists works correctly and securely.

## Overview
Several handlers fetch entities by raw Id without verifying clinic ownership, letting an authenticated user in clinic A read/modify clinic B's data. There is no EF backstop. This pass adds **both** an EF Core global query filter (defense-in-depth) **and** explicit per-handler clinic checks, plus fixes a Cloud-mode auth gap, the unvalidated OAuth `state`, a Google-sync Notes-corruption bug, an orphaned-blob leak, a broken insurance-clear branch, and a set of frontend/doc defects. No behavior is added beyond making current behavior correct.

## What Changes

### 1. Cross-clinic data isolation (BE)
- **Global query filter (backstop):** Add EF `HasQueryFilter` scoping by `ClinicId` to the entities that carry a direct `ClinicId`: **`Patient`, `Appointment`, and `ProcedureType`** (after §1a migration). The filter reads a per-request current-clinic value; when **no clinic is in scope** (background jobs, `reset-admin-password` CLI, anonymous auth/setup, non-request contexts) the filter is **inactive** (returns all rows) rather than filtering everything to empty. Do not filter `User` or `Clinic` (auth/join flows resolve them cross-clinic before a clinic context exists).
- The current-clinic value for the filter is exposed via a scoped provider read lazily inside the filter lambda (never baked into the compiled EF model). It is a backstop; the authoritative check remains the per-handler DB-resolved `User.ClinicId`.
- Where a request-scoped path must legitimately read across clinics, use `IgnoreQueryFilters()` explicitly with a comment stating why.
- **(1a) ProcedureType becomes tenant-scoped:** add `ClinicId` to the `ProcedureType` entity + EF migration; backfill existing rows to the earliest clinic (by `CreatedAt`). Scope Create/List/Get/Update/Delete by the caller's clinic (Create assigns the caller's `ClinicId`).
- **Explicit per-handler clinic checks** (inject the same clinic resolver the Stock commands / `GetPatientQuery` use; return generic "not found" on mismatch) in:
  - `UpdatePatientCommand` (currently no clinic context at all)
  - `UpdateAppointmentCommand`
  - `CreateAppointmentCommand` — verify the referenced `Patient` **and** `ProcedureType` belong to the caller's clinic
  - `GetMedicalDocumentQuery`, `UpdateMedicalDocumentCommand`, `DeleteMedicalDocumentCommand` — `MedicalDocument` has no `ClinicId`; verify via its owning `Patient`
  - `GetMedicalDocumentsQuery` — the no-arg branch currently returns **all** documents across clinics; scope to the caller's clinic, and verify the patient on the by-`PatientId` branch
  - `CreatePatientMedicalHistoryCommand` + all siblings: medical-history / family-history / dental-record **Create, Update, Delete** — verify the owning `Patient`'s clinic before mutating
  - `UpdateProcedureTypeCommand`, `DeleteProcedureTypeCommand` (now scoped by `ClinicId`)

### 2. Auth gap — GoogleCalendarController (BE)
- Add class-level `[Authorize]`; keep `[AllowAnonymous]` **only** on `authorize` and `callback`. (In Cloud the fallback policy is null, so `sync-from-google` / `status` / `sync-appointment/{id}` are currently reachable unauthenticated.)

### 3. OAuth `state` validation (BE)
- Persist the generated `state` server-side (short-lived `IMemoryCache` entry keyed by the state value; register `AddMemoryCache` if absent) on `authorize`, and on `callback` reject (400/error redirect) if the returned `state` is missing or does not match a stored entry; consume the entry on use.

### 4. Data-integrity bugs (BE)
- **Google Calendar Notes round-trip:** on Google→App (`GoogleCalendarSyncService` ~L449) stop assigning the whole event `Description` into `appointment.Notes`. Parse back only the `Notes:` line from the composite block built by `BuildAppointmentDescription` (~L318); leave `Notes` unchanged if no `Notes:` line is present. Prevents the metadata block from accumulating/nesting each sync.
- **Orphaned blob on document delete:** `DeleteMedicalDocumentCommand` must delete the underlying file. Resolve the `PatientFile` via `document.FileId` to get its `StorageKey`, delete the blob via `IFileStorage.DeleteAsync` (best-effort, mirroring the upload-failure cleanup in `CreateMedicalDocumentCommand`), and remove the orphaned `PatientFile` row, then delete the document row.
- **Insurance cannot be cleared:** in `UpdatePatientCommand`, treat a null/omitted `InsuranceInfo` as **clear** — call `patient.UpdateInsuranceInfo(null)` (the entity mutator already accepts null). Removes the empty no-op branch at L100-105. (Sole caller, the edit dialog, already sends `undefined` when both insurance fields are emptied — no frontend change needed.)

### 5. Frontend / smaller bugs (FE)
- `use-clinic-access.ts` — do **not** redirect to `/setup` from the `catch` on transient failures. "Not a member" is already the HTTP-200 `hasClinic:false` success path (which correctly redirects); the catch only sees `ApiError` (status `0` network / `>=500`). On those, set an error/retry state and keep the user in place — never boot an authenticated member to `/setup` on a blip.
- Remove dead client methods `appointmentsApi.get` and `appointmentsApi.delete` (`web/lib/api/appointments.ts` L15, L51) — routes don't exist on the controller and both are unused.
- Remove debug `console.log` calls (keep `console.error`): `use-clinic-access.ts` L54,L58; `setup-wizard.tsx` L225; `join-wizard.tsx` L106; `medical-documents.ts` L43,L57,L59; `clinic-settings.tsx` L397.
- Hide the misleading fake notifications UI: remove the `<NotificationsList/>` render + import from `web/app/page.tsx` (L9, L87). It is the only mount; the component stays in the tree but is no longer shown.

### 6. Stale docs (docs)
- Correct the false "hardcoded/sample data" claims — dashboard stats, `appointment-list`, and the whole stock feature are **API-wired**; only `notifications-list` is static: root `CLAUDE.md` L72; `web/CLAUDE.md` L76, L86, L100; `web/components/CLAUDE.md` L15, L28 (leave the L16 notifications claim as-is, it is correct).
- Correct stale `/api/auth/*` → `/bff/auth/*`: `web/CLAUDE.md` L46 and `web/components/CLAUDE.md` L35 (and the matching stale comment in `web/app/change-password/page.tsx` L8).

## Data / Schema Changes
- **`ProcedureType.ClinicId`** (new, `Guid`, non-nullable) + EF migration; existing rows backfilled to the earliest clinic by `CreatedAt`.
- No other schema changes (`Patient`/`Appointment` already have `ClinicId`; child entities keep parent-based scoping).

## Acceptance Criteria
- **AC-1:** An authenticated user in clinic A receives "not found" (not the data) when passing a clinic-B GUID to any of the listed patient/appointment/document/history/dental/procedure-type Get/Update/Delete handlers.
- **AC-2:** Creating an appointment referencing a patient or procedure type from another clinic fails with a not-found/validation error.
- **AC-3:** With the global query filter active, a normal request only ever sees its own clinic's `Patient`/`Appointment`/`ProcedureType` rows; background jobs, the `reset-admin-password` CLI, Local login/setup, and admin recovery continue to work (filter inactive / `IgnoreQueryFilters` where needed).
- **AC-4:** In Cloud mode, calling `GoogleCalendarController` `sync-from-google` / `status` / `sync-appointment/{id}` without a bearer token returns 401; `authorize`/`callback` remain anonymous.
- **AC-5:** A Google OAuth `callback` with a missing or mismatched `state` is rejected; a matching `state` succeeds.
- **AC-6:** After a Google→App sync round-trip, `appointment.Notes` holds only the user's notes (no `Doctor:/Status:/Patient ID:` block), and repeated syncs do not accumulate/nest metadata.
- **AC-7:** Deleting a medical document removes both the DB row and its stored blob (no orphaned file / no orphaned `PatientFile` row).
- **AC-8:** Updating a patient with insurance omitted/null clears the stored insurance; providing insurance still updates it.
- **AC-9:** A transient API failure (network / 500) during the clinic-access check does not redirect an authenticated member to `/setup`.
- **AC-10:** `appointmentsApi.get`/`.delete` and the listed debug `console.log`s are gone; `NotificationsList` no longer renders on the dashboard; the corrected docs no longer make the false claims.

## Out of Scope (explicit decisions)
- **Not built** (new/unfinished features, not bugs): the notifications feature (backend + real data), AI patient-summary, the stubbed working-hours save (`clinic-settings.tsx` L392-408), and patient delete. Notifications' only in-scope action is hiding the fake UI (§5).
- No new endpoints beyond scoping/auth changes; no changes to the App→Google (inline) sync direction beyond the Notes parse fix.

## Edge Cases (critical only)
- **Global filter + Google→App sync:** the manual `sync-from-google` runs in an authenticated request, so the filter is active — it will only sync the caller's clinic's appointments (correct), and any appointment/patient it creates from Google events must be assigned the caller's `ClinicId`. Use `IgnoreQueryFilters()` only if a cross-clinic read is genuinely intended.
- **ProcedureType backfill ambiguity:** if a multi-clinic install already shares procedure types, backfilling all to the earliest clinic is a lossy but documented default; operators reassign manually. (Local single-clinic installs are unaffected.)
- **Clinic resolver returns failure** (user not resolvable): handlers return the existing generic failure rather than 500.
