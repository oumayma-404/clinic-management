# Implementation Plan: Official Documents — Production-Ready

**Status:** APPROVED
**Challenged:** Yes
**Created:** 2026-07-20
**Spec:** [spec.md](./spec.md) (APPROVED, Challenged: Yes) · **Design:** [design.md](./design.md) (APPROVED) · **Tests:** [test-plan-integration.md](./test-plan-integration.md) (APPROVED)

## Overview

Make `/documents` legally/fiscally correct for a Tunisian dental cabinet by reusing existing pipelines (Invoice, image-upload, `ProcedureType` CRUD template) rather than building parallel systems. Delivered as **one story** (per user decision) organized into six ordered, dependency-respecting implementation parts (A–F). Each part is a vertical increment (DB → service → API → UI). Backend is xUnit+Moq-tested per the integration test plan.

### Locked implementation decisions

| Decision | Choice |
|---|---|
| Cachet/ordre/city snapshot vehicle | **`ContentJson`** (spec FR-3.3 preference; BS1 already flows `doctorCodeProfessionnel` through `Content`; `MedicalDocument.Update()` never touches snapshot columns, and no new columns/migration needed on `MedicalDocument`) |
| VLC storage | **New global `CnamLetterValue` entity/table** (LettreCle key, Value, provisional flag), same no-`ClinicId` pattern as the catalog |
| Doctor cachet/ordre editing | **New single-doctor endpoints** `GET/PUT /api/doctors/me` (own) + admin `PUT /api/doctors/{id}`; own-or-admin in-handler (`user.IsAdmin() || doctor.UserId == userId`); cachet via `[FromForm]`. Bulk `UpdateDoctorsCommand` left untouched |
| Reimbursement estimate | **Domain calculator + read query/endpoint** (age-bracket rule server-authoritative, unit-testable) |
| CNAM catalog scoping | **Global** — add `DbSet` + EF config but **no `HasQueryFilter`** line (mirrors `User`/`Clinic`/`NotificationRead`); writes gated `AdminOnly`, reads plain `[Authorize]` |
| Language | French for all new/edited surfaces |

## Files to Modify/Create

### Backend — new
- `Domain/Entities/CnamNomenclatureEntry.cs` (global `AggregateRoot<Guid>`, no `ClinicId`; `CodeActe`, `DesignationFr`, `LettreCle`, `Coefficient`, `Category`, `IsActive`, `IsProvisional`; `Update`/`Deactivate`/`Confirm`)
- `Domain/Entities/CnamLetterValue.cs` (global; `LettreCle`, `Value` decimal, `IsProvisional`; `SetValue`/`Confirm`)
- `Domain/Repositories/ICnamCatalogRepository.cs` (+ VLC methods, or a sibling `ICnamLetterValueRepository`)
- `Infrastructure/Repositories/CnamCatalogRepository.cs`
- `Infrastructure/Persistence/Configurations/CnamNomenclatureEntryConfiguration.cs` (+ `CnamLetterValueConfiguration.cs`) — unique index on `CodeActe`; auto-discovered
- `Infrastructure/Migrations/<ts>_AddCnamCatalog.cs` — create tables + `migrationBuilder.Sql(...)` seed of the current in-code ~24 entries + VLC values (all `IsProvisional = true`)
- `Application/Features/CnamNomenclature/Commands/` — `CreateCnamEntryCommand`, `UpdateCnamEntryCommand`, `DeactivateCnamEntryCommand`, `ConfirmCnamDataCommand` (clears provisional flags), `UpdateCnamLetterValueCommand` (all **AdminOnly**)
- `Application/Features/CnamNomenclature/Queries/` — `GetCnamLetterValuesQuery`, `GetReimbursementEstimateQuery` (uses the calculator)
- `Domain/Services/` (or `Application/.../`) — `CnamReimbursementCalculator` (coefficient × VLC × age-rate; 70% ages 4–18 inclusive, 60% else; unknown DOB → 60%; missing VLC → omitted)
- `Application/Features/Doctors/` — `GetMyDoctorProfileQuery`, `UpdateMyDoctorProfileCommand` (own-or-admin), `UpdateDoctorProfileCommand` (admin, by id), `GetDoctorCachetQuery` (streams image w/ persisted content type)
- `API/Controllers/DoctorsController.cs` — `GET/PUT /api/doctors/me`, `PUT /api/doctors/{id}` (admin), `GET /api/doctors/{id}/cachet`; `[FromForm]` for cachet upload
- `Application/Features/CnamNomenclature/Commands`/controller split so admin CRUD carries `[Authorize(Policy = AuthorizationPolicies.AdminOnly)]`

