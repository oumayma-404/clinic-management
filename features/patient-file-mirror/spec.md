# Feature Specification: Miroir navigable des fichiers patients

**Status:** APPROVED
**Type:** Small
**Created:** 2026-08-28
**Scope:** Full
**Feature:** The desktop shell keeps the cabinet's patient files as loose, browsable, per-patient folders on the admin's laptop — current within the day, openable in the Explorer without unzipping anything.

## Overview

`clinic-archive-auto-copy` (phase 1) puts the whole record on the laptop, but inside a `.zip` that is rebuilt
whole every N days. A doctor who uploads a panoramique this morning and wants it on their own disk this afternoon
has no answer: the next archive is days away, and when it arrives the image is buried in a several-gigabyte
archive nobody opens casually.

This is phase 2. The shell walks a new **clinic-wide file manifest**, compares it against a folder tree, and pulls
only what is missing — so the laptop grows a `fichiers/<patient>/` tree of real files that Explorer, the Windows
photo viewer and any imaging tool can open directly. It reuses phase 1's device grant, phase 1's destination
folder and phase 1's `.part`-then-rename discipline; nothing new is issued and nothing new is asked of the admin
beyond one checkbox.

The server stays authoritative. The mirror is **write-only** — never read back, never uploaded from, never
reconciled — so this is a safety copy that is also convenient, not a sync engine.

## What Changes

- A new admin-only endpoint lists **every `PatientFile` of the caller's clinic** — id, patient, name, type, size,
  upload time — paged, so the shell can diff without opening each patient in turn.
- The shell gains a « Copier aussi les fichiers des patients » setting beside the archive cadence.
- When enabled, the shell mirrors missing files into `<dossier>/fichiers/<Nom du patient>/<date> - <nom>`.
- The mirror runs at launch and every 30 minutes while the app is open, so a file uploaded during a consultation
  reaches the laptop the same day rather than at the next archive.
- The settings window reports progress and the outcome on the status line it already has.

## Acceptance Criteria

- **AC-1:** The manifest returns the caller's clinic's files and nothing else. A test asserts a cross-clinic
  refusal **directly** — by resolving a second clinic's file id — rather than trusting the ambient tenant filter,
  the shape `clinic-archive-auto-copy`'s AC-4 established.
- **AC-2:** The manifest orders on `UploadedAt` then `Id` **ascending**. A file uploaded while a walk is in
  progress therefore lands *after* the last page read and never shifts a page already consumed — the
  `OFFSET`-over-a-shifting-set trap, in its inserting form.
- **AC-3:** A manifest entry's local path is a **pure function of the manifest**, so the same manifest yields the
  same tree on any machine. Two entries that would occupy one path (same patient name, same day, same file name)
  are **both** suffixed with their file id's first 8 characters; one alone is never silently overwritten or
  skipped.
- **AC-4:** A file already on disk at its path with the manifest's byte size is not fetched again. A first mirror
  of a large cabinet is expensive; the second is a manifest walk and nothing else.
- **AC-5:** Each file is written to `.part` and renamed only on a complete stream. An interrupted mirror leaves no
  truncated file at a real path, and the next run finishes the job.
- **AC-6:** The mirror **never deletes**. A file removed on the server stays on the laptop, and the window says so
  — this is the doctor's copy, and « le serveur a supprimé votre radio » is not a behaviour a backup may have.
- **AC-7:** The exchanged token lives 30 minutes and a first mirror can run for hours. The shell re-exchanges the
  grant on a `401` and resumes at the file it was on, rather than failing the run.
- **AC-8:** Free space is checked before each file against that file's own size. Exhausting the disk stops the run
  with a French sentence naming the figure, keeps everything already written, and is retried at the next tick.
- **AC-9:** With the setting off — which is what an existing `archive-copy.json` reads as — nothing about the
  shell changes. The feature is absent, not idle.
- **AC-10:** Patient and file names are sanitised for Windows (`\ / : * ? " < > |`, trailing dots and spaces,
  reserved device names) and the full path is kept under the 260-character limit by truncating the *file* part,
  never the patient folder. A patient whose name cannot produce a folder is still mirrored, under their id.

