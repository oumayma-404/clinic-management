# Progress: Le coffre tient ses promesses (file-upload repair)

**Started:** 2026-09-02
**Type:** Repair pass over `clinic-file-vault` + `patient-file-uploads`
**Branch:** `feature/security-remediation` (the branch the vault work itself landed on)
**Origin:** a `/think-solution` audit of the whole upload surface in `HostedMultiTenant` mode. The owner chose
**« Repair — make it keep its promises »** over adding a `Pending` residency or retreating to hosted-only.

## Status

| Part | Covers | Status |
|---|---|---|
| P1 — a lapsed coffre grant is one click | critical #2 | done |
| P2 — the preview blob gets a lifecycle | critical #3, #4 | done |
| P3 — the empreinte becomes evidence | critical #5 | done |
| P4 — the copy report is compared with the record | critical #6 | done |
| P5 — residency follows the bytes, not just the format | major | done |
| P6 — the Android picker stops hiding study formats | major | done |
| P7 — the four pickers that disagreed with the server | minor ×6 | done |
| P8 — the test pass that never ran | major | done |

## What was wrong, and what each part changed

### P1 — « lapsed » was reported as « never paired », and the one-click recovery had no caller

`useVault` had four states and needed five. A browser drops a File System Access grant when the last tab for the
origin closes, so the cabinet's first visit each morning found the coffre folder **stored and un-granted** —
reported as `unpaired`, which offered the whole folder picker again. `reconnectVault()` and `useVault().reconnect`
existed for exactly this and had **zero callers**: `patient-files-manager.tsx:213` destructured `pair` only.

- `web/lib/vault/handle.ts` — `currentVault()` returns a `VaultLookup` (`ready` / `lapsed` / `none`) instead of
  collapsing the last two into `null`; `storedVaultExists()` added.
- `web/lib/hooks/use-vault.ts` — `VaultStatus` gains `"lapsed"`; `reconnect` falls through to `unpaired` when
  nothing is stored, so the button can never be a dead control.
- `web/components/patient-files-manager.tsx` — a third banner branch, « Reconnecter le coffre ».

### P2 — `PreviewStorageKey` had no lifecycle at all

The stand-in image is the **one hosted blob a coffre file owns**. Both delete commands branched on residency for
the original and never named it, and `ClinicArchiveScope.BlobProperties` mapped **one** blob column per table, so
a preview could not be archived or restored either. Latent only because P-note below means no preview is written
yet — the day a decoder lands, every deleted study orphans an object and every restore loses its picture.

- **new** `Application/Features/Files/PatientFileBlobs.cs` — the one answer to « what goes when this row goes? ».
  ⚠️ **Not de-duplicated**, deliberately: keys are minted per upload or derived from the row's own id, both
  backends' `DeleteAsync` is idempotent, and collapsing would let one row's blob stand in for another's.
- `DeletePatientFileCommand` / `DeletePatientFolderCommand` — both read `PatientFileBlobs`.
- `ClinicArchiveScope.BlobProperties` → `IReadOnlyDictionary<string, IReadOnlyList<string>>`;
  `ClinicArchiveStore.StorageKeysOf` iterates the list.
- `ClinicArchiveScopeTests` gains **`Every_StorageKey_Shaped_Property_On_An_Archived_Entity_Is_Declared`** — the
  guard that would have caught `PreviewStorageKey` on the day it was added.

### P3 — the SHA-256 was computed, sent, stored, and read by nothing

`findVerifiedInVault` compares **size only**, so a study replaced by a different file of the same length read as
genuine. Size stays the test for *opening* (free, needed on every click); the hash is now askable.

- `web/lib/vault/path.ts` — **`verifyVaultIntegrity`** (one streamed pass, `hash-wasm`), returning
  `intact` / `missing` / `size-mismatch` / `hash-mismatch` / `unknown-hash`. `extensionOf` renamed
  **`dottedExtensionOf`**, since `upload-policy.ts` exports a same-named dot-less one into the same feature.
- `patient-files-manager.tsx` — « Vérifier l'intégrité » on a `Vault` row when a coffre is present.
  ⚠️ Deliberately an action, not a check on open: a 25 Go file is about a minute.

### P4 — a copy report was believed rather than compared

The shell reported a file count and a byte total the server could not corroborate, and **any** report cleared the
30-day staleness alert. A copy covering three studies of four hundred cleared it exactly as a complete one did.

- **new** `Domain/Repositories/VaultContentTotals.cs` — what the coffre is *supposed* to hold.
  ⚠️ `IsCoveredBy` is « at least », not « exactly »: the coffre is the practice's own folder and may legitimately
  hold more than the app filed there.
- `IPatientFileRepository.GetVaultTotalsAsync` replaces `CountVaultFilesAsync` (one query answers both the
  coverage comparison and the alert's « is there anything to lose? » test; the old member had no caller left, and
  an unused contract member is one nobody is honouring).
