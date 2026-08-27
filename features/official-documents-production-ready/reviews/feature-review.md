# Feature Review: official-documents-production-ready

**Status:** COMPLETE
**Challenged:** Yes
**Date:** 2026-07-21
**Challenged Date:** 2026-07-21
**Parent Branch:** main (feature lives on `feature/windows-desktop-app`)
**Merge Base / scope parent:** `085ae5b` (commit before Part A) → HEAD `ef6a46c`
**Feature commits:** `af7ea02`..`ef6a46c` (Parts A–F, one story)
**Files Reviewed:** 92 code files (+5441, -625) — excluded lock files, EF `*.Designer.cs` + `ApplicationDbContextModelSnapshot.cs` (generated) and `features/**` docs.

**Review method:** 6 parallel agents adapted to the .NET 8 Clean-Architecture + MediatR `Result<T>` stack (no ROP/Marten): (1) Backend Code Quality & Architecture, (2) Error-handling & CQRS conventions [ROP mandate repointed to `Result<T>`], (3) Business Logic Correctness, (4) Breaking Changes & Regression, (5) Frontend (Next.js 15/React 19/TS), (6) Security / Authorization / Multi-tenancy. Orchestrator pre-verified (and handed to the agents as "do not re-flag"): the 3 migrations match the model snapshot + entity configs; the cachet/ordre/city `ContentJson` snapshot round-trips consistently across all three producers (create command, `PdfGenerationJob`, download controller); reimbursement age-bracket math; CNAM write endpoints all carry `AdminOnly`; own-or-admin cachet auth precedes mutation; the `bulletin-cnam` update-path filename fix (FR-6.2); no new anonymous endpoints (allow-list unchanged).

## Challenge Summary

| Metric | Count |
|--------|-------|
| Original findings | 24 |
| Confirmed | 22 |
| Confirmed (adjusted) | 2 |
| Dismissed (false positive) | 0 |
| Dismissed (pre-existing) | 0 |
| **Final findings** | 24 |

Every finding was verified against the full source (not just the diff). No false positives surfaced — consistent with the orchestrator having pre-verified the highest-risk round-trip/auth/math items and handed agents a "do-not-re-flag" list. Two severities were adjusted **down** (Findings 15 and 18) where the flagged code matches an existing, intentional best-effort pattern in the same codebase; see their Challenge notes.

## Findings

### Finding 1
- **Severity:** Major
- **Category:** Security
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/Controllers/MedicalDocumentsController.cs
- **Line:** 280
- **Anchor:** MedicalDocumentsController.GeneratePdfForDownload
- **Comment:** IDOR via a client-controlled storage key. `documentData` is `[FromBody]`-bound (`MedicalDocumentPdfData`, which exposes `DoctorCachetKey`/`DoctorCachetContentType`). The server snapshot overlay only *replaces* those fields when the caller's own snapshot has a cachet (`if (!string.IsNullOrWhiteSpace(snap.DoctorCachetKey)) { … }`, L280-284). A user who has never set a cachet keeps their attacker-supplied `DoctorCachetKey`; `PdfGenerationService.LoadCachetImageAsync` (`PdfGenerationService.cs:512-531`) then `DownloadAsync`es it and embeds the bytes in the returned PDF. The cachet key is deterministic (`{clinicId}/doctors/{doctorId}/cachet`, `UpdateDoctorProfileCommand.cs:117`), and both GUIDs are exposed to same-clinic users via the doctors list — so a colleague's cachet/signature can be read and embedded (forgery value); path-traversal is blocked by `ResolveWithinBase`, but not cross-doctor access within the store. **Fix:** unconditionally assign all four snapshot fields from the server-resolved snapshot (assign even when null, so an injected value is cleared); do not trust these fields from the client. This also lets the download and background-job paths apply the snapshot through one code path (removes the controller-side orchestration, restoring the thin-controller convention).

