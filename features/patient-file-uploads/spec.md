# Feature Specification: Patient File Uploads — format breadth, validation shape, and the file-manager UX

**Status:** APPROVED
**Approved:** 2026-08-07 — by the user's answers to `/think-solution`'s option questions (approach, format
breadth, large-file degradation). Not challenged through `/challenge-spec`.
**Type:** Small-to-medium, full-stack
**Created:** 2026-08-07
**Scope:** `api/` (Application + API) + `web/`. **No schema change** — see [Out of scope](#out-of-scope).
**Branch:** `feature/audit-sections-3-to-10` — by explicit user decision. That branch also carries the in-flight
`clinic-self-signup` work uncommitted; this story stages only its own files, by path.
**Source:** a user report — « I created a txt file, renamed it to .pdf, tried to upload it, the upload failed with
400 » — plus two observations from the same report: the accepted-format list is too narrow for a dental clinic,
and the upload/browse UI is weaker than the rest of the app.
**Exploration:** two parallel Explore passes (backend upload paths + frontend file UI), 2026-08-07. Findings are
quoted inline below with `file:line` references and were re-verified against source.

---

## Overview

Three asks, one root cause between the first two.

**The reported 400 was two defects stacked, and only the second is what the user saw.** The refusal itself was
correct: `FileContentValidation.MatchesSignature` (`api/ClinicManagement.Application/Common/FileContentValidation.cs:66-82`)
looked for `%PDF-`, did not find it, and returned
`SignatureMismatchMessage` — « Le contenu du fichier ne correspond pas à son format déclaré. Le fichier a peut-être
été renommé. » That message never reached the browser: `web/lib/api/patient-files.ts:78-80` reads
`errorData.message`, while the backend's canonical failure body is `{ error }`
(`ApiControllerBase.cs:27-34`), so the reason was dropped and the user got the English fallback
`HTTP 400: Bad Request`. Every one of that module's four raw-`fetch` calls has the same bug, and all four also
bypass `client.ts`'s 180 s upload deadline and its one-shot 401 retry.

**The format list cannot be widened where it currently lives.** The allow-list is keyed on the declared
`Content-Type` (`FileContentValidation.cs:27` — `{ application/pdf, image/png, image/jpeg }`), and a browser
derives that header from the file extension via the OS registry. Windows registers no type for `.stl`, `.dcm`,
`.ply` or `.obj`, so those arrive as `application/octet-stream`: **adding `model/stl` to the list would not admit
a single STL file.** Two further blockers sit behind it — the signature switch's default arm is `_ => false`
(`:71`), so any format with no magic bytes is refused by construction, and DICOM's `DICM` marker sits at **offset
128**, which the current `MatchesSignature(contentType, bytes)` shape cannot express.

**The validator has one caller, and three of the six upload paths are unprotected.** `FileContentValidation`'s own
header comment (`:6-8`) claims it extracted the doctor-cachet logic "rather than reimplementing it"; the cachet
path was never migrated and still carries private `MaxCachetBytes`, `IsPng` and `IsJpeg` copies
(`UpdateDoctorProfileCommand.cs:30-31, 129-133, 204-210`), which leaves
`FileContentValidation.MaxCachetBytes` (`:20`) and `.ImageTypes` (`:30`) as **dead constants**. The clinic logo
(create and update) and the medical-document PDF have **no** type check, no size cap and no signature check at
all — and the medical-document path writes a `PatientFile` row with a hardcoded `"application/pdf"`
(`CreateMedicalDocumentCommand.cs:185-227`), bypassing the patient-file checks for the same table. This is the
repo's documented `fixes-dont-propagate` shape.

**The UI is thin in ways that compound the above.** There is no client-side size or type check before POST
(`patient-files-manager.tsx:140-186` goes straight to `Promise.all`), so a refusal costs a full upload; one
rejection in the batch reports the whole batch as failed and the successes are indistinguishable; there is **no
rename affordance anywhere in the app, for anything**, and no `UpdatePatientFileCommand` — `PatientFile.Rename`
does not exist and `UpdateDescription`/`MoveToFolder`/`PatientFolder.UpdateName` have **zero callers**; the list
renders an icon by MIME with no thumbnails; and the preview logic exists in **two** copies
(`patient-files-manager.tsx` and `app/patients/[id]/page.tsx`), only the PDF frame having been extracted.

---

## Acceptance criteria

### AC-1 — The server's refusal reaches the user, in French

- **AC-1.1** Every call in `web/lib/api/patient-files.ts` goes through `web/lib/api/client.ts`, so a failure
  surfaces the backend's `{ error }` body verbatim and an unreadable body falls back to `statusMessageFr`.
- **AC-1.2** No user-visible string from this module is English. Uploading a `.txt` renamed to `.pdf` shows
  « Le contenu du fichier ne correspond pas à son format déclaré. Le fichier a peut-être été renommé. »
- **AC-1.3** Failures throw `ApiError`, not `Error`, so `showErrorToast` can offer « Réessayer » on a network
  fault and `client.ts`'s 426 / `must_change_password` hooks fire for this module too.
- **AC-1.4** The upload carries the 180 s deadline and the download the 20 s one; both get the one-shot 401
  retry. A hung upload settles instead of freezing the drop zone for ever.
- **AC-1.5** `apiHeaders` remains the only header writer; the `api-headers` check still passes.

### AC-2 — One extension-keyed catalog, and every upload path uses it

- **AC-2.1** A single authority — `Application/Common/Files/` — decides what may be uploaded. Entries are keyed
  on **extension**; the declared content type is never the allow-list key and is never stored.
- **AC-2.2** Each entry declares its canonical content type, its `FileType` category, its own byte cap, whether
  it is browser-previewable, and a signature rule that is one of `Required` / `Advisory` / `None(reason)`. A
  `None` with an empty reason is a build failure, not a code-review question.
- **AC-2.3** A `Required` entry refuses bytes that do not match. An `Advisory` or `None` entry refuses bytes
  that **positively match a different entry's `Required` signature** — so `.txt`→`.pdf` stays refused with the
  same message, and an ASCII `.stl` (which has no signature at all) is accepted.
- **AC-2.4** Signature rules carry an **offset**, so DICOM's `DICM` at byte 128 is expressible. A DICOM file
  with no preamble is accepted (`Advisory`), because preamble-less exports are real.
- **AC-2.5** A deny-list is checked **before** the allow-list and refuses with its own message: executables and
  scripts, and anything that renders as markup in the app's own origin (`svg`, `html`, `xhtml`, …).
  ⚠️ SVG's refusal becomes load-bearing under AC-5: a `blob:` document inherits the creating origin, so the
  attachment-only download that makes SVG harmless today stops being the only protection.
- **AC-2.6** All six upload sites reference a named profile: patient file, doctor cachet, clinic logo (create),
  clinic logo (update), medical-document PDF, CSV import. `FileContentValidation` is deleted and
  `UpdateDoctorProfileCommand`'s private magic-byte copies are deleted.
- **AC-2.7** A derived guard fails the build if any `.cs` outside `Common/Files/` carries a magic-byte literal
  or an inline content-type allow-list. A hand-maintained list of today's upload sites is **not** acceptable —
  it cannot fail on the seventh site, which is the only case a guard exists for.
- **AC-2.8** Validation reads a bounded header (4 KB) and streams the remainder. A 150 MB upload is never
  buffered whole in memory, and validation still completes **before** any blob is written.
- **AC-2.9** The refusal message names what the profile accepts, derived from the profile — not one hardcoded
  sentence naming PDF/PNG/JPEG.
- **AC-2.10** The stored `FileName` is sanitized (no path segments, no control characters, bounded length,
  never an empty base name). It is currently stored verbatim and handed to `File(..., fileDownloadName)`.

### AC-3 — The accepted formats a dental clinic actually uses

- **AC-3.1** Imaging and photos: `pdf`, `png`, `jpg`/`jpeg`, `webp`, `gif`, `tiff`/`tif`, `bmp`, `heic`/`heif`.
  HEIC is included deliberately — an iPhone photographing a case is the normal path, and the current list
  refuses it.
- **AC-3.2** Dental 3D and CBCT: `dcm`/`dicom`, `stl`, `ply`, `obj`, `3mf`, `zip` (lab and aligner packages ship
  as archives).
- **AC-3.3** Office and text: `docx`, `xlsx`, `doc`, `xls`, `odt`, `ods`, `rtf`, `txt`, `csv`.
  ⚠️ This **reverses** `FileContentValidation.cs:26`'s stated "deliberately excludes Office formats (macro
  vector)". The reversal is recorded where the new entries are declared: the app never executes the bytes,
  download is `attachment` + `nosniff` + bearer-only, and the comparable product accepts exactly this set.
