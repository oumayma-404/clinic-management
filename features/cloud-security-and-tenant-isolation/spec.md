# Feature Specification: Cloud Multi-Tenant Security & Reachability Hardening

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-23
**Scope:** Full
**Feature:** Close the multi-tenant security failles that live in the Cloud auth path and make the admin surface reachable in Cloud, so the hosted SaaS is tenant-safe and exposes the same Tunisia-specific admin features the Local build already does.

## Overview
The app ships defaulted to **Cloud** auth mode (`web/lib/auth/local-auth.ts:14`). In that mode almost every serious security weakness the audit found is active, while the Local (offline-LAN) path is clean. This feature hardens the Cloud/multi-tenant path to the same bar as Local, without changing Local behavior.

Six problems are in scope, all verified against source:

1. **The Cloud admin surface is unreachable.** Cloud never populates `user.role` (`web/lib/auth/session.tsx:50-55`), yet every admin screen is gated on `role === "admin"` (`web/components/dashboard-sidebar.tsx:51-55`, `web/components/clinic-settings.tsx:1133`, `web/app/cnam-nomenclature/page.tsx:21`, and the same guard in `medications/page.tsx` / `dental-acts/page.tsx`). Result: in the default build a dentist cannot see or use SMS/WhatsApp reminder settings, the CNAM nomenclature catalog, the medication catalog, or the dental-act catalog — features that are fully built and wired but locked behind a role that is never set.
2. **Cloud `/hangfire` is open to anyone.** `HangfireAuthorizationFilter.Authorize` returns `true` in Cloud (`api/.../Program.cs:533`) and `/hangfire` is mapped unconditionally (`Program.cs:421`). Anyone reaching the host can inspect job payloads (PII) and trigger/delete jobs.
3. **Live secrets are committed** to tracked `appsettings.json`: Google `ClientSecret` (`:111`), Google `RefreshToken` (`:113`), HuggingFace `ApiKey` (`:117`), DB password (`:41`). Repo access = credential compromise. (Reminder/Meta secrets are already externalized to env — this feature makes these four follow the same pattern.)
4. **One Google Calendar is shared by all clinics.** `FileGoogleTokenStore` is a singleton holding a single refresh token in `.local/google-refresh-token`, and `GoogleCalendarService` uses one `CalendarId` (default `"primary"`). Every clinic's appointments sync into the same Google calendar → cross-clinic PHI leakage. The `GET /api/googlecalendar/authorize` endpoint is `[AllowAnonymous]` and overwrites this global token.
5. **Shared reference catalogs are mutable by any clinic admin.** CNAM nomenclature, medications, and dental-act codes are deliberately not clinic-scoped (`api/.../Persistence/ApplicationDbContext.cs:56-66`); their write/deactivate/confirm endpoints are only `[Authorize(AdminOnly)]`. Any one clinic's admin can edit or deactivate entries every other clinic reads — one clinic can corrupt shared nomenclature/pricing globally.
6. **Tenant isolation is fail-open.** The EF global query filter (`ApplicationDbContext.cs:88-103`) covers only ~7 entities and resolves the clinic from the **JWT `clinic_id` claim** (`api/.../CurrentClinicProvider.cs:30`), not the DB, and defaults **open** when no clinic is in scope. All other clinic-scoped entities rely on hand-written per-handler `ClinicId` checks — a wide surface where one missing check is a silent cross-clinic leak.

## What Changes

### Frontend — make the Cloud admin surface reachable (problem 1)
- `web/lib/auth/session.tsx`: the Cloud session provider must resolve and expose `role` (`admin` / `doctor` / `secretary`) for the signed-in user, mirroring how the Local provider already does. The backend already resolves the caller's clinic membership and role server-side (`IClinicContext`); surface that role to the client via the existing membership endpoint (`GET /api/clinics/user-status` — extend its response to include `role` if it does not already) and set it on the session `user`.
- No change to the admin **gates** themselves (`dashboard-sidebar.tsx`, `clinic-settings.tsx`, the three catalog pages) — once `role` is correctly populated, they render as intended in Cloud exactly as they do in Local.