### Finding 2
- **Severity:** Major
- **Category:** Security
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Documents/Commands/CreateMedicalDocumentCommand.cs
- **Line:** 330
- **Anchor:** CreateMedicalDocumentCommandHandler.SnapshotPractitionerAndClinicAsync
- **Comment:** Same class of flaw as Finding 1, but **persisted**. `SnapshotPractitionerAndClinicAsync` (L304-347) parses the client's `originalContentJson` into a `JsonObject` and only *sets* the reserved snapshot keys when the server has a value (L330-338); it never strips a pre-existing client-supplied `doctorCachetKey`/`doctorCachetContentType`/`doctorOrdreNumber`/`clinicCity`. A caller with no cachet can inject `"doctorCachetKey":"{otherClinic}/doctors/{otherDoctor}/cachet"` into `ContentJson`; it survives into the stored document and the **unauthenticated** `PdfGenerationJob` (which rebuilds render data from the stored `ContentJson`) later dereferences it, embedding another doctor's cachet into the stored PDF. **Fix:** remove the four reserved `PractitionerRenderSnapshot.*Key` keys from the incoming JSON object *before* merging, then write only server-resolved values — so those keys can only ever originate server-side.

### Finding 3
- **Severity:** Major
- **Category:** Security
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Doctors/Commands/UpdateDoctorProfileCommand.cs
- **Line:** 118
- **Anchor:** UpdateDoctorProfileCommandHandler.Handle (cachet upload branch)
- **Comment:** Stored-XSS / content-type-confusion via unvalidated cachet upload. `request.CachetContentType` is the raw multipart `Cachet.ContentType` (`DoctorsController.cs:62`); the handler only checks it is non-empty (L110-113), then persists it verbatim and stores the blob (L117-119) with no magic-byte/image validation or allow-list. `DoctorsController.GetCachet` (`DoctorsController.cs:48-55`) serves it back with `File(stream, ContentType)` — inline, at the app origin (same origin as the SPA behind the Local YARP front door), with no `Content-Disposition: attachment` and no `X-Content-Type-Options: nosniff`. An `image/svg+xml` (SVG with embedded `<script>`) or `text/html` "cachet" executes in the app origin when the URL is opened directly. The comment (L116) deliberately touts persisting "the real content type (unlike the clinic-logo path, which hardcodes image/png)", so the trust is intentional. **Fix:** allow-list raster image types (`image/png`, `image/jpeg`), reject `image/svg+xml`/`text/html`/everything else (ideally verify leading magic bytes); in `GetCachet` add `X-Content-Type-Options: nosniff` and `Content-Disposition: attachment` (or serve as a fixed safe type).

### Finding 4
- **Severity:** Major
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Documents/Commands/UpdateMedicalDocumentCommand.cs
- **Line:** 163
- **Anchor:** UpdateMedicalDocumentCommandHandler.Handle (document.Update call)
- **Comment:** The create path enriches `ContentJson` with the cachet/ordre/city snapshot (`SnapshotPractitionerAndClinicAsync`), but the update path stores `request.ContentJson` verbatim (L163-169) with no equivalent merge. The structured certificat/liaison editor rebuilds `ContentJson` from its own form fields (`document-editor-content.tsx:1346-1356`) and does not carry the server-injected keys (`doctorCachetKey`, `clinicCity`, `doctorOrdreNumber`), so a user **edit** of a document drops the creation snapshot. The re-rendered document (via the background `PdfGenerationJob`, which reads the now-stripped stored `ContentJson`) loses the cachet + cabinet city, and the CNOMDT ordre falls back to the legacy typed `doctorOrderNumber` key only — breaking FR-2.2 ("survive regeneration") / FR-3.3. (The immediate-download path masks this because it re-overlays from the live caller snapshot; only the stored copy + background regen are affected.) **Fix:** re-apply the same snapshot merge in the update handler before `document.Update`.

