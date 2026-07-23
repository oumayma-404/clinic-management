# Progress: Cloud Multi-Tenant Security & Reachability Hardening

**Started:** 2026-07-23
**Type:** Small
**Branch:** features/cloud-security-and-tenant-isolation

## Status
- [x] Implementation — Group A (role-in-Cloud, Hangfire lockdown, secret externalization)
- [~] Implementation — Group B:
  - #4 per-clinic Google: **DONE** — schema + migration + full behaviour (per-clinic connection resolution in the calendar service & sync service, retired the global `IGoogleTokenStore`/`FileGoogleTokenStore`, authenticated admin `POST /connect` that binds the OAuth state to the caller's clinic, callback saves the token to that clinic, per-clinic `status`, admin-gated sync, FE `connect()`). Solution build clean.
  - #6 tenancy: **DONE** — audit complete (no strict leaks) + all 6 RELIES-ON-FILTER handlers hardened onto the authoritative DB-resolved clinic check. Build clean.
  - #5 per-clinic catalogs: **DONE** — model = "same seed on every clinic, admin edits private per clinic":
    `ClinicId` added to CnamNomenclatureEntry/CnamLetterValue/Medication/DentalActCode (+ configs: per-clinic
    composite unique indexes + Clinic FK), 4 DbContext query filters, Create handlers stamp the resolved clinic
    (uniqueness auto-scopes), a runtime `IClinicCatalogSeeder`/`ClinicCatalogSeeder` clones the shared default
    per clinic (deterministic per-clinic ids) — wired into `CreateClinicCommand` (best-effort) + a startup
    backfill in Program.cs (Cloud) and `DeferredStartupService` (Local). Migration `AddPerClinicCatalogs`
    generated + hand-augmented to clear the old global seed rows (reset-to-clean-default; backfill re-seeds).
    Solution builds 0 errors / 0 new warnings.

