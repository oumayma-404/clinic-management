# Integration Test Plan: Official Documents — Production-Ready

**Status:** APPROVED
**Created:** 2026-07-20
**Source spec:** `spec.md` (APPROVED, Challenged: Yes)
**Test project:** `api/ClinicManagement.UnitTests` (xUnit + Moq)
**Test style:** handler-level and domain-level tests — mock the Domain repository interfaces + `ICurrentClinicResolver`/`IClinicContext`, assert on `Result.IsSuccess`/`IsFailure`, verify persistence side-effects via `Mock.Verify(..., Times.Never/Once)`. Infrastructure renderers are exercised through their public/reflected seams (as `CnamBs1BulletinRendererTests` does). **No BDDfy / FluentAssertions / Testcontainers** — this repo does not use them.

> **Scope note.** This repo has no E2E (Cucumber/Playwright) and no API (Postman/Newman) harness, so those test-plan layers were auto-skipped. These xUnit tests are the **only** automated layer for the feature; the plan therefore aims for full backend coverage of the five FR groups. UI-only concerns (FR-6.3 non-editable preview blocks, honoraires card → InvoiceFormModal wiring) are noted as out of scope for this layer.

---

## Feature Overview

Make `/documents` legally/fiscally correct for a Tunisian dental cabinet:
- **FR-1** honoraires routes to the existing compliant Invoice pipeline (mostly frontend; backend only *removes* the `honoraires` document type).
- **FR-2** certificat correctness: consistent content schema across save/render, mandatory deontological mention, CNOMDT label, no data loss on background re-render.
- **FR-3** per-doctor cachet/signature image + CNOMDT order number, own-or-admin authorization, content-type persistence, snapshot onto the document at creation.
- **FR-4** structured lettre de liaison to an **external** confrère (free recipient, guided optional fields, empty fields omitted).
- **FR-5** DB-backed, **global**, admin-managed CNAM nomenclature catalog + VLC values + reimbursement estimate (coefficient × VLC × age-based rate). **Confirmed: the reimbursement calc moves to the backend and is tested here.**
- **FR-6** localization/correctness: cabinet city (not "Paris"), TND (not €), `bulletin-cnam` filename on the update path, and preview-block edit loss (FE).

---

## Existing Coverage (reviewed)

| Existing test | Covers | Impact of this feature |
|---|---|---|
| `Features/CnamNomenclature/GetCnamNomenclatureQueryHandlerTests` | `GetCnamNomenclatureQuery` filter logic (q/category/lettre-clé, case-insensitive, blank = no filter), against a **mocked `ICnamNomenclatureProvider`** | The read moves from the in-code provider to a **DB-backed** source (FR-5.1). These filter tests must be **migrated/retargeted** to the new read path (repository-backed). Filter behavior itself is unchanged and re-asserted. |
| `Infrastructure/Services/CnamNomenclatureProviderTests` | The hardcoded in-code catalog data | The in-code provider is **superseded** by the DB catalog. These tests are retired or repurposed to assert the **seed** content instead. |
| `Features/Documents/MedicalDocumentTenantIsolationTests` | Get/GetAll/Delete tenant isolation + blob+file cleanup on delete | Reused as-is; new certificat/liaison/snapshot scenarios extend the Documents suite without altering these. |
| `Infrastructure/Services/CnamBs1BulletinRendererTests` | BS1 overlay model mapping, honoraires TND formatting, act pagination, fail-fast | **Out of scope** (BS1 renderer untouched, per spec). Only relevant as the *pattern* for reflected render-model assertions. |
| `Features/ProcedureTypes/ProcedureTypeTenantIsolationTests` | CRUD template (create stamps clinic; cross-clinic update/delete = not found) | Reference pattern for the new CNAM CRUD tests — but CNAM is **global**, so the new tests assert the *opposite* of clinic-stamping (see CNAM-6). |

---

## New Test Classes & Scenarios

Scenarios are grouped by test class. Each `[Fact]`/`[Theory]` is one behavior. `// [FR-x.y]` tags map to the spec.

### 1. `Features/CnamNomenclature/CnamNomenclatureCrudTests`
New DB-backed catalog CRUD (mirrors `CreateProcedureTypeCommandHandler` etc., but **global**, AdminOnly writes). Mocks a new `ICnamNomenclatureRepository` + `IUnitOfWork`.

