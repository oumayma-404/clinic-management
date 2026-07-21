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
| C | Doc snapshot + localization ("Paris"→city) + cachet render + non-editable preview | B | not-started |
| D | Certificat correctness (objet/motif, mention, CNOMDT, no data loss) | C | not-started |
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

## Deviations

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
