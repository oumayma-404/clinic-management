# Blueprint — the cabinet's own letterhead on every document

Status: **CHOSEN** (owner, 2026-10-06) — option « Top and bottom bands ». Not started.
Asked by several cabinets: « that the entête looks like the doctor's usual entête ».

## Summary

The cabinet imports **its own letterhead page** (the PDF from its printer, Word saved as PDF, or a scan of a
blank sheet). A browser tool cuts it into two **bands** — the top of the page down to the end of the entête,
and (optionally) the bottom of the page from the start of the pied de page — at **true scale**: the page image
is mapped to 210 mm, so the bands print exactly where they sit on the doctor's real paper. Every QuestPDF
document draws them on **every page**; the content between them (date, title, patient, lines, signature,
cachet) stays vector text. The CNAM forms are out of scope (not shown in the product today).

The doctor sees the **real server-rendered PDF** of a sample ordonnance before saving, so what they approve is
what prints.

## What the code is today (measured 2026-10-06)

| Fact | Where |
|---|---|
| Every QuestPDF document is A4, 2 cm margins, Helvetica 11 | `PdfGenerationService.cs:107-111, 182-185, 335-338, 513-516, 626-629` |
| The header is the **first item of the content column** → page 1 only | same, e.g. `:119` |
| Medical documents share `ComposeHeader` (`:755-773`); invoice, devis, reçu, avoir each carry an **inline copy** | `:192-202, :345-355, :523-533, :637-647` |
| The editor draws two more copies: the A4 HTML preview and the DOCX export | `document-editor-content.tsx:2473-2513, :1177-1194` |
| The clinic **logo is drawn on no PDF**, though `Clinic.cs:18-26` says it is | — |
| The cachet key is fixed per doctor → a re-upload changes every past document | `UpdateDoctorProfileCommand.cs:149-151` |
| Images are never decoded server-side: no dimensions, no DPI; no image library referenced | `FileUploadValidator.cs:63-107` |
| No test reads a PDF back; PdfSharp is available to reopen one | `Infrastructure.csproj:55` |

## Decisions (made, with the reason)

| # | Decision | Why |
|---|---|---|
| D1 | **One letterhead per cabinet** (on `Clinic`), admin-managed in `/settings` | All 6 live cabinets have one doctor; money documents are clinic-level; a group practice shares one letterhead. A per-doctor override is a later, additive column if a group practice asks |
| D2 | **Bands, not a full-page background** | 300 KB not 1–3 MB per PDF; a scan's grey stays in the bands; no content/design collision |
| D3 | **True scale**: the source page's width = 210 mm | The band lands where it is on the real paper; no « how big? » control to get wrong |
| D4 | **Every page**, via `page.Header()` / `page.Footer()` | A 2-page ordonnance on real letterhead paper has it on both |
| D5 | **With bands, the text identity header is withheld** — except the **matricule fiscal** on money documents | A letterhead *is* the identity; printing both doubles it. The matricule fiscal is a fiscal mention letterheads rarely carry |
| D6 | **Without bands, nothing changes** — same margins, same text header, byte-for-byte layout | Every cabinet that never imports one keeps today's documents |
| D7 | **Versioned, immutable blob keys**; a superseded band is never deleted | Old documents keep the letterhead they were issued with (D8); immutable keys also make any later byte cache trivially correct |
| D8 | **Medical documents snapshot the band keys** at issue (`PractitionerRenderSnapshot`); money documents read the clinic's current bands live | Medical documents already freeze identity; money documents already read it live — each keeps its existing rule |
| D9 | A medical document issued **before** any letterhead keeps its text header | Its snapshot has no band key; it never changes appearance |
| D10 | The browser does the image work (PDF raster, cut, whiten, downscale); the server **validates dimensions** | No new server image library; the doctor sees each step; the server still refuses what would print badly |
| D11 | Reuse the **`profile-image` door** (PNG/JPEG, 5 Mo) | Same rationale (« read into memory on every render »); dimension rules are letterhead-specific and live in the command |
| D12 | Date of birth **removed from every document** (owner, 2026-10-06) | Not needed on any of them; sexe and poids went for the same reason |

## Part 0 — date of birth off the documents (quick fix, do first)