| # | Test | Given / When / Then |
|---|---|---|
| CNAM-1 | `Create_Succeeds_And_Persists_All_Fields` // [FR-5.1] | Given no entry with the same code · When an entry is created (code, désignation FR, lettre clé, coefficient, catégorie) · Then `IsSuccess`, `AddAsync` called once, `SaveChangesAsync` once, captured entry carries all fields and `IsActive = true`. |
| CNAM-2 | `Create_Duplicate_CodeActe_Is_Rejected_With_French_Message` // [FR-5.1, edge: duplicate CodeActe] | Given the repository reports the code already exists · When a second entry with that code is created · Then `IsFailure`, French error, `AddAsync`/`SaveChangesAsync` **never** called. |
| CNAM-3 | `Create_Seeded_Entry_Carries_Provisional_Flag` // [FR-5.1] | Given a newly created (or seeded) entry · Then its provisional/"à vérifier" flag is set by default. |
| CNAM-4 | `Update_Existing_Entry_Succeeds` // [FR-5.1] | Given an existing entry · When updated (désignation/coefficient/lettre clé) · Then `IsSuccess`, fields changed, `SaveChangesAsync` once. |
| CNAM-5 | `Update_Unknown_Id_Returns_NotFound` // [FR-5.1] | Given the id resolves to nothing · When updated · Then `IsFailure`, no save. |
| CNAM-5b | `Update_To_Duplicate_CodeActe_Is_Rejected` // [edge: duplicate CodeActe] | Given changing an entry's code to one already held by another entry · Then `IsFailure`, no save. |
| CNAM-6 | `Catalog_Is_Global_Not_Clinic_Scoped` // [FR-5.1] | Given two different calling clinics · When each lists the catalog · Then both see the **same** global rows (no `ClinicId` stamped on create; reads not filtered by clinic). Contrast with `ProcedureTypeTenantIsolationTests`. |
| CNAM-7 | `Deactivate_Sets_Inactive_And_Excludes_From_Active_Reads` // [FR-5.1] | Given an active entry · When deactivated/deleted · Then `IsSuccess`; subsequent active-only read excludes it. |
| CNAM-8 | `Confirm_Clears_Provisional_Flag` // [FR-5.1] | Given a provisional entry/catalog · When an admin confirms · Then the "à vérifier" flag clears; nothing was blocked while it was set. |

### 2. `Features/CnamNomenclature/CnamVlcTests`
Valeur de la lettre clé — admin-managed set, keyed by lettre clé (entity or keyed rows; tested at behavior level, implementation-agnostic).

| # | Test | Given / When / Then |
|---|---|---|
| VLC-1 | `Read_Returns_Seeded_Values_For_Any_Authenticated_User` // [FR-5.2, FR-5.3] | Given seeded VLC values (CD/CDS/VD/D/RD…) · When any authenticated user reads them · Then all are returned with their provisional flag. |
| VLC-2 | `Update_By_Admin_Persists_New_Value` // [FR-5.2] | Given an existing VLC key · When an admin sets a new dinar value · Then `IsSuccess`, value persisted, save once. |
| VLC-3 | `Seeded_Vlc_Values_Are_Provisional` // [FR-5.2] | Given the seed · Then every VLC value is flagged "à vérifier" until confirmed. |

### 3. `Features/CnamNomenclature/CnamReimbursementEstimateTests`
Reimbursement estimate — **new backend calculation** (per user decision). estimate = coefficient × VLC × rate; July-2021 rates (70% ages 4–18 inclusive, 60% otherwise), age at care date; unknown DOB → non-child.

| # | Test | Given / When / Then |
|---|---|---|
| REIMB-1 | `Estimate_Equals_Coefficient_Times_Vlc_Times_Rate` // [FR-5.5] | Given coefficient=10, VLC=1.500, adult rate 0.60 · Then estimate = 10 × 1.5 × 0.60 = 9.000. |
| REIMB-2 (Theory) | `Rate_Is_Child_Band_For_Ages_4_To_18_Inclusive` // [FR-5.5, edge: age boundaries] | `[InlineData]` age 3→0.60, 4→0.70, 5→0.70, 17→0.70, 18→0.70, 19→0.60. Then the applied rate matches the band. |
| REIMB-3 | `Age_Is_Computed_At_Care_Date_Not_Today` // [FR-5.5] | Given DOB and a care date where the patient is 18 at care time (but 19 today) · Then child rate 0.70 applies (age snapped to the care date). |
| REIMB-4 | `Unknown_Dob_Uses_NonChild_Rate` // [FR-5.5, edge: unknown DOB] | Given no DOB · Then adult rate 0.60. |
| REIMB-5 | `LettreCle_With_No_Vlc_Value_Omits_Estimate` // [edge: missing VLC] | Given an act whose lettre clé has no VLC value · Then the estimate is **omitted (—)**, not computed as zero. |

