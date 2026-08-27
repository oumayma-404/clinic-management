# Feature Review: harden-existing-features

**Status:** COMPLETE
**Challenged:** Yes
**Date:** 2026-07-10
**Challenged Date:** 2026-07-10
**Parent Branch:** feature/windows-desktop-app (uncommitted working tree)
**Merge Base:** c9dd835 (working tree vs HEAD)
**Files Reviewed:** 38 modified + ~10 new source files (+451/-79 in modified production code; new provider/migration/tests additional). Excluded from review: `ApplicationDbContextModelSnapshot.cs` and `*.Designer.cs` (generated), `features/**`, `define-small-feature-prompt.md`.
**Review method:** 5 parallel agents (Code Quality/Architecture, Business Logic, Security, Breaking Changes/Regression, Frontend). Every finding below was re-verified against the full source during the challenge step.

## Challenge Summary

| Metric | Count |
|--------|-------|
| Original findings | 14 |
| Confirmed | 10 |
| Confirmed (adjusted) | 2 |
| Dismissed (false positive) | 0 |
| Dismissed (pre-existing) | 2 |
| **Final findings** | 12 |

**Dismissed:**
- **Orig. Finding 12** (token-response logging / raw Google error echo / `ex.ToString()` in `GoogleCalendarController`) — **Dismissed (pre-existing)**. Verified: the `LogDebug` of the token response (L310), the `details = errorContent` (L306), `details = errorDescription` (L328), and `details = ex.ToString()` (L67) are all pre-existing lines untouched by this diff. The diff added only the class `[Authorize]` and the OAuth-`state` validation. The feature's declared GoogleCalendar scope (spec §2 auth gap, §3 OAuth state) does not include log redaction, so this is out of scope for this pass. Worth a future quick-fix, but not a finding against this branch.
- **Orig. Finding 14** (`useClinicAccess` `setState` after await with no unmount/stale guard) — **Dismissed (pre-existing)**. Verified: the missing guard predates this diff (the AC-9 change added the `error` field but the awaited-`setState` shape was already there) and the reviewer itself notes it is benign under React 19. Not introduced by this branch.

## Findings

### Finding 1
- **Severity:** Critical
- **Category:** Security
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Patients/Queries/GetDentalRecordsQuery.cs
- **Line:** 26
- **Anchor:** GetDentalRecordsQueryHandler.Handle
- **Comment:** AC-1 promises a *dental Get* for another clinic's patient reads as "not found", but this handler has **no tenant check at all**. It calls `_dentalRecordRepository.GetByPatientIdAsync(request.PatientId)`, which queries `DentalRecords` directly by `PatientId`. `DentalRecord` is a child entity that is **not** one of the three globally-filtered aggregates (only `Patient`/`Appointment`/`ProcedureType` got `HasQueryFilter`), and the handler never loads/validates the owning `Patient.ClinicId`. `GET /api/patients/{patientId}/dental-records` is `[Authorize]`d but not tenant-scoped, so in a multi-clinic (Cloud) install an authenticated clinic-A user passing a clinic-B patient GUID receives clinic-B's dental records (procedures, costs, clinical notes) — a cross-tenant PHI disclosure with zero defense in either mode. The sibling dental Update/Delete commands got explicit `patient.ClinicId != clinicResult.Value` checks in this pass; this read path was missed. **Fix:** resolve the caller's clinic, load the patient, and return "not found" when the patient is null or belongs to another clinic — same pattern as `UpdateDentalRecordCommand`.
- **Challenge verification:** Confirmed by reading the full handler — it injects only `IDentalRecordRepository`, has no clinic resolver, and returns the DTOs straight from `GetByPatientIdAsync`. No global filter covers `DentalRecord`. Real cross-tenant read; AC-1 explicitly lists the dental Get path.

