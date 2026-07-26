# Spec: Adoption QA — Batch D (data hygiene & minor loops)

**Status:** APPROVED
**Type:** Small (forced multi-item pass — user fed the full adoption-QA blueprint)
**Created:** 2026-07-24
**Scope:** Full
**Branch:** feature/windows-desktop-app (existing)
**Feature:** Clear the 🟡 minor/latent findings (#11–#20) — small, independent correctness + data-hygiene fixes, each with a confirmed in-repo pattern.

## Overview
Ten low-risk annoyances that each break a small loop: fields that display but can't be entered, a prescription that prints without its DCI, a euro-denominated dead-end editor, stock you can't consume/restock, no way to delete a patient, onboarding that drops working hours, a free-text governorate, and an odontogram that can't record surfaces. None touches money integrity; all are additive or localized.

## What Changes
- **#11 Emergency contact.** Add `EmergencyContactName` + `EmergencyContactPhone` to `Create/UpdatePatientCommand` → `patient.UpdateEmergencyContact(...)`; add the two inputs to `edit-patient-dialog.tsx` "Coordonnées". Mirror the CNAM/insurance VO wiring.
- **#12 Ordonnance DCI.** Add `[JsonPropertyName("dci")] List<string>?` to `MedicationData` (`PdfGenerationService.cs:816`) and append it in the render loop (`:652-665`). FE already captures + sends `dci`.
- **#13 Honoraires €→DT + dead-end.** Switch `€`→DT via `formatDT` in `document-editor-content.tsx:283,315`; stop `patients/[id]/page.tsx:978` routing a legacy honoraires doc to the retired editor (or remove the honoraires editor branch).
- **#14 Stock actions.** Add consume/restock commands + endpoints that call the existing `StockItem.RemoveStock`/`AddStock` (`:63,50`) as deltas (not absolute overwrite); inject `INotificationGenerator` into `CreateStockItemCommandHandler` to fire `LowStockAsync` when an item is created already-low; subscribe the stock page to `useClinicRealtime(RealtimeResource.Stock, refetch)`.
- **#15 Delete patient.** New `DeletePatientCommand` (+ handler, tenant-checked) calling the existing uncalled `IPatientRepository.DeleteAsync`; `DELETE /api/patients/{id}`; a guarded delete action in the patient UI. Copy `DeletePatientMedicalHistoryCommand`.
- **#16 Onboarding hours.** Thread `setup-wizard.tsx` `workingHours` into a new `CreateClinicCommand.WorkingHoursJson`; `clinic.SetWorkingHours(...)` in both handler branches, normalized like `UpdateClinicCommand.cs:100-165`.
- **#17 Governorate dropdown.** Replace the free-text input (`edit-patient-dialog.tsx:670-679`) with a `<Select>`; lift `tunisianGovernorates` (`setup-wizard.tsx:19-44`) into a shared module imported by both.
- **#19 Odontogram surfaces.** Add a MODVL surface picker to the diagnose form (`odontogram.tsx:367-393`) and include `surfaces` in the `odontogramApi.diagnose` payload (`:252-256`). Backend already supports it.
- **#20 Latent (best-effort).** Waiting-list "Promouvoir" failure must not orphan/double-book (reuse Batch A overlap on promote); enabling a reminder channel backfills already-booked upcoming appointments; caisse date bounds normalized to UTC.

## API Contract
### POST /api/stock/{id}/consume  ·  POST /api/stock/{id}/restock
Request: `{ quantity: int, batchNumber?: string, expiryDate?: date }` (restock)
Response 2XX: `StockItemDto`
Errors: `400 quantity<=0 / insufficient stock` · `404 not found / other clinic`
### DELETE /api/patients/{id}
Response 2XX: no content · Errors: `404 not found / other clinic`

## Data / Schema Changes
- `CreateClinicCommand.WorkingHoursJson` — command field only (persisted to existing `Clinic.WorkingHoursJson`; no new column).
- No new columns for emergency contact / stock (entity fields already exist). Stock movements are delta mutations, **not** a new audit table (see Out of Scope).

## Acceptance Criteria
- **AC-1 (#11):** Emergency name + phone entered in the patient dialog persist and display on the patient page.
- **AC-2 (#12):** A printed/previewed ordonnance shows the DCI for medications that have one.
- **AC-3 (#13):** The honoraires editor shows DT (3-decimal) not €; a legacy honoraires document no longer opens a save/PDF path that 500s.
- **AC-4 (#14):** Consume/restock change quantity by delta with the domain guards (reject ≤0 / insufficient); an item created already-low fires a low-stock notification; a peer's stock change live-refreshes the stock page.
- **AC-5 (#15):** A patient can be deleted via the endpoint/UI; the deleted patient no longer appears; cross-clinic delete is rejected.
- **AC-6 (#16):** Working hours set in the onboarding wizard are persisted on the clinic and visible in Settings.
- **AC-7 (#17):** Governorate is chosen from the 24-governorate dropdown on the patient form.
- **AC-8 (#19):** Diagnosing a tooth can set surfaces (MODVL); the saved diagnosis shows them.
- **AC-9:** New commands (delete patient, stock consume/restock) carry a tenant-isolation assertion.

## Out of Scope
- A stock-movement **audit ledger** (deltas mutate `CurrentStock` via existing domain methods; historical movement log is a separate feature).
- Soft-delete / cascade rules for patient deletion beyond what the repo already does.
- Reworking the honoraires editor into a new document type (only currency + dead-end closed here).

## Edge Cases (Critical only)
- #15: deleting a patient with invoices/appointments — confirm the repo's existing behavior (block vs cascade) and surface a clear error rather than a raw FK failure.
- #14: consume that would drive stock negative is rejected, not clamped.
- #16: a clinic created without hours (Cloud path where wizard omits them) must still save (nullable).
