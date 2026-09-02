# Feature Specification: Le coffre du cabinet (file residency)

**Status:** APPROVED
**Type:** Small
**Created:** 2026-08-31
**Scope:** Full
**Feature:** A stored patient file gains a residency; imaging above 25 Mo lives only in the cabinet's own folder, never in the hosted object store.

## Overview

`HostedMultiTenant` is now the primary mode, and every uploaded byte becomes one live copy, ~14 nightly
tarballs on the same VPS disk, and one unbounded off-site copy per night. A CBCT study is 50 Mo–1 Go and a raw
scanner export can be tens of gigabytes; at Tunisia's ~9,2 Mbps median uplink a 25 Go file is six hours of
saturated clinic internet, so no storage price makes hosting it workable.

A `PatientFile` therefore gains a **residency**. `Hosted` is today's path, unchanged. `Vault` means the bytes
live only in the cabinet's own `coffre` folder while the hosted side keeps the row, a SHA-256 and a small
derived preview. The catalogue that already decides *what* may be uploaded now also decides *where it lives*,
and `GET /api/meta/upload-policy` tells the browser before the user picks — the existing "the browser is told
rather than trusted" rule from `patient-file-uploads`.

The feature is `HostedMultiTenant`-only: on `SelfHostedLan` the bytes are already on the clinic's disk
(`UsesDiskStorage`), so there it is **absent, not refusing** — the `SellsVendorMessaging` shape.

## What Changes

- `PatientFile` carries `Residency`, `ContentHash` and `PreviewStorageKey`; `StorageKey` becomes nullable.
- `FileTypeCatalog` gains a residency rule per format. `.dcm .dicom .stl .ply .obj .3mf .zip` are hosted up to
  `DocumentBytes` (25 Mo) and vault above it; every other format is always hosted.
- A new registration door writes metadata and a preview but never the original bytes.
- The desktop shell exposes `window.__clinicShell` for the first time, carrying a pre-granted directory handle
  for the coffre.
- A vault file opens at full resolution on any device holding the coffre, and shows its preview elsewhere.
- The shell copies the coffre alongside the archive, and a vault with no second copy for 30 days is nagged.

## Acceptance Criteria

- **AC-1:** Every `PatientFile` has a residency. Existing rows are `Hosted`. A `Hosted` row has a non-null
  `StorageKey`; a `Vault` row has a null one — held by a DB check constraint **and** by `verify-schema`'s new
  `patient-file-has-one-residency-form`.
- **AC-2:** Residency is decided only by `FileTypeCatalog`: at or below 25 Mo a vault-eligible format goes
  `Hosted` through today's endpoint; above it, `Vault`. `MaxBytes` and `MaxBytesAcrossCatalog` are unchanged,
  so `[RequestSizeLimit]` on `PatientFilesController` is untouched.
- **AC-3:** `GET /api/meta/upload-policy` carries `residency` and `vaultMaxBytes` per format, and the picker
  states which of the chosen files will stay at the cabinet **before** the user confirms.
- **AC-4:** With a coffre present, adding a 340 Mo DICOM writes **zero original bytes** to the server: the
  browser hashes and copies locally in one pass over the file and posts metadata plus preview only.
- **AC-5:** `POST /api/patients/{patientId}/files/vault` refuses `vault_too_large` above `VaultMaxBytes`. When
  it fails for any reason the client removes the vault file it just wrote, leaving no orphan.
- **AC-6:** With no coffre, a vault-class file is refused with `vault_unavailable` and the server's own French
  sentence (served through `upload-policy`, so client and server cannot word it differently). There is **no**
  hosted fallback — residency is a property of the file, never of the device.
- **AC-7:** On `SelfHostedLan` the feature is absent: `POST /files/vault` 404s before the mediator and
  `upload-policy` reports every format as always hosted.
- **AC-8:** The shell reports `version` `1.2.0` and `platform: "windows"`, and posts a pre-granted ReadWrite
  handle for `{dossier}\coffre` through `window.__clinicShellDeliverVault` only after checking the message
  target's origin. A WebView2 runtime lacking the API degrades to vault-unavailable and never crashes the shell.
- **AC-9:** Opening a vault file on a device holding the coffre opens the original at full resolution with no
  network transfer, gated on `file.size === row.fileSize`. Elsewhere it renders the preview and names where the
  original is.
  ⚠️ **CORRECTED 2026-09-02 — the second clause is false in v1 and was false the day it shipped.** All seven
  coffre formats are `isBrowserPreviewable: false`, so `web/lib/vault/preview.ts`'s `buildPreview` returns `null`
  for every one of them: **no coffre file has ever had a preview**, and on a device without the coffre the row
  shows a typed placeholder, its badge, and the path to the machine that holds it. The preview pipeline is built
  and correct and has nothing to feed it until a DICOM/STL decoder is added. Recorded rather than quietly fixed
  because `preview.ts` owns this decision in its own comment while this AC still promised the opposite.
