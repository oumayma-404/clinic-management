# Progress: Derive & Confirm — Batch 2

**Started:** 2026-07-24
**Type:** Small (forced multi-item pass — user: "continue in one pass, you have the specs")
**Branch:** feature/windows-desktop-app

## Status
- [x] Implementation
- [x] Quality checks (build, typecheck, production build)
- [ ] Tests (handled by /test-small-feature)

## Working tree note (start of session)
Pre-existing unrelated changes present before this work (excluded from this feature's commits):
several `CLAUDE.md` docs, `FUNCTIONAL_ADOPTION_REVIEW.md`, `api/.../UnitTests/CLAUDE.md`, `packaging/CLAUDE.md`.
Stage this feature's files by path.

## Files Changed
- (P2-C) DELETED web/app/records/ and web/app/files/ (orphan routes; unreachable)
- (P2-C) web/CLAUDE.md, web/components/CLAUDE.md — removed stale /records + /files rows; fixed PatientSummaryModal note
- (P2-A/B) web/components/document-editor-content.tsx — read ?patientId (P2-A); "Renouveler" via renewedRef (P2-B)
- (P2-A) web/app/patients/[id]/page.tsx — "Nouvelle ordonnance" button (patient preset)
- (P1-B) web/components/create-appointment-dialog.tsx — new onCreated(appointmentId) callback
- (P1-B) web/app/waiting-list/page.tsx — Promouvoir opens prefilled dialog; auto-promote with new appt id
- (P1-D) web/components/factures/invoices-table.tsx — "Émettre et encaisser" compound action
- (P1-E) api/.../Features/Invoices/Commands/IssueInvoiceCommand.cs — best-effort auto-queue El Fatoora on issue
- (P1-A) api/.../Features/Appointments/Commands/UpdateAppointmentCommand.cs — re-anchor post-visit review to now on Completed

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Kept the /documents "Note d'honoraires" card + honoraires-launcher (spec said remove for "single invoice entry") | The launcher is a genuinely low-friction path (pick patient → invoice prefilled from un-invoiced records) — removing it subtracts value against this pass's "fewer clicks" north star. The cohesion "single entry" nit belongs to the (unpicked) Direction-2 pass. Low-regret: keep working functionality. |
| Waiting-list uses `defaultPatientId` (not `presetPatientId`) on the dialog | `presetPatientId` only applies during plan-scheduling (guarded on `isPlanScheduling`); `defaultPatientId` is the correct plain-preselect prop. |
| "Renouveler" gated to `documentType === "prescription"` | Matches the spec's ordonnance-reissue intent; avoids surprising renew behavior on other doc types. |
| El Fatoora auto-queue not gated on signing-cert presence | Mirrors the existing manual SubmitInvoiceToElFatooraCommand (also gates only on TtnEInvoicingEnabled + CanSubmitToElFatoora). Cert-gating (review #6) is a separate improvement for BOTH paths. |

## Significant Deviations
(none — the two below are scope deferrals, not in-file deviations)

## Deferred to their own passes (with reason)
- **P0-3 (Facturer + reconcile):** `Invoice` has no `TreatmentPlanId` column (only `DentalRecordId`) → needs a
  migration + devis↔invoice dedup in "Solde patient" + a double-count regression test. Money-trust sensitive.
- **P1-C (CNAM code record→invoice):** `DentalRecordAct` carries no dental-act/CNAM code today → needs a
  record-act model change (entity + migration + record UI) first. Its own feature.
- **P2-D (multi-tooth batch diagnosis):** optional UI-only enhancement, lowest priority.

## Quality check results
- Backend: `dotnet build ClinicManagement.sln --no-incremental` → Build succeeded, 0 errors. Scoped build of
  all changed Application files → 0 warnings (repo baseline is pre-existing CS8618 in Domain).
- Frontend: `npm run build` → exit 0 (clean; /records + /files no longer in the route list). `npx tsc --noEmit`
  → exit 0 after clearing the stale `.next` cache (the initial tsc failure was stale generated route-types for
  the deleted pages, not a source error).
- Tests deferred to /test-small-feature.