- `ReportVaultCopyCommand` — a short-fall does **not** stamp `LastVaultCopyAtUtc` and does **not** clear the alert;
  it returns `Success(false)`.
- `BackupController` `POST vault-copy` — **200 `{ covered }`** instead of 204, so the shell can say so.
- `desktop/…/VaultCopyService.cs` — three outcomes (`Covered` / `Incomplete` / `NotReported`) with distinct French,
  because a network fault and a short copy are different things to do about. An older server's empty 204 body still
  reads as `Covered`.

### P5 — residency was keyed on the format, not on the bytes

A 40 Mo TIFF or PNG panoramique was flatly refused: capped at the document ceiling with no coffre route — the exact
problem the coffre exists for, on the formats a cabinet produces most.

- `FileTypeCatalog` — **`ImageBytes` (50 Mo)** for PNG / JPEG / WebP, and `tiff`/`tif` joins the coffre-eligible set.
- ⚠️ **The rule, stated once and asserted twice:** a format a browser can paint stays hosted whatever its size,
  because a coffre file opens only where its bytes are; one it cannot decode is what belongs at the cabinet.
  `FileTypeCatalogTests.No_Browser_Previewable_Format_Is_Sent_To_The_Coffre` and
  `GetUploadPolicyQueryTests.A_Previewable_Image_Stays_Hosted_Even_Where_A_Coffre_Exists` hold both halves.
- **`ProfileImageBytes` (5 Mo)** + `FileUploadProfile.CapFor(entry)` — a **per-door** cap. PNG and JPEG are shared
  between the patient drawer and the cachet/logo, so without it those are one number and raising it for the
  radiograph raises it for the letterhead. `MaxBytesAcrossCatalog` is unchanged, so no `[RequestSizeLimit]` moved.

### P6 — the Android shell hid the coffre formats from its own picker

`FileChooser.resolveMimeTypes` widened to `*/*` only when the resolved list was **empty**. The served `accept` mixes
resolvable (`.pdf`, `.png`) with unresolvable (`.dcm`, `.stl`, `.ply`, `.obj`, `.3mf`), so `mapNotNull` dropped the
latter, the list stayed non-empty, and `EXTRA_MIME_TYPES` filtered DICOM and 3D meshes **out of the picker**. The
code contradicted its own comment two lines above.

Now widens when **any** entry failed to resolve. ⚠️ Not a local extension→MIME table: that is a second copy of the
catalogue, on a device, drifting the first time a format is added server-side.

### P7 — the four pickers that disagreed with the server, and the logo that would not render

`GET /api/meta/upload-policy` served **one** door. The other four each carried a hand-written `accept`:

| Surface | Was | Server | Now |
|---|---|---|---|
| `doctor-document-identity-dialog` | `image/png,image/jpeg` + **2 Mo** | 25 Mo | served policy |
| `mon-profil-content` | `image/*`, **no size check** | PNG/JPEG | served policy |
| `clinic-settings`, `setup-wizard` | `image/*`, **no size check** | PNG/JPEG | served policy |
| `import-patients-dialog` | `.csv,text/csv` | csv **+ txt** | served policy |

- `GetUploadPolicyQuery` takes an optional `Profile`; `FileUploadProfile.ByName`/`TryByName` publish the four doors.
  ⚠️ An unknown door is **refused**, not defaulted — handing a logo picker the drawer's policy would offer DICOM as
  a clinic logo and quote a ceiling six times too high.
- `useUploadPolicy(profile)` caches **per door**; `acceptHint(policy)` replaces the hand-written « 2 Mo maximum. ».
- `import-patients-dialog` now clears `event.target.value` — a failed preview could not be retried with the same CSV.
- `[RequestSizeLimit]` + `[RequestFormLimits]` added to the two cachet, two clinic and two medical-document doors.
  ⚠️ Sized from the catalogue **`const`**, since an attribute argument must be a compile-time constant.
- **`Clinic.LogoContentType`** + migration `20260902194333_AddClinicLogoContentType`. `GetClinicLogoQuery` hardcoded
  `image/png` while the door accepts JPEG, and with global `nosniff` **a JPEG logo did not render anywhere in the
  product**. This is the follow-up `patient-file-uploads` dropped because the tree then carried an uncommitted model
  snapshot; the tree was clean, so it landed.

### P8 — the tests that were deferred and never written

`FileUploadValidator`, `FileTypeCatalog`, `GetUploadPolicyQuery`, `RegisterVaultFileCommand`, `ResidencyRule`,
`FileResidencyPolicy` and `VaultPath` had **zero** coverage between them, and `FileTypeCatalog.cs:28` named
`FileTypeCatalogTests` as its guard while no such file existed.

