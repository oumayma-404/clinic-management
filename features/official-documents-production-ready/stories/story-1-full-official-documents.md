# Story 1 (Full): Official Documents Production-Ready

**Status:** APPROVED
**Layer:** Full
**Depends On:** —
**Plan:** [../plan.md](../plan.md) · **Spec:** [../spec.md](../spec.md) · **Tests:** [../test-plan-integration.md](../test-plan-integration.md)

## Objective

Deliver the whole `official-documents-production-ready` feature as one full-stack story: compliant honoraires (routed to the Invoice pipeline), valid certificats with the mandatory mention + CNOMDT ordre + practitioner cachet, structured lettres de liaison to external confrères, an admin-managed global CNAM nomenclature catalog + VLC + age-based reimbursement, and the localization/correctness fixes. Implemented **part-by-part (A→F)** — each part is a vertical increment that compiles, passes its tests, and can be committed on its own.

_From spec:_ FR-1 (honoraires), FR-2 (certificat), FR-3 (cachet), FR-4 (liaison), FR-5 (CNAM catalog/VLC/reimbursement), FR-6 (localization/correctness).

## Entry Criteria

- `plan.md` APPROVED + Challenged; `test-plan-integration.md` APPROVED; `design.md` APPROVED (5 mockups).
- Backend seams verified (CNAM module plumbing, Doctor/storage, MedicalDocument/PDF, frontend wiring) — see plan "Files to Modify/Create".
- Working tree builds (`dotnet build`, `npm run build`); test project `ClinicManagement.UnitTests` present.

## Global implementation rules

- Backend: Clean Architecture + inline `Result.Failure` validation (no FluentValidation); French user-facing messages; xUnit+Moq tests per the integration test plan; 0-error/0-warning quality policy.
- Cachet/ordre/city ride in `ContentJson` (no `MedicalDocument` migration); snapshot at **create** time (immutable through update / background re-render).
- Commit at each part boundary; update the status tracker + `progress.md` as parts land.

---

## Part A — Honoraires → invoice + correctness quick wins
_Spec: FR-1.1–1.5, FR-6.2 · Tests: TYPE-1, TYPE-2, FILE-1, FILE-2_

**Steps**
1. Documents landing (`web/app/documents/page.tsx`): honoraires card opens a patient-picker dialog (reuse the Popover+Command combobox).
2. On patient select, compute the patient's **not-yet-invoiced** dental records client-side: `dentalRecordsApi.list(patientId)` + `invoicesApi.list()` (exclude records referenced by a non-cancelled invoice's `dentalRecordId`, mirroring `web/app/patients/[id]/page.tsx`'s `invoicedDentalRecordIds`); map to `presetLines`.
3. Open `InvoiceFormModal` with `presetPatientId` + `presetLines` (draft; no auto-issue).
4. Remove the `honoraires` document type from the editor (`document-editor-content.tsx`: `formFields`, `ProcedureItem`, form/preview/Word branches, `getDocumentTitle`) and from `PdfGenerationService` (delete the honoraires QuestPDF case incl. its `€`).
5. Reject `honoraires` in `CreateMedicalDocumentCommand`/`UpdateMedicalDocumentCommand` (`Result.Failure`, French message).
6. Fix the missing `"bulletin-cnam" => "bulletin-de-soins-cnam"` arm in `UpdateMedicalDocumentCommand.GetDocumentTypeName`; extract the duplicated `GetDocumentTypeName` into one shared helper.

**Files:** `web/app/documents/page.tsx`, `web/components/document-editor-content.tsx`, patient-picker (reuse), `CreateMedicalDocumentCommand.cs`, `UpdateMedicalDocumentCommand.cs`, `PdfGenerationService.cs`.

**Verification**
- Clicking "Note d'honoraires" → picker → seeded `InvoiceFormModal` draft (TND); creating lands in Factures, no number consumed.
- Creating a `honoraires` `MedicalDocument` fails (`TYPE-1`); legacy honoraires docs still listed (`TYPE-2`).
- Re-saving a `bulletin-cnam` yields `bulletin-de-soins-cnam-…pdf` (`FILE-1`/`FILE-2`).

---

## Part B — Per-doctor cachet & CNOMDT ordre  (depends on: —)
_Spec: FR-3.1, FR-2.5 · Tests: CACHET-1..5, DOC-1..3_

