# Progress: CNAM — Bulletin de soins (BS1), first slice

**Started:** 2026-07-17
**Type:** Small
**Branch:** feature/windows-desktop-app (user chose to continue on current branch)

## Status
- [x] Implementation
- [x] Quality checks (build, lint, typecheck)
- [ ] Tests (handled by /test-small-feature)

## Quality check results
- Backend: `dotnet build` Infrastructure + API → 0 errors, 0 new warnings (47 pre-existing warnings
  in untouched files: entity CS8618, lowercase migration-name CS8981, AIActionService CS8602, etc.).
  API host built to a scratch `-o` dir to avoid the running-app bin lock.
- Frontend: `npx tsc --noEmit` → 0 errors. `npm run build` (next build) → success, 17/17 pages
  (first attempt failed at prerendering /appointments — a stale `.next` cache false failure per
  LEARNINGS; `rm -rf .next` + rebuild was green). ESLint not installed (documented in LEARNINGS —
  FE gate is tsc + build).
- EF migration hand-authored (dotnet ef WDAC-blocked): `20260717120000_AddCnamBulletinFields`
  (+ .Designer.cs derived mechanically from the updated snapshot). **Must be regenerated/verified
  with the EF tool in an unrestricted environment before merge.**

## Files Changed
Backend — Domain:
- ValueObjects/CnamInfo.cs (new)
- Entities/Patient.cs (CnamInfo + UpdateCnamInfo)
- Entities/Clinic.cs (MatriculeFiscal + Update param)
- Entities/Doctor.cs (CodeProfessionnelSante + ctor/Update params)
Backend — Infrastructure:
- Persistence/Configurations/{Patient,Clinic,Doctor}Configuration.cs
- Migrations/20260717120000_AddCnamBulletinFields.cs (new, hand-authored)
- Migrations/20260717120000_AddCnamBulletinFields.Designer.cs (new)
- Migrations/ApplicationDbContextModelSnapshot.cs
- Services/PdfGenerationService.cs (2-page BS1 composer + helpers)
Backend — Application:
- DTOs/CnamInfoDto.cs (new, + ToDomain/ToDto mapping)
- DTOs/{PatientDto,ClinicDto,CreateClinicRequest(DoctorDto)}.cs
- Features/Patients/Commands/{CreatePatientCommand,UpdatePatientCommand}.cs
- Features/Patients/Queries/{GetPatientQuery,GetPatientsQuery}.cs
- Features/Clinics/Commands/{UpdateClinicCommand,UpdateDoctorsCommand}.cs
- Features/Clinics/Queries/GetUserStatusQuery.cs
- Features/Documents/Commands/CreateMedicalDocumentCommand.cs (bulletin-cnam filename)
Backend — API:
- Models/UpdateClinicRequest.cs, Controllers/ClinicsController.cs (MatriculeFiscal)
Frontend:
- lib/api/types.ts (CnamInfo + PatientDto.cnamInfo)
- lib/api/clinics.ts (DoctorDto.codeProfessionnelSante, ClinicDto.matriculeFiscal, update())
- lib/api/patients.ts (create cnamInfo)
- app/documents/page.tsx (5th card)
- components/document-editor-content.tsx (bulletin-cnam editor mode + preview)
- components/edit-patient-dialog.tsx (CNAM identity block)
- components/clinic-settings.tsx (Matricule Fiscal + per-doctor code)

## BS1 PDF fidelity pass (post-review, against official CNAM BS1)
Corrected `PdfGenerationService.GenerateBulletinCnamPdf` against the real official BS1 form
(user supplied cnam.nat.tn/doc/upload/BS1.pdf):
- Replaced invented roman-numeral sections with the official headings ("A REMPLIR PAR L'ASSURE SOCIAL"
  → L'assuré social / Le malade sub-boxes; "A REMPLIR PAR LES PROFESSIONNELS DE SANTE").
- Régime (CNSS/CNRPS/Convention bilatérale), malade lien (assuré social/conjoint/enfant+rang/
  ascendant+rang), and care type (APCI+code/MO/Hospitalisation/Suivi de grossesse) now render as
  ☒/☐ checkbox rows, not text fields.
- Dental act tables gained the two official columns Code Prof. santé + Cachet et signature (7 cols total);
  the doctor code auto-fills the code column on filled rows.
- Tooth chart now shows permanent (11–48) AND temporary (51–85) dentition with D/G markers.
- Official footnotes + the "déposer sous 60 jours" notice.
- Verified: `dotnet build` Infrastructure → 0 errors / 0 warnings in the file.
- ⚠ NOT yet visually rendered (QuestPDF `Table` is new to this file; PDF rendering is operator-verified).
  Render one sample bulletin and eyeball it against BS1.pdf before merge.
- Still out of scope: the verso non-dental tables (Consultations/visites, Actes médicaux/paramédicaux,
  Biologie, Pharmacie, Accouchement/Hospitalisation, vignettes) — not needed for a dental clinic.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Added `CnamInfoMapping` extension methods (ToDomain/ToDto) in CnamInfoDto.cs | Internal helper to avoid duplicating the 8-field VO↔DTO mapping across 4 handlers; no public API/behavior change. |
| Added optional trailing `codeProfessionnelSante` param to `Doctor` ctor | Lets new doctors carry the code without a post-create Update() bumping UpdatedAt; backward-compatible default. |
| BS1 PDF is a best-effort faithful layout (FDI chart = static reference grid; ☒/☐ checkboxes) | No official BS1 reference image available; matches the structure described in the spec. Flagged for design review. |

## Significant Deviations
DEV-1 — UpdatePatient CNAM clear semantics. Spec implies CNAM is edited like insurance, but a null/omitted
`CnamInfo` on UpdatePatientCommand **leaves it unchanged** (present-but-empty clears it), unlike InsuranceInfo
which clears on null. Rationale: avoids an unrelated partial patient update silently wiping CNAM data; the edit
dialog always sends a present block so clearing still works. Approved implicitly by the one-pass scope decision;
surfaced here for review.

## Working tree note (start of session)
Large pre-existing uncommitted work from other features (graceful-error-handling,
patient-record-payments-summary, post-visit-review, windows-desktop-app phases) is present.
Only files listed under "Files Changed" belong to this feature; everything else is excluded
from this feature's commits. Stage explicitly by path.

## Scope note
Spec is Type: Small but is full-pipeline sized (~20+ files, 4 backend layers, a 2-page BS1 PDF).
User explicitly chose to proceed as a single small-feature pass (AskUserQuestion, 2026-07-17).
BS1 PDF is a best-effort faithful layout (no official reference image available).

## Files Changed
(tracked below as implemented)

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|

## Significant Deviations
(none yet)