| File | Change |
|---|---|
| `Infrastructure/Services/DocumentIdentity.cs:100-101` | drop the « Date de naissance » line; extend the sexe/poids tombstone to name it |
| `web/components/document-editor-content.tsx:2550-2560` | drop the A4 preview block |
| `web/components/document-editor-content.tsx:1232` | drop the DOCX paragraph |
| `UnitTests/.../DocumentIdentityTests.cs` | assert the line is **absent** for every generic type |

No migration: `MedicalDocument.PatientAge` stays as a column, it is just no longer printed. Old documents stop showing it on re-render.

## Part 1 — server

### Files to create

- `Infrastructure/Services/DocumentPage.cs` — **the one owner of page geometry**. `Apply(PageDescriptor page, LetterheadBands? bands, Action<IContainer> footerAboveBand)`: without bands → `Margin(2 cm)` exactly as today; with bands → `Margin(0)`, `page.Header()` = header band `FitWidth`, content padded 2 cm left/right (+ a top gap), `page.Footer()` = the document's own footer (signature / mention) padded, then the footer band `FitWidth`.
- `Infrastructure/Services/ClinicIdentityHeader.cs` — the **one** money-document text header (clinic name, address, Tél, matricule fiscal), replacing the four inline copies; with bands it renders the matricule fiscal line only (D5).
- `Application/Common/Files/ImageDimensions.cs` — pure reader of PNG (`IHDR`) and JPEG (`SOFn`) width/height from the header bytes. No decode, no dependency.
- `Application/Features/Clinics/Commands/UpdateClinicLetterheadCommand.cs` — admin-only; validates both files through `FileUploadValidator` (`ProfileImage`); reads dimensions; refuses with codes:
  - `letterhead-too-small` — band narrower than **1240 px** (150 dpi at 210 mm);
  - `letterhead-header-too-tall` — header taller than **1/3 of an A4** at its own scale (> 99 mm);
  - `letterhead-footer-too-tall` — footer taller than **1/6** (> 49 mm).
  Uploads under `letterhead/{guid}/header` and `letterhead/{guid}/footer` (via `ClinicStorageKey.Compose`), calls `clinic.SetLetterhead(...)`, honours `Version`, and on a failed save deletes **only the blobs it just wrote** (the logo test's shape). Never deletes a superseded band (D7).
- `Application/Features/Clinics/Commands/RemoveClinicLetterheadCommand.cs` — clears the columns; deletes nothing (old documents may reference the blobs).
- `Application/Features/Clinics/Queries/GetClinicLetterheadBandQuery.cs` — streams `header` or `footer` for the settings card and the editor preview (any clinic role; the `GetClinicLogoQuery` shape).
- `Application/Features/Clinics/Queries/PreviewClinicLetterheadQuery.cs` — renders a **sample ordonnance** (the clinic's real identity, the caller's doctor, two sample lines, two pages so the doctor sees page 2) with the **candidate** bands held in memory; persists nothing. Goes through `IPdfGenerationService` with in-memory bands, so the preview cannot differ from the real render.

### Files to modify

| File | Change |
|---|---|
| `Domain/Entities/Clinic.cs` | `LetterheadHeaderStorageKey`, `LetterheadFooterStorageKey` (nullable, ≤ 400), `LetterheadUpdatedAt`; `SetLetterhead(header, footer?)`, `RemoveLetterhead()`. Fix the false « appears at the top of every ordonnance » remark on `LogoContentType` |
| `Infrastructure/Persistence/Configurations/ClinicConfiguration.cs` + migration `AddClinicLetterhead` | three nullable columns, no backfill. Check the scaffold for the `xmin` trap |
| `Infrastructure/Persistence/ClinicArchiveScope.cs:165` | add both keys to `BlobProperties[Clinic]` (the `*StorageKey` guard at `ClinicArchiveScopeTests.cs:346` forces it) |
| `Application/Features/Documents/PractitionerRenderSnapshot.cs` | reserved keys `letterheadHeaderKey`, `letterheadFooterKey` — strip-then-write in `ApplyTo`, carried by `ReadFrom` / `OrElse` like the cachet key |
| `Application/Common/Models/MedicalDocumentPdfData.cs` + `MedicalDocumentPdfMapping.cs` | carry the two keys from the snapshot |
| `Application/Common/Models/{Invoice,Devis,Receipt,CreditNote}PdfData` + their 5 `Get*PdfQuery` handlers | carry the clinic's **current** keys (D8) |
| `Infrastructure/Services/PdfGenerationService.cs` | every QuestPDF entry point calls `DocumentPage.Apply`; `LoadLetterheadAsync` beside `LoadCachetImageAsync` (never throws; a missing blob → text header, D9 fallback); inline money headers → `ClinicIdentityHeader` |
| `API/Controllers/ClinicsController.cs` | `PUT /api/clinics/letterhead` (multipart `header`, `footer?`, `version`), `DELETE /api/clinics/letterhead`, `GET /api/clinics/letterhead/{band}`, `POST /api/clinics/letterhead/preview` → PDF. Size limits from `FileTypeCatalog.ProfileImageBytes` ×2 |
| `Application/DTOs` clinic DTO | `hasLetterhead`, `hasLetterheadFooter`, `letterheadUpdatedAt` |

### Key signatures (sketch)

```csharp
public sealed record LetterheadBands(byte[] Header, byte[]? Footer);
public static class DocumentPage { public static void Apply(PageDescriptor page, LetterheadBands? bands, Action<IContainer>? footer); }
public static class ImageDimensions { public static (int Width, int Height)? Read(ReadOnlySpan<byte> head); }
public record UpdateClinicLetterheadCommand(Stream Header, string HeaderName, long HeaderLength,
    Stream? Footer, string? FooterName, long? FooterLength, uint Version) : IRequest<Result<ClinicDto>>;
```

### Wiring

MediatR picks up the handlers; no new service registration (`DocumentPage` / `ClinicIdentityHeader` / `ImageDimensions` are static). The `Clinics` realtime key already exists, so `SetLetterhead` refreshes open settings tabs.

## Part 2 — web

### Files to create

- `web/components/settings/letterhead-card.tsx` — a `/settings` card « En-tête des documents »: an A4 thumbnail with the current bands, « Importer mon papier à en-tête », « Remplacer », « Retirer » (confirm names what it removes). Admin only, like the logo.
- `web/components/settings/letterhead-import.tsx` — heavy surface → **Sheet below `md:`**, dialog above (`frontend-web.md` § 5). Three steps:
  1. **Fichier** — PDF / PNG / JPEG. A PDF is rasterised in the browser at 300 dpi (page 1).
  2. **Découpe** — the page at A4 ratio with two draggable lines, *Fin de l'entête* and *Début du pied de page* (the second can be dropped: no footer). **Pre-placed automatically** on the first and last wide white gap (row ink density). Keyboard: arrows move the focused line; coarse pointer: 44 px handles.
  3. **Aperçu** — `POST …/letterhead/preview`, the real PDF framed (`document-preview-dialog`'s pattern; the coarse-pointer two-tree rule for PDF iframes), then « Enregistrer ».
  A **quality verdict** sits beside step 2 only when it is not good: « Image trop petite — importez le PDF de votre imprimeur ou un scan à 300 dpi » (refusal below 1240 px; the server repeats it).
- `web/lib/letterhead/` — pure, testable-by-eye helpers: `rasterizePdfPage`, `suggestCuts` (ink-density scan), `whiten` (near-white & low-saturation → pure white; transparency flattened onto white), `cutBands` (crop at true scale, downscale to 2480 px wide max, encode PNG).

### Files to modify

| File | Change |
|---|---|
| `web/package.json` | `pdfjs-dist`, **lazy-imported** in step 1 only; its worker served **same-origin** from `public/` — never a `blob:` worker (the CSP trap: works on the laptop, grey on the VPS; the policy lives in 4 byte-identical copies) |
| `web/lib/api/clinics.ts` | `uploadLetterhead`, `removeLetterhead`, `getLetterheadBand`, `previewLetterhead` |
| `web/components/clinic-settings.tsx` | mount the card; carry the clinic's new `version` after a letterhead save (`xmin` is per row — the settings form beside it holds the same token). Fold in: « Supprimer le logo » only clears local state (`:896-921`) |
| `web/components/document-editor-content.tsx` | the A4 HTML preview draws the bands instead of its text header when the cabinet has them; the DOCX export puts them in the Word header/footer (`docx` `ImageRun`). Both or neither — they are the 2 browser mirrors |

## Blast radius

| # | Touching | Other consumers | Verdict |
|---|---|---|---|
| 1 | Page setup of 5 QuestPDF methods | every PDF of the product | **must re-test** all 5 with and without bands; D6 says « without » is unchanged |
| 2 | Header drawn in 7 places | `ComposeHeader`, 4 inline copies, HTML preview, DOCX | **must change** — all in this feature |
| 3 | `PractitionerRenderSnapshot` reserved keys | Create / Update document, `FicheOrdonnanceEmitter`, `generate-pdf-download`, the PDF job | **must re-test**; strip-then-write keeps forged keys out |
| 4 | `Clinic` row writes | settings form (`xmin`), realtime | **must change** — carry `version` |
| 5 | `ClinicArchiveScope.BlobProperties` | archive / restore | **must change**; superseded bands are not archived → a restored old document falls back to the current bands, then to text |
| 6 | `DocumentIdentity.PatientLines` | all generic medical types | **must re-test** (Part 0) |

## Potential pitfalls

- **An office printer cannot print the last 3–5 mm of the sheet.** An edge-to-edge colour band from an offset-printed letterhead loses that strip — exactly as the doctor's own Word file does. Say nothing in the UI; the preview shows the page.
- **A source that is not a page** (a banner made in Canva): aspect ratio far from A4 (h/w < 1.2) → treat the whole image as the header band at full width, still subject to the height cap.
- **Phone photos**: whitening fixes the grey, not the tilt or the shadow. The size check and the preview are the defence; no perspective correction (not worth the surface).
- **Liaison / certificat** suppress the patient block today and keep doing so; their top gap below the band must not double the spacing.
- **The signature block lives in `page.Footer()`**: with a footer band it sits *above* the band; on a long ordonnance check the content area still fits a line.
- **`Version == 0` skips the check** — the card must send the version it read.
- **QuestPDF's `FontManager` scans `AppContext.BaseDirectory`** — never stage band files under the install directory (LAN install).
- **A band blob missing at render** (restored archive, manual deletion) → fall back, never fail — the cachet's rule.

## Test strategy

**Unit (`api/ClinicManagement.UnitTests`)**

- `ImageDimensionsTests` — PNG and JPEG corpora (incl. progressive JPEG, EXIF before SOF, truncated header → null).
- `UpdateClinicLetterheadCommandTests` — admin only; each refusal code; versioned key shape; superseded blob **not** deleted; new blobs deleted when the save fails; `Version` honoured.
- `RemoveClinicLetterheadCommandTests` — clears columns, deletes no blob.
- `PractitionerRenderSnapshotTests` — new keys stripped from client JSON, written from the server, preserved by `OrElse`.
- `GenericDocumentRenderTests` + the four money renders — with bands: `DownloadAsync` called for each key; missing blob still renders.
- **The first real PDF assertion in the repo**: reopen the bytes with PdfSharp and count image XObjects per page — a 2-page ordonnance has the band on **both** pages; without bands, the image count equals today's (cachet only).
- **Derived guard** `Every_QuestPdf_Page_Uses_DocumentPage` — scans `PdfGenerationService.cs`: every `.Page(` block calls `DocumentPage.Apply`, and `Margin(` appears nowhere else. A sixth document cannot forget the letterhead.
- `DocumentIdentityTests` — « Date de naissance » absent (Part 0).
- `ClinicArchiveScopeTests` — already forces the two `*StorageKey` columns in.

**Browser (`/test-in-browser`)** — a printer PDF, a 300 dpi scan, a phone photo (refused or visibly weak), a Canva banner; footer present / absent; the 2-page ordonnance; invoice, devis, reçu, avoir; a cabinet **without** a letterhead (regression, D6); the editor preview and the DOCX; 320 / 390 / 820 / 1180 / 1440 and 1536 × 730 for the import sheet; keyboard-only line placement.

**Production** — `verify-schema` before/after the migration.

## Found on the way, not in scope

- Doctor profile `Version` is sent by the web but never bound (`UpdateDoctorProfileRequest.cs`) → its concurrency check is always skipped.
- Logo upload deletes the old blob **before** the new one is saved (`UpdateClinicCommand.cs:167-178`).
- The cachet is not frozen per document (same shape as D7, other image).
- The clinic logo still prints nowhere (a « no letterhead » default could use it — option 3's territory).
