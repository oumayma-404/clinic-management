# Story 1 — Patient file uploads: breadth, validation shape, and the manager UX

**Status:** in progress
**Covers:** AC-1 … AC-5 (all of [spec.md](../spec.md))
**Structure:** five ordered parts, each a build gate and a commit boundary. See
[plan.md § Story shape](../plan.md#story-shape).

## Entry criteria

- [x] `spec.md` APPROVED, `plan.md` APPROVED
- [x] Branch settled (`feature/audit-sections-3-to-10`, user decision)
- [x] Pre-existing working-tree changes identified and excluded (R-2)
- [x] No new dependencies needed — `@radix-ui/react-progress` and `@radix-ui/react-aspect-ratio` are already in
      `web/package.json`; the missing pieces are the shadcn wrappers, not the packages

---

## P1 — The server's refusal reaches the user (AC-1)

1. In `web/lib/api/client.ts`: extract the `!response.ok` branch of `handleResponse` into
   `throwIfNotOk(response)`; have `handleResponse` call it. **No behaviour change** — the branch moves whole.
2. Add `readBlob(response)` = `throwIfNotOk` then `response.blob()`.
3. Give `handleRequest` an optional third parameter, the body reader, defaulting to `handleResponse`.
4. Extract `apiGet`'s URL-building (the `new URL` + params walk, including the SSR `window` guard) into
   `buildUrl(endpoint, params?)`; `apiGet` uses it.
5. Export `apiGetBlob(endpoint, params?, accessToken?)` — `buildUrl` + `handleRequest(..., readBlob)` with the
   20 s deadline.
6. Rewrite `web/lib/api/patient-files.ts`: `initializeDefaultFolders` → `apiPost`, `createFolder` → `apiPost`,
   `uploadFile` → `apiPostFormData`, `downloadFile` → `apiGetBlob`. Drop the module's `API_BASE_URL`, its
   `getAccessToken`/`apiHeaders` imports and all four hand-written error blocks.

**Verify:** `npx tsc --noEmit` · `npm run check:responsive` (the `api-headers` check must still pass, and the
module no longer touches headers at all) · `npm run build`. Then confirm by inspection that an upload failure
path now reaches `handleResponse`, i.e. reads `error` before `title`/`message`.

## P2 — One extension-keyed catalog, six call sites (AC-2, AC-3)

1. `Application/Common/Files/SignatureRule.cs` — `Required(offset, magic, …alternates)` · `Advisory(offset,
   magic)` · `None(reason)`, reason non-empty.
2. `FileTypeEntry.cs` — extensions, canonical content type, `FileType` category, `MaxBytes`, signature rule,
   `IsBrowserPreviewable`, French label.
3. `FileTypeCatalog.cs` — the AC-3.1–3.3 entries, `DeniedExtensions` (AC-2.5), `TryGet`,
   `MaxBytesAcrossCatalog`.
4. `SignatureIndex.cs` — `IdentifyOrNull(header)` for the AC-2.3 cross-check.
5. `FileNameSanitizer.cs` — AC-2.10.
6. `FileUploadProfile.cs` — `PatientFile` · `ProfileImage` · `MedicalDocumentPdf` · `Csv`, each with its own
   derived French refusal message (AC-2.9).
7. `FileUploadValidator.cs` — deny-list → catalog lookup → declared length vs cap → 4 KB header read →
   signature → `header + remainder` stream (AC-2.8).
8. Rewire: `UploadPatientFileCommand` (drop the `MemoryStream` and `DetermineFileType`),
   `UpdateDoctorProfileCommand` (delete its three private copies), `CreateClinicCommand`,
   `UpdateClinicCommand`, `CreateMedicalDocumentCommand` + its update path, the CSV import.
9. Delete `Application/Common/FileContentValidation.cs`.
10. `PatientFilesController.UploadFile` — `[RequestSizeLimit]` + `[RequestFormLimits]` from
    `MaxBytesAcrossCatalog` (AC-3.6).
11. Tests: `FileTypeCatalogTests` (derived) · `FileUploadValidatorTests` (the named cases, incl. the reported
    txt→pdf) · `ProfileImageValidationTests` (carry over `DoctorCachetTests`' spoof case) ·
    **`MagicByteOwnershipTests`** (AC-2.7, source scan — prove it fails first, per R-4).

**Verify:** `dotnet build --no-incremental` (0 new warnings) · the new test classes green · confirm
`MinioFileStorage.UploadAsync` accepts a 150 MB stream (R-5).

## P3 — The policy is served, not mirrored (AC-5.1)

1. `Features/Meta/Queries/GetUploadPolicyQuery.cs` + `DTOs/UploadPolicyDto.cs`, projected from the catalog.
2. `MetaController` — `GET upload-policy`, `[Authorize(Authenticated)]`, **not** added to the client-version
   floor's exemption (only `client-requirements` earns that).
3. `web/lib/api/upload-policy.ts` + a module-cached `useUploadPolicy()`; builds the `accept` string and the
   pre-check.

**Verify:** backend build + `ControllerAuthorizationCoverageTests` still green (a new action needs its policy) ·
`tsc` · `check:responsive` · `build`.

## P4 — Rename, describe, move (AC-4)

1. `PatientFile.Rename(baseName)` — recomposes from the stored extension (AC-4.1).
2. `UpdatePatientFileCommand` (tri-state) and `RenamePatientFolderCommand`.
3. `PatientFilesController` — `PUT {fileId}`, `PUT folders/{folderId}`, `AnyClinicRole`; request models.
4. `ClinicalRecordAccessTests` — classify both new actions (AC-4.4).
5. `web/lib/api/patient-files.ts` — `updateFile`, `renameFolder`.
6. `rename-file-dialog.tsx` — `mobile="sheet"`, editable base name beside a fixed extension suffix, real
   `<Label htmlFor>`, in-flight disabled, `showErrorToast` leaving the dialog open.
7. Tests: `PatientFileRenameTests` (extension immutable, sanitization, tenant isolation).

**Verify:** backend build + tests · `tsc` · `check:responsive` · `build` · eye pass.

## P5 — The manager UX (AC-5.2 … AC-5.10)

1. `use-file-preview.ts` + `file-preview-dialog.tsx` — one copy; delete both existing ones (AC-5.3).
2. `ui/aspect-ratio.tsx`, `ui/progress.tsx` — scoped Radix imports, revert the CLI's `package.json` edit.
3. `file-thumbnail.tsx` — `IntersectionObserver` + previewable + size gate, bounded revoked URL pool (AC-5.2).
4. `upload-queue.tsx` — per-file rows on `import-patients-dialog.tsx`'s `RowCard` shape, bounded concurrency
   replacing `Promise.all` (AC-5.4).
5. In `patient-files-manager.tsx`: one actions menu (AC-5.5) · `showErrorToast` (AC-5.6) · `EmptyState` + a
   distinct skeleton (AC-5.7) · `sm:grid-cols-2` (AC-5.8).
6. `getFiles` paged (AC-5.9).

**Verify:** `tsc` · `check:responsive` · `build` · eye pass at 320 / 390 / 820 / 1180 / 1440 + landscape phone +
keyboard, widths named in `progress.md` (AC-5.10).
