# Progress: CNAM dental nomenclature lookup + indicative reimbursement

**Started:** 2026-07-17
**Type:** Small
**Branch:** feature/windows-desktop-app (continuing on current branch — extends the shipped cnam-bulletin-soins, whose code lives here)

## Status
- [x] Implementation
- [x] Quality checks (build, lint, typecheck)
- [x] Tests (added — see Test Plan / Tests Run below)

## Test Plan
| AC | Action | Target file | Notes |
|----|--------|-------------|-------|
| AC-1 (filter logic q/category, not clinic-scoped) | New test class | UnitTests/Features/CnamNomenclature/GetCnamNomenclatureQueryHandlerTests.cs | Mocked provider (deterministic 4-entry set): full list, blank→no-filter, category case-insensitive, unknown category→empty, free-text on code/designation/lettre clé, q+category combined, trim. Handler ctor takes only provider+logger → proves not clinic-scoped. |
| AC-1 (curated data integrity) | New test class | UnitTests/Infrastructure/Services/CnamNomenclatureProviderTests.cs | Real provider: non-empty, all 5 categories present, every entry has code+designation, lettre clé ∈ {CD,CDS,VD,D,RD} (guards the FE estimate contract), coefficient>0, unique code acte. |
| AC-1 (requires authenticated user) | Coverage note (existing test) | UnitTests/Api/ControllerAuthorizationCoverageTests.cs | Controller is `[Authorize]` → absent from the anonymous allow-list; the existing reflection guard enforces it stays non-anonymous. Run green with the new controller present. |

### Coverage notes (ACs with no backend/unit surface)
- **AC-2, AC-3, AC-4, AC-6** are **frontend-only** (bulletin editor: Code acte autocomplete fills code+cotation & stays editable; free-text still works; per-act + total indicative estimate, APCI-aware; French labels; four other document types gated out). The **web project has no test runner** (`package.json` scripts = dev/build/start/lint only; no vitest/jest/playwright) — these are covered by the `npx tsc --noEmit` + `next build` gate run at implementation time. Adding a TS test framework is out of scope for a small-feature test pass.
- **AC-4 estimate math** (`estimateReimbursement` in `lib/api/cnam-nomenclature.ts`) is pure TS logic that would be unit-test-worthy, but there is no TS test harness to host it; its correctness contract (lettre clé keys, positive coefficient) is guarded backend-side by `CnamNomenclatureProviderTests`.
- **AC-5 (estimate never persisted / never on the PDF)** is satisfied by construction (estimate not added to the `acts` array, not passed to `PdfGenerationService`, absent from the preview panel) — verified by code inspection + the unchanged `ContentJson` shape; no serialization path to test.

## Tests Run
| Suite | Filter | Result |
|-------|--------|--------|
| Unit | `FullyQualifiedName~CnamNomenclature\|FullyQualifiedName~ControllerAuthorizationCoverage` | **17 passed, 0 failed, 0 skipped** |

Run recipe (Smart App Control ON + running-app bin lock → isolated OutDir + `dotnet vstest`, per LEARNINGS):
`dotnet build ClinicManagement.UnitTests.csproj -p:OutDir=<scratch>/utbuild/` then
`dotnet vstest <scratch>/utbuild/ClinicManagement.UnitTests.dll --TestCaseFilter:"FullyQualifiedName~CnamNomenclature|FullyQualifiedName~ControllerAuthorizationCoverage"`.
The test-project build compiled the whole graph (Application + Infrastructure + API) at **0 errors** = final quality gate.

## Quality check results
- Backend: `dotnet build` Infrastructure (leaf) + API host (to a scratch `-o` dir, avoiding the
  running-app bin lock) → **0 errors**. 13 pre-existing warnings, all in untouched files
  (AIActionService, AppointmentsController, ProcedureTypesController, PatientsController,
  MedicalDocumentsController, FileUploadOperationFilter, Program.cs Hangfire obsolete). **No new
  warning in any of the four new/edited backend files.**
- Frontend: `npx tsc --noEmit` → **0 errors**. `npm run build` (next build, after `rm -rf .next`) →
  **success**; `/documents/[type]` (the editor) compiled. ESLint not installed (documented in
  LEARNINGS — FE gate is tsc + build).
- No EF migration (feature adds no schema — static in-code reference data only).

## Recovery note (2026-07-17) — reverted work reconstructed
The working tree had been reset (`git restore`), discarding every uncommitted tracked-file edit for
cnam-bulletin-soins, cnam-nomenclature-lookup, and other features (only new untracked files survived).
None of this was ever committed (verified against all branches). Reconstructed both cnam features:
- Backend cnam identity + endpoint recovered/re-applied onto current HEAD (post-facturation merge), so
  the invoices/reminders features are untouched. `MatriculeFiscal` is now owned by the facturation
  feature; cnam adds only `Doctor.CodeProfessionnelSante` + `Patient.Cnam*` columns.