### 4. `Features/Doctors/DoctorCachetTests`
Per-doctor cachet + CNOMDT number update (via the doctor update path — today `UpdateDoctorsCommand`, Clinics area). Own-or-admin authorization; content-type persistence.

| # | Test | Given / When / Then |
|---|---|---|
| CACHET-1 | `Admin_Can_Set_Any_Doctors_OrderNumber_And_Cachet` // [FR-3.1, FR-2.5] | Given caller is admin · When setting another doctor's order number + cachet · Then `IsSuccess`, both persisted. |
| CACHET-2 | `Doctor_Can_Set_Own_Cachet` // [FR-3.1] | Given caller is the doctor whose record is targeted · When uploading their own cachet · Then `IsSuccess`. |
| CACHET-3 | `NonAdmin_Cannot_Set_Another_Doctors_Cachet` // [FR-3.1] | Given a non-admin caller targeting a different doctor's record · When setting the cachet · Then `IsFailure` (forbidden), storage upload **never** called. |
| CACHET-4 (Theory) | `Cachet_Persists_Actual_ContentType` // [FR-3.1, edge: content type] | `[InlineData("image/png")]`, `[InlineData("image/jpeg")]` · When a cachet of that type is uploaded · Then the doctor's stored cachet content type equals the uploaded type (NOT hardcoded `image/png`, unlike the logo path). |
| CACHET-5 | `Remove_Cachet_Clears_Key_And_ContentType` // [FR-3.1] | Given a doctor with a cachet · When removed · Then key + content type cleared; no error. |

### 5. `Domain/Entities/DoctorEntityTests`
Pure domain invariants on the extended `Doctor` aggregate.

| # | Test | Given / When / Then |
|---|---|---|
| DOC-1 | `SetCachet_Sets_Key_And_ContentType_And_Bumps_UpdatedAt` // [FR-3.1] | `SetCachet(key, contentType)` stores both and updates `UpdatedAt`. |
| DOC-2 | `SetOrderNumber_Persists_CNOMDT_Number` // [FR-2.5] | Order number set and readable. |
| DOC-3 | `RemoveCachet_Clears_Both_Fields` // [FR-3.1] | After removal, key and content type are null. |

### 6. `Features/Documents/CertificatContentTests`
Certificat content-schema round-trip (fixes the save-vs-render field mismatch) + snapshot for the unauthenticated render job.

| # | Test | Given / When / Then |
|---|---|---|
| CERT-1 | `Create_Certificat_Persists_All_Fields_Consistently` // [FR-2.2] | Given a certificat with objet/motif + order number + start date + rest duration · When created · Then `ContentJson` persists **all four**, and deserializing with the *render* model reads them back identically (one consistent schema across save and render). |
| CERT-2 | `Certificat_With_Only_ObjetMotif_Omits_Repos_Block` // [FR-2.1] | Given objet/motif filled, repos fields empty · Then the render model renders only the objet/motif (rest sentence omitted). |
| CERT-3 | `Certificat_With_Repos_Fields_Renders_Rest_Sentence` // [FR-2.1] | Given start date + duration filled · Then the rest sentence is present in the render model. |
| CERT-4 | `Update_Certificat_Preserves_All_Fields` // [FR-2.2, FR-6.3 no silent drop] | Round-trip through update keeps objet/motif + order number + dates. |
| CERT-5 | `Doctor_Cachet_And_Order_Number_Are_Snapshotted_At_Creation` // [FR-3.3, edge: unauth render] | Given a doctor with cachet key + order number · When a certificat/prescription/liaison is created · Then those values are written into the document snapshot/`ContentJson`, so the background render can use them **without a live doctor lookup**. |

### 7. `Infrastructure/Services/GenericDocumentRenderTests`
QuestPDF generic-doc render behavior (`PdfGenerationService`), asserted via its content→model seam and end-to-end byte output (BS1-renderer test pattern). *If the render model has no reflectable seam, these downgrade to "renders a non-empty PDF without throwing" smoke assertions — flagged in Building/Helpers below.*

