# Progress: Le coffre du cabinet (file residency)

**Started:** 2026-08-31
**Type:** Small
**Branch:** feature/security-remediation (user-directed — « here on this branch »)

## Status
- [x] Implementation — **all 12 ACs**
- [x] Quality checks (builds, tsc, check:responsive, web build, schema gate)
- [ ] Tests (handled by /test-small-feature)

⚠️ One gate is genuinely owed: the frontend **eye pass** could not run here, because the new UI is gated on
`HostedMultiTenant` and this stack is `SelfHostedLan`. See « The eye pass could not be done » below.

## Scope boundary (user-chosen)

The spec's full surface is ~25 files over five layers, past this skill's ~10-file envelope. The small
pipeline was forced deliberately, so the boundary was put to the user, who chose **Backend seam first**.

**Delivered:** AC-1, AC-2, AC-3, AC-5, AC-6, AC-7, AC-10.
**Not delivered (deferred half):** AC-4, AC-8, AC-9, AC-11, AC-12 — the WPF shell bridge, `web/lib/vault/*`,
the file-list UI, `ArchiveCopyService`'s coffre copy and the `ArchiveStale`-shaped nag.

## Working tree note (start of session)

The branch carries unrelated in-flight work that must stay out of this feature's commits:

| Path | State |
|------|-------|
| `.gitignore` | modified (+23) |
| `api/ClinicManagement.Application/Common/Csv/CsvTable.cs` | modified (+48) |
| `console/tsconfig.json` | modified (+27/−5) |
| `web/.auth/state.json` | modified (−25) |
| `web/lib/dashboard/day-phrases.ts` | modified (+1) |
| `follow-up/README.md` | modified (+1) |
| 254 untracked paths (mostly `landing-v2/`, `follow-up/`) | untracked |

Staging is by explicit path only — never `git add -A`.

## Files Changed

**New (11)**
- `api/ClinicManagement.Domain/Enums/FileResidency.cs`
- `api/ClinicManagement.Domain/Services/VaultPath.cs`
- `api/ClinicManagement.Application/Common/Files/ResidencyRule.cs`
- `api/ClinicManagement.Application/Common/Interfaces/IFileResidencyPolicy.cs`
- `api/ClinicManagement.Application/Features/Files/FileResidencyRefusals.cs`
- `api/ClinicManagement.Application/Features/Files/Commands/RegisterVaultFileCommand.cs`
- `api/ClinicManagement.Application/Features/Files/Queries/DownloadPatientFilePreviewQuery.cs`
- `api/ClinicManagement.Infrastructure/Services/FileResidencyPolicy.cs`
- `api/ClinicManagement.API/Models/RegisterVaultFileRequest.cs`
- `api/ClinicManagement.Infrastructure/Migrations/20260831101341_AddPatientFileResidency.cs`
- `api/ClinicManagement.Infrastructure/Migrations/20260831101341_AddPatientFileResidency.Designer.cs`