**New (5):** `Common/Files/FileTypeCatalogTests` · `Common/Files/FileUploadValidatorTests` ·
`Common/Files/FileResidencyTests` · `Common/PatientFileResidencyCoverageTests` ·
`Features/Meta/GetUploadPolicyQueryTests` · `Features/Files/RegisterVaultFileCommandTests`.

**Modified:** `UploadPatientFileAtomicityTests` — the committed « NOTE (security-remediation): … Its owner should
replace this » placeholder mock is gone, replaced by a real case (a study belonging in the coffre is refused at the
ordinary door before anything is written). `ClinicArchiveScopeTests` — the two-direction blob-property guard.

## Deviations

### DEV-1 — `OwnedByAll` does not de-duplicate, and one existing test is why

First written with `.Distinct()`. `FilesTenantIsolationTests.DeleteFolder_…` gives both of its fixture files the
same storage key (`files/scan.pdf`) and asserts **two** delete calls, so it went red. The fixture is unrealistic —
`ClinicStorageKey.Compose` mints a unique leaf per upload — but the resolution was to drop `Distinct` rather than
edit that assertion: per-row deletion is the honest semantic, both backends' delete is idempotent, and collapsing
would let one row's blob silently stand in for another's. Recorded because the reasoning is not visible in the diff.

### DEV-2 — `Clinic.LogoContentType` has no EF configuration, so it is `text`

`Clinic` has **no** `IEntityTypeConfiguration` at all, so `LogoUrl` beside it is already unbounded `text`. Adding a
configuration for one column would newly bring every other Clinic column under one, which is a larger change than
this repair. Consistent with its neighbour; `Doctor.CachetContentType` is bounded at 100 because that entity has a
configuration. Left as-is deliberately.

### DEV-3 — `PatientFileResidencyCoverageTests` scans from `api/`, not the repository root

`SolutionSources.Root()` returns the directory holding `ClinicManagement.sln`, which **is** `api/`. The first
version prefixed `"api"` and therefore scanned nothing — and passed. Caught by its own non-vacuity test, which is
the whole reason that test exists.

## Gates — all green

| Gate | Result |
|---|---|
| `dotnet build` API | **0 errors**, 57 warnings — all pre-existing, none in changed files |
| `dotnet build` DesktopShell | **0 errors, 0 warnings** |
| `./gradlew :app:compileDebugKotlin` | **exit 0** |
| `dotnet vstest` (whole suite) | **4030 passed, 0 failed** — including the three `Backup.ExchangeArchiveGrant` cases the previous progress note listed as red |
| `npm run check:responsive` | **all 27 checks passed** |
| `npx tsc --noEmit` | **0 errors** |
| `npm run build` | **succeeded** |
| `verify-schema` after the migration | the **same 4 pre-existing** drifts (`audit-chain-intact`, `overlapping-appointment-pairs`, `messaging-month-covers-every-clinic`, `key-ring-protection`). **No new drift, and none on `Clinics` or `PatientFiles`** — the model and the catalogue agree, which is what the gate exists to prove |
| migration `AddClinicLogoContentType` | scaffolded clean (one additive nullable column, **no `xmin`**, nothing dropped), applied |

## Eye pass — DONE, and it found a defect

Both new surfaces are gated on `policy.vaultAvailable` **and** on a `Vault` row existing, so the two capability
reads were route-intercepted — the technique `features/clinic-file-vault/progress.md` documents for this surface.
The vault *state* was not stubbed: an OPFS directory handle is a genuine `FileSystemDirectoryHandle`, so seeding
one into the app's own IndexedDB store and pinning what `queryPermission` answers makes `currentVault()` run for
real and reach `lapsed` or `ready`.

**Widths walked: 320 · 390 · 820 · 1180 · 1440, plus a 740×380 landscape phone**, in both states. At every one:
`scrollWidth − clientWidth === 0` and `scrollHeight − innerHeight === 0`. **Zero page errors** across the whole
walk. The only 404 is `GET /api/connectivity`, which 404s **by design** on `HostedMultiTenant` (it gates on
`ExposesTrustEndpoints`) and which the connectivity provider correctly reads as an absent signal, not as offline.

What the screenshots show:
- **320 px** — the notice wraps to four lines with its button full-width beneath it; « Au cabinet » on the card.
- **820 px** (the width the rules call most-often-broken) — three lines of notice with the button beside it.
- **1440 px** — one row, text left, button right.
- **740×380 landscape** — two lines, bottom bar clear of it.

### ⚠️ The defect the walk found — `preventDefault()` on the menu item trapped the page