| # | Test | Given / When / Then |
|---|---|---|
| REND-1 | `Certificat_Renders_Mandatory_Deontological_Mention` // [FR-2.3] | The rendered certificat model/content includes **"Certificat établi à la demande de l'intéressé(e) et remis en main propre."** above the signature block. |
| REND-2 | `Certificat_Ordre_Label_Is_CNOMDT` // [FR-2.4] | The ordre label reads **"Ordre National des Médecins Dentistes (CNOMDT)"**, not "Ordre des Médecins". |
| REND-3 | `Cachet_Image_Rendered_When_Present` // [FR-3.2] | Given a snapshot cachet key resolving to a blob · Then the signature area uses the image. |
| REND-4 | `Missing_Cachet_Falls_Back_To_Signature_Line_No_Error` // [FR-3.2, edge: no cachet / deleted blob] | Given no cachet (or the blob is missing at render) · Then the plain signature line renders and no exception is thrown. |
| REND-5 | `Generic_Docs_Use_Clinic_City_Not_Paris` // [FR-6.1] | Given a clinic city in the snapshot · Then the doc's place/date uses that city; "Paris" never appears. |
| REND-6 | `Generic_Docs_Use_TND_Never_Euro` // [FR-6.1] | No `€` symbol on any generic clinical document; monetary values follow TND/millimes (3-decimal) conventions. |

### 8. `Features/Documents/LiaisonContentTests`
Structured lettre de liaison to an external confrère.

| # | Test | Given / When / Then |
|---|---|---|
| LIA-1 | `Create_Liaison_With_External_Recipient_Succeeds` // [FR-4.1] | Given a free-text external recipient name (+ optional specialty/address) · When created · Then `IsSuccess`; recipient stored from free text, **no** internal-doctor lookup performed. |
| LIA-2 | `Missing_Recipient_Name_Is_Rejected` // [FR-4.2] | Given no recipient name · Then `IsFailure` (name is the only required field). |
| LIA-3 | `Guided_Fields_Persisted_In_Content` // [FR-4.2] | Motif, examen clinique, examen radiologique, actes réalisés, prescriptions (posologie/durée) round-trip through `ContentJson`. |
| LIA-4 | `Empty_Optional_Fields_Are_Omitted_From_Render` // [FR-4.2] | Given some guided fields empty · Then the render model omits them (no empty headings). |
| LIA-5 | `Legacy_Internal_Recipient_Liaison_Still_Readable` // [edge: legacy liaison] | Given a legacy document whose snapshot names an internal recipient · When read · Then it renders from the stored snapshot without error (only new letters use the external model). |

### 9. `Features/Documents/DocumentTypeAndFilenameTests`
Honoraires removal (FR-1.4) + bulletin filename on update (FR-6.2).

| # | Test | Given / When / Then |
|---|---|---|
| TYPE-1 | `Create_With_Honoraires_Type_Is_Rejected` // [FR-1.4] | Given document type `honoraires` · When create is attempted · Then `IsFailure` (type no longer supported); no `honoraires` `MedicalDocument` persisted. |
| TYPE-2 | `Legacy_Honoraires_Documents_Remain_Readable` // [FR-1.5] | Given an existing `honoraires` document · When read/listed · Then it is still returned (not migrated or deleted). |
| FILE-1 | `Update_Of_Bulletin_Cnam_Uses_French_Filename` // [FR-6.2] | Given a `bulletin-cnam` document being updated with a regenerated PDF · Then the file name maps to `bulletin-de-soins-cnam-…` (the mapping now present in the **update** path, matching create). |
| FILE-2 (Theory) | `Filename_Mapping_Consistent_Create_And_Update` // [FR-6.2] | `[InlineData("prescription","ordonnance")]`, `[InlineData("certificat","certificat-medical")]`, `[InlineData("liaison","lettre-de-liaison")]`, `[InlineData("bulletin-cnam","bulletin-de-soins-cnam")]` · Both create and update produce the same French base name for each type. |

---

## Coverage Mapping (spec → tests)