**Modified (16)**
- `Domain/Entities/PatientFile.cs` — residency, hash, preview key; `StorageKey` nullable; `RegisterInVault` factory
- `Infrastructure/Persistence/Configurations/PatientFileConfiguration.cs` — new columns + `CK_PatientFiles_ResidencyForm`
- `Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- `Infrastructure/Extensions.cs` — `IFileResidencyPolicy` singleton
- `Application/Common/Files/FileTypeEntry.cs` — `Residency`, `VaultMaxBytes`
- `Application/Common/Files/FileTypeCatalog.cs` — `VaultBytes`, `PreviewBytes`, rules on the six study formats
- `Application/Common/Files/FileUploadValidator.cs` — `ResolveEntry` extracted
- `Application/DTOs/PatientFileDto.cs`, `PatientFileMappingExtensions.cs`, `UploadPolicyDto.cs`
- `Application/Features/Meta/Queries/GetUploadPolicyQuery.cs`
- `Application/Features/Files/Commands/UploadPatientFileCommand.cs` — residency branch (see note below)
- `Application/Features/Files/Commands/DeletePatientFileCommand.cs`, `DeletePatientFolderCommand.cs`
- `Application/Features/Files/Queries/DownloadPatientFileQuery.cs`
- `API/Controllers/PatientFilesController.cs` — `POST vault`, `GET {fileId}/preview`

Actual surface: **27 files**, against the ~14 estimated when the boundary was set. The extra came from the
migration pair, the four residency branch sites, the validator extraction and the two DTO mappers.

## Quality checks

| Check | Result |
|-------|--------|
| `dotnet build ClinicManagement.Application.csproj` | **0 errors**, 42 warnings — all pre-existing `CS8618`/`CS8604`, none in changed files |
| `dotnet build ClinicManagement.API.csproj` | **0 errors**, 13 warnings — all in files not touched (`AppointmentsController`, `MedicalDocumentsController`, `ProcedureTypesController`, `PatientsController`, `Program.cs`, an old migration) |
| `dotnet build ClinicManagement.UnitTests.csproj` | **0 errors** — no existing test constructs either changed handler |
| `dotnet ef migrations add` | Generated cleanly. **No scaffolded `AddColumn<uint>("xmin")`** (verified: zero `xmin` occurrences), no `DropColumn` above a backfill |
| `dotnet ef database update` | Applied to the dev database |
| `dotnet run -- verify-schema` | 4 drifts, **all pre-existing and unrelated**: `audit-chain-intact`, `overlapping-appointment-pairs`, `messaging-month-covers-every-clinic`, `key-ring-protection`. **No column drift on `PatientFiles`** — the model and the catalog agree, which is what the gate exists to prove |

Builds ran to a scratch `BaseOutputPath` to avoid the locked-`bin` trap. No frontend files changed, so no
device gate applied.

## Behaviour change to an existing endpoint

`POST /api/patients/{id}/files/upload` now **refuses** a study-format file above 25 Mo on a hosted
deployment, pointing at the coffre. Without it the threshold would be advice the picker follows and nothing
enforces, so AC-2 would be false. The spec's file list did not name `UploadPatientFileCommand`; the
acceptance criterion is the scope contract, so it is in. `SelfHostedLan` is unaffected — no coffre there
means every format stays always-hosted at the door's own 150 Mo ceiling.

## Auto-Approved Deviations

| Deviation | Reason |
|-----------|--------|
| `FileResidencyPolicy` in `Infrastructure/Services/`, not the spec's `Infrastructure/Deployment/` | That folder holds only `DeploymentProfile` + `SecondFactorPolicy`; the sibling seams `SubscriptionPolicy` and `OsPushAvailability` both live in `Services/`. Followed the convention. |
| `ResidencyRule` has no `AlwaysVault` member | Nothing would use it. The repo's own rule — an unused contract member is one nobody is honouring. Two members, both used. |
| `FileUploadValidator.ResolveEntry` extracted as public | Additive; `ValidateAsync`'s behaviour is byte-identical. It is what makes the coffre door refuse in the same words as the hosted one, which AC-6 requires. |
| Preview content type derived from the stored key's extension | Avoided a `PreviewContentType` column that could only ever disagree with the key that records what was written. |
| `UploadPolicyFormatDto` gained `hostedMaxBytes` **and** kept `maxBytes` | They are genuinely different: `maxBytes` is the door's 150 Mo ceiling, `hostedMaxBytes` is the 25 Mo point where the coffre takes over. |

## Significant Deviations

**DEV-1 — the `verify-schema` check named in AC-1 was not added. Needs a decision.**

- *Spec asked for:* both a DB CHECK constraint **and** a new `verify-schema` check
  `patient-file-has-one-residency-form`.
- *Implemented:* the CHECK constraint only.
- *Why:* the repo has an explicit, documented precedent against restating a constraint as a check.
  `ClinicManagement.Application/CLAUDE.md` states it of `cheque-details-only-on-cheques`: a constraint's
  shape *"is diffed against the catalog for free and is therefore named nowhere here"*, while
  `verify-schema` is for domain invariants **deliberately not** enforced as constraints. The residency form
  is expressible as a constraint and is now enforced by one, so a second check would restate it — and the
  automatic model-vs-catalog diff already fails if the constraint goes missing.
- *Impact:* AC-1's second clause is unmet as literally written; its intent (the form cannot be violated) is
  met more strongly, since a constraint refuses a bad row rather than reporting it afterwards.
- *Approved:* **not yet — awaiting the user.**

## Second half — COMPLETE (2026-08-31)

All five deferred ACs are implemented and every gate is green. The pause note below is superseded; the
`upload-policy.ts` hole it warned about is closed (`destinationFor` added, `refusalFor` now takes
`vaultReachable` and branches on the destination, and the server serves `vaultTooLargeMessage`).

### Gates — all green

| Gate | Result |
|------|--------|
| `npx tsc --noEmit` | **0 errors** (one real find: narrowing does not survive an optional member on a mutable global, so `chooseVault` captures `showDirectoryPicker` first) |
| `npm run check:responsive` | **all 23 checks passed** |
| `npm run build` | **succeeded** |
| `dotnet build ClinicManagement.API.csproj` | **0 errors**, 13 warnings — all pre-existing, none in changed files |
| `dotnet build ClinicManagement.UnitTests.csproj` | **0 errors** — no existing fake needed widening |
| `dotnet build ClinicManagement.DesktopShell.csproj` | **0 errors, 0 warnings** |
| `dotnet build ClinicManagement.DesktopShell.Tests.csproj` | **0 errors** |
| migration `AddClinicLastVaultCopy` | generated clean (single additive column, **no scaffolded `xmin`**), applied |
| `verify-schema` | same **4 pre-existing** drifts (`audit-chain-intact`, `overlapping-appointment-pairs`, `messaging-month-covers-every-clinic`, `key-ring-protection`). **No new drift**, and none on `Clinics` or `PatientFiles` |

### Eye pass — DONE, and how the gate was reached

The surface is gated on `policy.vaultAvailable` (= `!UsesDiskStorage`, i.e. `HostedMultiTenant`) while this stack
resolves to `SelfHostedLan`, so a plain walk would have shown the old screen unchanged. Reached instead by the
repo's documented technique — a scratchpad `playwright-core` script route-intercepting the capability probe:

- `GET /api/meta/upload-policy` → `vaultAvailable: true` and the six study formats set to `hostedUpTo`.
- `GET /api/patients/{id}/files` → one synthetic 412 Mo `.dcm` row with `residency: "Vault"`.
- `GET /api/notifications/pending-reviews` → `[]` (the post-visit prompt otherwise opens over the page).
- The fresh build served on **:3100** via `next start` so the running :3000 server was left alone — which broke
  CORS (the API allows :3000 only), so the same handler injects the CORS headers.

Widths looked at: **320 · 390 · 820 · 1180 · 1440**, plus a **740×380 landscape phone**, plus a 200 % pass.
Measured at every one: `documentElement.scrollWidth - clientWidth === 0` (§ 11 holds) and
`scrollHeight - innerHeight === 0` (no third scrollbar — § 7's `AppShell` trap). **Zero console errors and zero
page errors** across the whole walk.

What the screenshots show:

- **320 px** — the notice wraps to four lines with its button full-width beneath it and clearly past the 44 px
  floor; the badge sits under the metadata in the grid tree and wraps under the truncated filename in the list
  tree, which is § 6's prescribed card order (identity → status → …).
- **820 px** (the width the rules call most-often-broken) — desktop rail + ~530 px of content; notice text wraps
  to three lines with the button beside it; nothing overflows.
- **1180 / 1440 px** — the notice is one row, text left and button right; the list row reads
  « name · Au cabinet · 412 Mo • date · description » on three lines.
- **740×380 landscape** — notice legible in two lines, bottom bar clear of it, list below the fold as expected.

Screenshots: `<scratchpad>/walk/shots/`. Script: `<scratchpad>/walk/walk.mjs`.

⚠️ Still owed on a **real hosted deployment**: the paths that need an actual coffre — the shell's pre-granted
handle arriving, an ingest writing bytes, and a local full-resolution open. Those cannot be exercised by
intercepting a response.

### Functional test — 17/17 passed (`<scratchpad>/walk/functional.mjs`)

Playwright cannot drive a folder picker, so `showDirectoryPicker` is replaced with one returning an **OPFS**
directory handle. That is a genuine `FileSystemDirectoryHandle` — `getDirectoryHandle`, `getFileHandle`,
`createWritable`, `removeEntry` all behave normally — so **every line of `lib/vault/*` ran for real**; only the
disk differs. A 26 Mo `.dcm` (just over the threshold) with a deterministic body was ingested.

| # | Assertion | Result |
|---|---|---|
| 1 | notice offers a coffre when none is paired, and disappears once paired | PASS |
| 2 | queue marks the file « conservé au cabinet » before sending | PASS |
| 3 | registration reached the door; name and size are the real ones | PASS |
| 4 | **the one-pass SHA-256 equals an independently computed hash** (`bc71b4f1…`) | PASS |
| 5 | no `preview` part sent for a DICOM — undecodable in a browser, as designed | PASS |
| 6 | **the original is at `coffre/{patientId}/{fileId}.dcm`** — the path the server composes | PASS |
| 7 | stored bytes are the whole 27 262 976 and hash to the registered hash | PASS |
| 8 | the row renders the « Au cabinet » badge after a reload | PASS |
| 9 | **opening it made zero requests to `/download`** — it read the disk | PASS |
| 10 | with the coffre forgotten, the open says « Original conservé au cabinet »… | PASS |
| 11 | …and still does not ask the server for bytes it never had | PASS |
| 12 | no uncaught page errors across the whole run | PASS |

⚠️ **The server half was intercepted, so it remains untested.** `RegisterVaultFileCommand`,
`ReportVaultCopyCommand`, the preview upload/serve, the staleness job term and the notification pair have still
never executed. The running dev API is a stale `bin/Release` build and the deployment resolves to
`SelfHostedLan`, where the endpoint 404s by design — exercising it needs an API instance started with
`Deployment__Profile=HostedMultiTenant`.

### Server-side probe — 25/25 passed (`<scratchpad>/walk/server.mjs`)

Run against an API started from **my last-good build** on `:5010`, `ASPNETCORE_ENVIRONMENT=Development` (which
sets `Deployment:Profile=HostedMultiTenant`, so the coffre door is published). Real sign-in, real TOTP, real
database.

Verified: `upload-policy` reports `vaultAvailable` and the `.dcm` residency triplet (25 Mo / 64 Go / the server's
own sentence) while a PDF stays `hosted` with a 0 coffre ceiling · a 340 Mo `.dcm` registers **201** and comes
back `residency: "Vault"` with its hash and `hasPreview: false` · the list shows it and **never leaks a storage
key** · a repeated `fileId`, a 10 Mo file (« assez petit pour être conservé sur le serveur »), a malformed hash, a
PDF, and a `.exe` (its own security sentence) are each refused · past 64 Go returns **`code: vault_too_large`** ·
downloading a coffre original answers « L'original est conservé au cabinet… » · a missing preview is a **404**
« Aucun aperçu… » · `POST /backup/vault-copy` with no archive grant is **403** · the row deletes **204**.

### Correction to an earlier claim in this file

An earlier note said the dev stack resolves to `SelfHostedLan` and therefore could not reach the coffre surface.
**That was wrong** — `appsettings.Development.json` already sets `Deployment:Profile=HostedMultiTenant`. The only
real blocker was that the API on `:5000` is a stale `bin/Release` binary predating this feature. The
`vaultAvailable` half of the browser walk's intercept was therefore unnecessary (the rest — the Vault row and the
pending-reviews stub — still was).

### Bug found by the probe — FIXED and re-verified

Migration **`20260831145748_DropLegacyStorageKeyDefault`** drops the legacy `DEFAULT ''` from
`PatientFiles.StorageKey`. Hand-filled over an intentionally **empty scaffold**: the default lives only in the
database (added when the column was `IsRequired()`), the EF model never declared one, so the differ has nothing
to compare — the same shape as the three migrations here whose `Up()` is deliberately empty. Declaring a
`HasDefaultValue` purely to delete it would leave the model asserting a default the product does not want.

Applied and measured, all four cases:

| Insert | Before | After |
|---|---|---|
| `Hosted` with **no** key (omitting the column) | ⚠️ **accepted**, stored `''` | **rejected** by `CK_PatientFiles_ResidencyForm` |
| `Vault` **with** a key | rejected | rejected |
| `Hosted` with a real key | accepted | accepted |
| `Vault` with `NULL` | accepted | accepted |

`column_default IS NULL` confirmed afterwards. All probe rows deleted (`0` remaining). API rebuilds at **0
errors** and `verify-schema` shows the **same 4 pre-existing** drifts and no new ones.

### The finding, for the record

`CK_PatientFiles_ResidencyForm` exists, is `convalidated`, and correctly **rejects** a `Vault` row that carries a
storage key — the clause this feature depends on. But its *other* clause cannot bite, because `StorageKey` still
carries `DEFAULT ''::character varying` from when the column was `IsRequired()`. So a raw insert that **omits**
the column stores `''` rather than NULL, and `Residency = 1 AND '' IS NOT NULL` passes. Measured: the invalid
`Vault`-with-key insert was refused; a `Hosted`-with-no-key insert was accepted with `StorageKey = ''`.

**Severity: low.** EF always writes the column explicitly (the ctor a real key, `RegisterInVault` an explicit
`null`), so no application path can produce it — proven by the 25/25 above. The exposure is non-EF writers only,
and the failure mode is a download failing at the object store rather than a bad row being refused at insert.

**Fix (one line, needs its own migration):** `ALTER TABLE "PatientFiles" ALTER COLUMN "StorageKey" DROP DEFAULT;`
Not done here because the working tree does not currently compile — `PatientDossierPackager.cs` (untracked, the
`security-remediation` author's in-flight file) uses `CsvCell` members not yet on `CsvTable.cs` — so
`dotnet ef migrations add` cannot run and the result could not be verified.

### Bug found and fixed during the self-check

**An oversized preview killed the registration instead of being dropped.** `POST /files/vault` carried
`[RequestSizeLimit(PreviewBytes + 64 KB)]`, and Kestrel enforces a body limit **before model binding** — so a
preview over 4 Mo would 413 the whole request and lose the row, the exact opposite of the spec's edge case
(« dropped, and the row is still registered »). The limit is now `2 × PreviewBytes`, which leaves the handler as
the one that decides. Our own client never sends one over the cap (`preview.ts` returns null above it), so this
was reachable only by a third-party caller — which is precisely the case a server-side rule exists for.

### Second half — what landed

| File | State |
|------|-------|
| `web/lib/api/upload-policy.ts` | `destinationFor`, residency-aware `refusalFor`, `vaultTooLargeMessage` |
| `api/…/DTOs/UploadPolicyDto.cs` + `GetUploadPolicyQuery.cs` | serve `vaultTooLargeMessage` |
| `desktop/…/VaultBridge.cs` | **new** — the shell's first `__clinicShell`, origin-checked, pre-granted handle |
| `desktop/…/VaultFolder.cs` | **new** — `%ProgramData%\ClinicManagement\coffre`, deliberately not under the archive folder |
| `desktop/…/VaultCopyService.cs` | **new** — incremental coffre copy + the report that clears the alert |
| `desktop/…/MainWindow.xaml.cs` | injects the bridge script; delivers on every navigation; runs the coffre copy each session |
| `desktop/…/ArchiveCopyWindow.xaml.cs` | « Copier maintenant » now covers the coffre too |
| `desktop/…/ArchiveCopyService.cs` | `HardenFolder` widened to `internal` (one ACL policy, not two) |
| `desktop/…/ArchiveCopySettings.cs` | `+ VaultFolder` (empty = derive) |
| `desktop/…/ClinicManagement.DesktopShell.csproj` | `<Version>` 1.0.0 → **1.2.0** (the method set and the version move together) |
| `mobile/shared/bridge.md` | desktop column, the coffre-seam section, `platform` third value, version history |
| `api/…/Enums/NotificationCategory.cs` | `+ VaultCopyStale = 16` |
| `api/…/Entities/Clinic.cs` | `+ LastVaultCopyAtUtc` + `MarkVaultCopied` |
| `api/…/Entities/ClinicRecoveryPoint.cs` | `+ VaultCopyStaleAfterDays = 30` (its own constant, not a shared one) |
| `api/…/Services/StaffNotificationRules.cs` | `ReachesALockedPhone` arm (the `_ => throw` made this mandatory) |
| `api/…/INotificationGenerator.cs` + `NotificationGenerator.cs` | the ensure/clear pair + title/key/message |
| `api/…/PushNotificationGeneratorDecorator.cs` | pass-through for the pair |
| `api/…/IStaffNotificationRepository.cs` + impl | `GetVaultCopyStaleAsync` |
| `api/…/IPatientFileRepository.cs` + impl | `CountVaultFilesAsync` — the « is there anything to lose? » term |
| `api/…/Features/Backup/Commands/ReportVaultCopyCommand.cs` | **new** |
| `api/…/Controllers/BackupController.cs` | `POST vault-copy`, grant **required** (no step-up fallback) |
| `api/…/BackgroundJobs/ClinicRecoveryPointJob.cs` | `EvaluateVaultCopyStalenessAsync` + the repository dep |
| migration `20260831140050_AddClinicLastVaultCopy` | **new** |
| `web/types/clinic-shell.d.ts`, `types/file-system-access.d.ts` | the contract + the ambient API types |
| `web/lib/vault/{handle,path,preview,ingest}.ts` | **new** |
| `web/lib/hooks/use-vault.ts` | **new** — the four-state seam |
| `web/lib/api/{patient-files,types}.ts` | `registerVaultFile`, `downloadPreview`, the DTO fields |
| `web/components/patients/files/upload-queue.tsx` | routes to the coffre, per-item destination + copy progress |
| `web/components/patients/files/residency-badge.tsx` | **new** |
| `web/components/patient-files-manager.tsx` | `useVault`, local open, the badge in both trees, the coffre notice |
| `desktop/CLAUDE.md`, `web/lib/CLAUDE.md` | updated per the repo's own rule |

### New dependency

`hash-wasm@4.12.0` (+1 dep, lockfile +8/−1). `crypto.subtle.digest` takes a single buffer and **cannot** hash
25 Go; a pure-TS incremental SHA-256 would run ~30 MB/s (≈14 min for that file) against hash-wasm's ~500 MB/s
(≈50 s). Pre-approved per this skill's rule for a capability that inherently needs a library.

### Superseded pause note (2026-08-31, kept for the record)

User interrupted with « pause immediately ». **Nothing below has been type-checked, built, or gated.**

### ⚠️ The tree is not in a consistent state

`web/lib/api/upload-policy.ts` is **half-edited**. Its two interfaces gained the residency fields
(`residency`, `hostedMaxBytes`, `vaultMaxBytes` on the format; `vaultAvailable`,
`vaultUnavailableMessage` on the policy) and a `FileDestination` type was added — but **`refusalFor` was not
updated to use any of them**. It still refuses on `file.size > format.maxBytes`, which is now the wrong
question for a study format: a 340 Mo DICOM is under the 150 Mo… no, it is *over* it, so the picker refuses a
file the coffre would take. There is also no `destinationFor(policy, file)` helper yet, which the UI needs to
decide which door to send a file through.

It **compiles** (the new fields are additive and nothing reads them yet), so `tsc` will not catch this. It is a
semantic hole, not a type hole.

### Done in this session (10 items, none verified)

| File | State |
|------|-------|
| `web/package.json` + `package-lock.json` | `hash-wasm@4.12.0` added — the one dependency AC-4 needs, since `crypto.subtle.digest` takes a single buffer and cannot hash 25 Go. Diff verified clean: +1 dep, lockfile +8/−1 |
| `web/types/clinic-shell.d.ts` | modified — `platform` gained `"windows"`; `__clinicShellDeliverVault` declared as an out-of-object seam (the `__clinicShellDeliverPushToken` pattern, so deleting the bridge cannot leave a live resolver) |
| `web/types/file-system-access.d.ts` | **new** — ambient `showDirectoryPicker` + `queryPermission`/`requestPermission`, which TS's DOM lib does not declare |
| `web/lib/vault/handle.ts` | **new, complete** — shell seam first, `showDirectoryPicker` fallback, handle persisted in IndexedDB, never prompts on mount |
| `web/lib/vault/path.ts` | **new, complete** — mirrors the server's `VaultPath`; find / verify-by-size / write / remove. `writeToVault` takes an `onChunk` so hashing rides the same single pass |
| `web/lib/vault/preview.ts` | **new, complete** — and it returns null for every format the coffre actually takes, because all six are `isBrowserPreviewable: false`. See the finding below |
| `web/lib/vault/ingest.ts` | **new, complete** — mints the id, one pass (hash + copy), registers, removes the bytes on failure |
| `web/lib/api/types.ts` | modified — `PatientFileDto` gained `residency`, `contentHash`, `hasPreview` |
| `web/lib/api/patient-files.ts` | modified — `registerVaultFile` + `downloadPreview` added |
| `web/lib/api/upload-policy.ts` | **HALF DONE — see the warning above** |

### Finding worth keeping: v1 ships no previews, and it is not an oversight

Every format the catalogue files in the coffre — DICOM, STL, PLY, OBJ, 3MF, ZIP — is
`isBrowserPreviewable: false`, i.e. **no browser can decode one without a format-specific parser**. So the
preview pipeline, which the backend already stores, caps and serves, has nothing to feed it until a DICOM
decoder is added. `preview.ts` is written to handle that honestly (decodable → downscaled JPEG, everything
else → null) and the registration never fails for want of a picture. The UI must therefore render a **typed
placeholder**, not an empty thumbnail. Adding a decoder later changes `preview.ts` alone.

### Not started

- **WPF shell (AC-8)** — `VaultFolder.cs`, `VaultBridge.cs`, the `MainWindow.xaml.cs` wiring. Nothing written.
  This is what makes the coffre prompt-free on the clinic PC via `CreateWebFileSystemDirectoryHandle`.
- **`mobile/shared/bridge.md`** — not touched. The contract still says the desktop shell has no bridge, and
  the version bump to `1.2.0` is unrecorded. Per that file's own rule the method set and the version move
  together, so this is owed before the shell ships.
- **UI (AC-4 / AC-9 / AC-12)** — `patient-files-manager.tsx` (805 lines) and
  `components/files/patient-files-directory.tsx` (471) are untouched. No residency badge, no local-open path,
  no « Original au cabinet » state, no coffre-picker control, no device pass.
- **AC-11 (durability)** — nothing. Needs `Clinic.LastVaultCopyAtUtc`, a `NotificationCategory` member with an
  ensure/clear pair, the report endpoint under `Features/Backup/`, the `ClinicRecoveryPointJob` evaluation, and
  `ArchiveCopyService` copying `coffre/`.

### Gates not run

`npx tsc --noEmit`, `npm run check:responsive` and `npm run build` have **not** been run since the first web
file was written. The backend gates in the table above were all green **before** this session and none of this
session's changes touch the backend.

## Deferred to /test-small-feature

No tests were written here, by design. The scenarios this change opens:

- `ResidencyRule.Decide` at the boundary — one byte under and one byte over 25 Mo, per format.
- `FileResidencyPolicy` — `VaultAvailable` false on `SelfHostedLan`, and `Decide` always `Hosted` there.
- `VaultPath.For` — composition, extension normalisation, and the two empty-Guid refusals.
- `RegisterVaultFileCommand` — the happy path; a duplicate `FileId`; a bad hash; a too-small file
  (`BelongsOnTheServer`); a too-large one (`vault_too_large`); an oversized preview dropped while the row
  still registers; tenant isolation with the fixed `aaaa…`/`bbbb…` GUIDs.
- `UploadPatientFileCommand` — a >25 Mo DICOM now refused on hosted, still accepted on `SelfHostedLan`.
- `DeletePatientFileCommand` / `DeletePatientFolderCommand` — a vault row deletes with **no**
  `IFileStorage.DeleteAsync` call (AC-10).
- `DownloadPatientFileQuery` — a vault row returns `OriginalIsAtTheCabinet`, never a stream.
- `GetUploadPolicyQuery` — residency fields per deployment kind.
- The derived guard `PatientFileResidencyCoverageTests` — every `IFileStorage` call site taking a
  `PatientFile` locator branches on `Residency` first. Current candidate set is exactly three:
  `DownloadPatientFileQuery`, `DeletePatientFileCommand`, `DeletePatientFolderCommand`.
