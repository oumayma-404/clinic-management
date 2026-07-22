# Feature Specification: Clinical Loop Integration (Treatment Plan as the Spine)

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-22
**Scope:** Full
**Feature:** Connect the four clinical modules that today work in isolation — odontogram, treatment plan, appointment, dental record — so a dentist can diagnose on the chart → generate a plan → schedule a plan step → execute it, with the plan, odontogram, and record staying in sync automatically. Also surface medical/allergy alerts at the point of care.

## Overview
Each clinical feature exists and is polished on its own, but the loop is not wired: the odontogram is read-only (it only mirrors *completed* acts, never a diagnosis), treatment plans are typed from scratch with no link to charted teeth, plan items can't be scheduled, and marking a plan item "Réalisé" is a manual toggle with no tie to the acte actually performed. This feature makes `TreatmentPlanItem` the backbone: a charted diagnosis feeds the plan, a plan item is bookable as an appointment, and recording the acte auto-completes the item and updates the chart. It also puts allergy/flag/medical-history warnings in front of the dentist while they record treatment. Builds on the shipped `dental-core` feature; adds only the connective tissue between its parts.

## What Changes

### Odontogram becomes a diagnosis tool (not just history)
- The odontogram tab gains a **write path**: clicking a tooth lets the dentist record a **diagnostic condition** (e.g. carie, fracture, à extraire) directly, persisted as a `ToothState` marked as **diagnostic** (source = `Diagnosis`) — distinct from the existing treatment-derived states (source = `Treatment`) still written by the dental-record flow.
- Diagnostic and treatment states are visually distinguishable on the chart (e.g. diagnosis = outline/amber, completed treatment = filled), and a tooth's popover lists both its diagnosis history and its treatment history.

### Diagnosis → treatment plan
- A **"Créer un plan depuis l'odontogramme"** action seeds a new treatment plan with one draft item per tooth that has an open (not-yet-treated) diagnosis, pre-filling `ToothNumbers` and a **suggested act** (mapped from the diagnostic condition to a `ProcedureType`/`DentalActCode` where a mapping exists; blank designation otherwise). All seeded lines remain fully editable before the plan is saved.

