# Camera photo import — progress

Worktree `.claude/worktrees/camera-photo-import`, branch `feature/camera-photo-import` (from `origin/main` d46c3d11,
upstream unset on purpose — **never push without the owner's OK**). Owner said 2026-11-13: « start implementing, do
not ask me any questions, go with all recommended » → import option 1 + Windows-app pairing + storage A.

## Phase 1 — API (done, suite green)

- Domain: `FileOrigin` (Upload=0, Camera=1), `PhotoCaptureFacts` (key shape, plausible-time clamp ≥ 2001, ≤ now+1h),
  `PatientFile` (Origin, CapturedAt, CaptureKey, CameraModel, Original*, `RecordCameraCapture`, `RecordOriginal`,
  `MoveToPatient`), `FileUploadSession` capture fields, `ClinicCamera` aggregate, `IClinicCameraRepository`,
  `PhotoImportRows` (ImportedCaptureRow, PhotoImportVisitRow, FicheSaveRow, CameraOriginalTotals).
- Repos: `GetImportedCapturesAsync`, `CaptureKeyExistsAsync`, `GetCameraOriginalTotalsAsync` (files);
  `GetPhotoImportVisitsAsync` (appointments); `GetFicheSavesAsync` (fiches); `AnyForFileAsync` (documents);
  `ClinicCameraRepository`. Config + DbSet + tenant filter + DI.
- Migration `20261113173552_AddCameraPhotoImport` (xmin stripped).
- Application: `CameraCaptureInput` + `CameraImportRefusals` + `UniqueViolation` (Features/Files/CameraCapture.cs);
  upload / start / complete carry capture facts, refuse a known key before storing, map the unique violation;
  completion re-checks the quota (found-on-the-way fix); `DefaultPatientFolders` (moved out of the initializer);
  `EnsureDefaultPatientFolderCommand`, `MovePatientFileToPatientCommand` (+`FileMoveRefusals`),
  `RecordFileOriginalCommand`; `GetImportedCaptureKeysQuery`, `GetPhotoImportContextQuery` (Features/PhotoImport);
  camera registry commands/queries (Features/Cameras → realtime key `cameras`); upload policy `cameraCopy`.
- API: `PatientFilesController` (+ `folders/default/{key}`, `{fileId}/patient`, `{fileId}/original`, capture fields on
  both upload doors), `PhotoImportController` (`context`, `known`), `ClinicCamerasController`.
- Tests (`UnitTests/Features/PhotoImport/`, +77): domain rules, the wire → facts conversion, upload with capture
  facts and the duplicate refused **before storage** (asserted on the storage mock), the context read (wall clock,
  late arrival in the expected end, range codes, the doctor filter on fiches), known keys, move (default folder →
  target's same folder, created if missing; refusals by code), the original report (idempotent), the camera
  registry (upsert keeps the pairing, other cabinet's doctor refused, mark forward only, future time ignored), and
  the upload policy's `cameraCopy` (hosted drawer only). Full suite: **7158 passed, 0 failed** (2026-11-13).

## Phase 1 — web (done, gate green)

- `web/lib/photo-import/`: `exif.ts`, `keys.ts`, `wall-clock.ts`, `match.ts` (`MATCH_RULES`), `sources.ts`,
  `cloud-copy.ts`, `session.ts` (module store; survives navigation; `beforeunload` while sending).
- API modules `photo-import.ts`, `clinic-cameras.ts`; upload policy `cameraCopy`; `refreshUploadPolicy()`;
  `encodeBitmap(quality)` + `fitWithin(maxEdge)`; `preview.ts` pins `imageOrientation: "from-image"`.
- Screen `/photos/import` (`components/photo-import/*`), « Importer des photos » on « Fichiers », « Photos de
  l'appareil » on a patient's files, « Changer de patient » in the file menu, the camera badge + preview line,
  « Appareils photo » card in Paramètres, zone + icon for `/photos`.
- `check:responsive` N55 `exif-has-one-owner`, N56 `match-rules-have-one-owner` (both red-proofed with a throwaway file).
- `e2e/specs/photo-match.spec.ts` (18 pure tests: 13 matcher scenarios held to « never sure and wrong », 5 EXIF/key
  tests on a JPEG built byte by byte, both byte orders) + `e2e/playwright.pure.config.ts` to run them with no stack.
- Gate: `check:responsive` 88/88 · `tsc` 0 · `npm run build` OK (`/photos/import` built) · pure specs 18/18 ·
  API suite 7158/0. `check-coverage.mjs` exits 1 on main already (64 tier-0 rows untested) — unchanged by this.
- Matcher changes found while writing the scenarios: the clock offset is the MIDDLE of the best-scoring run (the
  smallest one put every photo on the first minute of its visit); the order check compares real arrivals
  (`start + late`), not booked starts; « Couper » cuts where the first patient's visit ends (equal 2-minute gaps
  have no widest gap).

## Next

1. Browser pass of `/photos/import` on the isolated stack (`:5099`/`:3099`) — not run yet.
2. Phase 2: shell 1.8.0 (camera on USB / SD detection, originals store, archive copy) + `ReportPhotoOriginalsCopyCommand`
   and the 7-day alert.
3. Phase 3: per-clinic quota, console storage column, 80 / 95 % bell alerts, server-disk warning.

## Deviations

- **No extraction of the drawer's upload engine.** The blueprint planned to lift `upload-queue.tsx`'s engine into a
  shared module. The import keeps its own small queue in `web/lib/photo-import/` instead: one POST per photo (a 2 560 px
  copy is ~1,5 Mo, and the SelfHostedLan original is ≤ 50 Mo on a LAN), module-level so it survives navigation. The
  drawer's queue is resumable-chunk aware and tied to one patient's folder; bending it to a multi-patient batch would
  touch the screen every cabinet already uses for a gain this batch does not need.
