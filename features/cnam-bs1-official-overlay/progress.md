# Progress: CNAM BS1 Official-Form Overlay

**Started:** 2026-07-20
**Type:** Small
**Branch:** feature/windows-desktop-app (user chose to reuse the current branch)

## Status
- [x] Implementation
- [x] Quality checks (build, lint, typecheck)
- [x] Tests (added — see Test Plan / Tests Run below)

## Working tree note (start of session)
Unrelated untracked items present at session start — excluded from this feature's staging:
- `.claude/worktrees/` (sibling feature worktrees)
This feature's own untracked items (BS1 asset + spec) are in scope:
- `api/ClinicManagement.Infrastructure/Assets/BS1.pdf`
- `features/cnam-bs1-official-overlay/`

## BS1 geometry (measured from the bundled `Assets/BS1.pdf` via PyMuPDF)
A4 landscape, 2 pages, 841.89 × 595.276 pt, no rotation, origin top-left (matches PdfSharp XGraphics).
- **Page 0 right half** — Identifiant Unique digit comb (10 cells, centers x≈555,571,587,603,618,634,650,665,681,697; y≈175–196); régime boxes CNSS(548.8,205.1)/CNRPS(625.2)/Conv.bilat(755.6); assuré dotted lines Prénom y≈250, Nom y≈269, Adresse y≈287, Code postal y≈324; malade lien cells L'ascendant(483.4,363.6)/L'enfant(562.9)/Le conjoint(630.5)/L'assuré social(696.9); malade PRENOM y≈411, NOM y≈429, DATE NAISSANCE y≈448, N° TEL PORTABLE after x≈601 y≈464.
- **Page 0 left half** — "Consultations et actes de soins dentaires" table: 6 rows, first row top y=94, row height ≈23.66; column left edges DATE 61.5 / DENT 102 / CODE ACTE 130.7 / COTATION 217.7 / HONORAIRES 241 / CODE Prof 289.3 / CACHET 339.
- **Page 1 left half** — Cadre de soins checkboxes APCI(105.5,80)/MO(164.4)/Hospitalisation(263.9)/Suivi de Grossesse(365.0) (17.8×17.5 squares); "Préciser le code APCI" box (105.2,100.8,143.0,122.8).

## Files Changed
- `api/ClinicManagement.Infrastructure/ClinicManagement.Infrastructure.csproj` — added `PdfSharp` 6.1.1 (MIT); bundle `Assets/BS1.pdf` (CopyToOutputDirectory=PreserveNewest).
- `api/ClinicManagement.Infrastructure/Services/Bs1FontResolver.cs` — **new**. Process-wide PdfSharp font resolver (core PdfSharp ships no fonts); loads Arial/Liberation/DejaVu from the OS, fails fast in French if none found.
- `api/ClinicManagement.Infrastructure/Services/CnamBs1BulletinRenderer.cs` — **new**. Stamps bulletin-cnam data onto the official BS1 at calibrated coordinates; appends identity+acts page copies when acts exceed 6 (AC-4); fails fast if the BS1 asset is missing/unreadable (AC-6).
- `api/ClinicManagement.Infrastructure/Services/PdfGenerationService.cs` — branch `bulletin-cnam` → BS1 overlay renderer; removed the old bulletin-cnam QuestPDF drawing (ComposeContent case + ComposeTitle entry).
- `web/components/document-editor-content.tsx` — `buildBulletinContent` now also emits `maladeFirstName`, `maladeLastName`, `patientPhone`, `doctorCodeProfessionnel` (approved FE plumbing, see DEV-1).
- `api/ClinicManagement.Infrastructure/CLAUDE.md` — documented the BS1 overlay path + PdfSharp.

## Quality Checks
- Backend: `dotnet build ClinicManagement.Infrastructure.csproj` → **0 errors, 0 warnings** in changed files (3 pre-existing repo warnings only: `addclinics` migration ×2, `AIActionService` CS8602). Built the leaf Infrastructure project directly to avoid the running-host `bin` copy-lock.
- Frontend: `npx tsc --noEmit` → **0 errors**. `npm run lint` fails with "eslint is not recognized" (ESLint not installed; `next build` disables lint) — documented repo gap, so `tsc --noEmit` is the real type gate (green). The change is a 4-key additive object literal.