### Finding 2
- **Severity:** Major
- **Category:** Security / Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/ProcedureTypes/Queries/GetProcedureTypesQuery.cs
- **Line:** 39
- **Anchor:** GetProcedureTypesQueryHandler.Handle (also GetProcedureTypeQuery.cs:31, GetProcedureTypeQueryHandler.Handle)
- **Comment:** The two procedure-type **read** handlers have no explicit tenant check. `ProcedureType` only gained `ClinicId` in this diff, so these reads now depend **entirely** on the new EF global query filter for cross-clinic isolation — unlike `GetPatientQuery`/`GetPatientsQuery`/`GetAppointmentsQuery` (explicit or DB-resolved scoping) and unlike the procedure-type Update/Delete siblings which *did* get explicit `ClinicId` checks in this pass. Because the filter is fail-open (see Finding 3), an authenticated-but-clinicless caller (Cloud: a registered user who has not yet created/joined a clinic → no `clinic_id` claim), or any user if the Cloud tenant never emits the claim, calls `GET /api/procedure-types` and receives **every** clinic's procedure types (names, costs, colours). **Fix:** add an explicit clinic scope to both read handlers (`ICurrentClinicResolver.GetClinicIdAsync` → filter/verify `pt.ClinicId == clinicId`) so isolation does not hinge on the backstop.
- **Challenge verification:** Confirmed — both handlers inject only `IProcedureTypeRepository` + `ILogger` and call `GetActiveAsync`/`GetAllAsync`/`GetByIdAsync` with no `ClinicId` predicate. Isolation depends solely on the fail-open filter (Finding 3).

### Finding 3
- **Severity:** Major
- **Category:** Security / Breaking Change
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Persistence/ApplicationDbContext.cs
- **Line:** 68
- **Anchor:** ApplicationDbContext.OnModelCreating (HasQueryFilter setup) / CurrentClinicProvider.ClinicId
- **Comment:** The global "backstop" filter derives the current clinic from the **JWT claim** (`CurrentClinicProvider` → `IClinicContext.GetClinicId()`), **not** the authoritative DB-resolved `User.ClinicId` that every handler and `CurrentClinicResolver` uses — even though CLAUDE.md states the clinic is resolved "per-request from the DB, not purely from the JWT claim." Two consequences: **(1) Fail-open** — when no `clinic_id` claim is present the filter returns all rows, so the backstop does not backstop the one case it exists for (a handler that forgot its check); this is the root cause of Findings 1–2. **(2) Stale-claim divergence** — if a user's clinic changes in the DB but their still-valid token carries the old `clinic_id`, filtered reads surface the *old* clinic's rows while write handlers use the *new* clinic; conversely a diverging claim can hide the user's own data as a false "not found". The lazy per-query evaluation itself is implemented correctly (no compiled-model caching leak). **Fix:** feed the filter from the same authoritative clinic id handlers use, or fail **closed** when a request is authenticated but has no clinic in scope (distinguish "authenticated principal, no clinic" → no rows, from "no principal / background job" → all rows). At minimum, document the invariant that `clinic_id` claim must always equal `User.ClinicId`.
- **Challenge verification:** Confirmed — `CurrentClinicProvider.ClinicId => _clinicContext.GetClinicId()` (the claim), and the filter is `!IsClinicScoped || ... == ScopedClinicId` where `IsClinicScoped` is false whenever the claim is absent → all rows. Fail-open behavior and claim/DB divergence are real.

### Finding 4
- **Severity:** Major
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/lib/hooks/use-clinic-access.ts
- **Line:** 78
- **Anchor:** useClinicAccess (catch block) — consumed by ClinicGuard
- **Comment:** The AC-9 fix removed the `/setup` redirect on transient failure and added an `error` state + `refresh`, but the only rendering consumer never reads them. `ClinicGuard` (web/components/clinic-guard.tsx:25) destructures only `{ hasAccess, isLoading }`; on a transient `ApiError` (network 0 / >=500) the hook returns `hasAccess:false, error:"..."`, and ClinicGuard treats that identically to a legitimate "not a member", rendering `<UnauthorizedPage/>` ("Access Restricted"). So an authenticated member hitting a blip is told they have no clinic, with no retry — recoverable only by full reload. The stated goal ("surface an error/retry state and keep the user in place") is not met; the redirect was removed but the misleading unauthorized screen remains. **Fix:** have ClinicGuard read `error`/`refresh` and, when `error` is set (vs. `hasAccess===false && error===null`), render a distinct "connexion au serveur impossible" state with a Réessayer button calling `refresh()`.
- **Challenge verification:** Confirmed — `clinic-guard.tsx:25` destructures only `{ hasAccess, isLoading: clinicLoading }` and falls through to `return <UnauthorizedPage />` for any `!hasAccess`. The hook's new `error`/`refresh` are unused by the sole consumer. Note: the literal AC-9 ("no redirect to /setup") does pass (ClinicGuard already calls `useClinicAccess(false)` and the catch no longer redirects), but the fix's broader intent is unmet — hence still a valid Major UX defect.