| Spec item | Covered by |
|---|---|
| FR-1.4 honoraires type removed | TYPE-1 |
| FR-1.5 legacy honoraires kept | TYPE-2 |
| FR-2.1 objet/motif + optional repos | CERT-2, CERT-3 |
| FR-2.2 consistent schema, no data loss | CERT-1, CERT-4 |
| FR-2.3 mandatory mention | REND-1 |
| FR-2.4 CNOMDT label | REND-2 |
| FR-2.5 order number on profile | CACHET-1, DOC-2 |
| FR-3.1 per-doctor cachet, own-or-admin, content type | CACHET-1..5, DOC-1/3 |
| FR-3.2 cachet render + fallback | REND-3, REND-4 |
| FR-3.3 snapshot at creation | CERT-5 |
| FR-4.1 external recipient | LIA-1 |
| FR-4.2 guided fields, name required, omit empty | LIA-2, LIA-3, LIA-4 |
| FR-4.3 renders on letterhead w/ cachet | REND-3 (+ LIA-3) |
| FR-5.1 DB-backed global catalog + provisional | CNAM-1..8 |
| FR-5.2 VLC admin-managed | VLC-1..3 |
| FR-5.3 read all / write admin | VLC-1, CNAM controller-authz (see Out of Scope note) |
| FR-5.5 reimbursement rates | REIMB-1..5 |
| FR-6.1 city not Paris, TND not € | REND-5, REND-6 |
| FR-6.2 bulletin filename on update | FILE-1, FILE-2 |
| Edge: duplicate CodeActe | CNAM-2, CNAM-5b |
| Edge: age boundaries / unknown DOB | REIMB-2, REIMB-4 |
| Edge: missing VLC | REIMB-5 |
| Edge: no/deleted cachet blob | REND-4 |
| Edge: content type persisted | CACHET-4 |
| Edge: unauth background render | CERT-5 |
| Edge: legacy internal-recipient liaison | LIA-5 |

---

## Test Helpers / Fakes Needed

This repo has no Building Blocks framework; tests construct handlers directly with Moq. New helpers to add:

- **`ICnamNomenclatureRepository` mock setup helper** — seed a small in-memory catalog + existence checks (mirrors the `_procedures` mock in `ProcedureTypeTenantIsolationTests`).
- **VLC store mock** — whatever shape planning picks (entity or keyed rows); the tests target the read/update behavior, not the storage shape.
- **Doctor factory** for the cachet tests (extends the `Doctor` ctor once the entity gains order-number + cachet fields).
- **Caller-role helper** — set the current user as admin vs the target doctor vs an unrelated non-admin (reuse the `IClinicContext`/`ICurrentClinicResolver` mock pattern from `MedicalDocumentTenantIsolationTests`).
- **Render-model accessor** — if `PdfGenerationService`'s generic-doc mapping is `internal`/private (as `CnamBs1BulletinRenderer` is), add a reflection helper like the existing `Bs1Model.From(...)` accessor to assert on the mention/label/city text without brittle PDF byte parsing.

---

## Out of Scope (this test layer)

- **FR-1.1–1.3** honoraires → `InvoiceFormModal` wiring, patient picker, draft seeding from uninvoiced dental records, issue step — this is **frontend + reuse of the unchanged Invoice pipeline**; the Invoice pipeline already has its own tests (`IssueInvoiceCommandHandlerTests`, `InvoiceCalculatorTests`, `InvoiceEInvoiceTests`). No new integration test duplicates them.
- **FR-5.3 write-side authorization enforcement** — CNAM catalog/VLC writes are gated by `[Authorize(Policy = AdminOnly)]` at the controller. Attribute presence is verified by the existing reflection scan `ControllerAuthorizationCoverageTests` (extend its allow-list for the new controller); the *handler* tests here do not re-test the ASP.NET policy pipeline.
- **FR-6.3 non-editable preview blocks** — pure frontend behavior (React `contentEditable` removal); no backend surface.
- **PDF pixel/coordinate/layout fidelity** — geometry is verified visually per the BS1 pattern; these tests assert content/model correctness, not visual placement.
- **BS1 overlay renderer** — untouched by this feature (already correct); only consumes the verified nomenclature.
- **Seed data value correctness vs the real CNAM convention** — the seed ships provisional ("à vérifier"); reconciling the actual codes/coefficients/VLC values against the convention is a data task, not an automated test.

---

## Open Questions for Implementation

1. **VLC storage shape** (entity vs keyed config rows) is sized during `/plan-feature`; tests are written implementation-agnostic (behavior only) and finalized once the shape is chosen.
2. **Reimbursement calc home** — confirmed backend; whether it is a dedicated domain method, a query handler, or a small domain service is a planning decision. REIMB tests target the calculation contract regardless.
3. **Doctor update path** — cachet/order-number update currently threads through `UpdateDoctorsCommand` (Clinics area). If planning introduces a dedicated per-doctor profile command, the CACHET tests attach to that handler instead (same scenarios).
