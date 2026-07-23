# Progress: Finalization Pass — Close Adoption-Review Gaps

**Started:** 2026-07-23
**Type:** Small
**Branch:** feature/windows-desktop-app

## Status
- [x] Implementation
- [x] Quality checks (build, lint, typecheck)
- [ ] Tests (handled by /test-small-feature)

## Quality checks
- Backend: `dotnet build ClinicManagement.sln` → 0 errors, 0 new warnings. (Repo baseline is CS8632 +
  a broad CS8602 family across controllers; the one CS8602 left in a touched file — `PatientsController.cs:73`,
  `result.Value.Id` in the pre-existing CreatePatient action I did not modify — is baseline, matching ~6 other
  controllers. `AIActionService` was fully rewritten, so its `result.Value` deref was guarded with `!`.)
- Frontend: `npx tsc --noEmit` → 0 errors. `next build` → success (first run hit the known flaky
  "Collecting page data / PageNotFoundError" on a fresh `.next`; a rebuild is green — environmental, not a code fault).
- Tests skipped here by design (→ /test-small-feature). No unit test constructs the changed handler ctors, so no test-compile break.

## Files Changed
Backend:
- `Application/Features/Patients/Queries/GetPatientsQuery.cs` — searchTerm + limit, accent-insensitive filter, map flags (fix 1)
- `API/Controllers/PatientsController.cs` — GetPatients accepts searchTerm/limit query params (fix 1)
- `Infrastructure/Repositories/PatientRepository.cs` — include active flags in list + GetById (fix 1/10)
- `Application/Features/Billing/Queries/GetCaisseSummaryQuery.cs` — add installment cash to caisse (fix 4)
- `Application/Features/Patients/Commands/UpdatePatientCommand.cs` — IsFlagged/FlagNotes wiring (fix 10)
- `Application/Features/Patients/Commands/CreatePatientCommand.cs` — IsFlagged/FlagNotes + flag DTO map (fix 10)
- `Infrastructure/Services/AIActionService.cs` — all user-facing responses → French + fr-FR dates (fix 9)

Frontend:
- `app/appointments/page.tsx` — read ?patientId, preselect patient in create dialog (fix 2)
- `components/create-appointment-dialog.tsx` — defaultPatientId prop (fix 2)
- `components/edit-patient-dialog.tsx` — enable flag toggle, send isFlagged/flagNotes, pass created patient to onSuccess (fix 8/10)
- `lib/api/patients.ts` — isFlagged/flagNotes on create/update payloads (fix 8/10)
- `app/patients/page.tsx` — navigate to new patient after create; read ?flagged=1 (fix 8/12)
- `app/patients/[id]/page.tsx` — Documents tab, balance subtotal labels, odontogram caption (fix 3/5/10)
- `lib/api/medical-documents.ts` — (already had list/get; consumed by new tab) (fix 3)
- `components/odontogram.tsx` + `components/treatment-plans/treatment-plan-form-modal.tsx` — seed cost from procedure match (fix 7)
- `components/stats-card.tsx` + `app/page.tsx` — clickable KPI cards (fix 12)
- `components/dashboard-sidebar.tsx` — grouped sections, config group, recurring-series linked, records/files removed, profile removed (fix 11)
- `components/dashboard-header.tsx` — Mon profil in user menu (fix 11)
- `components/factures/invoices-table.tsx` — gate El Fatoora button on clinic TTN toggle (fix 6)

## Working tree note (start of session)
Pre-existing unrelated modifications NOT part of this feature (excluded from any commit):
- Modified: root + per-project `CLAUDE.md` files (user's own doc refresh), `desktop/CLAUDE.md`, `web/**/CLAUDE.md`
- Untracked: `FUNCTIONAL_ADOPTION_REVIEW.md`, `api/ClinicManagement.UnitTests/CLAUDE.md`, `packaging/CLAUDE.md`
- Untracked (this feature): `features/finalization-pass/`

## Scope note (forced small pipeline)
User explicitly forced the small-feature pipeline for all 12 fixes ("do not challenge my choice
for small feature it can handle it, all fixes now"). Real surface exceeds the ~10-file small
envelope (~20 files, full-stack), accepted deliberately per user direction — not auto-escalating.

## Files Changed
(tracked below as edits land)

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Search runs as an in-memory accent-insensitive filter in the handler (no Postgres `unaccent`/migration) | Matches the existing "load-all-then-map" behavior; reliable diacritic-insensitivity without a DB extension; spec said no schema change |
| Patient LIST now includes active flags (repo include + list DTO) | Required so the flagged filter (AC-10) and the "Urgents" drill-through (AC-12) reflect flag state; additive read |
| `GetByIdAsync` now `.Include(Flags)` | Needed so the update path can detect/deactivate the active flag; additive read used by other handlers harmlessly |
| Flag toggle maps to a single active `HighPriority` `PatientFlag` (desc. "Patient signalé") | The domain uses typed flags; the UI toggle is boolean — chose the highest-priority type, matching how "Urgents" already counts any active flag |
| Nav "Configuration" rendered as a labeled section (not an independently-collapsible group) | The whole sidebar already collapses; a labeled section is lower-risk and satisfies "config out of the daily rail" |
| Fix 5 done as label/caption change only (DTO already carried invoice/installment split) | Backend already returned the split; only the presentation needed the explicit "= factures + échéanciers" |
| `AIActionService` `result.Value!` null-forgiving | Keep the fully-rewritten file warning-free (non-null after the IsFailure guard) |

## Significant Deviations
- **DEV-1 (El Fatoora "cert requis up front", fix 6/AC-6):** the submit button is now gated on
  `clinic.TtnEInvoicingEnabled` (the primary AC). The "certificat requis" surfacing relies on the EXISTING
  submit-time behaviour — a submit that hits the missing-cert transient failure returns the invoice `Queued`
  with `eInvoiceLastError`, which `handleSubmitEInvoice` already shows as a `toast.warning`, and the badge
  tooltip shows it too. I did **not** change the outbox retry semantics to fail-fast on a missing cert
  (that would alter `EInvoiceService`/outbox behaviour — out of the spec's additive/wiring scope). Flagged
  here in case a true fail-fast is wanted later.

## Scope confirmation
All 12 spec fixes implemented in one pass. Forced-small pipeline (user-directed) — real surface ≈24 files,
full-stack; not auto-escalated per explicit user direction.
