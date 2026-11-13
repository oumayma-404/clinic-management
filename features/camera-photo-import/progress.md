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

## Next

1. Web: lib/photo-import (exif, keys, match, sources, cloud-copy), api modules, a filing queue of its own (NOT a refactor of `upload-queue.tsx` — see Deviations), import screen,
   settings card, move dialog, files manager badges, clinic-hub `cameras`, check:responsive rules.
2. Phase 2 shell; Phase 3 console storage + per-clinic quota + alerts.

## Deviations

- **No extraction of the drawer's upload engine.** The blueprint planned to lift `upload-queue.tsx`'s engine into a
  shared module. The import keeps its own small queue in `web/lib/photo-import/` instead: one POST per photo (a 2 560 px
  copy is ~1,5 Mo, and the SelfHostedLan original is ≤ 50 Mo on a LAN), module-level so it survives navigation. The
  drawer's queue is resumable-chunk aware and tied to one patient's folder; bending it to a multi-patient batch would
  touch the screen every cabinet already uses for a gain this batch does not need.
