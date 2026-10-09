# Progress: the cabinet's own letterhead on every document (+ date of birth off the documents)

**Started:** 2026-10-06
**Type:** Small (run as one pass over the three parts of `blueprint.md`, on the owner's instruction)
**Branch:** `feature/security-remediation` (the owner's standing instruction: all work on this branch)
**Spec:** `blueprint.md` — option « Top and bottom bands », chosen by the owner 2026-10-06; CNAM out of scope.

## Status
- [x] Implementation — Part 0 (date of birth), Part 1 (server), Part 2 (screens)
- [x] Quality checks — API build 0 errors / 0 new warnings · unit suite **4940 passed, 0 failed** (unfiltered) ·
      `check:responsive` 76/76 · `tsc --noEmit` clean · `npm run build` green · migration scaffolded clean
- [x] Blast radius closed (8 rows: 4 changed, 4 to re-test)
- [x] Tests — 38 new cases, full suite **4978 passed, 0 failed, 6 skipped** (unfiltered); page guard red-proofed
- [x] Browser QA — `qa/run-3.md` GREEN (33 ✅, 2 ⏭ with reasons) after 2 fix cycles (F1–F5)
- [x] Docs — `notes.md` + the root, Application, Infrastructure, API, `web/lib` and `web/components` maps

## Test Plan
| Area | Action | Target file |
|------|--------|-------------|
| PNG size read · band rules (each refusal code, header vs footer caps, the door refusing JPEG) | new class | `UnitTests/Features/Clinics/LetterheadBandTests.cs` |
| Save / remove (admin-only, one batch, version checked, superseded band never deleted, footer dropped, refused footer stores nothing, failed save and 409 clean up) | new class | `UnitTests/Features/Clinics/ClinicLetterheadCommandTests.cs` |
| Snapshot keys (server overrides a forged key, stripped when none, no footer without header, `OrElse` keeps the pair) | added scenarios | `UnitTests/Features/Documents/PractitionerRenderSnapshotTests.cs` |
| Render (both bands on every page incl. page 2+, none without a letterhead, header-only, unreadable header / footer, note d'honoraires, preview) + guard « every `.Page(` goes through `DocumentPage` » | new class | `UnitTests/Infrastructure/Services/LetterheadRenderTests.cs` |
| Test PNG helper | new | `UnitTests/Common/TestPng.cs` |
| Browser-only rules (cut suggestion, dialog reset, card state, 320/730 fit) | covered by QA | `qa/run-3.md` |

## Blast Radius
| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | Page setup of the 5 QuestPDF methods | every PDF of the product | ordonnance, examens, certificat, liaison, honoraires (generic) · note d'honoraires · devis · reçus · avoir | **must re-test** — all now go through `DocumentPage.Compose`; without a letterhead the geometry is the old one byte for byte (A4, 2 cm, same text header) |
| 2 | Header drawn in 7 places | identity at the top of a document | `ComposeHeader`, 4 inline money copies (now `ClinicIdentityHeader`), editor A4 preview, editor DOCX | **changed** — all 7 |
| 3 | `PractitionerRenderSnapshot` reserved keys | ContentJson snapshot | Create/Update document, `FicheOrdonnanceEmitter`, `generate-pdf-download`, `PdfGenerationJob`, `GetPractitionerRenderSnapshotQuery` | **changed** (2 keys, strip-then-write) / **must re-test** the 5 callers |
| 4 | `Clinic` row writes | `xmin` token | settings form (`clinicVersion`), realtime `clinics` | **must re-test** — the letterhead save broadcasts `clinics`, so the settings form reloads its version |
| 5 | `ClinicArchiveScope.BlobProperties` | archive / restore | archive guard test | **changed** — both keys declared; guard green |
| 6 | `DocumentIdentity.PatientLines` | patient block | all generic medical types, editor preview, DOCX | **changed** (date of birth gone) — one test flipped |
| 7 | `FileUploadProfile.ByName` | upload doors | `GET /meta/upload-policy`, TS `UploadProfile` union | **unaffected** — additive door `letterhead-band` |
| 8 | `IPdfGenerationService` | interface | one implementation, Moq mocks in tests | **unaffected** — additive method |

## Working tree note (start of session)
Dirty before this feature and excluded from it: `.claude/rules/*`, `.claude/skills/start-clinic/SKILL.md`, `CLAUDE.md`
(the owner's), `FEATURE-OVERVIEW.md`, `AuthController.cs`, `ClinicUserDto*.cs`, two test files, `console/tsconfig.json`,
`features/devis-fiche-rdv-flexibility/qa/*`, `features/password-recovery-gaps/notes.md`, `packaging/README.md`,
`site/src/partials/footer.html`, `web/app/login/page.tsx`, `web/components/CLAUDE.md`, `web/components/ui/card-list.tsx`,
`web/components/user-management.tsx`, `web/lib/api/{auth,users}.ts`, `web/lib/hooks/use-password-policy.ts`.

## Files Changed

**Part 0 — date of birth**
- `api/…/Infrastructure/Services/DocumentIdentity.cs` — line removed, tombstone extended
- `api/…/Infrastructure/Services/PdfGenerationService.cs` — comment
- `api/…/Application/Common/Models/MedicalDocumentPdfData.cs` — remark
- `web/components/document-editor-content.tsx` — A4 preview block + DOCX paragraph
- `api/…/UnitTests/Infrastructure/Services/PrescriptionRenderContentTests.cs` — assertion flipped (see deviations)

**Part 1 — server**
- Domain: `Clinic.cs` (2 columns, `SetLetterhead`/`RemoveLetterhead`, false logo remark fixed)
- Infrastructure: `ClinicConfiguration.cs`, `ClinicArchiveScope.cs`, migration `20261006213251_AddClinicLetterhead` (+ Designer, snapshot),
  **new** `Services/DocumentPage.cs` (`DocumentPage` + `ClinicIdentityHeader`), `PdfGenerationService.cs` (every page
  through `DocumentPage`, `RenderMedicalDocument` shared with the preview, `LoadLetterheadAsync`)
- Application: **new** `Common/Files/PngDimensions.cs`, `Common/Models/LetterheadImages.cs`, `DTOs/ClinicLetterheadDto.cs`,
  `Features/Clinics/{LetterheadRules,LetterheadBandReader}.cs`, `Commands/{Update,Remove}ClinicLetterheadCommand.cs`,
  `Queries/{GetClinicLetterhead,GetClinicLetterheadBand,PreviewClinicLetterhead}Query.cs`; modified `FileUploadProfile.cs`,
  `IPdfGenerationService.cs`, the 5 PDF data models, the 5 money PDF queries, `PractitionerRenderSnapshot.cs`,
  `GetPractitionerRenderSnapshotQuery.cs`, `MedicalDocumentPdfMapping.cs`
- API: `ClinicsController.cs` (4 endpoints), **new** `Models/UpdateClinicLetterheadRequest.cs`, `MedicalDocumentsController.cs`
  (strip + overlay the two keys on `generate-pdf-download`)

**Part 2 — web**
- **new** `web/lib/letterhead/{page-image,bands,use-clinic-letterhead,word}.ts`
- **new** `web/components/letterhead/{letterhead-card,letterhead-import-dialog}.tsx`
- `web/lib/api/client.ts` (`apiPostFormDataBlob`), `web/lib/api/clinics.ts` (5 calls + types), `web/lib/api/upload-policy.ts`
  (door name), `web/components/clinic-settings.tsx` (mount), `web/components/document-editor-content.tsx` (preview + DOCX)
- `web/package.json` + lock — `pdfjs-dist@^6.4.299`

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| `ClinicDto` not extended; a dedicated `GET /clinics/letterhead` read | `ClinicDto` is built in 7 places — a field on it is a 7-site mirror for one card |
| No `LetterheadUpdatedAt` column; the DTO carries a `Revision` hash of the keys | keys are never reused, so a hash of them is exactly « the bands changed » — one column fewer |
| Dedicated PNG-only door `letterhead-band` instead of reusing `profile-image` | the tool always encodes PNG; refusing JPEG keeps ringing off printed text |
| The preview is a one-page sample ordonnance (blueprint said two pages) | three realistic lines; page-2 repetition is checked in QA rather than shown to the cabinet |
| `PrescriptionRenderContentTests.A_Legacy_Document_Prints_Neither_…` flipped and renamed | it pinned the date of birth the owner removed; the suite was otherwise red |
| `pdfjs-dist` 6.4.299, not 5.x | 5.6.83–6.2.108 carries a high-severity advisory |

## Significant Deviations
None.

## Found on the way, not in scope
- `source-map-js` high advisory (postcss / tailwind) — pre-existing, not from this feature.
- « Supprimer le logo » only clears local state (`clinic-settings.tsx`).
- Doctor-profile `Version` is sent but never bound (`UpdateDoctorProfileRequest.cs`).
- The cachet is not frozen per document.

## QA fix cycles
- Cycle 1: F1 dialog reset on broadcast · F2 settings cards collapsing · F4 cut page taller than the dialog · F5 320 px
  title/steps wrap · F3 partly (longest blank run).
- Cycle 2: F3 root cause — the 240 px low-quality probe skipped the rule (`bands.ts`: 800 px, high-quality sampling,
  paper-relative threshold). A9 is deliberate: « Télécharger Word » is withdrawn app-wide.

## Deferred to /test-small-feature (done — see Test Plan)
`PngDimensionsTests` · `UpdateClinicLetterheadCommandTests` (admin-only, each refusal code, versioned keys, no delete of
a superseded band, orphan cleanup on a failed save) · `RemoveClinicLetterheadCommandTests` · snapshot keys
(strip / write / `OrElse` moving the pair together) · render tests with and without bands (image XObjects per page
through PdfSharp, page 2 included) · derived guard « every `.Page(` goes through `DocumentPage` ».