### Finding 5
- **Severity:** Major
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** web/components/documents/honoraires-launcher.tsx
- **Line:** 97
- **Anchor:** HonorairesLauncher.handleContinue
- **Comment:** Duplicate-billing path. The draft is seeded from a patient's not-yet-invoiced dental records (L97-99), but the created invoice is not linked back to those records — an invoice carries at most a single `dentalRecordId` (`lib/api/invoices.ts:27`), and the launcher passes none (it only sets `presetLines` with `designation`/`quantity`/`unitPriceHt`, L97-104). The "already invoiced" guard everywhere else (`invoicedDentalRecordIds` in `patients/[id]/page.tsx:230-231` and this launcher's own `invoicedRecordIds` at L92-96) only counts invoices where `inv.dentalRecordId` is set, so a multi-record honoraires invoice leaves every seeded record still flagged "non facturé": the patient page keeps showing "Facturer", and re-running the honoraires flow re-seeds the identical records into a second draft. **Fix:** either associate the seeded records with the created invoice (so the per-record guard catches them) or exclude records already present on any non-cancelled invoice *line*, not just those linked via `dentalRecordId`.

### Finding 6
- **Severity:** Minor
- **Category:** Security
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/CnamNomenclature/Queries/GetReimbursementEstimateQuery.cs
- **Line:** 60
- **Anchor:** GetReimbursementEstimateQueryHandler.Handle (catch)
- **Comment:** Internal exception detail leaked to the client and English message, against the project's French + non-leaking convention. This handler returns `$"Error computing reimbursement estimate: {ex.Message}"` (L60-61); the same raw-`ex.Message` pattern repeats across the new CNAM handlers — `GetCnamNomenclatureQuery` (`"Error retrieving CNAM nomenclature: {ex.Message}"`, L74-75, English), `GetCnamLetterValuesQuery` (`"Error retrieving CNAM letter values: {ex.Message}"`, L48-49, English), and the admin write handlers (`Erreur … : {ex.Message}`, e.g. `CreateCnamEntryCommand.cs:78`). The three read endpoints are reachable by any authenticated user, so DB/EF/Npgsql text (schema, constraint names) can leak. **Fix:** log `ex`, return a generic French message (as `UpdateDoctorProfileCommandHandler` already does). Consolidated across all CNAM read/write handlers.

### Finding 7
- **Severity:** Minor
- **Category:** Security
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Doctors/Commands/UpdateDoctorProfileCommand.cs
- **Line:** 116
- **Anchor:** UpdateDoctorProfileCommandHandler.Handle (cachet upload branch)
- **Comment:** No size cap on the cachet upload → resource exhaustion. `request.CachetStream` is uploaded with no maximum (L108-119), and the blob is later read fully into memory on every document render (`PdfGenerationService.LoadCachetImageAsync`: `CopyToAsync` → `ToArray()`) and embedded into each generated PDF. A large "image" bloats storage, memory, and every subsequent certificat/liaison/prescription render for that doctor. **Fix:** enforce a small max size (a few MB) before `UploadAsync`, rejecting oversized streams with a French error.

### Finding 8
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Documents/Commands/UpdateMedicalDocumentCommand.cs
- **Line:** 80
- **Anchor:** UpdateMedicalDocumentCommandHandler.Handle (honoraires guard region)
- **Comment:** The create handler enforces FR-4.1/4.2's only-required-field for a liaison (rejects a blank `RecipientDoctorName`, `CreateMedicalDocumentCommand.cs:94-99`); the update handler adds the honoraires guard (L80-84) but no matching recipient-name check, so an edit can persist a liaison with an empty recipient. **Fix:** add the same `DocumentType == "liaison" && string.IsNullOrWhiteSpace(RecipientDoctorName)` guard on the update path.

### Finding 9
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Documents/PractitionerRenderSnapshot.cs
- **Line:** 45
- **Anchor:** PractitionerRenderSnapshot.ResolveAsync
- **Comment:** The cachet + ordre are resolved from `GetByUserIdAsync(userId)` (L43-45) — the *caller's* own doctor record — not the practitioner named on the document (`DoctorName`/`DoctorSpecialty` are supplied independently). In a multi-doctor cabinet, if doctor A (or an admin/secretary) creates/downloads a document issued in doctor B's name, the rendered cachet/ordre won't match the stated issuer (FR-3.2/3.3 intend "the issuing practitioner's cachet"). Same root cause in the immediate-download overlay (`GetPractitionerRenderSnapshotQuery` resolves the caller). Correct for the single-doctor case; incorrect when caller ≠ named issuer. **Fix:** resolve the snapshot from the document's issuing doctor rather than the caller (or document the single-doctor assumption).

