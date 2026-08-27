# Feature Specification: Dental Core

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-22
**Scope:** Full
**Feature:** Treatment plans + devis + échéanciers, a persistent odontogram, and a STMDLP/DCH dental-act catalog — the dental essentials that turn the app from a generic clinic tool into a product a dentist buys.

## Overview
Adds the four dental building blocks Tunisian dental software has and we lack. A **TreatmentPlan** aggregate is the spine: it holds planned acts and an installment schedule (**échéancier**), and renders a **devis** (quote) PDF. A **persistent odontogram** records per-tooth condition (with surfaces) per patient. A **global DentalActCode catalog** (STMDLP/DCH) backs plan line-items. Architecture is the "Plan-spine" option decided via `/think-solution`; the fiscal `Invoice` / TTN e-invoicing path is left untouched. UI is French, matching the Factures/CNAM style.

## What Changes

**Dental act catalog (STMDLP/DCH) — global reference data (clone of CNAM nomenclature)**
- New global `DentalActCode` catalog (not clinic-scoped), admin-gated CRUD + soft-deactivate + provisional-confirm, on a new `/dental-acts` admin page cloned from `/cnam-nomenclature`.
- **Seeded from the official Tunisian dental nomenclature** (CNAM « Liste des actes » — chapitre `DCH`, ~100 acts across 6 sections; full list in `dental-nomenclature-source.md`), each `IsProvisional`. Numeric coefficients (cotation) aren't in the source (NGAP arrêté), so `Coefficient` seeds null and is admin-editable.

**Persistent odontogram — per-patient tooth state**
- New `ToothState` per patient (FDI tooth number + condition + surfaces + note), replacing today's per-record-only selection.
- New "Odontogramme" tab on the patient detail page: color-coded chart (reusing existing SVG teeth), a condition legend, and a click-to-set popover with a 5-zone (M/O/D/V/L) surface picker.

**Treatment plans + échéanciers (the spine)**
- New `TreatmentPlan` root aggregate (Draft → Accepted → InProgress → Completed → Cancelled) with act line-items and an installment schedule.
- Line-items reference a catalog act **or** a free-text designation (needed for non-CNAM acts like crowns/implants that are absent from the nomenclature).
- Installments (échéancier) must sum to the plan's total planned cost; payments recorded against installments with an overpayment guard.
- Accepting a plan freezes a per-clinic-per-year number (`AAAA-NNNN`, separate sequence from invoices).
- New "Plan de traitement" tab on the patient page (build/edit plans, record installment payments, download devis) **and** a top-level "Plans / Devis" page (cross-patient table with status/total/encaissé/reste + filters, mirroring `/factures`).

**Devis (quote) — non-fiscal PDF**
- New devis PDF rendered from a plan (clinic header, act lines, total planned, échéancier schedule), labeled "DEVIS". No timbre fiscal, no VAT freeze, no TTN/El-Fatoora QR.
- Converting done work into a fiscal note d'honoraires reuses the **existing** frontend record→invoice bridge (`InvoiceFormModal` presetLines seeded from the plan's Done items) — no new backend conversion.

## Acceptance Criteria

**Act catalog**
- **AC-1:** The ~100 official `DCH` acts are seeded provisional on first migration; an admin can create/edit/deactivate a `DentalActCode` (CodeActe unique, DesignationFr, LettreCle, Category, RequiresAccordPrealable, optional Coefficient/DefaultFee) and confirm provisional entries; non-admins get reads only (writes return 403).
- **AC-2:** `DentalActCode` has no `ClinicId` and is visible identically to every clinic (global reference data).