## FEATURE COMPLETE
All 6 spec items implemented + verified (solution build 0 errors, FE tsc 0 errors). App left DOWN per user
instruction; migrations generated but NOT applied (`database update` not run) — they apply on next startup,
followed by the catalog backfill. Nothing committed (user commits manually). Tests: existing tests updated to
compile/pass; NEW test scenarios (per-clinic isolation, OAuth connect, catalog seeding) are for /test-small-feature.
- [x] Quality checks (Group A + #4 schema): backend `dotnet build` = 0 errors / 0 new warnings; frontend `tsc --noEmit` = 0 errors
- [ ] Tests (handled by /test-small-feature)

## App state
Per user instruction the running app (API PID 63292 + Next 60952/48056) was STOPPED to run `dotnet ef` cleanly, and left DOWN. Docker (Postgres/MinIO) untouched (`migrations add` doesn't touch the DB). `dotnet ef` was confirmed to work with the app down (WDAC only blocks `dotnet test`).

## Migrations generated
- `20260723100311_AddClinicGoogleCalendarConnection` — adds nullable `Clinics.GoogleCalendarId` + `Clinics.GoogleRefreshToken`. Verified Up/Down (additive, safe). NOT yet applied to any DB (`database update` deferred).
- `20260723110500_AddPerClinicCatalogs` — adds `ClinicId` to the 4 catalog tables, drops the old global unique indexes, adds per-clinic composite unique indexes + Clinic FKs (Restrict). Hand-augmented `Up` with `DELETE` of the old global seed rows (reset-to-clean-default; the runtime seeder re-seeds per clinic). NOT yet applied. `DentalActCodeId` on invoice lines / treatment-plan items is a soft Guid (not an FK), so the deletes are safe.

## #5 files changed
- Entities: `CnamNomenclatureEntry.cs`, `CnamLetterValue.cs`, `Medication.cs`, `DentalActCode.cs` (+ `ClinicId` + ctor param).
- Configs: `CnamNomenclatureEntryConfiguration.cs`, `CnamLetterValueConfiguration.cs`, `MedicationConfiguration.cs`, `DentalActCodeConfiguration.cs` (ClinicId + composite unique + FK).
- `ApplicationDbContext.cs` (4 query filters + corrected comments).
- Handlers: `CreateCnamEntryCommand.cs`, `CreateMedicationCommand.cs`, `CreateDentalActCommand.cs` (resolve + stamp clinic).
- Seeder: NEW `Application/Common/Interfaces/IClinicCatalogSeeder.cs` + `Infrastructure/Persistence/ClinicCatalogSeeder.cs`; DI in `Infrastructure/Extensions.cs`.
- Wiring: `CreateClinicCommand.cs` (seed on creation, both paths), `Program.cs` + `DeferredStartupService.cs` (startup backfill).
- Tests updated to compile + assert the new per-clinic contract: `CnamNomenclatureCrudTests.cs`, `CnamVlcTests.cs`, `GetCnamNomenclatureQueryHandlerTests.cs`, `MedicationCrudTests.cs`, `GetMedicationsQueryHandlerTests.cs`, `CreateClinicLocalSetupTests.cs`. (Seed-integrity tests unchanged — the seed *data* is unchanged.)

## #6 tenancy audit result (to apply)
Authoritative pattern = resolve caller clinic from DB (`ICurrentClinicResolver.GetClinicIdAsync` or inline `GetUserId`→`GetByAuth0SubAsync`→`user.ClinicId`) then verify `entity.ClinicId`. NO strict cross-clinic leaks found. 6 handlers only RELY-ON the fail-open Patient filter and should be brought onto the authoritative check (ranked):
1. `Patients/Queries/GetPatientMedicalHistoryQuery.cs` (PHI, no clinic logic)
2. `Patients/Queries/GetPatientFamilyHistoryQuery.cs` (PHI, no clinic logic)
3. `Documents/Commands/CreateMedicalDocumentCommand.cs` (resolver injected; patient gate missing ClinicId compare)
4. `Files/Commands/UploadPatientFileCommand.cs`
5. `Files/Commands/CreatePatientFolderCommand.cs`
6. `Files/Commands/InitializeDefaultFoldersCommand.cs`

## Working tree note (start of session)
Only this feature's files are in play: `features/cloud-security-and-tenant-isolation/`, plus the two
sibling specs authored in the same session (`features/french-localization-and-cleanup/`,
`features/clinical-workflow-depth/`) which are NOT part of this feature's commits.

## Scope split (decided during exploration)
The six spec problems fall into two groups after reading the ACTUAL current source (the api-layer
CLAUDE.md files were stale — they predate the billing/CNAM/treatment-plan/e-invoicing subsystem):

**Group A — implemented this pass (safe, aligned, no architectural fork):**
1. Role-in-Cloud reachability (#1) — FE only.
2. Cloud `/hangfire` lockdown (#2).
3. Secret externalization (#3) — Google ClientSecret + RefreshToken, HuggingFace ApiKey, DB connection string.

**Group B — surfaced to the user for a decision before implementing (see Significant Deviations):**
4. Per-clinic Google Calendar (#4) — schema + migration + behaviour change.
5. Tenant-safe catalogs (#5) — the catalogs are DELIBERATELY global (`ApplicationDbContext.cs:56-66`);
   the aligned fix is a super-admin WRITE boundary, not the spec's per-clinic scoping.
6. Fail-closed tenancy (#6) — the query filter is a DELIBERATE, documented fail-open backstop
   (`ApplicationDbContext.cs:83-103`, `CurrentClinicProvider.cs`); the authoritative guard is the
   per-handler DB-resolved check. Forcing the backstop closed would break the two active Hangfire jobs
   + the reset-admin CLI. Real value = auditing per-handler coverage, not flipping the backstop.

## Files Changed
- `api/ClinicManagement.API/Program.cs` — Hangfire dashboard loopback-only in BOTH modes (was `return true` in Cloud); fail-loud guard when no DB connection string is configured.
- `api/ClinicManagement.API/appsettings.json` — removed the committed Google ClientSecret + RefreshToken, HuggingFace ApiKey, and the DB connection string (now env/Development.json/installer-provided).
- `api/ClinicManagement.API/appsettings.Development.json` — added the local dev DB connection string (docker-compose default creds; non-secret).
- `web/lib/auth/session.tsx` — Cloud session now resolves `role` from `GET /api/clinics/user-status` so role-gated admin UI is reachable in Cloud.
- `api/ClinicManagement.Domain/Entities/Clinic.cs` — per-clinic Google connection fields (`GoogleRefreshToken`, `GoogleCalendarId`) + `SetGoogleCalendarConnection`/`ClearGoogleCalendarConnection` (#4 schema).
- `api/ClinicManagement.Infrastructure/Persistence/Configurations/ClinicConfiguration.cs` — map the two Google columns.
- `api/ClinicManagement.Infrastructure/Migrations/20260723100311_AddClinicGoogleCalendarConnection.*` — generated migration (#4 schema).
- #4 per-clinic Google behaviour: `IGoogleCalendarService` (+ new `GoogleCalendarConnection`), `GoogleCalendarService`, `GoogleCalendarSyncService`, `GoogleCalendarController` (new admin `connect`, per-clinic `callback`/`status`, admin-gated sync), `Infrastructure/Extensions.cs` (retired token-store DI); DELETED `IGoogleTokenStore.cs` + `FileGoogleTokenStore.cs`; tests: deleted `FileGoogleTokenStoreTests.cs`, updated `GoogleCalendarControllerHardeningTests.cs` + `ControllerAuthorizationCoverageTests.cs` allow-list + `UploadPatientFileAtomicityTests.cs`; FE `web/lib/api/google-calendar.ts` (`authorize`→`connect`) + `web/app/appointments/page.tsx`.
  **Back-compat note:** existing installs with a global `.local/google-refresh-token` lose their connection — each clinic must re-connect Google once (expected for the per-clinic move). The Google refresh token is now stored on `Clinic` in plaintext (matches the prior `.local/` file posture); encrypting it at rest is a recommended follow-up.
- #6 tenancy hardening (6 handlers, authoritative `ICurrentClinicResolver` + `patient.ClinicId` verify):
  `Patients/Queries/GetPatientMedicalHistoryQuery.cs`, `Patients/Queries/GetPatientFamilyHistoryQuery.cs`,
  `Documents/Commands/CreateMedicalDocumentCommand.cs`, `Files/Commands/UploadPatientFileCommand.cs`,
  `Files/Commands/CreatePatientFolderCommand.cs`, `Files/Commands/InitializeDefaultFoldersCommand.cs`.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Hangfire filter: dropped the `isLocalMode` ctor param and made both modes loopback-only | The two branches became identical after hardening Cloud to loopback; the param would be an unused field (0-warning policy). Only call site (Program.cs) updated. No test references it. |
| Secret externalization done by blanking values in tracked `appsettings.json` + `//`-comment pointers (mirroring the existing Reminders/Meta pattern) rather than adding a new secret store | The repo already externalizes reminder/Meta secrets this way; Google refresh token already persists via `IGoogleTokenStore`. Internal, no API/behaviour change beyond "value must now be supplied out-of-band". |

## Significant Deviations

**DEV-1 (#4 Google Calendar) — APPROVED: Full per-clinic Google.**
Plan: per-clinic refresh token + per-clinic `CalendarId`; `IGoogleTokenStore` becomes clinic-keyed;
`GoogleCalendarService`/`GoogleCalendarSyncService` resolve the caller's clinic; OAuth `authorize`/`callback`
bind the token to the authenticated caller's clinic; admin-gate the sync endpoints. Schema change
(per-clinic token + calendar id). **Migration DEFERRED** — `dotnet ef` is blocked by WDAC (0x800711C7) on
this machine; entities + EF config are committed and this note flags the migration must be generated in an
unrestricted environment before merge.

**DEV-2 (#5 Reference catalogs) — APPROVED: Scope catalogs per clinic.**
DEVIATES from the codebase's deliberate global-catalog design (`ApplicationDbContext.cs:56-66`, "every clinic
reads the same catalog"); user explicitly chose per-clinic ownership over the recommended super-admin-write
boundary. Plan: add `ClinicId` to CnamNomenclatureEntry, CnamLetterValue, Medication,
MedicationActiveIngredient, DentalActCode; add query filters + per-clinic scoping in repos/handlers/
controllers; readers (ordonnance picker, treatment-plan act picker, CNAM reimbursement) become per-clinic.
**Migration + existing-row backfill DEFERRED** (WDAC + a data decision: existing shared rows must be cloned
per clinic or seeded — confirm at migration time).

**DEV-3 (#6 Tenant isolation) — APPROVED: Audit per-handler coverage (keep fail-open backstop).**
Plan: verify every clinic-scoped handler applies the authoritative DB-resolved clinic check; add any missing
(driven by a read-only coverage audit). The fail-open EF backstop stays as-is (documented; required by the two
active Hangfire jobs + the reset-admin CLI). No schema change.

## Rotation reminder (AC-3)
The four credentials that were committed (Google client secret, Google refresh token, HuggingFace API key,
DB password) are compromised by their presence in git history and MUST be rotated before/at deploy — blanking
the working tree does not undo the git-history exposure.