### Treatment plan item → appointment
- `Appointment` gains an optional `TreatmentPlanItemId`. From a treatment plan, a per-item **"Planifier"** action opens the create-appointment dialog pre-filled with the patient, the item's procedure, and its teeth.
- The create-appointment dialog gains an optional **"Étape du plan"** picker (the patient's open plan items); selecting one links the appointment and prefills procedure + teeth.

### Treatment plan item → dental record (auto-complete)
- The record-entry modal (`patient-record-modal`) can **link an acte to an open plan item** (auto-offered when the appointment being recorded is itself linked to a plan item). On save, the linked item is marked **Réalisé automatically** with its `LinkedDentalRecordId` set — and the resulting condition writes the treatment `ToothState` as today, which also clears the matching open diagnosis on those teeth.
- The manual, evidence-free **"Réalisé" toggle is removed** from the plan "Gérer" dialog; an item becomes Réalisé only through a linked dental record. (Un-linking / correcting is via editing the dental record.)

### Medical alerts at the point of care
- The record-entry modal and the odontogram tab show a prominent, always-visible **alert banner** listing the patient's active flags, allergies, and key medical-history items (reusing the data already on the patient page) so the dentist sees contraindications before treating.

## Acceptance Criteria
- **AC-1:** From the odontogram tab, a dentist can record a diagnostic condition on a tooth; it persists (survives reload) as a `ToothState` with source `Diagnosis` and is visually distinct from treatment-derived states.
- **AC-2:** "Créer un plan depuis l'odontogramme" opens a new plan pre-populated with one editable draft line per tooth carrying an open diagnosis, teeth pre-filled and a suggested act where a condition→act mapping exists.
- **AC-3:** `Appointment.TreatmentPlanItemId` exists (nullable); an appointment can be created linked to a plan item, prefilling patient, procedure, and teeth from that item.
- **AC-4:** Recording a dental record linked to a plan item marks that item `Done` with `LinkedDentalRecordId` populated, with no separate manual click; the plan's completion status reflects it.
- **AC-5:** Completing a plan item via a dental record writes the treatment `ToothState` (as today) and clears/closes the corresponding open diagnosis on the same teeth, so the chart shows the tooth as treated, not still-diagnosed.
- **AC-6:** The manual "Réalisé" toggle no longer exists; a plan item can only reach `Done` through a linked dental record.
- **AC-7:** The record-entry modal and odontogram tab display an alert banner with the patient's active flags, allergies, and medical-history highlights; a patient with none shows no banner (not an empty box).
- **AC-8:** All new odontogram-write, plan-seed, appointment-link, and record-link paths are clinic-scoped: they never read or write another clinic's patient/plan/appointment.

## API Contract
### POST /api/patients/{patientId}/odontogram/conditions
Records a diagnostic tooth condition. Request: `{ toothNumber: int, condition: string, note?: string }`
Response 2XX: the updated tooth state(s). Errors: `404` (patient/other clinic), `400 { error }` (invalid FDI tooth / condition).

### DELETE /api/patients/{patientId}/odontogram/conditions/{toothStateId}
Removes a diagnostic condition (diagnostic-source only; treatment states stay record-owned). Errors: `404`, `400 { error }`.

### Changed — create/update appointment
Request gains optional `treatmentPlanItemId: guid|null`. Response `AppointmentDto` gains `treatmentPlanItemId`. Validation: the item must belong to the same patient + clinic, else `400 { error }`.

### Changed — create/update dental record
Request gains optional `treatmentPlanItemId: guid|null` per acte (or at record level). When set, the handler marks the item done with `LinkedDentalRecordId`. Errors: `400 { error }` if the item is already done or belongs to another patient.

## Data / Schema Changes
- **`Appointment.TreatmentPlanItemId`** — new nullable `Guid?` FK to `TreatmentPlanItem`. EF config + migration. (A dormant `AddDentalRecordAppointmentId` migration already hints this linkage was intended — reconcile with it.)
- **`ToothState.Source`** — new enum/flag distinguishing `Diagnosis` vs `Treatment` (default `Treatment` for existing rows so current behavior is unchanged). Diagnostic rows are patient-owned (not cascade-deleted with a dental record); treatment rows stay record-owned as today.
- **Condition→act mapping** — reuse `ProcedureType.ResultingCondition` in reverse (condition ⇒ candidate act) for AC-2 suggestions; no new table required.
- No change to the two act catalogs themselves (that consolidation is a separate concern).

## Out of Scope
- Merging the two act catalogs (`ProcedureType` vs CNAM `DentalActCode`) into one — separate effort.
- Billing/invoice links from the treatment plan — covered by the `unified-billing-ledger` spec.
- Drag-to-reschedule, recurring appointments, or multi-visit plan sequencing/auto-scheduling of all steps at once.
- Pediatric mixed-dentition in a single dental record (the existing single `IsAdultTeeth` flag is unchanged).
- A full periodontal chart (pockets/mobility); this is tooth-condition charting only.

## Edge Cases (Critical only)
- **Diagnosis already treated:** completing a plan item / recording a treatment on a tooth closes any matching open diagnosis so the chart never shows a tooth as both "à traiter" and "traité".
- **Appointment linked to a plan item, then item completed elsewhere:** booking still succeeds; the link is informational — completing the item via any dental record is what sets `Done`.
- **Deleting a dental record that completed a plan item:** the item reverts to its prior open status and `LinkedDentalRecordId` clears (no orphaned "Réalisé" with no evidence).
- **Plan `Accept()`/`Complete()` invariants:** `Complete()` still requires all items `Done`; with the manual toggle gone, a plan completes only when every item has a linked dental record.
- **Allergy banner data:** reuses the patient's already-loaded flags/allergies/history — no extra blocking fetch; if that data failed to load, the modal still opens (banner simply absent), never blocking treatment entry.