## Coordinate calibration (verified)
Calibrated numerically from the bundled BS1 (PyMuPDF text/vector extraction) and **visually verified** by replaying the exact coordinate map onto the real form with sample data and rendering both pages — every field lands in the correct box (IDU digit comb, régime tick, assuré Prénom/Nom/Adresse/CP, malade lien tick + rang, malade Prénom/Nom/DOB/tél, dental acts table incl. right-aligned honoraires + per-row doctor code, and page-2 APCI tick + code). Acts table font tuned to 8pt so the full date fits its narrow column.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Added `Bs1FontResolver` (OS-font resolver, fail-fast if absent) | Core PdfSharp ships no fonts; text stamping requires a resolver. Internal to Infrastructure, no contract change. |
| Added `PdfSharp` 6.1.1 package | Spec mandates PDFsharp; canonical minimal MIT package for opening + drawing on the BS1. Pre-approved per skill (spec-mandated library). |
| Malade Prénom/Nom fall back to splitting `PatientName` when the split keys are absent | Robustness for documents saved before the FE plumbing; behavior-preserving. |

## Significant Deviations
- **DEV-1 — Scope extended from BE to include additive FE plumbing (APPROVED).**
  - *Original plan:* spec `Scope: BE`, `Data/Schema Changes: None`, claiming all data is "already collected by the editor".
  - *Actual:* tracing both PDF paths (`generate-pdf-download` + `PdfGenerationJob`) showed two spec-listed BS1 fields never reach the PDF service — the doctor's «Code professionnel de santé» (acts table column) and the malade's «N° tél portable». The download endpoint receives `MedicalDocumentPdfData` straight from the browser (no DB lookup), so BE alone cannot fill them.
  - *Resolution:* user chose "BE + tiny FE plumbing" (via `/next` scope question). Added 4 additive keys to the editor's `buildBulletinContent` (`doctorCodeProfessionnel`, `patientPhone`, `maladeFirstName`, `maladeLastName`) — no schema change, no pinned-contract change (the `Content` dict is an open string map).
  - *Impact:* every field the spec lists now fills. FE touched: 1 file, additive only. Approved: **Yes**.

## Deferred to /test-small-feature
- Unit tests for `CnamBs1BulletinRenderer`: >6 acts appends page copies (AC-4); missing/unreadable BS1 asset → French fail-fast `InvalidOperationException` (AC-6); honoraires formatted to 3 decimals no symbol (AC-3); régime/lien value not in the known set → nothing ticked (edge case); empty CNAM identity → form still produced with acts (AC-5); act missing code/cotation still prints date/dent/honoraires.
- Unit test for `Bs1Model.From` parsing (acts JSON malformed → empty, not a throw; name split fallback).

## Test Plan (/test-small-feature, 2026-07-20)
New file `api/ClinicManagement.UnitTests/Infrastructure/Services/CnamBs1BulletinRendererTests.cs`.
The renderer is `internal` with private nested `Bs1Model`/`Bs1Act` and private-static helpers, and there is
no `InternalsVisibleTo`, so pure-logic cases are driven **via reflection** (same idiom as the repo's existing
`GoogleCalendarNotesParseTests`). Overflow-paging + fail-fast are exercised **end-to-end through `Render`**,
using the `Assets/BS1.pdf` that the Infrastructure project copies into the test output and the OS Arial font.

| AC / concern | Action | How covered |
|---|---|---|
| AC-3 honoraires 3 decimals, no symbol | New | `Bs1Model.From` → `act.Honoraires`: `12.5`→`12.500`, `0`→`0.000`, `150`→`150.000`, `12.345` kept; non-numeric/empty passed through |
| AC-4 >6 acts append pages | New | `ChunkActs` six-per-page (0/6/7/13 → page sizes) **and** end-to-end `Render(13 acts)` → PDF has 4 pages |
| AC-5 no CNAM identity still produces form | New | `From` empty content → all fields blank (no null); `Render` with empty identity → valid 2-page PDF, no throw |
| AC-5 act missing code/cotation | New | act with only date+honoraires → CodeActe/Cotation empty, Date + Honoraires still filled |
| AC-6 missing BS1 asset → French fail-fast | New | `Render` with the asset moved aside → `InvalidOperationException` whose message contains "BS1" |
| Edge: régime/lien not in known set | New | unknown values preserved verbatim by `From` (stamp switch then ticks nothing); known values carry through |
| Parsing robustness | New | malformed / non-array / empty acts JSON → empty list (never throws) |
| Name split fallback | New | no malade keys + `PatientName "Jean Dupont"` → First/Last split; single token; explicit keys win |
| Date formatting | New | ISO `2026-07-20` → `20/07/2026`; unparseable kept verbatim; empty → empty |
| AC-1 output is a valid filled BS1 | New (smoke) | `Render` full sample → non-empty PDF, ≥2 pages |