### Backend — modified
- `Domain/Entities/Doctor.cs` — add `OrdreNumberCnomdt?`, `CachetStorageKey?`, `CachetContentType?`; `SetOrdreNumber(...)`, `SetCachet(key, contentType)`, `RemoveCachet()`; extend ctor/`Update`
- `Infrastructure/Persistence/Configurations/DoctorConfiguration.cs` + migration `<ts>_AddDoctorCachetAndOrdre.cs`
- `Application/DTOs/CreateClinicRequest.cs` (`DoctorDto`) + `Features/Clinics/Queries/GetUserStatusQuery.cs` — expose ordre number + cachet content type for pre-fill
- `Application/Features/Documents/Commands/CreateMedicalDocumentCommand.cs` — snapshot doctor cachet key + ordre + clinic **city** into `ContentJson` at create (resolve current doctor via `IDoctorRepository.GetByUserIdAsync`); **reject `honoraires`** type (`Result.Failure`)
- `Application/Features/Documents/Commands/UpdateMedicalDocumentCommand.cs` — add the missing `"bulletin-cnam" => "bulletin-de-soins-cnam"` filename arm (FR-6.2); reject `honoraires`. Extract the duplicated `GetDocumentTypeName` into one shared helper
- `Application/Common/Models/MedicalDocumentPdfData.cs` — add `ClinicCity`, `DoctorOrdreNumber`, `DoctorCachetKey`, `DoctorCachetContentType` (populated from `ContentJson`/snapshots)
- `Infrastructure/Services/PdfGenerationService.cs` — certificat block (objet/motif + optional repos sentence + mandatory mention + **CNOMDT** label + cachet image w/ fallback); liaison block (external recipient + guided fields, omit empties); **remove honoraires** case (+ its `€`); replace hardcoded **"Paris"** with `ClinicCity` and force `fr-FR` culture; render cachet in `ComposeSignature` (fetch blob via `IFileStorage`, fallback to plain line)
- `API/BackgroundJobs/PdfGenerationJob.cs` — populate the new `MedicalDocumentPdfData` fields from persisted snapshot/`ContentJson` (unauth path)
- `Infrastructure/Extensions.cs` — register `ICnamCatalogRepository`, the calculator; retire/ repoint the `ICnamNomenclatureProvider` singleton; register `IDoctorRepository` already present
- `Infrastructure/Persistence/ApplicationDbContext.cs` — add `DbSet<CnamNomenclatureEntry>` + `DbSet<CnamLetterValue>`; **do not** add query filters for them
- `Application/Features/CnamNomenclature/Queries/GetCnamNomenclatureQuery.cs` — read from the repo instead of the in-code provider
- `Application/DTOs/CnamNomenclatureEntryDto.cs` — add `Id`, `IsActive`, `IsProvisional`

### Frontend — new
- `web/app/cnam-nomenclature/page.tsx` (admin-gated page + role guard) + `web/components/cnam-nomenclature-table.tsx`, `cnam-entry-form-modal.tsx`, `cnam-letter-values-card.tsx`
- `web/app/mon-profil/page.tsx` + `web/components/mon-profil-content.tsx` (ordre input + cachet upload)
- `web/lib/api/doctors.ts` (me/get/update/cachet upload via `apiPutFormData`)

### Frontend — modified
- `web/lib/api/cnam-nomenclature.ts` — add write methods (create/update/deactivate/confirm, VLC update) + move reimbursement to a backend call; `web/lib/api/types.ts` — extend `CnamNomenclatureEntryDto` (+ `CnamLetterValueDto`, `DoctorProfileDto`)
- `web/lib/realtime/clinic-hub.ts` — add `RealtimeResource.CnamNomenclature = "cnamnomenclature"` (must match backend area folder lowercased)
- `web/components/dashboard-sidebar.tsx` — add CNAM nav entry (admin-gated) + "Mon profil" entry
- `web/components/document-editor-content.tsx` — certificat objet/motif + collapsible repos (reconcile `handleSave` ↔ `buildDocumentData` so all fields persist to `ContentJson`); liaison external recipient + guided fields; remove honoraires type; make preview **non-editable** (drop `contentEditable` + liaison write-back); consume DB catalog + backend reimbursement in the bulletin editor; pre-fill ordre from current doctor
- `web/app/documents/page.tsx` — honoraires card → patient-picker → `InvoiceFormModal`
- `web/components/clinic-settings.tsx` — add ordre + cachet fields to each doctor card (admin-manage-others path)

## Implementation Story

### US-1: Documents legally & fiscally correct end-to-end

**As** a Tunisian dental cabinet, **I want** compliant honoraires, valid certificats/liaisons with my cachet, and a trustworthy admin-managed CNAM catalog, **so that** every document I issue is legally/fiscally sound.