### Backend — lock down `/hangfire` in Cloud (problem 2)
- `Program.cs`: `HangfireAuthorizationFilter.Authorize` must **never** return `true` unconditionally in Cloud. Restrict the dashboard to authenticated administrators (or, matching the Local loopback approach, to loopback only for out-of-band admin access). No anonymous access in any mode.

### Backend — externalize & rotate committed secrets (problem 3)
- Remove the real values for Google `ClientSecret`, Google `RefreshToken`, HuggingFace `ApiKey`, and the DB password from tracked `appsettings.json`; read them from environment variables / user-secrets, following the pattern already used for the reminder/Meta secrets.
- The four exposed credentials must be **rotated** as part of shipping this change (they are compromised by being in git history).
- Startup must fail with a clear message if a required secret is missing in Cloud (consistent with the existing `StartupDiagnostics` style), rather than silently running with an empty key.

### Backend — per-clinic Google Calendar (problem 4)
- Replace the singleton global token with **per-clinic** Google credentials: the token store keys refresh tokens by clinic id, and each clinic has its own target `CalendarId` (stored per clinic rather than the hardcoded `"primary"`).
- `GET /api/googlecalendar/authorize` must associate the resulting token with the **authenticated caller's clinic**, not a global store; it must not be usable anonymously to overwrite another clinic's token. `App→Google` sync (`CreateAppointmentCommand.cs:198` inline dispatch) and the manual `sync-from-google` endpoint both resolve the calling clinic's token/calendar.
- Admin-gate the sync endpoints (`GoogleCalendarController` sync-from-google / sync-appointment) — currently any authenticated staff member can trigger a sync.

### Backend — tenant-safe reference catalogs (problem 5)
- CNAM nomenclature + letter values are national reference data: make them **read-only to clinic admins** and writable only by a super-admin / seed path. A clinic admin can no longer mutate or deactivate entries other clinics read.
- Medications and dental-act codes: choose one tenant-safe model and apply it — either (a) global reference, super-admin-writable only, or (b) clinic-scoped so each clinic owns its own rows. (Recommended: CNAM nomenclature + medications global/read-only; dental-act **fees** are clinic-specific, so scope act pricing per clinic while keeping the national DCH codes shared read-only.) The exact split is confirmed at implementation time; the required outcome is that **no clinic admin can change data another clinic reads.**