**Steps**
1. `Doctor` (`Doctor.cs`): add `OrdreNumberCnomdt?`, `CachetStorageKey?`, `CachetContentType?`; add `SetOrdreNumber(...)`, `SetCachet(key, contentType)`, `RemoveCachet()`; extend ctor/`Update`. Map columns in `DoctorConfiguration.cs`; migration `AddDoctorCachetAndOrdre`.
2. New single-doctor endpoints (`DoctorsController.cs`): `GET/PUT /api/doctors/me` (own), admin `PUT /api/doctors/{id}`, `GET /api/doctors/{id}/cachet` (streams w/ persisted content type). Cachet upload via `[FromForm]`.
3. Handlers (`Features/Doctors/`): own-or-admin (`user.IsAdmin() || doctor.UserId == userId`); deterministic key `{clinicId}/doctors/{doctorId}/cachet`; **persist content type**; PatientFile-style store-then-orphan-cleanup.
4. Extend `DoctorDto` + `GetUserStatusQuery` projection with ordre number + cachet content type (pre-fill).
5. FE: `web/app/mon-profil/page.tsx` + `mon-profil-content.tsx` (ordre input + cachet upload w/ preview/remove) + `web/lib/api/doctors.ts` + nav entry (`dashboard-sidebar.tsx`); add ordre + cachet fields to each doctor card in `clinic-settings.tsx` (admin-manage-others).

**Files:** `Doctor.cs`, `DoctorConfiguration.cs`, migration, `Features/Doctors/*`, `DoctorsController.cs`, `DoctorDto`/`CreateClinicRequest.cs`, `GetUserStatusQuery.cs`, `web/app/mon-profil/page.tsx`, `web/components/mon-profil-content.tsx`, `web/lib/api/doctors.ts`, `dashboard-sidebar.tsx`, `clinic-settings.tsx`.

**Verification**
- Admin sets any doctor's ordre + cachet; a doctor sets their own; a non-admin cannot set another's (`CACHET-1..3`).
- Cachet persists the real content type (png/jpeg), remove clears both (`CACHET-4/5`, `DOC-1..3`).

---

## Part C — Doc snapshot + localization plumbing  (depends on: B)
_Spec: FR-3.2, FR-3.3, FR-6.1, FR-6.3 · Tests: CERT-5, REND-3, REND-4, REND-5, REND-6_

**Steps**
1. `CreateMedicalDocumentCommand`: resolve the current doctor (`IDoctorRepository.GetByUserIdAsync`) and snapshot cachet key + ordre + clinic **city** into `ContentJson` at create.
2. `MedicalDocumentPdfData`: add `ClinicCity`, `DoctorOrdreNumber`, `DoctorCachetKey`, `DoctorCachetContentType`; populate in **both** producers (frontend-download command path + `PdfGenerationJob`, reading persisted `ContentJson`/snapshots).
3. `PdfGenerationService`: replace hardcoded `"Paris"` with `ClinicCity` + force `fr-FR` culture; render cachet in `ComposeSignature` (fetch blob via `IFileStorage`, fallback to plain line if missing — never fail render).
4. Make the preview card non-editable (`document-editor-content.tsx`: drop `contentEditable`/`suppressContentEditableWarning` on preview spans).

**Files:** `CreateMedicalDocumentCommand.cs`, `MedicalDocumentPdfData.cs`, `PdfGenerationService.cs`, `PdfGenerationJob.cs`, `document-editor-content.tsx`.

**Verification**
- Generated docs show clinic city (never "Paris") + no `€` (`REND-5/6`); cachet renders when present, plain line when absent/missing blob (`REND-3/4`).
- Cachet + ordre are in the document snapshot so the unauthenticated `PdfGenerationJob` renders them without a live lookup (`CERT-5`).

---

## Part D — Certificat correctness  (depends on: C)
_Spec: FR-2.1–2.5 · Tests: CERT-1..4, REND-1, REND-2_

**Steps**
1. FE certificat form: objet/motif primary textarea + collapsible "Repos médical (optionnel)" (start date + duration); pre-fill CNOMDT (disabled, from profile).
2. Reconcile `handleSave` ↔ `buildDocumentData` ↔ load ↔ `resetForm` so objet/motif + ordre + start date + duration all round-trip through `ContentJson` (fixes the save-vs-render mismatch).
3. BE render: mandatory mention "Certificat établi à la demande de l'intéressé(e) et remis en main propre."; ordre label "Ordre National des Médecins Dentistes (CNOMDT)"; render objet/motif + conditional repos sentence.

**Files:** `document-editor-content.tsx`, `PdfGenerationService.cs`.

**Verification**
- Objet/motif + ordre + dates persist and survive re-render (`CERT-1/4`); repos block optional (`CERT-2/3`); mention + CNOMDT label present (`REND-1/2`).

---

## Part E — Structured lettre de liaison  (depends on: C)
_Spec: FR-4.1–4.3 · Tests: LIA-1..5_