Delivered in six ordered parts (implement in order; C precedes D/E; F1 precedes F2):

**Part A — Honoraires → invoice + correctness quick wins (FR-1, FR-6.2)**
- Documents landing: honoraires card opens a patient-picker dialog (reuse the Popover+Command combobox). After a patient is chosen, compute their **not-yet-invoiced** dental records **client-side** — `dentalRecordsApi.list(patientId)` + `invoicesApi.list()` for that patient, excluding any record whose id appears as a non-cancelled invoice's `dentalRecordId` (mirror the `invoicedDentalRecordIds` computation in `web/app/patients/[id]/page.tsx`). Map the remaining records to `presetLines` (`designation` = procedure, `unitPriceHt` = cost), then open `InvoiceFormModal` with `presetPatientId` + `presetLines` (draft; no auto-issue). No new backend endpoint.
- Remove the `honoraires` document type across the editor (formFields, `ProcedureItem`, form/preview/Word branches, `getDocumentTitle`) and the PDF service; reject `honoraires` in Create/Update document commands.
- Fix the `bulletin-cnam` filename on the Update path; extract shared `GetDocumentTypeName`.
- **Value:** users get the compliant TND invoice; no new euro notes; legacy notes still viewable.

**Part B — Per-doctor cachet & CNOMDT ordre (FR-3.1, FR-2.5)**
- `Doctor` fields + migration; `GET/PUT /api/doctors/me` + admin `PUT /api/doctors/{id}` + cachet read endpoint (own-or-admin; content type persisted; deterministic key `{clinicId}/doctors/{doctorId}/cachet`; PatientFile-style orphan cleanup).
- FE "Mon profil" page (ordre + cachet upload w/ preview/remove) + nav entry; admin fields added to Settings → Médecins cards.
- **Value:** a practitioner sets their ordre number + cachet (foundation for signatures).

**Part C — Doc snapshot + localization plumbing (FR-3.3, FR-6.1)**
- Snapshot doctor cachet key + ordre + clinic city into `ContentJson` at document create; add fields to `MedicalDocumentPdfData`; populate in both producers (frontend-download command + `PdfGenerationJob`).
- Replace "Paris" with clinic city; force `fr-FR`; render cachet in the signature area with plain-line fallback.
- Make the preview card non-editable (shared spans; FR-6.3).
- **Value:** all generated docs show the right city, TND, and the practitioner cachet.

**Part D — Certificat correctness (FR-2)**
- FE: objet/motif primary field + collapsible "Repos médical (optionnel)"; reconcile `handleSave`/`buildDocumentData`/load/reset so objet/motif + ordre + start date + duration all round-trip through `ContentJson`; pre-fill CNOMDT from profile.
- BE render: mandatory mention "Certificat établi à la demande de l'intéressé(e) et remis en main propre."; ordre label "Ordre National des Médecins Dentistes (CNOMDT)".
- **Value:** valid, non-lossy certificat.

**Part E — Structured lettre de liaison (FR-4)**
- FE: external "Confrère destinataire" block (nom* / spécialité / adresse) replacing the clinic-doctor select; guided fields (motif, examen clinique, examen radiologique, actes réalisés, prescriptions); persist structured fields to `ContentJson`; non-editable preview (remove write-back box).
- BE render: recipient block + only-filled guided sections; cachet.
- **Value:** structured liaison to an external confrère.

**Part F — CNAM nomenclature catalog, VLC & reimbursement (FR-5)**
- F1: `CnamNomenclatureEntry` global entity + repo + EF config + migration SQL-seed (provisional); CRUD (AdminOnly) + `ConfirmCnamDataCommand`; repoint GET query to the repo; admin `/cnam-nomenclature` page (banner + table + add/edit dialog) + nav (admin-gated) + role guard + realtime resource + api writes.
- F2: `CnamLetterValue` entity + admin edit (VLC card); `CnamReimbursementCalculator` + `GetReimbursementEstimateQuery` (age brackets); bulletin editor consumes the DB catalog + backend estimate.
- **Value:** trustworthy, admin-verifiable catalog + correct age-based reimbursement.

> **BS1 acts-lookup feed is frontend-driven (verified).** The spec's "feed verified nomenclature into the BS1 acts lookup" needs **no backend BS1/renderer change**: `ICnamNomenclatureProvider` has no server-side consumer besides `GetCnamNomenclatureQuery` (grep-verified), and the BS1 acts are filled in the **frontend** bulletin editor from `cnamNomenclatureApi.list()`. Pointing the frontend at the DB-backed catalog (this part) satisfies it. Retiring the in-code `CnamNomenclatureProvider` **Singleton** (Extensions.cs line 57) therefore only affects `GetCnamNomenclatureQuery` (repointed to the repo) and its two tests — **`GetCnamNomenclatureQueryHandlerTests`** (currently mocks the provider → retarget to the repo) and **`CnamNomenclatureProviderTests`** (asserts the in-code data → retire or repurpose to assert the migration seed).

