# Progress — Official Documents Production-Ready

**Story:** [story-1-full-official-documents.md](./story-1-full-official-documents.md) (Layer: Full, parts A–F)
**Branch:** `feature/windows-desktop-app` (staying on it per user decision; dependencies — invoice/CNAM/facturation — are committed here, not on main)

## Working tree note (start of session)
- `web/components/document-editor-content.tsx` had **uncommitted work from a different feature** (`cnam-bs1-live-preview` — live BS1 iframe preview). Our story edits the same file, so per the user's decision it was committed first to this branch as `085ae5b feat(cnam-bs1-live-preview): live BS1 PDF preview in the document editor` (+ its `features/cnam-bs1-live-preview/` folder). Our editor edits now start from a clean file.
- `.claude/worktrees/` — unrelated junk, excluded from all commits.
- `features/official-documents-production-ready/` — our feature artifacts (untracked); committed with our implementation.

## Part status (single Layer: Full story, landed part-by-part)

| Part | Delivers | Depends on | Status |
|------|----------|-----------|--------|
| A | Honoraires → invoice + `bulletin-cnam` filename fix | — | implemented (editor dead-code excision deferred — see DEV-1) |
| B | Per-doctor cachet & CNOMDT ordre + Mon profil | — | implemented (admin settings-card UI deferred — see DEV-2) |
| C | Doc snapshot + localization ("Paris"→city) + cachet render + non-editable preview | B | implemented (adds `Clinic.City` — see DEV-3; liaison write-back box left for Part E per FR-6.3 — see DEV-4) |
| D | Certificat correctness (objet/motif, mention, CNOMDT, no data loss) | C | implemented |
| E | Structured lettre de liaison | C | not-started |
| F1 | CNAM catalog + admin screen | — | not-started |
| F2 | VLC + reimbursement + bulletin consumes catalog | F1 | not-started |

## Session log
- 2026-07-21: Setup — committed the entangled BS1 feature; created progress.md. Awaiting part selection.
- 2026-07-21: **Part A implemented** (FR-1 honoraires → invoice + FR-6.2 filename fix).
  - Backend: new shared `DocumentFileNaming.GetDocumentTypeName` helper (adds the missing `bulletin-cnam → bulletin-de-soins-cnam` arm on the update path, FR-6.2); reject `honoraires` in `CreateMedicalDocumentCommand` (up-front, before any lookup) and `UpdateMedicalDocumentCommand`; removed the honoraires QuestPDF case (incl. its `€`) from `PdfGenerationService`.
  - Frontend: new `web/components/documents/honoraires-launcher.tsx` (patient picker → compute not-yet-invoiced dental records from `dentalRecordsApi` + `invoicesApi` → seeded `InvoiceFormModal` draft, no auto-issue); `web/app/documents/page.tsx` honoraires card now opens the launcher instead of the editor (also removed pre-existing dead imports + the unused `getDocumentTypeName` fn — trivial scout-boy cleanup).
  - Tests: `DocumentTypeAndFilenameTests` (TYPE-1 reject + FILE-1/FILE-2 filename map) — **11/11 pass**.
  - Quality gates: `dotnet build` 0 warnings/0 errors; `tsc --noEmit` clean; `next build` clean (ESLint not installed → build gate per skill Step 11; a stale `.next` `./611.js` error cleared with `rm -rf .next`).