### Finding 10
- **Severity:** Minor
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Doctors/Queries/GetDoctorCachetQuery.cs
- **Line:** 57
- **Anchor:** GetDoctorCachetQueryHandler.Handle
- **Comment:** The final `_fileStorage.DownloadAsync(doctor.CachetStorageKey, …)` (L57) is unguarded and the handler has no try/catch at all (deviating from the layer's documented "wrap the body in try/catch" convention). A row whose `CachetStorageKey` is set but whose blob is missing/unreadable throws → `ExceptionMiddleware` 500, even though the controller maps this query's failures to 404 (`DoctorsController.cs:54`). The sibling render path (`LoadCachetImageAsync`) already degrades gracefully. **Fix:** wrap the download (or the body) in try/catch and return `Result.Failure("Cachet introuvable.")` so a stale key surfaces as 404.

### Finding 11
- **Severity:** Minor
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Documents/Queries/GetPractitionerRenderSnapshotQuery.cs
- **Line:** 47
- **Anchor:** GetPractitionerRenderSnapshotQueryHandler.Handle
- **Comment:** No try/catch. The only caller (`GeneratePdfForDownload`) documents the snapshot as best-effort and guards on `snapshotResult.IsSuccess` (`MedicalDocumentsController.cs:275`), but that only covers a returned failure — not a thrown exception. Because the `_mediator.Send` sits inside the controller's broad `try { … } catch { return BadRequest(...) }`, any exception from `GetByAuth0SubAsync`/`ResolveAsync` (e.g. transient DB error) aborts the entire PDF download instead of rendering without the overlay, contradicting the stated best-effort contract. **Fix:** wrap the handler body in try/catch → `Result.Failure(...)` so the caller's `IsSuccess` check falls through and the download proceeds.

### Finding 12
- **Severity:** Minor
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/Controllers/CnamNomenclatureController.cs
- **Line:** 83
- **Anchor:** CnamNomenclatureController.UpdateEntry (also DeactivateEntry L92, UpdateLetterValue L111)
- **Comment:** These admin mutations route their "introuvable" failures through the default `HandleFailure(result)` → 400 BadRequest (L83, L92, L111), whereas the project's not-found convention (Notifications handlers; and `DoctorsController` in this same diff, which passes `StatusCodes.Status404NotFound` at L31/L54) maps missing/tenant failures to 404. A genuine not-found here is indistinguishable from a validation error and inconsistent with the sibling controller. **Fix:** pass `StatusCodes.Status404NotFound` for the by-id not-found paths.

### Finding 13
- **Severity:** Minor
- **Category:** Breaking Change
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Services/PdfGenerationService.cs
- **Line:** 314
- **Anchor:** PdfGenerationService.ComposeTitle (honoraires arm) / ComposeContent switch (L440)
- **Comment:** The `honoraires` arm was removed from the `ComposeContent` switch (L440 comment, no `default` arm), but `ComposeTitle` still maps `honoraires` → `"NOTE D'HONORAIRES"` (L314). Create/Update now reject `honoraires`, but the immediate-download path (`GeneratePdfForDownload`) renders whatever `DocumentType` the frontend posts and is **not** gated — and the editor retains honoraires branches (`document-editor-content.tsx:1343-1345` `handleSave`, plus `createNewProceduresIfNeeded` "for honoraires"), so opening/exporting a legacy honoraires document produces a PDF titled "NOTE D'HONORAIRES" with an empty body rather than an error. **Fix:** remove the honoraires editor/export entry point *and* drop the `honoraires` arm from `ComposeTitle` so the type is uniformly gone (or add an explicit rejection in the renderer).

### Finding 14
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/CnamNomenclature/Queries/GetCnamNomenclatureQuery.cs
- **Line:** 40
- **Anchor:** GetCnamNomenclatureQueryHandler.Handle (also GetCnamLetterValuesQuery L34)
- **Comment:** Both read handlers inline-project entities → DTOs (`GetCnamNomenclatureQuery.cs:40-50`, `GetCnamLetterValuesQuery.cs:34-40`), duplicating the `CnamEntryMapper.ToDto(...)` overloads the feature introduces to centralize exactly this mapping (`CnamEntryMapper.cs`, `internal static`, same assembly). The create/update handlers use the mapper (e.g. `CreateCnamEntryCommand.cs:73`); the read paths can now drift from it. **Fix:** call `CnamEntryMapper.ToDto` in both read handlers.

### Finding 15
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Doctors/Commands/UpdateDoctorProfileCommand.cs
- **Line:** 105
- **Anchor:** UpdateDoctorProfileCommandHandler.Handle (RemoveCachet branch)
- **Comment:** `catch { /* best-effort blob cleanup */ }` (L104-105) swallows every exception with no logging. Best-effort cleanup is fine, but logging at Warning/Debug (as `LoadCachetImageAsync` does) would make a persistently failing blob delete diagnosable. **Fix:** catch a scoped exception and log it.
- **Challenge note:** Severity lowered Minor → Suggestion. This silent best-effort blob-cleanup catch matches an existing, intentional pattern in the same codebase — the post-commit replaced-blob delete in `UpdateMedicalDocumentCommand.cs:178-179` and the orphan-cleanup catches in both document create/update handlers are all silent best-effort catches. Adding logging is a reasonable consistency improvement (some paths like `LoadCachetImageAsync` do log), but it is a hygiene suggestion, not a defect, since it conforms to the established local convention.

### Finding 16
- **Severity:** Minor
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/app/cnam-nomenclature/page.tsx
- **Line:** 62
- **Anchor:** CnamNomenclaturePage (refreshKey + remount pattern)
- **Comment:** Each admin write triggers redundant work and state loss: children already `load()` after a save (e.g. `cnam-letter-values-card.tsx:55`), then also call `onChanged` → `refreshKey++` (L38, L56), and the realtime `cnamnomenclature` signal returns to the writer (`useClinicRealtime(..., handleSuccess)` at L41 — `RealtimeBroadcastBehavior` broadcasts clinic-wide with no actor exclusion) → another `handleSuccess`. Because both children are `key={…-${refreshKey}}` (L57, L62), every save fully remounts both (a second fetch on top of `load()`, and any half-typed VLC draft in the sibling is discarded). The remount can unmount a child mid-`load()` (its `useEffect(load, [])` at `cnam-letter-values-card.tsx:40-43` has no mounted/abort guard) → setState after unmount. **Fix:** refetch in place via the callback / realtime signal instead of remounting via `key`.

### Finding 17
- **Severity:** Minor
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/components/document-editor-content.tsx
- **Line:** 381
- **Anchor:** DocumentEditorContent → CNOMDT ordre pre-fill useEffect
- **Comment:** The prefill (L381-386) only fills when `prev.doctorOrderNumber` is empty and only re-runs on `[currentUserDoctor]`. When editing a legacy certificat whose stored `doctorOrderNumber` is empty, the document-load effect sets it to `""` (L529) after `currentUserDoctor` already loaded (prefill already ran and won't re-run), clobbering the profile value. The field is `disabled readOnly` (L1877-1878), so the practitioner cannot correct it, and the body renders `[Numéro]` (L760). **Fix:** re-run the prefill when the loaded document leaves the ordre empty (add a "loaded" flag to the deps, or apply the profile fallback at render time). Related to Finding 4.

### Finding 18
- **Severity:** Suggestion
- **Category:** Frontend
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** web/components/mon-profil-content.tsx
- **Line:** 54
- **Anchor:** MonProfilContent → load useEffect cleanup
- **Comment:** The cleanup (L54-57) revokes only the object URL created for the initially-loaded cachet (`objectUrl`). A URL later created in `handleFile` (L67, stored via `setCachetPreview`) isn't tracked by this closure, so selecting a file then navigating away leaks that blob URL. **Fix:** track the current preview URL in a ref and revoke it in cleanup.
- **Challenge note:** Severity lowered Minor → Suggestion. `handleFile` already revokes the previous preview before creating a new one (L65-68) and `handleRemove` revokes on removal, so at most one object URL can be outstanding at unmount, and the browser reclaims all blob URLs when the page/tab unloads. Real leak, negligible impact — a hygiene suggestion, not a functional bug.

### Finding 19
- **Severity:** Minor
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/components/cnam-letter-values-card.tsx
- **Line:** 111
- **Anchor:** CnamLetterValuesCard → VLC value Input
- **Comment:** The per-row numeric value input (L111-118) has no associated label/`aria-label`; the "Valeur (TND)" column header (L90) is not programmatically tied to the control, so screen readers announce an unlabeled spinbutton. **Fix:** add `aria-label={`Valeur pour ${v.lettreCle}`}` (and to any other editable table cells).

### Finding 20
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Documents/Commands/CreateMedicalDocumentCommand.cs
- **Line:** 86
- **Anchor:** CreateMedicalDocumentCommandHandler.Handle
- **Comment:** Document-type discriminators are raw string literals (`== "honoraires"`, `== "liaison"`) repeated here, in `UpdateMedicalDocumentCommand`, `DocumentFileNaming`, and the `PdfGenerationService` switch — the same magic-string sprawl that a drifted duplicate switch (missing `bulletin-cnam`) previously caused. **Fix:** extract the type tokens into a shared constants class referenced everywhere.

### Finding 21
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/Controllers/CnamNomenclatureController.cs
- **Line:** 77
- **Anchor:** CnamNomenclatureController.UpdateEntry / DeactivateEntry
- **Comment:** These routes use `[HttpPut("{id}")]` / `[HttpDelete("{id}")]` (L77, L87) with no route constraint while the sibling `DoctorsController` (same feature) uses `{id:guid}` (`DoctorsController.cs:41,48`). **Fix:** add `:guid` for consistency and to reject non-GUID ids at routing (404) rather than a model-binding 400.

### Finding 22
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/Controllers/CnamNomenclatureController.cs
- **Line:** 70
- **Anchor:** CnamNomenclatureController.CreateEntry / UpdateEntry / UpdateLetterValue
- **Comment:** These actions bind the MediatR command directly as `[FromBody]` (L70, L79, L107), whereas the rest of the API binds a request model and maps it to the command. Binding the command straight from the body couples the public contract to the internal command and exposes command-only fields (e.g. `Id`, then overwritten from the route). **Fix:** introduce small request DTOs to match the established pattern.

### Finding 23
- **Severity:** Suggestion
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/components/documents/honoraires-launcher.tsx
- **Line:** 61
- **Anchor:** HonorairesLauncher → patients load useEffect
- **Comment:** `patientsApi.list({ limit: 500 })` (L60-61) hard-caps the picker at 500 patients with no truncation indication; a larger clinic silently cannot select some patients. **Fix:** wire `CommandInput` to a server-side search query instead of a fixed page + client filter.

### Finding 24
- **Severity:** Suggestion
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/components/document-editor-content.tsx
- **Line:** 1638
- **Anchor:** DocumentEditorContent → liaison recipient Nom field
- **Comment:** The liaison recipient is labelled `Nom *` (required, L1638) but nothing client-side enforces it — `handleSave` only guards patient selection (L1313-1319), so a liaison can be saved/exported with an empty recipient (backend now rejects create, but the UX shows no validation and the update path does not reject — see Finding 8). **Fix:** block save/export when the recipient name is blank (or drop the `*`). Pairs with Finding 8.

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 5 |
| Minor | 12 |
| Suggestion | 7 |
| **Total** | 24 |