- **AC-10:** Deleting a vault file removes the row and **leaves the bytes on the cabinet's disk**. The app never
  destroys originals on hardware it does not host.
  ⚠️ **AMENDED 2026-09-02:** it leaves the *original*. A coffre row still owns one **hosted** blob — its preview —
  and that one goes with the row. Both delete commands read `PatientFileBlobs`, which is the single answer to
  « what does this row own? »; they previously deleted the original and silently orphaned the preview.
- **AC-11:** The shell copies the coffre alongside the archive and reports it; a clinic whose vault has had no
  second copy for 30 days receives the `ArchiveStale`-shaped ensure/clear notification, cleared by the next copy.
- **AC-12:** At 320 px the patient's file list renders as cards with no horizontal scroll; the residency badge
  and the « Original au cabinet » state are legible, and add / open / delete are all reachable from one menu with
  44 px targets on a coarse pointer. Floor: `~/.claude/skills/DEVICE-CONTRACT.md`.

## API Contract

### POST /api/patients/{patientId}/files/vault
Request (multipart): `{ fileId: guid, fileName: string, fileSize: long, sha256: string, description?: string, folderId?: guid, preview?: file }`
Response 201: `PatientFileDto` (with `residency`, `contentHash`, `hasPreview`)
Errors: `400 vault_too_large — …` · `404` on `SelfHostedLan` (capability absent)

### GET /api/patients/{patientId}/files/{fileId}/preview
Response 200: image bytes, `nosniff`, inline · `404` when the row carries no preview

### GET /api/meta/upload-policy (modified)
Response 200: each format gains `residency: "hosted" | "hostedUpTo" | "vault"`, `hostedMaxBytes`, `vaultMaxBytes`; body gains the `vaultUnavailableMessage`.

## Data / Schema Changes

- `PatientFile.Residency` — `int`, required, `Hosted = 1` for every existing row.
- `PatientFile.ContentHash` — `varchar(64)`, nullable (required in practice for `Vault`).
- `PatientFile.PreviewStorageKey` — `varchar(500)`, nullable.
- `PatientFile.StorageKey` — **becomes nullable**. Deliberate: a null locator makes an unbranched call site
  throw rather than silently no-op a `coffre/...` delete against MinIO.
- Check constraint `(Residency = 1 AND StorageKey IS NOT NULL) OR (Residency = 2 AND StorageKey IS NULL)`.
- `Clinic.LastVaultCopyAtUtc` — `timestamp`, nullable.
- The vault path is **derived, never stored**: `VaultPath.For(patientId, fileId, extension)`.

## Device Behaviour

- **Leading device:** desk (the clinic PC running the shell — the only place a coffre can live).
- **Narrow width (< 640):** the file list is cards, not a table; residency is a badge on the card, and a vault
  file with no local coffre shows its preview plus one line naming the machine that holds the original. The
  add-file sheet opens in `dvh` and states the residency verdict per chosen file before confirming.
- **Touch:** every per-file action lives in one overflow menu with 44 px rows; nothing is hover-revealed. On a
  phone the vault-class add path is refused with the `vault_unavailable` sentence, never a dead control.

## Out of Scope

- `deploy/backup/backup.sh:71`'s full nightly tar of the object store, and the missing remote retention.
  **Urgent and independent of this feature** — capture as its own item.
- `DownloadAsync` buffering whole objects into a `MemoryStream` in both `MinioFileStorage` and
  `LocalDiskFileStorage`. Also urgent, also independent.
- A per-clinic byte quota / counter on the `ClinicMessagingMonth` pattern. The vault removes the cliff, not the
  slope; this earns its place later.
- Any vendor-console storage figure — `PlatformReadShape` is a closed field set, adding a name there *is* the
  review, and it changes the disclosure paragraph in `deploy/README.md`.
- 3D previews for `.stl .ply .obj .3mf` — v1 ships a typed placeholder for those.

## Edge Cases (Critical only)

- **Preview above 4 Mo:** dropped, and the row is still registered. A refused row would trade one storage
  problem for another; a missing preview is a placeholder, not a failure.
- **Vault file missing or size-mismatched on disk:** treated as *not available on this device*, never as
  deleted — the row stands and the preview shows. Never silently re-register or repair.
- **Two clinic PCs with different coffres:** a file is openable only where its bytes are. Pointing both at one
  LAN share (`\\SERVEUR\coffre`) resolves it with no code — it is just a path.
- **Hashing 25 Go:** `crypto.subtle.digest` takes a single buffer and cannot do this. An incremental SHA-256 is
  fed from the same stream that writes the copy, so the file is read exactly once.
- **`FileSystemFileHandle` has no rename** and `move()` is Chromium-only: write straight to the final name and
  `removeEntry()` on failure.