## API Contract

### `GET /api/backup/file-manifest` — admin only
Query: `?page=&pageSize=` (`PageRequest.From`, unpaged when both absent, `MaxPageSize` 200)
Response 200: `PagedResult<ClinicFileManifestEntryDto>` where an entry is
`{ fileId: guid, patientId: guid, patientName: string, fileName: string, contentType: string, fileSize: long, uploadedAt: iso }`
Errors: `403` (non-admin) — unchanged shapes.

⚠️ It returns **no `storageKey` and no `clinicId`**, exactly as `PatientFileDto` deliberately does not. The
manifest says which files exist; fetching one goes through the existing per-patient download, which re-checks the
patient's clinic itself.

### `GET /api/patients/{patientId}/files/{fileId}/download` — **unchanged, reused**
Already `AnyClinicRole`, and the grant exchange mints an ordinary clinic-admin JWT, so the shell reaches it with
no new endpoint and no second credential.

## Data / Schema Changes

**None. No entity, no column, no migration.** `PatientFile` already carries `ClinicId`, `FileSize` and
`UploadedAt`; the manifest is a read over rows that exist.

- New repository method `IPatientFileRepository.GetClinicManifestPageAsync(PageRequest?, CancellationToken)` —
  joins `Patient` for the display name, orders `UploadedAt` then `Id` ascending (AC-2).
- New query `Features/Backup/Queries/GetClinicFileManifestQuery` — **in `Features/Backup`, not a new area**, so no
  new realtime resource key is emitted and `RealtimeResourceResolverTests` stays green.
- New DTO `ClinicFileManifestEntryDto` + its mapper, beside `PatientFileMappingExtensions`.

## Device Behaviour

- **No web surface changes at all.** The manifest has no screen; its only caller is the shell.
- **Shell (WPF, desk only):** one checkbox in the existing `ArchiveCopyWindow`, above the cadence fields, with a
  one-line consequence — this folder will hold every radiograph the cabinet has, unencrypted. Progress and outcome
  land on the footer status line, which is already outside the `ScrollViewer` for the reason phase 1 found.

## Out of Scope

- **Dual-write on upload** (the shell also saving a file the moment it is uploaded, via a
  `window.__clinicShell` bridge). It needs a bridge the desktop shell does not have plus changes to a `web/` that
  browsers also serve, and it would cover only files uploaded *on that laptop* — not the ones uploaded from the
  phone, the tablet or the second PC. The 30-minute pull covers every device with one mechanism.
- **Deleting locally what the server deleted** (AC-6) and **restoring the server from the mirror** — the archive's
  « Restaurer une archive » remains the only way back in, unchanged.
- **The four non-patient blobs** — `Doctor.CachetStorageKey`, `Clinic.LogoUrl`,
  `DocumentEmail.AttachmentStorageKey`, `ClinicRecoveryPoint.StorageKey`. The first two are a signature and a logo
  the archive already carries, the third is a transient rendered PDF the job clears, and the fourth is a backup —
  mirroring it would copy the cabinet's own snapshots onto the laptop a second time.
- Encryption at rest — declined in phase 1 for the same reason and stated the same way.
- macOS/Linux: there is one desktop shell and it is Windows-only.

## Edge Cases (Critical only)

- **Two patients with the same name.** The folder is `<Nom> (<8 of patientId>)` whenever the manifest contains a
  second patient with the same sanitised name — never silently merged into one folder, which would make two
  people's radiographs indistinguishable.
- **A file whose blob is gone** (a pre-US-5 flat key, a storage failure). The download 404s; that one file is
  reported and skipped, and the run continues. One missing blob may not stop a mirror of forty thousand.
- **The manifest is large.** 50 000 files is 250 pages at `MaxPageSize`. The shell holds the manifest in memory
  (metadata only) because AC-3's collision rule is a property of the *whole* manifest, not of one page.
- **The mirror and the archive collide.** Both write under the same chosen folder; the mirror uses `fichiers/`
  and the archive `archive-*.zip`, and the archive's `Prune` matches on its own prefix, so neither can delete the
  other's work.