**Odontogram**
- **AC-3:** Setting a tooth's condition/surfaces/note upserts one `ToothState` for `(patientId, toothNumber)`; setting it back to `Sain` clears it; invalid FDI numbers are rejected.
- **AC-4:** The odontogram is patient-scoped — a user cannot read or modify tooth state for a patient outside their clinic (guarded via the patient's `ClinicId`).
- **AC-5:** The patient page's "Odontogramme" tab renders each tooth colored by condition, with a legend and a surface picker.

**Treatment plans / échéanciers**
- **AC-6:** A draft plan can be created/edited with act line-items (catalog-picked or free-text) and installments; only drafts are editable or deletable.
- **AC-7:** `SetInstallments` rejects a schedule whose amounts don't sum to the total planned cost; the last installment absorbs the millime rounding remainder (via `InvoiceCalculator.RoundMoney`).
- **AC-8:** Accepting a draft assigns a gapless per-clinic-per-year `AAAA-NNNN` number, requires ≥1 item, and retries on a numbering collision (mirrors `IssueInvoiceCommand`).
- **AC-9:** Recording an installment payment refuses an amount that would exceed that installment's remaining balance; the plan reflects total encaissé/reste.
- **AC-10:** A line-item can be marked Done (optionally linked to a dental record); cancelling a plan requires a reason and is Doctor/Admin-gated.
- **AC-11:** Plans are clinic-isolated — get/update/accept/pay/cancel on a foreign-clinic plan fails and never persists.

**Devis**
- **AC-12:** `GET /treatment-plans/{id}/devis-pdf` returns a PDF (clinic header, act lines, total, échéancier) labeled "DEVIS" with no fiscal stamp/VAT/QR.

## API Contract

**Dental acts (global; writes `AdminOnly`)**
- `GET /api/dental-acts?query&category&includeInactive` → `DentalActDto[]`
- `POST /api/dental-acts` · `PUT /api/dental-acts/{id}` · `DELETE /api/dental-acts/{id}` (soft) · `POST /api/dental-acts/confirm`

**Odontogram (patient-nested; `[Authorize]`)**
- `GET /api/patients/{patientId}/odontogram` → `ToothStateDto[]`
- `PUT /api/patients/{patientId}/odontogram/{toothNumber}` — `{ condition, surfaces?, note? }` → `ToothStateDto` (condition `Sain` clears the row)

**Treatment plans (`[Authorize]`; cancel = `AdminOrDoctor`)**
- `GET /api/treatment-plans?patientId&status&from&to` → `TreatmentPlanDto[]`
- `GET /api/treatment-plans/{id}` → `TreatmentPlanDto`
- `POST /api/treatment-plans` — `{ patientId, title, notes?, items[], installments[] }`
- `PUT /api/treatment-plans/{id}` — draft only
- `POST /api/treatment-plans/{id}/accept`
- `POST /api/treatment-plans/{id}/installments/{installmentId}/payments` — `{ amount, method, paidOn }`
- `POST /api/treatment-plans/{id}/items/{itemId}/done` — `{ doneOn?, linkedDentalRecordId? }`
- `POST /api/treatment-plans/{id}/cancel` — `{ reason }`
- `DELETE /api/treatment-plans/{id}` — draft only
- `GET /api/treatment-plans/{id}/devis-pdf` → `application/pdf`

All failures use the existing `{ error }` contract; French messages.

## Data / Schema Changes
- **`DentalActCode`** (global `AggregateRoot`, no `ClinicId`, no query filter): `CodeActe` (the `DCH…` code, unique), `DesignationFr`, `LettreCle` (default `"D"`), `Coefficient` `decimal(18,3)?` (nullable, pending NGAP), `Category` (one of the 6 sections), `RequiresAccordPrealable` (bool), `DefaultFee` `decimal(18,3)?`, `IsActive`, `IsProvisional`. Seeded via a deterministic-GUID `DentalActCatalogSeed` + migration `InsertData` (source: `dental-nomenclature-source.md`).
- **`ToothState`** (child of Patient, no `ClinicId`): `PatientId`, `ToothNumber` (FDI-validated), `Condition` (enum int), `Surfaces` (string?, subset of `MODVL`), `Note?`; unique index `(PatientId, ToothNumber)`.
- **`TreatmentPlan`** (root, clinic-scoped, one `HasQueryFilter`): `ClinicId`, `PatientId`, `Number?` (`AAAA-NNNN`), `Status` (enum int), `Title`, `Notes?`, `AcceptedDate?`, `CancellationReason?`; children:
  - **`TreatmentPlanItem`**: `DentalActCodeId?`, `CodeDch?`, `DesignationFr`, `ToothNumbers` (JSON `text` + `ValueComparer`), `PlannedCost` `decimal(18,3)`, `ItemStatus` (Planned/Done), `DoneDate?`, `LinkedDentalRecordId?`.
  - **`Installment`**: `DueDate`, `Amount` `decimal(18,3)`, `AmountPaid` `decimal(18,3)`, latest `Method?`/`PaidOn?`.
- **Enums:** `ToothCondition {Sain, Carie, Obturation, Couronne, TraitementDeCanal, Bridge, Implant, ExtraitAbsent, ATraiter}`, `TreatmentPlanStatus {Draft, Accepted, InProgress, Completed, Cancelled}`, `TreatmentPlanItemStatus {Planned, Done}`. Reuses existing `PaymentMethod`.
- New EF migration(s) (auto-applied on startup); new DbSets (`DentalActCode` in the global/not-clinic-scoped group).
- Frontend: new `RealtimeResource` keys `treatmentplans` + `dentalacts` (odontogram rides `patients`); new sidebar entries (`/dental-acts` under the admin block, `Plans / Devis` in main nav).

## Out of Scope
- Any change to the fiscal `Invoice` entity, VAT/timbre, or the TTN El-Fatoora e-invoicing path.
- The numeric cotation coefficients per act (NGAP arrêté) — acts seed with `Coefficient` null, admin-editable; CNAM dental reimbursement estimation is not built.
- Non-CNAM acts (couronne céramique, bridge fixe, implant, blanchiment) are entered as free-text plan lines, not catalog acts.
- A new backend plan→invoice conversion command (reuse the existing frontend record→invoice bridge).
- Imaging / radio-panoramique storage; CNAM tiers-payant for dental; teleconsultation; patient portal.
- Per-installment multi-payment history as immutable rows (v1 accumulates `AmountPaid` + stores the latest method/date).

## Edge Cases (Critical only)
- Installment schedule that doesn't reconcile to total planned cost → rejected (French message); rounding remainder lands on the last installment.
- Installment payment exceeding the installment's remaining balance → refused.
- Accept with zero items → refused; numbering collision → recompute-and-retry (bounded).
- Edit/delete allowed only in Draft; Cancel keeps the number and requires a reason.
- Setting a tooth to `Sain` removes its `ToothState` row; `Surfaces` must be a subset of `MODVL`.

## Test-worthy behaviors (handed to /test-small-feature, not planned here)
Numbering retry on collision · installment overpayment guard · installments-sum/rounding validation · tenant isolation for `TreatmentPlan` (root) and odontogram (via patient) · FDI + surfaces validation · act-catalog CRUD, soft-deactivate, and provisional-confirm.
