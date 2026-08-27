# Progress: Document & CNAM Rendering Accuracy

**Started:** 2026-07-21
**Type:** Small
**Branch:** feature/windows-desktop-app (user declined a dedicated branch)

## Status
- [x] Implementation
- [x] Quality checks — `dotnet build ClinicManagement.Infrastructure.csproj` (compiles Application + Domain) → 0 errors, 0 new warnings (47 pre-existing CS8618, none in changed files). Backend-only spec. No public signature changed (ReadFrom/OrElse additive, FormatHonoraires private), so test projects still compile.
- [x] Tests — `CnamBs1BulletinRendererTests.cs` +3 comma-decimal cases (#3: "12,000"→"12.000", "35,500"→"35.500" — these fail pre-fix); new `PractitionerRenderSnapshotTests.cs` (5, #6: secretary edit preserves stored cachet/ordre, doctor edit uses live values, client-supplied reserved key stripped). #13 (DOB) = coverage note: create-path build-covered, download-path FE build-covered, rendered box is visual/integration. Green.

## Working tree note (start of session)
Unrelated in-flight work EXCLUDED from any staging: `medication-catalog-picker` (untracked `Medication*` + its migration, `Infrastructure/Extensions.cs`, `Persistence/ApplicationDbContext.cs`, `ApplicationDbContextModelSnapshot.cs`, `appsettings.Development.json`); prior fixes `fix-patient-file-tenant-isolation` + `fix-single-dentist-identity`; other `features/fix-*` folders.

## Files Changed
- `api/ClinicManagement.Infrastructure/Services/CnamBs1BulletinRenderer.cs` — `FormatHonoraires` comma-decimal parse (#3).
- `api/ClinicManagement.Application/Features/Documents/PractitionerRenderSnapshot.cs` — added `ReadFrom` + `OrElse` (#6).
- `api/ClinicManagement.Application/Features/Documents/Commands/UpdateMedicalDocumentCommand.cs` — preserve stored practitioner keys on non-doctor edit (#6).
- `api/ClinicManagement.Application/Features/Documents/Commands/CreateMedicalDocumentCommand.cs` — store DOB (dd/MM/yyyy) in the patient-info snapshot instead of "{age} ans" (#13).

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| `#6` implemented via new additive `PractitionerRenderSnapshot.ReadFrom`/`OrElse` helpers + per-field merge (caller's live doctor values, else the document's already-stored values) | The spec said "preserve the stored keys"; a whole-snapshot fallback fails because a secretary's snapshot still carries the (non-null) clinic city, so `HasAny` would be true and doctor keys would still be stripped. Per-field merge is the correct mechanism; client-supplied reserved keys are still stripped by `ApplyTo`. Internal, same error semantics. |
| `#3` parse restricted to decimal styles (no `AllowThousands`) with `,`→`.` normalization | Comma is the Tunisian decimal separator; keeping `AllowThousands` is what caused the 1000× bug. Unparseable input still passes through unchanged (existing fallback). |
| `#13` stores DOB as `dd/MM/yyyy` (InvariantCulture) | Matches the frontend download builder's `toLocaleDateString("fr-FR", dd/MM/yyyy)` so both PDFs render identically under the "Date de naissance" label. |

## Deferred to /test-small-feature
- Any existing unit test asserting the old `"{age} ans"` patient-info string, or the old `NumberStyles.Any` honoraires parse, must be updated to the new behavior (DOB / comma-decimal). No test scenarios written here.

## Significant Deviations
(none)
