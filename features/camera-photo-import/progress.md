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

## Phase 2 — server (done, suite green)

- `PhotoOriginalsCopy` (one row per clinic PC holding originals, `StaleAfterDays = 7`), its repository, config,
  migration `20261113182912_AddPhotoOriginalsCopies` (xmin stripped).
- `ReportPhotoOriginalsCopyCommand` under `Features/Backup` (no realtime broadcast) + `PhotoOriginalsCopyCheck`;
  served as `POST api/photo-import/originals-copy` (`AnyClinicRole`, `[AllowsWithoutSubscription]` — `BackupController`
  is `AdminOnly` at class level and policies AND). The server compares the report with the originals it has on record
  for that PC before it advances the stamp.
- `ClinicRecoveryPointJob` raises / clears `PhotoOriginalsCopyStale` (bell, Camera icon, amber).

## Phase 2 — Windows app 1.8 + the page (done, gates green)

- `desktop/ClinicManagement.DesktopShell/Camera/`: file kinds, batch planner (window: 7 days for a new source, the
  day before the newest filed photo for a known one; cap 1 000, newest kept), journal (per account, atomic, prune 180 /
  7 days), staging (one folder per photo, the camera's own name, SHA-256 while copying), card source, portable-device
  source (`StorageDevice`), sources finder, arrival watcher (`WM_DEVICECHANGE` volumes + a `DeviceWatcher`), originals
  store (`%ProgramData%\ClinicManagement\photos`, `index.csv`, 10 Go floor), originals copy
  (`{archive}\photos-originales\`), settings (`camera.json`, off by default, ignored devices), messages, import
  service, bridge. Root: `AddOnlyFolderCopy` (shared with the coffre), `PhysicalDisks` (disk extents).
- `VaultBridge.Script`: nine camera methods + the `__clinicShellDeliverCamera` seam with a queue; `MainWindow` routes
  `camera-*` and calls `OnClinicPageLoaded` last on a clinic page; `ShellNotifications.ShowCameraDetected`.
- Page: `lib/photo-import/shell-camera.ts` (the one reader), `components/photo-import/shell-camera-bridge.tsx` (in the
  root layout: listener, « Appareil photo détecté » toast with « Importer » / « Ignorer cet appareil »), the session
  acknowledges every photo (filed + keep original when the cloud stored a copy · already · skipped · failed), « Depuis
  l'appareil branché » + « N photos en attente », « Ajouter les photos plus anciennes restées sur l'appareil »,
  « Sur ce PC » in the cameras card, « Original » in the file preview.
- Found and fixed on the way: the coffre's copy reported « 0 octet » after its first run (the 30-day alert fired over
  complete copies); the mirror's manifest listed coffre files, and ten in a row stopped every mirror run.
- Tests: `CameraPlanningTests`, `CameraImportFlowTests` (card → staged → filed → original kept → not offered again;
  pending after restart; failed kept; housekeeping; add-only copy totals; same-disk refusal; originals copy),
  `CameraContractTests` (file kinds ↔ `sources.ts`, method set ↔ `clinic-shell.d.ts` + `bridge.md`, both red-proofed).
  Desktop: **238 passed**. Web: `check:responsive` 88/88 · `tsc` 0 · `npm run build` OK (`/photos/import` static).
  API: **7 186 passed, 0 failed**.

## Browser walk (done, GREEN)

[`qa/run-1.md`](qa/run-1.md): isolated stack (`:5099`/`:3099`, a copy of the dev database, a throwaway bucket), 26
scenarios pass, 1 not exercised. It found **F1** — a visit booked with no practitioner was invisible to a camera
following a dentist's agenda (most visits name nobody) — fixed on both sides (`agendaFor` and
`GetPhotoImportVisitsAsync`), pure test added. Gates after the fix: API 7 186 / 0 · `tsc` 0 · `check:responsive` 88/88
· build OK · pure specs 19/19.

## Phase 3 — storage you can see (done, gates green)

- `Clinic.StorageQuotaBytes` (null = the deployment's default; 1 Go – 2 To), migration `AddClinicStorageQuota`.
- `ClinicStorageAllowance` reads the cabinet's own quota; `ClinicStorageAlerts.LevelFor` (95 / 80 / clear under 75);
  `StorageNearlyFull` bell rows after each upload's commit (best-effort).
- Console: storage per cabinet (sortable « Stockage » column, card field), « Modifier le quota » with a mandatory
  motif journaled as `StorageQuotaChanged`, the summary's server-disk line (×2 for the backup, warns from 70 % of the
  declared `Deployment:ServerDiskBytes`). Console gate: typecheck · check:responsive 17/17 · build.

## Owed — not done, and why

| What | Why it is not done | Who |
|---|---|---|
| The vendor console's « Stockage » column and « Modifier le quota » in a browser | the walk ([`qa/run-1.md`](qa/run-1.md), GREEN) covered the cabinet's screens; the console needs its own app and a console sign-in. Its gate is green | next session |
| A real Canon and a real Nikon on USB | no camera here; the card path is unit-tested on folders | pilot |
| The Windows app on a real PC: detection toast, « Importer », original kept, copy to the archive disk | the shell cannot run in CI; build + 238 tests only | pilot |
| Tune `MATCH_RULES` on the pilot's real day | needs the pilot's card | pilot |
| Phase 0: PR #84 merged and one off-server backup seen; a decision on the server disk | owner | owner |
| PR for this branch | never pushed without the owner's OK | owner |

## Blast radius (phase 2 + 3)

| Touched | Other consumers | Verdict |
|---|---|---|
| `VaultBridge.Script` (methods + listener) | coffre seam, chat `notify`, relay, session lock | must re-test on the Windows app: coffre handle still delivered, chat toast still raised |
| `MainWindow` page load / message routing | coffre, archive copy, mirror, updates, relay | unaffected — camera runs last, guarded, only on a clinic page |
| `VaultCopyService` → `AddOnlyFolderCopy` | coffre copy + `POST backup/vault-copy` | must change (done): totals fixed; **same-disk refusal is a behaviour change** on such setups |
| Manifest hosted-only | the mirror | must re-test: one mirror run with a coffre file present |
| `ShellNotifications.Listen` | chat toasts | unaffected — optional parameter |
| `session.ts` acks, `SourceBatch.shell` | browser import path | unaffected — no shell, no item id, nothing sent |
| File preview slot | « Visionneuse », « Pleine résolution » | must re-test: a DICOM and a HEIC keep their button |
| `ClinicStorageAllowance` quota override | every upload door, upload policy | must re-test: an upload under and over a custom quota |
| Console portfolio read (`hostedBytes`) | the portfolio list and sort | must re-test: list loads, « Stockage » sort |

## Deviations

- **No extraction of the drawer's upload engine.** The blueprint planned to lift `upload-queue.tsx`'s engine into a
  shared module. The import keeps its own small queue in `web/lib/photo-import/` instead: one POST per photo (a 2 560 px
  copy is ~1,5 Mo, and the SelfHostedLan original is ≤ 50 Mo on a LAN), module-level so it survives navigation. The
  drawer's queue is resumable-chunk aware and tied to one patient's folder; bending it to a multi-patient batch would
  touch the screen every cabinet already uses for a gain this batch does not need.
- **The originals-copy report is served by `PhotoImportController`**, not `BackupController` (class-level `AdminOnly`
  would have refused reception's session, and policies AND). The command still lives under `Features/Backup`.
- **Journal and staging are per Windows account (`%LocalAppData%`)**, not in `photos\_import`: the photos folder is
  locked to the account that created it, and a journal the second account cannot write would break its import.
- **Staging is per photo, not per batch** (`{itemId}\{original name}`): two cameras both write `IMG_0001.JPG`, and the
  name must stay the camera's own.
- **A detection notifies; it does not copy.** The blueprint queued the batch on plug-in; a phone charged on the PC
  would then have had its owner's photos copied. Copying starts at « Importer ».
- **No camera clock read.** `StorageDevice` does not expose it; the matcher's offset detection stands in.
- **The mirror fix is server-side** (the manifest lists hosted files only) rather than a skip in the shell: it fixes
  every shell version already installed.