### Backend — fail-closed tenant isolation (problem 6)
- Extend the EF global query filter (`ApplicationDbContext.cs:88-103`) to cover **all** clinic-scoped entities, not just the current ~7 — at minimum: StockItem, MedicalDocument, DentalRecord (+ acts/teeth), PatientFile / PatientFolder, ToothState, PatientMedicalHistory, PatientFamilyHistory, Payment, and any other entity carrying a `ClinicId` or an owning-patient link.
- `CurrentClinicProvider` must resolve the current clinic **from the database** (consistent with `IClinicContext`'s Auth0-`sub`→membership lookup), not by trusting the JWT `clinic_id` claim.
- The resolver must **fail closed**: when no clinic is in scope for a request that touches clinic-scoped data, deny (throw / empty) rather than returning unfiltered rows.

## Acceptance Criteria
- **AC-1 (Cloud admin reachable):** In Cloud mode, an admin user sees and can use the reminder settings screen, the CNAM nomenclature catalog, the medication catalog, and the dental-act catalog; a non-admin does not. `user.role` is populated from the server-resolved membership. Local mode behavior is unchanged.
- **AC-2 (Hangfire closed):** In Cloud, `/hangfire` returns 401/403 (or is loopback-only) for an unauthenticated or non-admin request; the authorization filter never returns `true` unconditionally. Local loopback gating is unchanged.
- **AC-3 (no committed secrets):** Tracked `appsettings.json` contains no real Google client secret, Google refresh token, HuggingFace key, or DB password; all four are read from env/user-secrets. Startup fails loud in Cloud when a required secret is absent. The four credentials have been rotated.
- **AC-4 (per-clinic Google):** Two clinics that each connect Google Calendar sync appointments into **their own** calendar; clinic A never sees clinic B's events. `authorize` binds the token to the authenticated caller's clinic and cannot be driven anonymously to overwrite another clinic's token.
- **AC-5 (sync admin-gated):** The Google sync endpoints require an admin; a non-admin authenticated user gets 403.
- **AC-6 (catalogs tenant-safe):** A clinic admin cannot create, edit, deactivate, or confirm reference-catalog entries that another clinic reads; attempting a global-catalog write as a normal clinic admin is denied. Reads still work for all clinics.
- **AC-7 (fail-closed tenancy):** A request whose clinic cannot be resolved from the DB returns no cross-clinic data (denied/empty), never the full unfiltered set. Every clinic-scoped entity is covered by the global filter — a query for entity type X issued under clinic A never returns clinic B's rows, verified for the previously-uncovered entities (StockItem, MedicalDocument, DentalRecord, PatientFile, ToothState, medical/family history, Payment).
- **AC-8 (clinic resolution source):** The current clinic is derived from the DB membership of the authenticated principal, not solely from the `clinic_id` JWT claim; a forged/edited `clinic_id` claim cannot pivot a user into another clinic.
- **AC-9 (Local unaffected):** All existing Local-mode behavior and the `ControllerAuthorizationCoverageTests` allow-list still pass; changes are additive/gated so Cloud is hardened without regressing Local.

## API Contract
- `GET /api/clinics/user-status` response gains a `role` field (if not already present) so the Cloud client can populate the session role. No breaking change to existing consumers.
- `GET /api/googlecalendar/authorize` and the sync endpoints become clinic-scoped and admin-gated (authorize still initiates OAuth but now binds to the authenticated clinic). Behavior for a correctly-authenticated admin is unchanged; anonymous/other-staff access is removed.
- No other endpoint shapes change.

## Data / Schema Changes
- **Per-clinic Google credentials:** storage for a Google refresh token **per clinic** (keyed by clinic id) plus a per-clinic target `CalendarId`. May be a new column on `Clinic` (e.g. `GoogleCalendarId`) + a per-clinic token record, replacing the single `.local/google-refresh-token` file / singleton store.
- **Catalog scoping:** if dental-act fees (or medications) become clinic-scoped, add the `ClinicId` FK to those tables and a migration; if kept global with a super-admin boundary, no schema change beyond an authorization role. Decided at implementation.
- No change to patient/appointment/billing schemas.

## Out of Scope
- Any new clinical feature (recall, recurring appointments, scheduling depth) — see `features/clinical-workflow-depth`.
- French localization, branding, and dead-code cleanup — see `features/french-localization-and-cleanup`.
- Introducing a full super-admin console/UI: this feature only needs a super-admin **boundary** (role/seed path) for global catalogs, not a management UI.
- Re-enabling the disabled Google→App recurring sync job (that scaffolding is handled in the cleanup feature); this feature only makes the existing manual sync path per-clinic and admin-gated.
- Secret-scanning / git-history scrubbing tooling (rotation is required; history rewrite is optional and separate).

## Edge Cases (Critical only)
- **Cloud role for multi-membership / no membership:** a Cloud user with no clinic membership must get a safe, non-admin session (no crash, no accidental admin); the existing setup/join flow must still work when `role` is absent pre-membership.
- **Google token migration:** an existing single global token must not silently become one clinic's token for everyone — on upgrade, existing sync must be re-authorized per clinic (or the global token retired) so no clinic inherits another's calendar.
- **Fail-closed vs anonymous endpoints:** the fail-closed clinic resolver must not break the legitimately anonymous endpoints (`/api/connectivity`, `/api/auth/*`, Google OAuth callback, `setup`) — those must remain reachable; only clinic-scoped data access fails closed.
- **Catalog reads during scoping change:** existing invoices/treatment plans/prescriptions reference catalog rows by id; making catalogs read-only or per-clinic must not orphan historical references (existing `DentalActCodeId` on invoice lines must still resolve).
- **JWT-claim removal:** switching clinic resolution off the `clinic_id` claim must not regress the existing global-filter entities that currently rely on it; verify Patient/Appointment/Invoice/TreatmentPlan queries still scope correctly under the DB-resolved clinic.