**Steps**
1. FE liaison form: external "Confrère destinataire" block (nom* / spécialité / adresse) replacing the clinic-doctor `Select`; guided fields (motif, examen clinique, examen radiologique, actes réalisés, prescriptions); persist structured fields to `ContentJson`; remove the write-back preview box (non-editable).
2. BE render (`PdfGenerationService` liaison block): recipient block + only-filled guided sections (omit empties); render cachet.

**Files:** `document-editor-content.tsx`, `PdfGenerationService.cs`, `MedicalDocumentPdfData.cs` (recipient already present).

**Verification**
- External recipient (name required); guided fields round-trip; empty sections omitted; legacy internal-recipient liaison still renders (`LIA-1..5`).

---

## Part F — CNAM nomenclature catalog, VLC & reimbursement
_Spec: FR-5.1–5.5 · Tests: CNAM-1..8, VLC-1..3, REIMB-1..5_

**F1 — catalog + admin screen (depends on: —)**
1. `CnamNomenclatureEntry` global `AggregateRoot<Guid>` (no `ClinicId`; `IsActive`, `IsProvisional`; `Update`/`Deactivate`/`Confirm`) + `ICnamCatalogRepository` + `CnamCatalogRepository` + `CnamNomenclatureEntryConfiguration` (unique `CodeActe`); add `DbSet` to `ApplicationDbContext` **without** a `HasQueryFilter`; register repo in `Extensions.cs`.
2. Migration `AddCnamCatalog` — create table(s) + `migrationBuilder.Sql(...)` seed of the current in-code ~24 entries (all `IsProvisional = true`).
3. CRUD commands (AdminOnly) + `ConfirmCnamDataCommand`; repoint `GetCnamNomenclatureQuery` to the repo; **retire the `CnamNomenclatureProvider` Singleton** (Extensions.cs) and update its two tests (`GetCnamNomenclatureQueryHandlerTests`, `CnamNomenclatureProviderTests`); extend `CnamNomenclatureEntryDto` (`Id`, `IsActive`, `IsProvisional`).
4. FE: `web/app/cnam-nomenclature/page.tsx` (banner + table + add/edit dialog) + `cnam-nomenclature-table.tsx`/`cnam-entry-form-modal.tsx`; nav entry (admin-gated) + page role guard; `cnam-nomenclature.ts` write methods; `RealtimeResource.CnamNomenclature = "cnamnomenclature"`.

**F2 — VLC + reimbursement + bulletin consume (depends on: F1)**
5. `CnamLetterValue` global entity + config + seed (provisional) + admin update command; VLC card on the admin screen.
6. `CnamReimbursementCalculator` (coefficient × VLC × rate; 70% ages 4–18 inclusive, 60% else; unknown DOB → 60%; missing VLC → omitted) + `GetReimbursementEstimateQuery` + endpoint.
7. Bulletin editor (`document-editor-content.tsx`) consumes the DB catalog + backend estimate (replaces the frontend flat-0.7 calc). _(BS1 feed is frontend-driven — no backend BS1 change.)_

**Files:** `CnamNomenclatureEntry.cs`, `CnamLetterValue.cs`, repo(s), config(s), migration, `ApplicationDbContext.cs`, `Extensions.cs`, `Features/CnamNomenclature/{Commands,Queries}/*`, `CnamReimbursementCalculator`, `CnamNomenclatureController.cs` (+ admin authz), `CnamNomenclatureEntryDto.cs`, `web/app/cnam-nomenclature/page.tsx`, `web/components/cnam-*.tsx`, `web/lib/api/cnam-nomenclature.ts`, `web/lib/api/types.ts`, `web/lib/realtime/clinic-hub.ts`, `dashboard-sidebar.tsx`, `document-editor-content.tsx`.

**Verification**
- CRUD + duplicate-code rejection + global (two clinics see same rows) + provisional flag/confirm (`CNAM-1..8`); VLC admin edit (`VLC-1..3`); reimbursement age brackets + unknown DOB + missing VLC (`REIMB-1..5`).
- Admin CNAM/VLC writes carry `AdminOnly` (extend `ControllerAuthorizationCoverageTests`).

---

## Exit Criteria

- All six parts implemented; `dotnet build` + `npm run build` clean (0 errors/0 warnings); the mapped integration tests pass (SAC-blocked `dotnet test` noted per environment).
- Two migrations created (`AddDoctorCachetAndOrdre`, `AddCnamCatalog`); no `MedicalDocument` schema change.
- Honoraires routes to a compliant invoice; certificats/liaisons carry mention/CNOMDT/cachet on the cabinet city in TND; CNAM catalog + VLC admin-managed with provisional flag; reimbursement uses age brackets.
- Nearest `CLAUDE.md` files updated where structure changed (handled at `/update-memory`).