### Finding 5
- **Severity:** Minor
- **Category:** Code Quality / Efficiency
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Documents/Queries/GetMedicalDocumentsQuery.cs
- **Line:** 53
- **Anchor:** GetMedicalDocumentsQueryHandler.Handle
- **Comment:** The no-arg branch calls `_documentRepository.GetAllAsync(...)` (every clinic's documents, `.Include(d => d.Patient)`) and then filters `d.Patient.ClinicId == clinicId` **in memory**. This materializes all other tenants' rows before discarding them — inefficient and it pulls cross-tenant PHI into process memory. **Fix:** use a clinic-scoped repository query (e.g. a `GetByClinicIdAsync`, mirroring the existing pattern) so the filter runs in SQL.
- **Challenge verification:** Confirmed — L51 `GetAllAsync` then L56 `documents.Where(d => d.Patient != null && d.Patient.ClinicId == clinicId)` is a LINQ-to-objects in-memory filter. (Correctness is fine; efficiency/PHI-in-memory concern is real.)

### Finding 6
- **Severity:** Minor
- **Category:** Security
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/Controllers/GoogleCalendarController.cs
- **Line:** 211
- **Anchor:** GoogleCalendarController.Authorize / Callback
- **Comment:** The OAuth `state` is stored in a process-global `IMemoryCache` and is **not bound to the initiating user/browser**. Since both endpoints are `[AllowAnonymous]` (necessarily), the check proves only "this server issued some state recently", not "this user started this flow". An attacker can call `authorize` to mint a valid state, then lure an admin to `…/callback?code=<attacker_code>&state=<that_state>`; validation passes and the install's single Google refresh token (`IGoogleTokenStore`) is overwritten with the attacker's account (OAuth login-CSRF). The added check is still a real improvement (blocks blind/replayed states). **Fix:** double-submit — on `authorize` also set `state` in a short-lived HttpOnly cookie and require `callback` to match cookie == query. Also prefer `RandomNumberGenerator` over `Guid.NewGuid()` for the token (Guid is not a guaranteed CSPRNG).
- **Challenge verification:** Confirmed — `state = Guid.NewGuid().ToString()` (L211), cached as a global bool keyed by the state value (L212), and the callback only checks `_cache.TryGetValue` (L260) with no per-user/browser binding. Residual login-CSRF is real; the check is still a net improvement over none.

### Finding 7
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Documents/Commands/DeleteMedicalDocumentCommand.cs
- **Line:** 71
- **Anchor:** DeleteMedicalDocumentCommandHandler.Handle
- **Comment:** `try { await _fileStorage.DeleteAsync(...); } catch { }` swallows **every** exception (including `OperationCanceledException`) with no logging, so orphaned-blob cleanup failures are invisible — undermining the "a leaked blob is preferable to failing" rationale, since no one can find the leak. **Fix:** inject `ILogger` (the handler currently has none) and log the swallowed failure; consider not catching cancellation.
- **Challenge verification:** Confirmed — L77-78 `try { ... } catch { /* best-effort ... */ }` and the handler injects no `ILogger`. Silent swallow is real.

### Finding 8
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Services/GoogleCalendarSyncService.cs
- **Line:** 356
- **Anchor:** GoogleCalendarSyncService.ExtractNotesFromDescription
- **Comment:** The parser hardcodes `"Notes: "` / `"\nStatus: "`, duplicating the labels emitted by `BuildAppointmentDescription` (`$"Notes: {...}"`, `$"Status: {...}"`). The reader and writer are now coupled across two methods with no shared token; changing a label in the writer silently breaks the reader (returns null or a wrong slice), regressing AC-6. **Fix:** extract the field-label tokens to shared `const` strings used by both methods.
- **Challenge verification:** Confirmed — `ExtractNotesFromDescription` uses `const string marker = "Notes: "` (L358) and `"\nStatus: "` (L366); `BuildAppointmentDescription` emits `$"Notes: {...}"` (L329) and `$"Status: {...}"` (L332). No shared token — a real reader/writer coupling that can regress AC-6.

### Finding 9
- **Severity:** Minor
- **Category:** Breaking Change
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Migrations/20260710163519_AddProcedureTypeClinicId.cs
- **Line:** 52
- **Anchor:** AddProcedureTypeClinicId.Down()
- **Comment:** `Down()` recreates the old `UNIQUE(Name)` index. The forward schema (composite `UNIQUE(ClinicId, Name)`) deliberately allows two clinics to reuse the same name. If any such cross-clinic duplicate names exist at rollback time, `CreateIndex(unique: Name)` throws a duplicate-key error and `Down()` fails partway (column dropped, index not recreated), leaving an inconsistent schema. Rollback-only, low likelihood. **Fix:** dedupe or guard before recreating the unique index in `Down()`, or document that rollback requires no cross-clinic name collisions.
- **Challenge verification:** Confirmed — `Down()` drops the column (L48) then `CreateIndex(... column: "Name", unique: true)` (L52-56). A cross-clinic duplicate name (allowed by the forward composite index) makes the rollback throw. Real latent rollback bug.

### Finding 10
- **Severity:** Suggestion
- **Category:** Code Quality / Maintainability
- **Verdict:** Confirmed (adjusted — was Suggestion, retained)
- **File:** api/ClinicManagement.Application/Features/Documents/Queries/GetMedicalDocumentQuery.cs
- **Line:** 40
- **Anchor:** GetMedicalDocumentQueryHandler.Handle
- **Comment:** Three different clinic-resolution styles coexist within this one feature: `GetMedicalDocumentQuery`/`UpdateMedicalDocumentCommand` hand-roll `IClinicContext.GetUserId()` + `IUserRepository.GetByAuth0SubAsync(...)`; `GetMedicalDocumentsQuery` uses `ICurrentClinicResolver`; the EF filter uses `ICurrentClinicProvider`. The DEV-1 "skip when no clinic in scope" need is legitimate but is better served by a single resolver variant returning `Guid?` (null when unauthenticated) than by duplicating the user lookup per handler. Consolidating would also remove the divergence behind Finding 3. **Fix (optional):** unify on one resolver abstraction.
- **Challenge verification:** Confirmed — `GetMedicalDocumentQuery` injects `IClinicContext` + `IUserRepository` and hand-rolls the lookup (L43-51), while `GetMedicalDocumentsQuery`/`DeleteMedicalDocumentCommand` use `ICurrentClinicResolver` and the filter uses `ICurrentClinicProvider`. Three abstractions for the same concept — a real consolidation opportunity (kept at Suggestion).

### Finding 11
- **Severity:** Suggestion
- **Category:** Business Logic
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Patients/Commands/UpdatePatientCommand.cs
- **Line:** 119
- **Anchor:** UpdatePatientCommandHandler.Handle (insurance else branch)
- **Comment:** The new `else { patient.UpdateInsuranceInfo(null); }` makes an omitted/null `InsuranceInfo` **clear** stored insurance. Correct for the current sole caller (the edit dialog always sends the full form / `undefined`), so AC-8 passes today. But insurance becomes the **only** field in this otherwise partial-update command (name/email/address/medicalHistory/allergies are all "apply only if provided") where absence means "clear". Any future partial-update caller (a PATCH-style API call, the AI action layer, a single-field update) that omits `insuranceInfo` while changing one other field will silently wipe insurance. **Fix (optional):** distinguish "omitted" from "explicit clear" in the DTO (sentinel/flag), or document that this command requires full insurance state on every call.
- **Challenge note:** Severity lowered Minor → Suggestion. The behavior is **spec-mandated** — spec §4 ("Insurance cannot be cleared: treat a null/omitted `InsuranceInfo` as clear — call `patient.UpdateInsuranceInfo(null)`") and AC-8 explicitly require it, and the sole caller sends full state. The concern is a real forward-looking footgun for a hypothetical future partial-update caller, not a defect in the delivered behavior; downstream apply-review-fixes should treat it as a documentation/design note, **not** revert the §4 behavior.
- **Challenge verification:** Confirmed the code (L119-122 clears on null) and that it matches spec §4 + AC-8; the asymmetry vs. the other partial-update fields is real.

### Finding 12
- **Severity:** Suggestion
- **Category:** Breaking Change
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Infrastructure/Migrations/20260710163519_AddProcedureTypeClinicId.cs
- **Line:** 28
- **Anchor:** AddProcedureTypeClinicId.Up()
- **Comment:** The backfill assigns **every** previously-global procedure type to the single earliest clinic. Combined with the new `ProcedureType` query filter + the Create/Update appointment `ClinicId` checks, existing **multi-clinic (Cloud)** installs regress: clinics B/C… get empty procedure-type lists, their existing appointment procedure IDs read as "Procedure type not found", and `.Include(a => a.ProcedureType)` returns null navigations. **Mitigation if Cloud matters:** clone each global procedure type per clinic instead of collapsing to one.
- **Challenge note:** Severity lowered Minor → Suggestion. This is an **explicitly accepted, documented spec decision** — spec "Edge Cases" states "backfilling all to the earliest clinic is a lossy but documented default; operators reassign manually. (Local single-clinic installs are unaffected.)" and the spec's Data/Schema section documents the backfill. The feature targets single-clinic Local installs (branch `feature/windows-desktop-app`). Verified intended and documented; flagged only so the Cloud impact is on record. **Do not "fix"** in apply-review-fixes — reopen only if multi-clinic Cloud becomes an explicit target.
- **Challenge verification:** Confirmed the backfill SQL (L28-32: earliest clinic by `CreatedAt`, guarded by `EXISTS(Clinics)`), and confirmed the spec Edge Cases + Data/Schema sections accept it. Intended/documented.

## Review Summary (post-challenge)

| Severity | Count |
|----------|-------|
| Critical | 1 |
| Major | 3 |
| Minor | 5 |
| Suggestion | 3 |
| **Total** | 12 |

**Verified sound (no finding):** DI wiring is complete (`AddMemoryCache`, `IPatientFileRepository`, `IFileStorage`, `IUserRepository`, `ICurrentClinicProvider` all registered; `GoogleCalendarController`'s new `IMemoryCache` resolves); `ProcedureType` ctor change has no un-updated non-test caller/seeder; migration `Up()` ordering is safe (old index dropped first, non-null column added with `Guid.Empty` default, backfill guarded by `EXISTS(Clinics)`, composite index last); `MedicalDocumentRepository` eager-loads `.Include(d => d.Patient)` so the new `document.Patient` tenant checks are populated (no NRE); tenant checks precede mutations; blob cleanup is correctly post-commit best-effort; the DEV-1 skip branch is unreachable by a real authenticated HTTP request (`[Authorize]` + `sub` always present); the class-level `[Authorize]` correctly closes the Cloud null-fallback gap; removed FE `appointmentsApi.get/delete` and `NotificationsList` have zero remaining usages; all spec-listed `console.log`s removed and `console.error`s preserved; the `/bff/auth/*` fetch path is correct.

**Action needed:** 1 Critical + 3 Major findings remain — consider running `/apply-review-fixes`. Findings 1–4 are the priority (cross-tenant reads + the fail-open backstop + the unconsumed error state).