The « Vérifier l'intégrité » item was written with `onSelect={(event) => { event.preventDefault(); … }}` so it
could show « Vérification… » in place. **Radix keeps `pointer-events: none` on the document while a menu is
open**, so for the whole minute a 25 Go study takes to hash, nothing else on the screen was clickable — and the
menu was still sitting over the result afterwards. Caught because Playwright reported
`<html …> intercepts pointer events` on the *next* click; no type, no lint and no unit test can see this.

Fixed: the menu closes normally and progress lives in a `toast.loading` sharing one id with its outcome, so the
five verdicts replace the spinner rather than stacking beside it (and the `catch` dismisses it before
`showErrorToast` mints its own). Re-measured after the fix.

### Touch measurements — taken with a coarse pointer, which is the only way they mean anything

⚠️ The first pass measured **32 px** rows and that was a **false reading**: the 44 px floor is `coarse:`-gated, a
default Chrome context is a *fine* pointer, and 32 px is the correct desktop density. Re-run with
`hasTouch: true` + `isMobile: true` (`matchMedia('(pointer: coarse)')` asserted `true` in-page):

| Surface | 390 px | 320 px |
|---|---|---|
| « Reconnecter le coffre » | **44 px** (332 wide) | **44 px** (262 wide, full-width) |
| Every menu row incl. « Vérifier l'intégrité » | **44 px** | **44 px** |

⚠️ In the `lapsed` state the menu has **four** items, not five — « Vérifier l'intégrité » is correctly absent,
since it is gated on a usable handle and a lapsed grant cannot read the disk.

### Functional confirmation — the action verifies, it does not merely render

A real 300 000-byte file was written into an OPFS coffre at the path `VaultPath.For` composes, its SHA-256
computed **independently in Node**, and the list served with that hash — so `verifyVaultIntegrity` ran for real
over `hash-wasm`:

| Case | Verdict |
|---|---|
| untouched bytes | « **Original intact** — L'empreinte du fichier correspond à celle enregistrée lors de son dépôt. » |
| **one byte flipped, same length** | « **L'original a changé depuis son dépôt** — La taille est la bonne mais l'empreinte ne l'est pas… », with the path and « Copier le chemin » |
| file removed | « **Original conservé au cabinet** » — an *info*, not an error, with the path |

The middle row is the whole point of P3: that file is byte-for-byte the case the old size-only check reported as
genuine.

Scripts: `<scratchpad>/walk/{walk,coarse,integrity}.mjs`. Screenshots: `<scratchpad>/walk/shots/`.

⚠️ **Two traps this walk re-learned**, both of which cost runs: Playwright's route **glob treats `?` as a
single-character wildcard** (so `files?*` never matches `files?page=1`), and a substring **regex** on `/files`
also swallows `/files/folders` — feeding the folder read a page of files, which throws and renders the retry
banner instead of the list. Exact-path predicates are the answer. And the saved session state is **spent by one
browser**: refresh it before *every* context, or the walk lands on `/login` and reads as « the surface did not
render ».

## Owed

- **`P4`'s server half on a real deployment.** `ReportVaultCopyCommand`'s comparison is unit-tested against mocks;
  the shell→server round trip with a real coffre has not run.
- **P7's four re-pointed pickers were not walked.** The cachet, the two logo pickers and the CSV import now read
  `?profile=`, and the API process running on this machine predates that parameter — a stale server ignores it and
  answers with the patient drawer's policy, so walking them here would have measured the wrong thing rather than
  the new one. They are unchanged in layout (same controls, same containers; only `accept` and one helper line
  now come from the server), and `GetUploadPolicyQueryTests` holds the contract. Worth a look after the API is
  restarted on a build that has the parameter.

## Deliberately NOT done — and why

- **A DICOM/STL decoder.** `preview.ts`'s `decodable()` matches raster MIME types only, and all seven coffre
  formats are undecodable, so `buildPreview` returns `null` **100% of the time**: `PreviewStorageKey`, `hasPreview`,
  `GET …/preview` and the 4 Mo cap are unreachable in production today. That is owned in `preview.ts`'s own comment
  and is a **feature**, not a repair. What this pass fixed is the *lifecycle* (P2) and the docs that claimed a
  preview renders elsewhere — `features/clinic-file-vault/spec.md` AC-9's second clause is corrected below.
- **A per-clinic storage quota.** The coffre removed the cliff, not the slope; nothing bounds a cabinet's bytes on
  the shared VPS disk. Named in the vault spec's own out-of-scope list; still open.
- **`MinioFileStorage.DownloadAsync` buffering whole objects into a `MemoryStream`.** Every hosted download, the
  archive packager and the dossier export do this per blob. Also in that out-of-scope list, also still open.
- **A `Pending` residency.** Safari, Firefox, iOS and Android still cannot file a study over 25 Mo — `vaultSupported()`
  is `typeof window.showDirectoryPicker === 'function'`. This was option 2 of the audit and the owner chose option 1.