## Session log (Part B)
- 2026-07-21: **Part B implemented** (FR-2.5 CNOMDT ordre + FR-3.1 per-doctor cachet, own-or-admin).
  - Domain: `Doctor` gains `OrdreNumberCnomdt`/`CachetStorageKey`/`CachetContentType` + `SetOrdreNumber`/`SetCachet`/`RemoveCachet`; `DoctorConfiguration` maps them; migration `20260721092119_AddDoctorCachetAndOrdre` (3 nullable columns; no data touched — `dotnet ef migrations add` diffs the model offline).
  - Application/API: `UpdateDoctorProfileCommand` (own-or-admin, cachet upload via `IFileStorage` with content-type persisted, deterministic key `{clinicId}/doctors/{doctorId}/cachet`, blob delete on remove), `GetMyDoctorProfileQuery`, `GetDoctorCachetQuery`; `DoctorsController` (`GET/PUT /api/doctors/me`, admin `PUT /{id}`, `GET /{id}/cachet`); `DoctorProfileDto`/`DoctorCachetDto`; extended `DoctorDto` + `GetUserStatusQuery` projection (ordre + hasCachet).
  - Frontend: `doctorsApi` (me/get/update multipart + cachet blob fetch), `DoctorProfileDto` type, `/mon-profil` page + `MonProfilContent` (ordre input + cachet upload/preview/remove), sidebar nav entry.
  - Tests: `DoctorEntityTests` (DOC-1..3, 10 cases) + `DoctorCachetTests` (CACHET-1..5, own-or-admin) — **16/16 pass**; `ControllerAuthorizationCoverageTests` + `GetUserStatus` unaffected (25/25 in the combined run).
  - Quality gates: `dotnet build` 0 errors; **pre-existing warning baseline unchanged** — the ~50 solution warnings are all CS8618 EF-ctor / CS860x nullable-deref / CS0618 / CS8981 in pre-existing files (every entity's private EF ctor, existing controllers/services, old migrations), NONE in the files this part added/changed; my new nullable properties add zero warnings, and fixing the codebase-wide baseline is out of this feature's scope. `tsc` clean; `next build` clean (`/mon-profil` static).

## Session log (Part C)
- 2026-07-21: **Part C implemented** (FR-3.2/FR-3.3 cachet snapshot+render, FR-6.1 city/TND localization, FR-6.3 non-editable preview).
  - **`Clinic.City`** (DEV-3, user-directed): `Clinic` entity (`City` + ctor/`Update`) + `ClinicConfiguration` + migration `20260721100408_AddClinicCity` (one nullable column); `ClinicDto`/`CreateClinicRequest`(both API + Application)/`UpdateClinicRequest`/`SetupRequest` + `CreateClinicCommand`/`UpdateClinicCommand`/`AuthController.Setup` + `GetUserStatusQuery` projection. FE: `clinics.ts` (`ClinicDto`/`create`/`update`/`setup` + FormData), `setup-wizard`/`clinic-settings` send `city = governorate` (the Tunisian governorate is the cabinet city), `document-editor` reads `status.clinic.city`.
  - **Snapshot** (FR-3.3): new `PractitionerRenderSnapshot` helper (shared ContentJson key constants + null-safe resolver); `CreateMedicalDocumentCommand` (now injects `IClinicContext`/`IDoctorRepository`/`IClinicRepository`) merges `clinicCity`/`doctorOrdreNumber`/`doctorCachetKey`/`doctorCachetContentType` into ContentJson at create — best-effort, never fails creation. `MedicalDocumentPdfData` gains the 4 fields; `PdfGenerationJob` reads them from ContentJson; the download endpoint overlays them server-side via new `GetPractitionerRenderSnapshotQuery` (frontend can't know the cachet key).
  - **Render** (FR-3.2/FR-6.1): `PdfGenerationService` injects `IFileStorage`; place line is now `{City}, le {date}` (fr-FR month names, never "Paris"; `Le {date}` when no city); `ComposeSignature` draws the cachet image (blob fetched via `IFileStorage`, plain-line fallback on missing/error — never throws).
  - **Preview** (FR-6.3): all display spans in `document-editor-content.tsx` made non-editable (dropped `contentEditable`/`suppressContentEditableWarning`), header now "Aperçu en lecture seule…"; Word export + preview place line use the city. **Liaison free-text write-back box intentionally kept** (DEV-4).
  - Tests: `CertificatContentTests` (CERT-5, 5 cases) + `GenericDocumentRenderTests` (REND-3..6, 9 cases); updated the 2 handler-construction sites (`DocumentTypeAndFilenameTests`, `PostVisitReviewCompletionTests`) for the new ctor. **Full suite 465/465 pass.**
  - Quality gates: `dotnet build` 0 errors (warnings are the pre-existing baseline — none in changed files, verified via `--no-incremental`); `tsc --noEmit` clean; `next build` clean.

## Session log (Part D)
- 2026-07-21: **Part D implemented** (FR-2.1–2.5 certificat correctness; fixes the R-2 save-vs-render field mismatch).
  - **Root cause of the data loss (R-2):** the certificat FE wrote `reason`/`duration`/`notes` in `handleSave` but `doctorOrderNumber`/`startDate`/`duration` in `buildDocumentData` — and the renderer read the latter set. So the objet/motif was never rendered and the ordre/start-date were dropped from the *saved* (background-job) PDF. Also `patientDateOfBirth` was only added on the download path, so the saved certificat's DOB rendered as `[JJ/MM/AAAA]`.
  - **Unified certificat content schema** (`document-editor-content.tsx`): one shape — `objetMotif` + `doctorOrderNumber` + `startDate` + `duration` (+ `patientDateOfBirth`) — written **identically** by `handleSave` **and** `buildDocumentData`, read back by the load effect, mirrored in `resetForm`. `reason`/`notes` removed (they were certificat-only and never rendered).
  - **FE form:** objet/motif primary `Textarea`; collapsible `<details>` "Repos médical (optionnel)" wrapping duration + start date (auto-expands when editing a doc that already has repos data, via `reposOpen`); the ordre input is now **disabled/read-only** and **pre-filled from the doctor's profile** (`currentUserDoctor.ordreNumberCnomdt`, FR-2.5) via a fill-if-empty effect (keeps a legacy doc's stored ordre). Label corrected to "Numéro d'ordre (CNOMDT)".
  - **FE preview + Word export:** both now render from one shared closure `certificatBodyParagraphs()` (+ `formatFrDate`) so the read-only preview, the Word export, and the server PDF read identically — objet/motif body + optional repos sentence + the mandatory mention (`CERTIFICAT_MANDATORY_MENTION`) + the CNOMDT label (`CERTIFICAT_ORDRE_LABEL`). Old hardcoded "Ordre des Médecins" repos template removed.
  - **BE render** (`PdfGenerationService.cs` + new pure `CertificatTextBuilder`): extracted the certificat text into a deterministic, unit-testable `CertificatTextBuilder.Build(...)` (mandatory mention FR-2.3 above the signature footer; CNOMDT label FR-2.4; free objet/motif; repos clause only when a duration is set). The renderer now prefers the authoritative **profile snapshot** ordre (`data.DoctorOrdreNumber`, Part C key `doctorOrdreNumber`), falling back to the legacy typed `doctorOrderNumber` for pre-snapshot docs. `fr-FR` culture used for the date formats.
  - **FE type:** added `ordreNumberCnomdt`/`hasCachet` to `DoctorDto` in `clinics.ts` (backend `GetUserStatusQuery` already projects them — Part B).
  - Tests: new `CertificatTextBuilderTests` (REND-1 mention, REND-2 CNOMDT-not-"Ordre des Médecins", CERT-2 repos omitted, CERT-3 repos rendered, + singular-day + missing-ordre placeholder — 7 cases) and `CertificatContentTests` extended (CERT-1 create round-trip, CERT-4 update round-trip). **Targeted run: CertificatTextBuilder+CertificatContent 13/13, render+doctype+post-visit 30/30 — all pass** (SAC did NOT block `dotnet test --no-build` this session).
  - Quality gates: `dotnet build` (full, `--no-incremental`) 0 errors; **57 pre-existing warnings, 0 in changed files** (verified via grep over `CertificatTextBuilder`/`PdfGenerationService`/the 3 test files); `tsc --noEmit` clean; `next build` clean.

## Deviations

### DEV-4: Liaison free-text write-back preview box kept in Part C (removed in Part E)
**Date:** 2026-07-21 · **Story:** 1 / Part C · **Category:** Scope
**Original Plan:** Part C step 4 "make the preview card non-editable — drop `contentEditable`/`suppressContentEditableWarning` on preview spans."
**Actual Implementation:** All **display** spans (letterhead, place/date, patient, DOB, doctor name/specialty, recipient block) were made non-editable. The one remaining `contentEditable` — the liaison **free-text content box** with an `onBlur` write-back (`formFields.content`) — was left in place.
**Justification:** FR-6.3 itself scopes this: "The one preview field that currently writes back — the liaison content box — is superseded by FR-4's structured fields." That box is today the **only** way to enter liaison body text; removing it in Part C (before Part E adds the structured liaison form) would strand liaison content entry and ship a broken state. Part E replaces it with the guided structured fields and removes the write-back then.
**Impact:** Preview is fully read-only for every document type except the liaison content box, which remains editable until Part E. No data loss (the display spans never wrote back anyway).
**Approved:** Yes (matches FR-6.3's explicit carve-out)

### DEV-3: Part C adds a real `Clinic.City` column (+ migration + clinic CRUD/UI) instead of deriving the city from the free-text address
**Date:** 2026-07-21 · **Story:** 1 / Part C · **Category:** Scope / Technical
**Original Plan:** FR-6.1 says the document place line ("{ville}, le …") is "derived from clinic data"; the plan explicitly listed only **two** migrations (`AddDoctorCachetAndOrdre`, `AddCnamCatalog`) and no `Clinic` schema change, implying the city would be parsed/derived from the existing free-text `Clinic.Address`.
**Actual Implementation:** Per the user's explicit instruction ("add city field to clinic"), Part C adds a first-class nullable `Clinic.City` column: `Clinic` entity (`City` + threaded through ctor/`Update`), `ClinicConfiguration`, a **third** migration `AddClinicCity` (one nullable column, additive/safe), `ClinicDto` + create/update requests + `CreateClinicCommand`/`UpdateClinicCommand` + `GetUserStatusQuery` projection, and the FE surfaces to edit it (clinic-settings, setup-wizard) + read it (types.ts). The document create command then **snapshots the authoritative `Clinic.City`** (server-side, alongside the doctor cachet/ordre) into `ContentJson`, and the renderer prints `{city}, le {date}` (never "Paris").
**Justification:** A dedicated field is authoritative and reliable; deriving a city from a free-text address is fuzzy and error-prone on legal documents. User explicitly chose this. The extra migration is additive (nullable column, no data touched) and safe for existing Cloud/Local DBs.
**Impact:** +1 migration (3 total for the feature, not 2). Clinic create/update/settings/setup surfaces gain an optional Ville field. No breaking change (nullable; empty city → renderer prints "Le {date}" with no place). Cloud unchanged in behavior.
**Approved:** Yes (user-directed)

### DEV-2: Admin "manage another doctor's cachet/ordre" UI (Settings → Médecins card) deferred; backend capability delivered
**Date:** 2026-07-21 · **Story:** 1 / Part B · **Category:** Scope
**Original Plan:** Part B step 5 also adds ordre + cachet fields to each doctor card in `clinic-settings.tsx` (admin-manage-others).
**Actual Implementation:** The **backend** admin path is fully implemented and tested — `PUT /api/doctors/{id}` with own-or-admin (`user.IsAdmin() || doctor.UserId == userId`), covered by `CACHET-1` (admin sets any doctor's ordre + cachet). The self-service **Mon profil** page (`/mon-profil`) is delivered. The `clinic-settings.tsx` doctor-card UI affordance for an admin to edit *another* practitioner's ordre/cachet is deferred.
**Justification:** Admin-manage-others is fully reachable via the API today; the settings-card wiring is a secondary convenience surface (and cachet upload there needs the same new endpoint per doctor). Keeping the session focused on the tested vertical (entity → endpoints → self-service UI) avoids bloating Part B; the settings-card affordance is a small additive FE follow-up that pairs naturally with Part F's admin-screen work.
**Impact:** No capability gap at the API level. An admin currently sets another doctor's ordre/cachet via the endpoint (or that doctor sets their own via Mon profil); only the in-Settings UI shortcut is pending.
**Approved:** Yes (scope kept to the tested vertical)

### DEV-1: Editor internal honoraires dead-code excision deferred to the Parts C/D/E editor rework
**Date:** 2026-07-21 · **Story:** 1 / Part A · **Category:** Scope
**Original Plan:** Part A step 4 removes the `honoraires` type from `document-editor-content.tsx` (formFields, `ProcedureItem`, form/preview/Word branches, `getDocumentTitle`, auto-total effect, `createNewProceduresIfNeeded`) as well as the PDF path.
**Actual Implementation:** The PDF path and both command handlers reject/remove honoraires now; the documents-page card no longer routes to the editor. The ~20 honoraires touchpoints **inside** the 2200-line editor are left in place (now dead/unreachable) and will be excised during the Parts C/D/E editor rework, which restructures those exact regions (certificat/liaison forms, preview, Word export).
**Justification:** (a) The backend now **rejects** honoraires on create/update, so no new honoraires `MedicalDocument` can be produced regardless of the editor — no data-integrity risk. (b) The `/documents/honoraires` route is no longer reachable from the UI. (c) Excising ~20 interleaved spots from a file about to be heavily reworked in C/D/E would be redone/conflicting work and risks a fragile partial edit. Surfaced to the user before coding; they approved implementing Part A.
**Impact:** The editor temporarily retains unreachable honoraires code until C/D/E. No functional or fiscal impact (creation is blocked server-side).
**Approved:** Yes (scope discussed up-front)

## Auto-Approved Deviations
| Deviation | Classification | Reason |
|-----------|----------------|--------|
| Removed dead imports (`medicalDocumentsApi`, `MedicalDocumentDto`, `format`, `toast`, `Edit`, `Trash2`, `FolderOpen`, `Button`) + unused `getDocumentTypeName` fn from `documents/page.tsx` | Trivial | Pre-existing dead code in a file I was already editing; keeps typecheck/lint clean (scout-boy) |
| Left the pre-existing unused `ParseAmount` in `PdfGenerationService` | Trivial | Already dead before this story; out of Part A scope, not a compiler warning |
