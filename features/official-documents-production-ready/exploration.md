# Exploration — Official Documents Production-Readiness

**Explored:** 2026-07-20
**Method:** 5 parallel Explore/general-purpose agents (documents FE, documents BE, Invoice flow, upload+admin patterns, ProcedureType CRUD template) + external regulatory research (CNAM dentist convention, art.18 CTVA / 2016–2017 finance laws, code de déontologie, CNAM July-2021 dental reimbursement rates).

## Current documents subsystem

- **5 document types**, all keyed by a free-text `MedicalDocument.DocumentType` string (no enum): `prescription`, `liaison`, `honoraires`, `certificat`, `bulletin-cnam`. Enumerated in `web/app/documents/page.tsx:15-67` and `web/components/document-editor-content.tsx:1301-1316`.
- **Editor**: single 2216-line `document-editor-content.tsx` — left structured form, right A4 preview. `bulletin-cnam` right pane embeds the real generated BS1 PDF in an iframe; other types use a generic letterhead card.
- **Backend entity** `api/ClinicManagement.Domain/Entities/MedicalDocument.cs` — non-root `Entity<Guid>`. Snapshots patient/clinic/doctor data as **strings** (`DoctorName`, `ClinicName`, `ClinicAddress`…). **No `DoctorId`, no `ClinicId`** — tenant scoping via `Patient.ClinicId`. Body in opaque `ContentJson`.
- **PDF**: QuestPDF for the 4 generic docs (`PdfGenerationService.cs`), PdfSharp coordinate-overlay onto the real `Assets/BS1.pdf` for `bulletin-cnam` (`CnamBs1BulletinRenderer.cs`). Background `PdfGenerationJob` (Hangfire) re-renders from persisted `ContentJson` in an **unauthenticated scope**.

## Confirmed defects (from the review, verified)

- **Honoraires**: generic QuestPDF path renders `€`, no matricule fiscal, no number, no timbre, no TVA (`PdfGenerationService.cs:436/459`). Fiscally non-compliant vs art.18 CTVA + 2016/2017 finance laws.
- **"Paris, le …"** hardcoded on every generic doc (`PdfGenerationService.cs:71`, editor `:841/:2027`).
- **Certificat data-loss**: `handleSave` persists `reason/duration/notes` (`:1223-1226`); `buildDocumentData` reads `doctorOrderNumber/startDate/duration` (`:715-722`). Order number + start date never reach `ContentJson`; vanish on background re-render. `reason/notes` never collected in UI.
- **Liaison**: recipient picked from the clinic's OWN doctors (`:1504-1529`) — must be an external confrère. Body is one free-text box.
- **CNAM nomenclature**: hardcoded in-code Singleton `CnamNomenclatureProvider.cs` (lettres clés CD/CDS/VD/D/RD), explicitly "⚠ PENDING VERIFICATION". Reimbursement estimate `web/lib/api/cnam-nomenclature.ts:11-27` is flat `0.7` with no age handling (should be 70% for ages 4–18, 60% others since July 2021). Estimate is editor-only, never persisted/printed.
- **Small bugs**: `bulletin-cnam` filename mapping missing from `UpdateMedicalDocumentCommand.cs:206-216` (present in Create `:275`); `contentEditable` preview blocks silently lose edits (only liaison writes back, `:2108`).

## Reusable plumbing (mirror, don't reinvent)

- **Invoice creation**: `InvoiceFormModal` (`web/components/factures/invoice-form-modal.tsx`) already accepts `presetPatientId` / `presetLines` / `dentalRecordId`; patient page's "Facturer cette intervention" uses it. `POST /api/invoices` creates a **draft** (no number); `POST /api/invoices/{id}/issue` assigns `AAAA-NNNN` + freezes TVA/timbre from clinic settings; El Fatoora is a further separate step. Invoice has no `DoctorId`.
- **Image upload/storage**: clinic logo path — `apiPutFormData` → `ClinicsController` `[FromForm]` → `IFileStorage.UploadAsync(stream, contentType, customPath)` → deterministic key `{clinicId}/logo` stored on `Clinic.LogoUrl`; served by streaming `DownloadAsync`. **Gotcha**: `GetClinicLogoQuery` hardcodes `ContentType = "image/png"` (content type not persisted) — the cachet must persist its content type. `PatientFile` upload preserves content type (better reference).
- **Admin gating**: `AuthorizationPolicies.AdminOnly` + `[Authorize(Policy = AdminOnly)]` (class-level on `UsersController`). `User.IsAdmin()`. **Admin role is only minted in Local first-run** (`CreateClinicCommand.cs:293`) — Cloud installs have no admin user. FE gates via `user?.role === "admin"` (`web/app/users/page.tsx:16`) + sidebar entry.
- **CRUD catalog template**: `ProcedureType` end-to-end (entity → `IProcedureTypeRepository` → EF config → DI in `Extensions.cs:34` → migration → CQRS with inline `Result.Failure` → `[Authorize]` controller `api/procedure-types` → `procedure-types/page.tsx` + `procedureTypesApi` + table + form modal + `useClinicRealtime`). **But ProcedureType is clinic-scoped** (`ClinicId` + global query filter). The CNAM catalog must be **global** (no `ClinicId`, excluded from query filter) and **AdminOnly** for writes.
- **Seeding**: none exists. `SeedData/README.md` documents an unwired pattern. Global reference rows must be seeded via a migration `InsertData` (or `HasData`).

## Decisions taken (user)

1. Honoraires card → opens patient picker → existing compliant `InvoiceFormModal` (draft). Old `honoraires` editor type removed.
2. Signature/cachet → **per Doctor**. Order number (CNOMDT) also a Doctor field.
3. CNAM catalog → **global, seeded, admin-editable** (writes AdminOnly; reads any authenticated).
4. Lettre de liaison → **discrete guided fields**.

## Decisions taken (assistant defaults, low-risk/reversible)

- Reimbursement estimate: **kept**, rates fixed with age brackets, clearly labelled "estimation indicative, non contractuelle" (still editor-only, never printed).
- Liaison "copy to confrère": produced as a printable/PDF letter with the recipient block; **no automated delivery** (email infra is a dormant stub).
- Legacy `€` honoraires `MedicalDocument`s: left as-is (already-generated PDFs remain in patient files); the editor simply stops creating/handling new ones.