### Coverage notes (accounted for, no unit surface)
- **AC-1 / AC-2 coordinate correctness + "no extra headers/titles/watermarks"** — a geometry/visual concern with
  no meaningful unit assertion. Verified numerically + visually per "Coordinate calibration (verified)" above;
  by code inspection the renderer only draws data onto the opened official template (it never adds a title,
  header, or watermark). Exact box placement remains operator/visual-verified.
- **FE plumbing (`web/components/document-editor-content.tsx`, 4 additive keys)** — no FE test framework in this
  repo; covered by the `tsc --noEmit` typecheck gate run at implementation time (DEV-1).

## Bug found & fixed by the tests (AC-4)
The end-to-end AC-4 test (`Render_Appends_Pages_When_Acts_Exceed_Six`) surfaced a **real runtime defect**: the
overflow path stamped a second template opened in `PdfDocumentOpenMode.Modify` and then called
`document.AddPage(extra.Pages[0])`, but PdfSharp **forbids importing a page from a `Modify`-mode document**
(`InvalidOperationException: A PDF document must be opened with PdfDocumentOpenMode.Import to import pages
from it`). Any `bulletin-cnam` with **more than 6 acts** would therefore crash instead of appending pages —
the original visual verification only used ≤6 acts (a single page), so the overflow branch was never exercised.
Fix (`CnamBs1BulletinRenderer.Render`): open the overflow copy in **`Import`** mode, `AddPage` it into the main
`document` first (which returns the now-owned page), then stamp that returned page. AC-4 now genuinely works.

## Review fixes applied (/apply-review-fixes, 2026-07-20)
Challenged all 12 confirmed findings against the source; applied 9, deferred 3. All fixes are in the
working tree (uncommitted — manual-commit workflow, 0 commits since merge-base).

| # | Sev | Verdict | What changed |
|---|-----|---------|--------------|
| 3 | Minor | Fixed | `Bs1Model.From` strips non-digits from Identifiant Unique (`OnlyDigits`) so the digit comb stays aligned. |
| 4 | Minor | Fixed | `Bs1FontResolver._installed` marked `volatile` (release barrier before lock-free reads). |
| 5 | Minor | Fixed | After `??=`, assert the active resolver is ours; throw the French fail-fast if a foreign resolver won. |
| 6 | Minor | Fixed | Malformed acts JSON now sets `Bs1Model.ActsMalformed`; `Render` logs a Warning via an injected `ILogger` (renderer keeps its parameterless ctor → reflection tests unaffected). |
| 8 | Suggestion | Fixed | Extracted `ActRowBaselineOffsetY = 15` (was a bare literal). |
| 9 | Suggestion | Fixed | Fonts built once per page in `StampAssureAndMalade`/`StampActs` instead of per access. |
| 10 | Suggestion | Fixed | Overflow loop opens the Import-mode template **once** before the loop, not per extra page. |
| 11 | Suggestion | Fixed | Dropped the unreachable `"Suivi de Grossesse"` (capital-G) switch arm (FE only emits lowercase). |
| 12 | Suggestion | Fixed | `LoadFirstAvailable` captures the last swallowed exception; fail-fast message distinguishes "present but unreadable" from "none found". |
| 1 | Major | Deferred | OS-font hard-dep (Cloud/slim-container regression). → `follow-up/cnam-bs1-overlay-deferred-review.md` (bundle Liberation Sans). |
| 2 | Minor | Deferred | AC-6 French message not surfaced on the async attach path (pre-existing shared job). → same follow-up. |
| 7 | Minor | Deferred | Deterministic fail-fast retried 3× by Hangfire (pre-existing shared job). → same follow-up. |

Files touched: `Bs1FontResolver.cs`, `CnamBs1BulletinRenderer.cs`, `PdfGenerationService.cs` (logger pass-through).
Quality: Infrastructure build **0 errors, 0 new warnings** (3 pre-existing only); renderer tests **27 passed, 0 failed**.

## Tests Run (/test-small-feature)
Ran via the SAC-safe isolated-`OutDir` + `dotnet vstest` recipe (Smart App Control is ON on this box; the
recipe dodges the `0x800711C7` load block and the running-API `bin` copy-lock).

| Suite | Filter | Result |
|-------|--------|--------|
| Unit | `FullyQualifiedName~CnamBs1BulletinRendererTests` | **27 passed, 0 failed, 0 skipped** |

Build: `ClinicManagement.UnitTests` → 0 errors, 0 new warnings (only the 3 pre-existing repo warnings —
`addclinics` ×2, `AIActionService` CS8602). No Postman/Newman (user preference); no full-suite regression
(small feature). Tests are targeted to the renderer only.