- Bulletin editor UI + BS1 PDF composer were NOT in any recovery store → rebuilt from spec (functional;
  BS1 PDF is best-effort, as originally).
- ⚠ **Migrations need `dotnet ef` regen/verify** (WDAC-blocked here): the hand-authored
  `20260717120000_AddCnamBulletinFields` (+ its `.Designer.cs`) and the model snapshot were edited by
  hand to drop the `MatriculeFiscal` collision and add the cnam columns. Runtime `Database.Migrate()`
  is correct (Up() adds only the missing columns); the Designer/snapshot chain should be regenerated
  with the EF tool in an unrestricted environment before production.
- `DentalRecordPostVisitCompletionTests.cs` (post-visit-review, deferred) quarantined as `.deferred`
  so the test project compiles.

Validation: backend `dotnet build` 0 errors; unit tests 22 passed (cnam + coverage + patient/procedure);
frontend `tsc` 0 errors + `next build` success.

## Working tree note (start of session)
Large pre-existing uncommitted work from other features (graceful-error-handling,
patient-record-payments-summary, post-visit-review, windows-desktop-app phases,
cnam-bulletin-soins) is present. Only files listed under "Files Changed" belong to this
feature; everything else is excluded from this feature's commits. Stage explicitly by path.

## Files Changed
Backend — Application:
- DTOs/CnamNomenclatureEntryDto.cs (new) — response shape { codeActe, designationFr, lettreCle, coefficient, category }
- Common/Interfaces/ICnamNomenclatureProvider.cs (new)
- Features/CnamNomenclature/Queries/GetCnamNomenclatureQuery.cs (new) — q + category filter, NOT clinic-scoped
Backend — Infrastructure:
- Services/CnamNomenclatureProvider.cs (new) — curated 26-entry static catalogue across all 5 categories, flagged PENDING VERIFICATION
- Extensions.cs — registered ICnamNomenclatureProvider as Singleton (near the connectivity probe)
Backend — API:
- Controllers/CnamNomenclatureController.cs (new) — [Authorize], GET /api/cnam-nomenclature?q=&category=
Frontend:
- lib/api/types.ts — CnamNomenclatureEntryDto
- lib/api/cnam-nomenclature.ts (new) — cnamNomenclatureApi.list(q?, category?) + reimbursement config + estimateReimbursement()
- components/document-editor-content.tsx — bulletin acts table: Code acte searchable lookup (Popover+Command) fills Code acte + Cotation; per-act indicative estimate + total (APCI-aware), editor-only

## Acceptance-criteria notes
- **AC-5 (estimate never persisted / never on PDF):** satisfied by construction — the estimate is
  derived at render time from the cotation cell; it is NOT added to the `acts` array, so the saved
  `ContentJson` and the `MedicalDocumentPdfData` are byte-for-byte the same shape as before, and the
  server-side `PdfGenerationService` never sees an estimate. The on-screen document preview panel was
  left untouched (still shows only Code acte / Cotation), so the estimate appears only in the editing UI.
- **AC-2/AC-3 (both cells editable, free text still works):** Code acte stays a normal editable `<Input>`;
  the catalog is an *adjacent* search button/popover. Picking fills Code acte + Cotation; typing free
  text is unchanged. The estimate blanks out for any act whose cotation isn't a known `<lettreCle>
  <coefficient>` (free-text acts, unknown clé, zero coefficient) — spec edge cases.
- **AC-6 (four other document types unchanged):** all new editor code is gated on
  `documentType === "bulletin-cnam"` (acts UI block + the nomenclature-load effect early-returns otherwise).

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Reimbursement config (dinar value per lettre clé + standard/APCI rates) lives as **frontend constants** in `lib/api/cnam-nomenclature.ts`, not the backend. | Spec lists the config under "What Changes" but does NOT pin its location, and the pinned API-contract response for `GET /api/cnam-nomenclature` deliberately excludes monetary fields. The estimate is explicitly editor-only / never persisted / never on the PDF, so FE constants keep the endpoint contract intact and avoid an unspecified second endpoint. Flagged in-code as PENDING VERIFICATION. |
| Estimate derived by parsing the act's **cotation** string (`<lettreCle> <coefficient>`) rather than tracking a separate structured selection per act. | Keeps the persisted `acts` shape unchanged (AC-5), makes the estimate follow live hand-edits to the cotation, and naturally blanks for free-text acts / unknown clé / zero coefficient (the spec's critical edge cases). Internal to the editor; no contract/behavior change. |
| Catalog lookup is an adjacent search **button + Popover/Command** beside the editable Code acte input (not an inline type-ahead replacing the field). | Reuses the existing procedure-type Popover+Command pattern verbatim while keeping Code acte fully free-text editable (AC-2/AC-3 "both cells remain editable; free text still possible"). |

## Significant Deviations
(none)