- **AC-3.4** **Video is out of scope.** `mp4`/`mov` are refused, and the refusal names the accepted list so it
  is not a mystery. The catalog makes adding them one entry.
- **AC-3.5** Caps are per category: 25 MB for documents, text and raster images; 150 MB for `dcm`, `stl`, `ply`,
  `3mf` and `zip`.
- **AC-3.6** The upload action carries `[RequestSizeLimit]` and `[RequestFormLimits]` sized from the catalog.
  ASP.NET's default 30 MB body limit is otherwise the real ceiling and 150 MB would be unreachable behind a
  framework 413. The limit is raised **per action**, never globally.
- **AC-3.7** Files above the mobile shells' `saveFile` ceiling upload and preview normally and are **refused at
  download in a shell**, with the existing French message naming the size and pointing at a computer
  (`web/lib/download.ts:95-103`). No new code; the degradation is stated, not silent.

### AC-4 — A file can be renamed, described and moved

- **AC-4.1** `PatientFile.Rename(baseName)` recomposes the name from the **stored** extension, so changing the
  extension is unrepresentable through the API rather than merely refused.
- **AC-4.2** `UpdatePatientFileCommand` is the first caller of `Rename`, `UpdateDescription` and
  `MoveToFolder`. Fields are tri-state per the repo convention: omitted = unchanged, `""` = cleared.
