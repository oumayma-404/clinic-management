# Feature Specification: Finalization Pass — Close Adoption-Review Gaps

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-23
**Scope:** Full
**Feature:** One consolidated pass that closes every functional gap found in `FUNCTIONAL_ADOPTION_REVIEW.md` (search, patient-context booking, document re-access, money-screen agreement, El Fatoora honesty, nav grouping, and polish).

## Overview
The clinical/fiscal core is sound but held back by a handful of unfinished wires at high-traffic touch points. This feature fixes all of them in one pass — no architectural change, additive/wiring-level only, and Cloud + Local behavior preserved. Anchors below were verified against the codebase during review.

## What Changes

**A. Blocker loops**
- Patient search filters server-side (name + phone) instead of returning every patient.
- "Planifier un rendez-vous" from a patient opens the booking dialog with that patient preselected.
- A patient's saved medical documents are listed on the patient page and can be reopened for edit.

**B. Money trust**
- The daily Caisse includes treatment-plan installment cash, matching the Dashboard.
- "Solde patient" shows two clearly labeled subtotals (factures / échéanciers) and a combined total explicitly marked as the sum of both — no single silently-blended figure.
- The El Fatoora submit button appears only when the clinic has enabled TTN e-invoicing, and a missing signing certificate surfaces a clear up-front message instead of a silent retry-to-Failed.

**C. Polish**
- Seeding a treatment plan from the odontogram carries each act's catalog price.
- Creating a patient navigates straight to that patient's detail page.
- The AI chat's action responses are in French.
- The "Signaler ce patient" toggle persists a patient flag (feeding the existing "Urgents" KPI / flagged filter); the odontogram tab's misleading "(lecture seule)" caption is corrected.

**D. Navigation & dashboard**
- The sidebar is grouped into sections (Quotidien / Clinique / Finances / Gestion) with config/catalog screens moved into a collapsible admin "Configuration" group; "Mon profil" moves to the user menu; the orphan `/recurring-series` is linked under Rendez-vous; redundant top-level `/records` and `/files` entries are removed from the daily rail.
- Dashboard KPI cards are clickable and navigate to the relevant screen.

## Acceptance Criteria
- **AC-1 (search):** `GET /patients?searchTerm=&limit=` filters by first name, last name, or phone (case- and accent-insensitive) and caps results at `limit`. The header search and the patients-table search both return only matching patients.
- **AC-2 (booking):** From a patient, "Planifier un rendez-vous" opens `CreateAppointmentDialog` with that patient already selected (via `?patientId=` on `/appointments`).
- **AC-3 (documents tab):** The patient detail page has a "Documents" tab listing that patient's saved `MedicalDocument`s (type, date). Selecting one reopens the editor in edit mode via `?id=`. Empty state shows a French "aucun document" message.
- **AC-4 (caisse):** A treatment-plan installment collected today appears in the Caisse cash-in total; the Caisse figure equals the Dashboard "Recettes" contribution for the same period.
- **AC-5 (balance):** "Solde patient" renders `Solde factures`, `Solde échéanciers`, and a `Total dû` labeled as the sum of both sources; each subtotal is independently correct.
- **AC-6 (El Fatoora):** The submit-to-El-Fatoora control is hidden/disabled when `clinic.TtnEInvoicingEnabled` is false. When enabled but the signing certificate is missing, the UI shows a clear "certificat requis" state up front rather than the invoice silently retrying to Failed.
- **AC-7 (odontogram→plan):** A plan seeded from the odontogram pre-fills each line's planned cost from the matching procedure-type/dental-act default (0 only when no catalog match exists).
- **AC-8 (post-create nav):** After creating a patient, the app navigates to `/patients/{newId}`.
- **AC-9 (AI French):** All user-facing AI chat action responses (success/failure/not-found) are in French.
- **AC-10 (flags):** Toggling "Signaler ce patient" on persists an active patient flag (with the note); toggling off deactivates it. The change is reflected in the "Urgents" KPI and the flagged filter.
- **AC-11 (nav):** The sidebar renders grouped sections; config/catalog screens are no longer in the daily rail; "Mon profil" is reachable from the user menu; `/recurring-series` is reachable from a link under Rendez-vous; the odontogram caption no longer says "(lecture seule)".
- **AC-12 (dashboard):** Clicking "Rendez-vous du jour", "Urgents", and "Créances" KPI cards navigates to `/appointments`, the flagged-patients view, and `/creances` respectively.

## API Contract
### GET /api/patients
Request (query): `searchTerm?: string`, `limit?: int` (both optional; existing clinic scoping unchanged).
Response 2XX: same `PatientDto[]` shape, now filtered + capped.
Note: `searchTerm`/`limit` are already sent by the frontend; this wires them through `GetPatientsQuery` → handler → repository. When absent, behavior is unchanged (all patients).

### Patient flag persistence (AC-10)
Persist through the existing patient update path or a small dedicated flag endpoint, following current controller patterns. The toggle maps to a single active `PatientFlag` (default type `HighPriority`) carrying the note; toggling off deactivates the active flag(s). No new enum or entity.

## Data / Schema Changes
- **None.** All required entities/fields already exist (`PatientFlag` + `Patient.AddFlag/RemoveFlag`, `Clinic.TtnEInvoicingEnabled`, `ITreatmentPlanRepository.GetInstallmentCollectedBetweenAsync`, `IMedicalDocumentRepository` by patient, `MedicalDocument` load-by-id). DTOs may gain presentational fields (e.g. labeled balance subtotals) but no migration.

## Out of Scope
- A real Invoice↔TreatmentPlan link or a "Facturer ce plan" flow (balance uses labeled subtotals, not automatic reconciliation).
- Changing the AI chat model or "hide the chat" product decision (only the French translation is in scope).
- Verifying/enabling TTN **Production** transport (only the gating + missing-cert UX is in scope).
- Restructuring config screens into new Settings sub-pages (they stay at their current routes, just regrouped in the sidebar).
- Server-side pagination beyond honoring `limit`.

## Edge Cases (Critical only)
- Search: accent/case-insensitive match ("Amine" matches "amïne"); `<2` chars behaves as today (no query); phone match tolerates the +216/local forms via the existing normalization.
- Balance: a patient with only invoices (or only a devis) shows the other subtotal as 0, not blank.
- El Fatoora: the button state must reflect the clinic toggle even on already-issued invoices created before the toggle was set.
- Caisse: installment cash is counted by payment date within the selected day/range (consistent with how invoice payments are counted).
- Flags: re-toggling on/off must not create duplicate active flags.