## Testing Strategy

Per [test-plan-integration.md](./test-plan-integration.md) (xUnit + Moq, handler/domain level). Map: Part B → `DoctorCachetTests`/`DoctorEntityTests`; Part C/D → `CertificatContentTests` + `GenericDocumentRenderTests`; Part E → `LiaisonContentTests`; Part F → `CnamNomenclatureCrudTests`, `CnamVlcTests`, `CnamReimbursementEstimateTests`; Part A → `DocumentTypeAndFilenameTests`. Controller write-authz covered by the existing `ControllerAuthorizationCoverageTests` reflection scan (extend its allow-list). No E2E/Newman (no harness).

## Risk Register

| ID | Risk | Likelihood | Impact | Part | Mitigation |
|----|------|------------|--------|------|------------|
| R-1 | **Single story is large (~50+ files across BE+FE + 2 migrations)** — exceeds a typical one-session `/implement-story` capacity | High | Med | All | Parts A–F are independent vertical increments with explicit ordering; `/implement-story` can land them incrementally, committing per part. If a session can't hold it, split at the part boundary. (User chose one story deliberately.) |
| R-2 | Certificat `handleSave` vs `buildDocumentData` field mismatch causes silent data loss if not fully reconciled | Med | High | D | Single content schema; touch all five spots (formFields init, both builders, load map, resetForm) + integration test `CERT-1` round-trip |
| R-3 | Cachet must render in the unauthenticated `PdfGenerationJob` (no live doctor lookup) | Med | High | C | Snapshot cachet key + ordre into `ContentJson` at create; renderer fetches blob via `IFileStorage`, falls back to plain line if missing (never fails render) |
| R-4 | Cachet content-type bug repeated (logo hardcodes `image/png`) | Med | Med | B | Persist `CachetContentType` on `Doctor`; mirror the PatientFile flow, not the logo flow |
| R-5 | CNAM global entity accidentally clinic-scoped (added to query filter) → cross-clinic breakage / empty catalog | Low | High | F | Deliberately omit the `HasQueryFilter` line; integration test `CNAM-6` asserts two clinics see the same rows |
| R-6 | Migration seed drift / duplicate `CodeActe` | Low | Med | F | Unique index on `CodeActe`; `migrationBuilder.Sql` seed in the create migration; French duplicate error (`CNAM-2`) |
| R-7 | Removing honoraires breaks legacy honoraires documents | Low | Med | A | Reject only *new* honoraires; legacy `MedicalDocument`s remain viewable (already-generated PDFs untouched) — `TYPE-2` |
| R-8 | `RealtimeResource` string mismatch with backend area folder → broadcast not received | Low | Low | F | Value must equal the `Features/CnamNomenclature` folder lowercased (`cnamnomenclature`) |
| R-10 | **Global CNAM edits only live-broadcast to the editing admin's clinic** — `RealtimeBroadcastBehavior` resolves the caller's clinic and broadcasts there, so other clinics don't live-refresh on catalog/VLC edits | Med | Low | F | **Accepted limitation**: global reference data, edited rarely; other clinics pick up changes on next page load/fetch. `RealtimeResource.CnamNomenclature` is kept for the editing clinic's own-session consistency. No cross-clinic broadcast added (avoids complicating the realtime layer) |
| R-9 | Reimbursement estimate accidentally persisted/printed | Low | Med | F2 | Editor-only; never written to `ContentJson`/PDF; labelled "estimation indicative, non contractuelle" |

## Breaking Changes

- The `honoraires` document type no longer creates `MedicalDocument`s (redirected to invoices). Legacy honoraires documents remain viewable; no migration/deletion.
- The liaison recipient model changes from an internal clinic-doctor select to free-text external fields. Legacy liaison docs render from their stored snapshot (backward-compatible).
- The `/api/cnam-nomenclature` GET response gains `id`, `isActive`, `isProvisional` (additive; existing consumers unaffected).

## Migrations

1. `AddDoctorCachetAndOrdre` — 3 nullable columns on `Doctors` (ordre number, cachet key, cachet content type).
2. `AddCnamCatalog` — create `CnamNomenclatureEntries` (unique `CodeActe`) + `CnamLetterValues`; `migrationBuilder.Sql(...)` seeds the current in-code entries + VLC values, all `IsProvisional = true`. No `ClinicId`. Applied automatically at startup (Cloud: `Database.Migrate()`; Local: `DeferredStartupService`).

No `MedicalDocument` schema change (cachet/ordre/city ride in `ContentJson`).
