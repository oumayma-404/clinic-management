# Photos de l'appareil, rangées par l'agenda — blueprint

> **Status:** blueprint 2026-11-13, not started. Chosen on the options page
> (https://claude.ai/artifact/LFwsFR3dsmGjaxMpeXHseM): import **option 1** (sorted by the agenda) with **pairing in
> the Windows app**, storage **A** (« deux maisons »). Asked by a dentist who spends a long time copying her camera's
> photos to the PC and sorting them into each patient by hand.
> Code facts were read on `origin/main` (`d46c3d11`). ⚠️ The usual checkout (`feature/security-remediation`) is 237
> commits behind — build in a worktree from `origin/main`.

## Summary

The dentist plugs her camera into the clinic PC (Windows app), or puts its SD card in a reader (any browser; a phone
gallery works too). One screen opens with only the new photos, already grouped by patient: each burst of photos is
matched to that day's appointments, using late arrivals and the time each fiche was saved. She checks it and presses
« Ranger ». Each photo lands in that patient's « Photographie » folder with the time it was taken.

On a hosted clinic the cloud stores a 2 560 px copy of each photo (≈ 1.5 Mo) that opens anywhere; the Windows app keeps
the full-size original on the clinic PC and copies it to the clinic's archive disk. A clinic running on its own PC
stores the original directly — it has no quota.

Nothing is filed before « Ranger ». Nothing on the camera or the card is ever modified or deleted.

## The facts that decided the shape

| Fact | Source |
|---|---|
| A browser cannot read a camera on a USB cable on Windows: the OS driver owns it and WebUSB is incompatible with it | web.dev, « Porting libusb to WebUSB » |
| An SD card in a reader is a drive letter; Chrome's folder picker does not block removable drives | Chromium `chrome_file_system_access_permission_context.cc` |
| Canon/Nikon developer kits: application by a registered company, renewed yearly, revocable, per model; none for the D3500 | asia.canon SDK terms; sdk.nikonimaging.com |
| Bluetooth on these cameras pairs, fires the shutter and sets the clock; Canon sends photos over Wi-Fi, Nikon shrinks them to 2 MP; the protocol is private | Nikon SnapBridge pages; Canon Camera Connect manual |
| Every dental tool checked (Dolphin, Kitview, Open Dental, Carestream) makes you pick the patient first; none sorts by appointment | research, 2026-11-13 |
| 10 Go per hosted clinic = ≈ 1 280 full-size photos ≈ 2 months at 30 a day | `ClinicStoragePolicy.cs:31` |
| The server disk (96 Go) holds ≈ 3 full clinics: every byte is stored twice (backup on the same disk) | `deploy/README.md:544-565` |
| The hosted backup fix (PR #84) is still open | `gh pr view 84`, 2026-11-13 |
| JPEG is cloud-only by design; the coffre refuses it and a test holds that | `FileTypeCatalog.cs:23-33`, `FileTypeCatalogTests.cs:115` |
| Nothing records when a visit really started: `InProgress` / `AwaitingClosure` follow the clock | `AppointmentProgressJob.cs:96-195` |
| Real timing evidence that exists: `Appointment.LateArrivalMinutes` / `ExpectedEndUtc`, `DentalRecord.CreatedAt` + `AppointmentId` | `Appointment.cs:25,405`; `DentalRecord.cs:41,137` |
| No EXIF is read anywhere today; RAW (CR2/CR3/NEF/DNG) is refused as unsupported | `FileTypeCatalog.cs:141-170` |

## Decisions (made — reversible, so not asked)

| Question | Decision | Why · rejected |
|---|---|---|
| How photos reach the app | **Phase 1:** SD card, files or phone gallery, in any browser. **Phase 2:** the Windows app detects a camera on USB or an SD card and opens the import | The card works with every camera. ❌ Canon/Nikon kits: licence on request, per model, and they sort nothing. ❌ Bluetooth: no full-size photos. ❌ Wi-Fi SD cards: FlashAir support ended March 2026. |
| How a photo finds its patient | Bursts matched to the day's visits in the browser (pure `matchPhotosToVisits`), fed by one server read: visits, late arrivals, fiche save times | Nobody else does it. ❌ « The patient open on screen » (the sensor model): an end-of-day batch has no open patient. ❌ A QR card first: one extra photo per patient, ~15 % unread — kept as a later fallback. |
| Who confirms | Always a review screen; nothing is filed before « Ranger » | A photo in the wrong patient is a clinical error; one tap is cheap. Unsure groups wait for a pick. |
| What the cloud stores (hosted) | A 2 560 px JPEG copy, quality 0.92 (≈ 1.5 Mo), made in the browser with `decoders/raster.ts` | ≈ 6 800 copies per 10 Go ≈ 10 months at 30 a day; 2 560 is the repo's measured « nobody can tell apart in a 1000 px dialog » edge (`web/lib/CLAUDE.md:252`). ❌ Full size: quota, 21 min to upload 40 photos on 2 Mbit/s, a server disk that fits ~3 full clinics. |
| What the cloud stores (clinic's own PC) | The original, untouched | The server is the clinic's PC; `ClinicStoragePolicy` applies to hosted only. |
| Who decides the copy rule | The server, in the upload policy: `cameraCopy: { maxEdgePx, quality } \| null` | « What may be uploaded has one authority, and the browser is told rather than trusted. » Null on self-hosted. |
| Where the original goes (hosted) | The Windows app's own folder `%ProgramData%\ClinicManagement\photos\`, keyed by file id. The server records hash, size and PC name — never the bytes | ❌ The coffre: its record lives only at the clinic (the cloud keeps a preview), it refuses JPEG by design and by test, takes no phone import, and its copy was never run on a real deployment. Here the cloud copy is the record and the original is an extra: losing the PC loses detail, never the photo. |
| The original in a plain browser | Not kept by the app (it stays on the card) | A browser folder grant lapses every day (`patient-files-manager.tsx:620-653`). The Windows app is where originals are kept. |
| The original's safety | Add-only copy to the clinic's archive disk, refused on the same physical disk; server check + bell alert after **7 days** without a complete copy | One disk is one failure. `VaultCopyService` has the add-only logic but no same-disk check — extract it once, use it twice, and the coffre gains the check. The coffre's 30 days is too long for daily photos. |
| Layout of the originals store | `photos\{fileId[0..2]}\{fileId}.jpg` + `photos\index.csv` (file id, patient, taken at, original name) | Keyed by file id, a move between patients never touches the disk; the CSV makes the folder readable without the app. ❌ `{patient}\{date}\name.jpg`: renames and moves re-plan paths (the mirror's problem). |
| Duplicate photos | `CaptureKey` = SHA-256 of camera fingerprint + `DateTimeOriginal` + `SubSecTimeOriginal` + original name + original size; unique per clinic | Re-inserting a card must not re-import 500 photos. The key needs only the EXIF slice; hashing whole files means reading GBs from a card reader. ❌ `ContentHash`: filled only for coffre files, and the copy's bytes differ from the original's. |
| Which doctor's agenda | The camera's doctor (registry), else the signed-in user's doctor (`IDoctorRepository.GetByUserIdAsync`), else every doctor | A shared clinic camera still works; candidates show the doctor. |
| Pairing identity | New aggregate `ClinicCamera`: fingerprint, label, doctor, clock offset, last import | It must survive across PCs and browsers. ❌ Per-PC shell settings: the browser path cannot see them. |
| The camera clock | Offset detection on every import, stored per camera; in phase 2 the Windows app reads the camera's own clock over USB (exact) | A camera an hour off files every photo one patient early. |
| Timezone | The server sends visit times as clinic wall-clock (`ClinicClock.ToClinicLocal`); the browser compares wall-clocks; the server turns the photo's time back to UTC with `ClinicClock.ToUtc` | No timezone maths in the browser. `OffsetTimeOriginal`, when the camera writes it, is honoured. |
| EXIF | One small parser, `web/lib/photo-import/exif.ts` (7 tags + the embedded thumbnail); no dependency | The page is the one reader on every path (the shell hands files over). The embedded 160 px thumbnail makes the review list instant. |
| Formats in v1 | JPEG only; RAW, HEIC, video counted and skipped | RAW is refused by the catalogue; « 37 RAW ignorées — réglez l'appareil en RAW + JPEG » is a fix the dentist can make. |
| Where photos land | The patient's « Photographie » folder, created on demand with the same predictable id | `InitializeDefaultFoldersCommand` creates every missing default folder — it would resurrect folders the clinic deleted. |
| Correcting a wrong patient | `MovePatientFileToPatientCommand`, any role | Delete is doctor/admin only and loses the capture data. The original is keyed by file id, so nothing moves on disk. |
| Uploads survive leaving the page | The upload engine moves out of `upload-queue.tsx` into a layout-level provider | Today leaving the page cancels a chunked upload (`upload-queue.tsx:311-316`). One engine for « Fichiers », the import and later the sensor. |
| Auto-detection in the Windows app | Off by default, a per-PC switch in /settings | A `desktop/**` merge to main ships to every clinic silently (`client-installer.yml`, Velopack). A USB key must not raise a camera prompt in a clinic that never asked. |

## Phases

| Phase | What ships | Who |
|---|---|---|
| **0 · Prerequisites** | PR #84 merged and one off-server nightly backup seen · a decision on the server disk (bigger disk or external file storage) before several clinics import · the pilot's camera model and PC | Owner |
| **1 · Import in any browser** | the import screen, EXIF, matching, camera registry, duplicate check, cloud copy rule, « Photographie » on demand, capture metadata, « Changer de patient », the upload engine move, the quota re-check at completion | Web + API |
| **2 · Pairing in the Windows app** | camera-on-USB and SD-card detection, staging + journal, file handles to the page, the camera clock read, originals store, « Ouvrir l'original », copy to the archive disk + server check + alert, toast, per-PC switch, the mirror fix; shell 1.8.0 | Desktop + small API |
| **3 · Storage you can see** | storage per clinic in the vendor console, a per-clinic quota the vendor can raise (journaled), bell alerts at 80 % and 95 %, a server-disk warning | Console + API |
| Later — named, not planned | QR card as a fallback signal · RAW (original on the PC, cloud copy from its embedded JPEG) · iPhone HEIC · watching the Wi-Fi tools' folder (Canon Image Transfer Utility 2, Nikon Wireless Transmitter Utility) · a photo series per filed group (`clinical-photo-series`) | — |

Each phase is its own branch and PR. Nothing is pushed or merged without the owner's OK.

## Flow

```
Camera ──USB──▶ Windows app (phase 2) ── staging + journal ──▶ handles ─┐
SD card ─reader─▶ Windows app (phase 2) ──────────────────────▶ handles ─┤
SD card / files / phone gallery ──▶ browser picker or drop (phase 1) ────┤
                                                                          ▼
                                     /photos/import  — one screen, every path
                   read EXIF (first 256 Ko of each file) ─▶ capture keys, fingerprint
                   POST photo-import/known ─▶ « 112 déjà importées »
                   camera registry ─▶ doctor + clock offset (asked once for a new camera)
                   GET photo-import/context ─▶ visits + late arrivals + fiche times
                   matchPhotosToVisits ─▶ groups: sure · ask · skipped
                                                                          │ « Ranger »
                                                                          ▼
            ensure « Photographie » ─▶ cloud copy (hosted) or original (clinic PC)
            upload engine (layout provider, 3 at a time) ─▶ PatientFile
                 origin = Camera · capturedAt · captureKey · cameraModel
                                                                          │ Windows app only
                                                                          ▼
            camera-ack(fileId) ─▶ shell keeps the original ─▶ camera-original-kept
            ─▶ PUT files/{id}/original (sha256, size, PC) ─▶ « Original : PC ACCUEIL »
            ─▶ add-only copy to the archive disk ─▶ POST backup/photo-originals-copy
```

## Matching rules

All in `web/lib/photo-import/match.ts`, pure (no React, no fetch). The constants live in one exported `MATCH_RULES` and
are **starting values, tuned on the pilot's real day** (owed, below).

```
Inputs
  photos  { id, takenAt (wall-clock), takenAtOffset?, fingerprint }
  days    per local date — visits { appointmentId, patientId, patientName, doctorId, start, expectedEnd, ficheSavedAt? }
                         — walk-in fiches { patientId, patientName, savedAt }   (a fiche with no appointment)

1 Clock     use the exact skew when the Windows app read the camera clock; else apply the camera's stored offset,
            then score every offset −180…+180 min (1-min steps):
              score(o) = photos whose shifted time falls inside a visit's core.
            Propose o* only if |o*| ≥ 3 min and score(o*) ≥ max(5, 1.3 × score(0)).
            Shown: « L'appareil semble avoir 1 h 02 de retard — corrigé » [Annuler]; saved on the camera after « Ranger ».
            Photos outside the clinic's opening hours flag « L'heure de l'appareil semble fausse ».
2 Bursts    sort by time; a new burst when the gap to the previous photo is > 4 min.
3 Core      a visit's core = [start + lateArrival, max(expectedEnd, ficheSavedAt)].
  Margin    the core widened by 15 min before and 20 min after.
  Walk-in   [savedAt − 45 min, savedAt + 10 min].
4 Decide    t = the burst's median time.
              exactly one core holds t ............ sure
              two or more cores hold t ............ ask (top candidates)
              no core, exactly one margin holds t . sure
              otherwise ........................... ask (candidates by distance, then « Choisir le patient »)
5 Straddle  first and last photo inside two different cores → ask, with « Couper ici » at the largest internal gap.
6 Order     walk the sure bursts in time order; a burst sure about an earlier visit than the burst before it
            demotes both to ask.
7 Same visit twice → two groups on the same patient (avant / après), never merged silently.
8 No date   no DateTimeOriginal → the file date, marked « heure incertaine », always ask.

Visits used: Scheduled, Confirmed, InProgress, AwaitingClosure, Completed — with a patient.
Excluded:    Cancelled, NoShow, blocked slots (no patient).
```

The rule every test holds above all: **never sure and wrong** on the scenario table below. An « ask » costs one tap; a
sure-and-wrong is a clinical error.

## Data model

**`PatientFile`** — all nullable or defaulted, nothing backfilled:

| Column | Type | Note |
|---|---|---|
| `Origin` | `FileOrigin` (int, default 0) | `Upload = 0, Camera = 1` — appended only; the sensor's `Sensor` becomes 2 |
| `CapturedAt` | `DateTime?` (UTC) | from the wall-clock the page sends, via `ClinicClock.ToUtc`; null if > now + 1 h or before 1990 |
| `CaptureKey` | `string(64)?` | unique partial index `(ClinicId, CaptureKey) WHERE "CaptureKey" IS NOT NULL` — delete is a hard delete (`DeletePatientFileCommand.cs:79`) |
| `CameraModel` | `string(100)?` | « Canon EOS 250D », a display snapshot, no FK |
| `OriginalSha256` | `string(64)?` | set by `RecordFileOriginalCommand` |
| `OriginalSizeBytes` | `long?` | 〃 |
| `OriginalHeldBy` | `string(100)?` | the PC's name |
| `OriginalKeptAt` | `DateTime?` | 〃 |

**`FileUploadSession`** gains `Origin`, `CapturedAt`, `CaptureKey`, `CameraModel`: the resumable path builds the file
from the session at completion (`CompleteFileUploadCommand.cs:130-137`), and on a clinic-PC install originals over
8 MiB go in parts.

**New aggregate `ClinicCamera`** (`AggregateRoot<Guid>`, tenant query filter, audited, `xmin`): `ClinicId`,
`Fingerprint string(64)` (unique per clinic), `Make`, `Model`, `SerialSuffix string(8)?` (display only),
`Label string(60)`, `DoctorId Guid?` (nullable, no FK, indexed — the `MedicalDocument.DentalRecordId` shape),
`ClockOffsetMinutes int`, `LastImportedAt DateTime?`, `CreatedAt`.

- Fingerprint = SHA-256 of `Make|Model|BodySerialNumber`; with no serial (older Canons keep it in the maker notes)
  `Make|Model` — two identical bodies then share one entry, which only costs the doctor default.
- `CaptureKey` = SHA-256 of `fingerprint|DateTimeOriginal|SubSecTimeOriginal|original name|original size`. Computed in
  the page only.

**Migration `AddCameraPhotoImport`** — ⚠️ strip the scaffolded `AddColumn<uint>("xmin")` lines; check no `DropColumn`
sits above a backfill; run `dotnet run -- verify-schema` before and after and diff.

## API

Handlers validate inline and return a `Result` with a `Code`; the message is French, the body `{ error }`.

| Verb + route | Role | Handler | Refusal codes |
|---|---|---|---|
| `GET api/photo-import/context?from&to&doctorId` | any clinic role | `GetPhotoImportContextQuery` | `photo-import-range-invalid`, `photo-import-range-too-long` (> 31 days) |
| `POST api/photo-import/known` `{ captureKeys }` (≤ 2 000) | any | `GetImportedCaptureKeysQuery` → `[{ captureKey, fileId, patientId, patientName }]` | `photo-import-too-many-keys` |
| `GET api/clinic-cameras` | any | `ListClinicCamerasQuery` | — |
| `POST api/clinic-cameras` (upsert by fingerprint) | any | `RegisterClinicCameraCommand` | `camera-doctor-unknown` |
| `PUT api/clinic-cameras/{id}` (label, doctor, offset, version) | any | `UpdateClinicCameraCommand` | 409, `camera-doctor-unknown` |
| `DELETE api/clinic-cameras/{id}` | admin or doctor | `ForgetClinicCameraCommand` | — |
| `POST api/patients/{patientId}/files/folders/default/{key}` | any | `EnsureDefaultPatientFolderCommand` | `folder-key-unknown` |
| `POST …/files/upload`, `POST …/files/uploads` (existing) | — | + `origin`, `capturedAtLocal`, `captureKey`, `cameraModel` | `photo-already-imported` (+ `fileId`, `patientId`) |
| `PUT api/patients/{patientId}/files/{fileId}/patient` | any | `MovePatientFileToPatientCommand` | `file-move-same-patient`, `file-move-target-archived`, `file-move-vault`, `file-move-linked-document` |
| `PUT api/patients/{patientId}/files/{fileId}/original` | any | `RecordFileOriginalCommand` | `file-original-not-camera` |
| `POST api/backup/photo-originals-copy` (beside `vault-copy`) | any | `ReportPhotoOriginalsCopyCommand` | — |
| upload policy (existing) | — | + `cameraCopy: { maxEdgePx: 2560, quality: 0.92 } \| null` | — |

Where they live and how they are wired:

- `Features/Files/Commands/` (flat): `EnsureDefaultPatientFolder`, `MovePatientFileToPatient`, `RecordFileOriginal`.
  ⚠️ The namespace must **end in `.Commands`** or nothing broadcasts `files` (`RealtimeResourceResolver.cs:90-110`) —
  no sub-folder.
- `Features/PhotoImport/Queries/`: `GetPhotoImportContext`, `GetImportedCaptureKeys`. The context read follows
  `TreatmentsInProgressReader` (an Application reader over one repository projection); `paging: null`, bounded by the
  31-day cap.
- `Features/Cameras/Commands|Queries/` → a new realtime key **`cameras`**, declared in `web/lib/realtime/clinic-hub.ts`
  (`RealtimeResourceResolverTests` fails otherwise).
- `Features/Backup/Commands/ReportPhotoOriginalsCopyCommand` beside `ReportVaultCopyCommand`, with the same
  subscription treatment as `vault-copy` (a safety report must never be blocked by an expired subscription).
- `ClinicRecoveryPointJob` gains the originals check: originals held by a PC with no complete copy for 7 days → one
  bell notification (« N photos originales ne sont copiées sur aucun disque »), shaped like the coffre alert.
- `EnsureDefaultPatientFolderCommand` reuses `InitializeDefaultFoldersCommand.GenerateDefaultFolderId` (made
  `internal static` — one owner) and creates **only** the asked folder; two parallel calls race to one predictable id,
  so a unique violation re-reads.
- The context query drops `Cancelled`, `NoShow` and patient-less slots, returns times as clinic wall-clock, filters by
  doctor when asked, and returns the fiches of those days with `CreatedAt` (and `AppointmentId` when set).
- The duplicate check runs **before** any byte is stored; the unique index is the backstop when two PCs race (its
  violation maps to the same refusal).
- New controllers `PhotoImportController`, `ClinicCamerasController`; three actions added to `PatientFilesController`.

## Web

**Create**

| File | Purpose |
|---|---|
| `web/app/photos/import/page.tsx` | the screen, behind `ClinicGuard`; opened from `/fichiers` (« Importer des photos »), the patient's « Fichiers » tab, and by the Windows app |
| `web/components/photo-import/import-screen.tsx` | state machine: source → reading → review → filing → done |
| `web/components/photo-import/burst-card.tsx` | time, count, thumbnails (tap one to leave it out), patient or candidates, « Couper ici », « Ignorer ce groupe » |
| `web/components/photo-import/camera-owner-prompt.tsx` | a new camera: « Cet appareil est à : [Dr ▾] » |
| `web/components/photo-import/clock-offset-notice.tsx` | « L'appareil semble avoir … — corrigé » + « Annuler » |
| `web/lib/photo-import/exif.ts` | the one EXIF reader: Make, Model, BodySerialNumber (0xA431), DateTimeOriginal (0x9003), SubSecTimeOriginal (0x9291), OffsetTimeOriginal (0x9011), Orientation (0x0112), the IFD1 thumbnail; reads ≤ 256 Ko |
| `web/lib/photo-import/keys.ts` | `cameraFingerprintOf`, `captureKeyOf` (SubtleCrypto SHA-256) |
| `web/lib/photo-import/match.ts` | `MATCH_RULES`, `detectClockOffset`, `groupIntoBursts`, `matchPhotosToVisits` |
| `web/lib/photo-import/sources.ts` | files from a directory handle (recursive `DCIM`), `<input multiple>`, a drop (`webkitGetAsEntry` recursion), a shell batch; counts what it skips (RAW, HEIC, video, other) |
| `web/lib/photo-import/cloud-copy.ts` | `makeCloudCopy(file, rule)`: `createImageBitmap(file, { imageOrientation: "from-image" })` → `fitWithin` → `encodeBitmap(…, quality)`; the file untouched when `rule` is null |
| `web/lib/photo-import/shell-camera.ts` | phase 2 — the only reader of the camera half of `window.__clinicShell` |
| `web/lib/api/photo-import.ts`, `web/lib/api/clinic-cameras.ts` | API modules over `client.ts` |
| `web/lib/uploads/engine.ts` + `web/components/uploads/upload-engine-provider.tsx` | the engine moved out of `upload-queue.tsx`: `enqueueUpload({ patientId, folderId, file, meta })`, mounted in `app/layout.tsx`, `beforeunload` warning while items are pending |
| `web/components/patients/files/move-to-patient-dialog.tsx` | « Changer de patient » — picker = `staff-chat/patient-link-picker.tsx`, the one reusable patient picker |
| `web/components/settings/cameras-card.tsx` | list, rename, doctor, clock correction, forget; phase 2 adds the PC switch and the originals copy status |

**Modify**

| File | Change |
|---|---|
| `web/components/patients/files/upload-queue.tsx` | a view over the engine — same behaviour for « Fichiers » |
| `web/lib/api/patient-files.ts` | upload metadata, `moveToPatient`, `recordOriginal`, `ensureDefaultFolder` |
| `web/lib/api/upload-policy.ts`, `web/lib/hooks/use-upload-policy.ts` | `cameraCopy`; a `refresh()` |
| `web/components/patients/files/storage-usage-line.tsx` | re-read after a batch (today: once per page load) |
| `web/lib/files/preview.ts` | pin `createImageBitmap(source, { imageOrientation: "from-image" })` (`:57` relies on the default) |
| `web/lib/files/decoders/raster.ts` | `encodeBitmap` gains an optional `quality` (decoders keep their default) |
| `web/components/patient-files-manager.tsx`, `…/file-preview-dialog.tsx` | « Appareil photo · prise le 12 nov. à 09:14 », « Original : PC ACCUEIL » + « Ouvrir l'original » (only where the shell holds it), « Changer de patient » |
| `web/app/fichiers/page.tsx` | « Importer des photos » |
| `web/components/clinic-settings.tsx` | mount `cameras-card` |
| `web/lib/realtime/clinic-hub.ts` | `cameras` |
| `web/types/clinic-shell.d.ts`, `mobile/shared/bridge.md` | phase 2 methods and messages |
| `web/scripts/check-responsive.mjs` | `exif-has-one-owner` · `match-rules-have-one-owner` · `upload-engine-has-one-owner` (no upload call outside the engine) · `camera-bridge-has-one-owner` (phase 2). Use the next free N-numbers at implementation time (main stops at N54) |

**Screen contract** (`.claude/rules/frontend-web.md`): usable at 320 px; groups are cards, never a table; 44 px targets
on a coarse pointer; the « Ranger N photos » bar is sticky and stays visible at **1536 × 730** (the owner's laptop);
long lists virtualised past ~30 groups; thumbnails from the EXIF embedded thumbnail, a full decode only as fallback.

## Windows app (phase 2)

**Spike first (½ day, decides one file).** Which API lists and reads JPEGs from a Canon and a Nikon on USB inside this
**unpackaged** WPF app (`net8.0-windows10.0.19041.0`), and whether it exposes the camera's current date-time:
1. `Windows.Media.Import.PhotoImportManager` — built in; use from an unpackaged desktop app is unconfirmed;
2. `Windows.Devices.Portable.StorageDevice`;
3. the `MediaDevices` NuGet (a WPD wrapper targeting net8.0-windows; MIT for 1.x — read the 2.x licence file).

Take the first that works on both brands.

**Create — `desktop/ClinicManagement.DesktopShell/Camera/`**

| File | Purpose |
|---|---|
| `CameraSettings.cs` (+ store) | `%AppData%\ClinicManagement\camera.json`: `{ AutoImport }`, **off by default** |
| `DeviceArrivalWatcher.cs` | `WM_DEVICECHANGE` through `HwndSource`: a volume holding a `DCIM` folder, or a WPD device (`GUID_DEVINTERFACE_WPD`) |
| `ICameraSource.cs`, `CardCameraSource.cs`, `WpdCameraSource.cs` | list JPEGs (and count RAW/video), copy one item to a path, read the camera clock when the API allows; read-only on the device, always |
| `CameraJournal.cs` | `%ProgramData%\ClinicManagement\photos\journal.json`; key = source fingerprint + object id + size + date; states `Staged · Delivered · Filed(fileId) · AlreadyImported · Skipped · Failed`; pruned after 180 days |
| `PhotoStaging.cs` | `photos\_import\{batchId}\` — a WPD object has no path and a file handle needs one; cleared on `Filed`, swept after 7 days |
| `OriginalsStore.cs` | `photos\{fileId[0..2]}\{fileId}.jpg`; SHA-256 while copying; `index.csv` rewritten atomically with a unique temp name; refuses below 10 Go free (the cloud copy still files) |
| `OriginalsPathPlanner.cs` | pure and unit-tested — `MirrorPathPlanner`'s shape |
| `CameraBridge.cs` | `camera-*` messages; posts `{kind:"camera-batch", batchId, source, clockSkewSeconds?, items}` with `CreateWebFileSystemFileHandle(path, ReadOnly)`; a **queue**, parked as `__clinicShellPendingCameraBatches` (the coffre's single slot would drop batches) |
| `AddOnlyFolderCopy.cs` | extracted from `VaultCopyService`: copy by size, never delete, free-space check, **and a same-volume refusal** |
| `OriginalsCopyService.cs` | `photos\` (minus `_import`) → `{archive folder}\photos-originales\`, once per session and after each batch; the result goes to the page, which posts `backup/photo-originals-copy` |

**Modify**

| File | Change |
|---|---|
| `MainWindow.xaml.cs` | start the watcher after a clinic-page `NavigationCompleted` when `AutoImport` is on; route `camera-*`; check origins with `IsClinicPage` so the PC de secours works |
| `VaultBridge.cs` (`Script`) | `cameraStatus()`, `setCameraAutoImport(on)`, `cameraAck(itemId, outcome, fileId?)`, `openOriginal(fileId)`, `originalsStatus()`; a listener branch for `kind:'camera-batch'` with a queue |
| `ShellNotifications.cs` | generalise beyond chat (group + action arguments): « Appareil détecté — 37 nouvelles photos · Ranger » when the window is not in front |
| `VaultCopyService.cs` | uses `AddOnlyFolderCopy` — the coffre copy gains the same-disk refusal it was missing |
| `FileMirrorService.cs` | skip coffre-resident files and do not count their 400 as a failure (found: ten in a row abort the whole run) |
| `ClinicManagement.DesktopShell.csproj` | `<Version>1.8.0</Version>`; the spike's package, if any |
| `desktop/CLAUDE.md` | the `Camera/` folder; fix the stale test-project line (`:50`) |

**Bridge**

| Direction | Message |
|---|---|
| page → shell | `camera-config:<json>` · `camera-ack:<itemId>:<filed\|already\|skipped\|failed>:<fileId?>` · `camera-open-original:<fileId>` |
| shell → page | `camera-batch` (+ handles) · `camera-original-kept { fileId, sha256, sizeBytes, heldBy }` · `originals-copy { heldBy, files, bytes, completedAt }` |

- The page makes every API call: auth lives in the page, and the shell's only credential is refused outside
  `[AcceptsScopedToken]` (`ScopedTokenFilter`).
- First plug-in of a camera: the default range is the last 7 days; afterwards « since this camera's `LastImportedAt` »;
  « Voir les plus anciennes » always available.
- « Ouvrir l'original » opens the file in the Windows default viewer; it is offered only when `originalsStatus()` says
  this PC holds it.

## Blast radius

| Touched | Who else uses it | Verdict |
|---|---|---|
| Upload engine out of `upload-queue.tsx` | « Fichiers » uploads, coffre ingest, resumable resume | **must re-test**: small, > 8 MiB resumable, coffre (hosted) |
| `PatientFile` + `FileUploadSession` columns | DTO/mapping, archive/relay/purge (derived), dossier packager, merge (rows move) | one archive → restore round trip; the PC de secours hand-back carries them |
| Upload commands take metadata | every upload | optional; omitted = today's behaviour |
| `GetUploadPolicyQuery` + `cameraCopy` | `upload-policy.ts`, `use-upload-policy.ts` | additive |
| `preview.ts` orientation pin | every thumbnail | re-test a portrait phone photo and a HEIC |
| `encodeBitmap(quality?)` | HEIC/TIFF decoders | default unchanged; re-test one HEIC |
| `GenerateDefaultFolderId` shared | the initializer | no behaviour change |
| New area `Features/Cameras` | the realtime set | `cameras` in `clinic-hub.ts` |
| `ClinicRecoveryPointJob` new check | the coffre alert | a separate notification; coffre unchanged |
| `VaultBridge.Script` methods | coffre, staff chat, relay, session lock, `client.ts` | bridge.md + 1.8.0; re-test coffre delivery and the chat toast |
| `NavigationCompleted` handler | coffre deliver, archive copy, mirror, updates, `FollowRelayAfterSuccess` | the watcher starts last; re-test all five |
| `VaultCopyService` → `AddOnlyFolderCopy` | the coffre copy and `ReportVaultCopyCommand` | re-test a coffre copy and its report |
| `ShellNotifications` generalised | staff chat toasts | chat unchanged; `check:responsive` N52 stays green (camera toasts are native, not a page `.notify(`) |
| `FileMirrorService` skip | the mirror | re-test one mirror run |

## Pitfalls

- **Wrong patient** is the risk that matters: mandatory review, the order check, « Changer de patient » for every role,
  an audit row on every move.
- **A camera clock reset** by a battery change files every photo one patient early. On a full day, a clock off by
  exactly one slot can still look right to the detector — the USB clock read (phase 2) is the real fix; on the card
  path the stored offset and the thumbnails beside each name are the last line. The user guide says: Nikon —
  SnapBridge sets the clock over Bluetooth; Canon — check the clock monthly.
- **A late day without fiches** gives more « ? » rows, by design; the fiche save time is what turns them sure.
- **One camera, two doctors**: no doctor on the camera → every doctor's visits; candidates show the doctor.
- **A card full of old photos** on the first plug-in: the 7-day default, then « since the last import ».
- **Re-inserting a card**: the known-keys check before anything uploads; the unique index when two PCs import at once.
- **Orientation**: the copy carries no EXIF, so the rotation must be baked in — the copy and the preview both decode
  with `imageOrientation: "from-image"`.
- **EXIF is stripped from the cloud copy** (GPS included — a privacy win); that is why `CapturedAt` and `CameraModel`
  are columns. The copy becomes sRGB; the original keeps its colour profile.
- **Quota mid-batch**: pre-flight the batch (≈ 1.5 Mo × N) against the remaining space and say how much is missing;
  per-photo `storage_full` still shown. The usage figure is read once per page today — refresh it.
- **Found on the way — fixed in this branch:** `CompleteFileUploadCommand` does not re-check the quota (`:102`), so three
  parallel uploads can end a few files over.
- **Subscription expired (402)**: uploads refused; the shell keeps items `Staged` and offers them again.
- **Tab closed mid-batch**: `beforeunload` warns; staged originals survive in the journal → « 12 photos en attente de
  rangement » next time.
- **PC de secours**: an import during a cut lands on the relay PC; the hand-back must carry the new columns and treat
  `photo-already-imported` as done. Exercise once.
- **Disk on the clinic PC**: ≈ 60 Go a year at 30 photos a day. The 10 Go floor keeps Windows alive; the settings card
  shows the free space.
- **Windows accounts**: the store follows the coffre's ACL (`VaultFolder` `Prepare`); another account sees « Original
  réservé au compte … ».
- **Auto-update**: shell 1.8.0 reaches every clinic on merge; `AutoImport` off by default keeps the camera code dormant.
- **WebView2 runtime**: `CreateWebFileSystemFileHandle` is in the pinned SDK (1.0.2903.40); check the runtime on the
  pilot PC.
- **RAW-only cameras** import nothing: « 37 RAW ignorées — réglez l'appareil en RAW + JPEG ».
- **iPhone HEIC** from a gallery: skipped in v1 with a count; the regular « Fichiers » upload still takes them.
- **The mirror** also copies the cloud copies to the archive disk — about +20 % there, accepted.

## Tests

**API unit** (`ClinicManagement.UnitTests`, xUnit + Moq)
- `GetPhotoImportContextQuery`: drops Cancelled, NoShow and patient-less slots; applies `LateArrivalMinutes`; returns
  fiche times with and without `AppointmentId`; doctor filter; 31-day cap; wall-clock across the Tunisian midnight;
  another clinic reads nothing.
- `GetImportedCaptureKeysQuery`: this clinic only; > 2 000 refused.
- Uploads: metadata persisted; `capturedAt` clamped; a known key → `photo-already-imported` with the existing ids and
  **no bytes stored**; the resumable start stores the metadata and completion copies it to the file; completion
  re-checks the quota.
- `EnsureDefaultPatientFolderCommand`: finds by name (case-insensitive), creates with the predictable id, creates
  nothing else, idempotent.
- `ClinicCamera`: upsert by fingerprint, 409 on a stale version, forget is admin/doctor, a doctor of another clinic
  refused, audit row.
- `MovePatientFileToPatientCommand`: every refusal code; annotations follow; audit row; allowed for a secretary.
- `RecordFileOriginalCommand`: camera files only; the same values twice is a no-op; another clinic → not found.
- `ReportPhotoOriginalsCopyCommand` + the job: 7 days without a complete copy → one notification; a complete copy →
  none; counted per PC.
- Upload policy: `cameraCopy` set on hosted, null on self-hosted.
- Guards that stay green and will name a miss: `TenantScopeFilterTests`, `ConcurrencyConflictTests` (+ a row for
  `UpdateClinicCameraCommand`), `ClinicalRecordAuditCoverageTests`, `ClinicArchiveScopeTests`, `ClinicRelayScopeTests`,
  `ClinicPurgePlanTests`, `ControllerAuthorizationCoverageTests`, `SubscriptionExemptionCoverageTests`,
  `PatientFileResidencyCoverageTests`, `RealtimeResourceResolverTests`, `FileTypeCatalogTests` (JPEG never coffre).

**Database (wire)**
- the unique index refuses a second key in one clinic and accepts the same key in another;
- `verify-schema` before and after the migration, diffed;
- one archive → restore round trip brings `ClinicCameras` and the new columns back.

**Matcher** — `e2e/specs/photo-match.spec.ts`, a Playwright test with **no page** that imports
`web/lib/photo-import/match.ts` (`web/` has no test runner). Every case asserts **never sure and wrong**:
on-time day · late day with fiches (sure) · late day without fiches (asks) · camera + 1 h 02 (offset proposed, then
sure) · camera + 4 min (nothing proposed) · a straddling burst (ask + split point) · two bursts on one visit · a walk-in
fiche only · no appointment that day · two doctors with the filter · a three-day card · `OffsetTimeOriginal` set · no
date · an order violation demoted · an exact USB skew overriding detection.

**Browser** — `e2e/specs/photo-import.spec.ts`; rows `PHOTO-01…10` added to `features/e2e-hot-paths/scenarios.md` (a new
§ 15) and marked browser-only in § Layer; `node e2e/scripts/check-coverage.mjs` green.

| Row | Scenario |
|---|---|
| PHOTO-01 | twelve fixture JPEGs (EXIF times written by the fixture builder) → 3 sure groups + 1 ask → pick → « Ranger » → each patient's « Photographie » lists them with « Prise le … » |
| PHOTO-02 | the same files again → « 12 déjà importées », nothing sent |
| PHOTO-03 | hosted: each stored copy ≤ 2.5 Mo with a 2 560 px long edge; a portrait fixture stays upright (copy and preview) |
| PHOTO-04 | a camera 62 min off → the notice and corrected groups; « Annuler » restores |
| PHOTO-05 | a new camera → the owner prompt; the next import uses that doctor without asking |
| PHOTO-06 | « Changer de patient » moves a filed photo |
| PHOTO-07 | 390 px with a coarse pointer (CDP emulation — a resized desktop browser cannot see `coarse:`): the whole flow; « Ranger » visible at 1536 × 730 |
| PHOTO-08 | leaving the page mid-batch keeps uploading; the storage line updates |
| PHOTO-09 | a fake `__clinicShell` (`addInitScript`) posts `camera-batch` with `{ getFile }` handles → the screen opens loaded; `camera-ack` per photo; `camera-original-kept` → « Original : PC TEST » |
| PHOTO-10 | regression: « Fichiers » small upload, > 8 MiB resumable, coffre ingest — through the moved engine |

**Windows app** (`ClinicManagement.DesktopShell.Tests`): `OriginalsPathPlanner`; `CameraJournal` (dedupe across a
restart, prune); `AddOnlyFolderCopy` (same-volume refusal, never deletes, free space); `PhotoStaging` sweep; the
fingerprint; `camera-*` message parsing; `FileMirrorService` skips coffre files.

**Owed — needs hardware, cannot be automated:** one Canon EOS and one Nikon on the pilot PC, by USB and by card reader;
a real 40-photo day replayed through the matcher to tune `MATCH_RULES`; the copy time per photo on the slowest clinic PC
(target < 1 s, else move `cloud-copy` into a worker); the WebView2 runtime version there. Record the models tested in
`notes.md`.

**Gate:** full `BaseOutputPath=<temp> dotnet test api/ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj -c Release`
(never `--filter`); `npm run check:responsive` + `npx tsc --noEmit` + `npm run build`; `node e2e/scripts/check-coverage.mjs`;
the desktop tests; an eye pass at 320 / 390 / 820 / 1180 / 1440 and 1536 × 730; then `/test-in-browser` →
`/fix-qa-findings` (three cycles at most).

## Phase 3 — storage you can see

| Piece | Change |
|---|---|
| Per-clinic quota | `Clinic.StorageQuotaBytes long?` (null = the deployment default); `ClinicStoragePolicy` answers per clinic; `ClinicStorageAllowanceTests` extended (override, null, bad value) |
| Vendor console | clinic list: a sortable « Stockage » column (used / quota / %); clinic detail: the figure + « Modifier le quota » with a required reason, journaled like every console action |
| Clinic alerts | a bell notification to the clinic's admins when an upload crosses 80 % and 95 % (once per crossing, re-armed below 75 %) — computed where the allowance already is, no job |
| Server disk | Σ hosted bytes × 2 against a configured `Deployment:ServerDiskBytes`; a console warning at 70 % — today nothing compares the total with the disk |

## Working setup

- A git worktree from `origin/main` (`feature/camera-photo-import`). ⚠️ Unlink any `node_modules` junction before
  removing the worktree (`[IO.Directory]::Delete($j, $false)`) or it deletes the real packages.
- Test on the isolated stack (`:5099` / `:3099`, own Chrome, the main key ring, `Console__Port=0`, a throwaway MinIO
  bucket); never restart a peer's tier (`.claude/rules/shared-stack.md`).
- `appsettings.Development.json` pins `HostedMultiTenant`, so the copy rule is on by default; the self-hosted half runs
  with `Deployment__Profile=SelfHostedLan`.
- Clean up Docker images, build cache and the worktree when done (global rules).

## Rollout

1. Phase 0, by the owner: PR #84, the server disk, the pilot named.
2. Phase 1 PR → the screen exists for every clinic and does nothing until someone opens it. Pilot with the dentist who
   asked.
3. Phase 2 PR (shell 1.8.0) → reaches every Windows app on merge; switched on only on the pilot's PC.
4. Phase 3 PR → before a second photo-heavy clinic.
5. `features/camera-photo-import/notes.md` after each phase: what shipped, and the traps met.

## Effects on the two unbuilt blueprints

- `features/sensor-auto-import/blueprint.md`: `FileOrigin.Sensor` becomes **2**. `CapturedAt`,
  `MovePatientFileToPatientCommand`, the upload engine, the queued shell delivery and the generalised toast are built
  here first. Its `capture-duplicate` check relies on `ContentHash`, which ordinary uploads never fill — it needs a key
  like `CaptureKey`. Its « `InProgress`, started < 30 min » has no start time to read (status follows the clock); use
  the app's own `chairClaim` (`web/lib/dashboard/day-summary.ts:257`).
- `features/clinical-photo-series/blueprint.md`: `CapturedAt` and « Photographie » on demand exist after phase 1, and a
  filed group is the natural seed of a series. Its `Features/Files/PhotoSeries` folder would broadcast nothing — the
  namespace must end in `.Commands`.