- **AC-4.3** `RenamePatientFolderCommand` is the first caller of `PatientFolder.UpdateName`.
- **AC-4.4** Both are `AnyClinicRole` — record yes, erase no. `ClinicalRecordAccessTests` classifies them, and
  its drift guard still fails on an unclassified new action.
- **AC-4.5** Moving a file into a folder belonging to another patient, or renaming a file of another clinic, is
  refused by the per-handler tenant check.
- **AC-4.6** The rename UI shows the extension as a fixed, non-editable suffix beside the editable base name.

### AC-5 — The manager looks like the rest of the app

- **AC-5.1** The `accept` attribute and the client-side pre-check are derived from
  `GET /api/meta/upload-policy`, not from a constant mirrored by hand. An oversized or unsupported file is
  refused instantly, in French, before any bytes leave the browser; the server still re-checks.
- **AC-5.2** Images render a thumbnail. Fetch is gated on `IntersectionObserver`, on the entry's
  `isBrowserPreviewable` flag and on a size threshold; live object URLs are pooled, bounded and revoked on
  eviction and unmount. A HEIC file shows an icon and says why — never a broken image.
- **AC-5.3** The preview hook and dialog exist **once**, consumed by both the manager and the patient page's
  « Fichiers » tab. The two current copies are deleted.
- **AC-5.4** Upload is a per-file queue with a per-file outcome, on `import-patients-dialog.tsx`'s `RowCard`
  shape. One refusal no longer reports the batch as failed.
- **AC-5.5** Per-row actions live in one menu (`coarse:py-3`), replacing the two adjacent 32 px buttons — an
  overlay hit area there would have the later sibling steal the earlier one's taps.
- **AC-5.6** `showErrorToast` replaces every raw `toast.error` in the manager, so a network failure offers
  « Réessayer ».
- **AC-5.7** `EmptyState` covers the three kinds — nothing yet / nothing matching the filter / failed to load —
  and a loading skeleton is distinct from empty.
- **AC-5.8** The folder grid's ungated `grid grid-cols-2` (`patient-files-manager.tsx:535`) becomes
  `sm:grid-cols-2`; it is two columns at 320 px today.
- **AC-5.9** `getFiles` is paged. It currently returns every file of the patient, unbounded.
- **AC-5.10** The device contract holds at 320 / 390 / 820 / 1180 / 1440 px and on a landscape phone; the
  widths walked are named in `progress.md`.

---

## Out of scope

- **`Clinic.LogoContentType`.** The logo gains real validation, but `GetClinicLogoQuery.cs:74` keeps serving its
  hardcoded `"image/png"`. Storing the validated type needs a migration, and the working tree carries an
  uncommitted `20260807102000_AddClinicSignups` migration plus a modified `ApplicationDbContextModelSnapshot` —
  scaffolding a second migration over an uncommitted snapshot is how two migrations come to duplicate each
  other. Dropping it makes this story **migration-free**, which is also why `verify-schema` is honestly not
  applicable here rather than a place for a gap to hide. Captured as a follow-up.
- **Video formats** (AC-3.4).
- **Server-generated thumbnails.** Would need a new native image dependency (`api/` has PdfSharp, QuestPDF and
  QRCoder and no image codec), a derivative table, a backfill over every existing blob and a new blob lifecycle
  — and it addresses only the preview half. Revisit if a grid of 40 MB panoramiques proves slow.
- **A patient-file merge or soft delete.** Neither exists; neither is added.
- **E2E tests**, per the pipeline's own sequencing.
