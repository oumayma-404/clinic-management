# Feature Specification: Local-Disk File Storage (Windows Desktop — Phase 2)

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-08
**Scope:** BE
**Feature:** In Local (offline) mode, store patient files, medical-document PDFs, and clinic logos on the server PC's local disk instead of MinIO; Cloud mode keeps using MinIO unchanged.

## Overview
Today `IFileStorage` — the storage seam every file feature uses (patient files, clinic logos, medical-document PDFs) — has only a MinIO implementation; when MinIO isn't configured the registration throws on first use. This adds a real local-disk implementation of `IFileStorage` selected automatically in Local mode (`Auth:Mode = Local`), so an offline Windows/LAN install works with no MinIO. It also closes the atomicity gap where an upload can leave an orphaned blob if the following DB save fails. Realizes spec **FR-C** of `features/windows-desktop-app/`.

## What Changes
- Add `LocalDiskFileStorage : IFileStorage` (Infrastructure) storing blobs under a configurable base folder (`FileStorage:BasePath`), returning an opaque relative storage key; honors the `customPath` overload (used for clinic logos) and creates the folder if missing.
- Storage backend is chosen by `Auth:Mode`: **Local** → `LocalDiskFileStorage`; **Cloud** → `MinioFileStorage` (unchanged). The current "MinIO not configured → throw" fallback is replaced by this mode branch.
- Close the FR-C3 atomicity gap in the `IFileStorage`-consuming write paths (patient-file upload/delete, medical-document PDF save, clinic-logo upload): if the DB save fails after a successful upload, the just-uploaded blob is deleted so no orphan remains; a store failure aborts before any DB record is written. Applies in both modes (shared handler code).
- Remove the orphaned `IFileStorageService` / `LocalFileStorageService` (path-based, implemented the wrong contract, consumed by no handler) — superseded by `LocalDiskFileStorage`.

## Acceptance Criteria
- **AC-1:** With `Auth:Mode = Local`, uploading a patient file, saving a medical-document PDF, and uploading a clinic logo all persist to the configured local folder and are downloadable, with no MinIO configured (FR-C1).
- **AC-2:** With `Auth:Mode = Cloud`, storage still uses MinIO with no behavior change (FR-C2).
- **AC-3:** If the DB record save fails after a successful upload, the operation returns failure **and** leaves no stored blob behind; on success the stored key matches the persisted record (FR-C3).
- **AC-4:** Downloading or deleting a key that doesn't exist on disk reports a clean failure (no unhandled crash), matching how the MinIO path surfaces errors.
- **AC-5:** The `customPath` overload writes to a deterministic key (clinic-logo path) and overwrites in place, mirroring MinIO semantics.

## Data / Schema Changes
- None. `PatientFile.StorageKey`, `Clinic.LogoUrl`, and the medical-document file key already store an opaque storage key; the local implementation reuses these unchanged.

## Out of Scope
- Migrating existing MinIO-stored blobs to local disk (fresh Local installs start empty; Cloud stays on MinIO).
- Backup / restore of the storage folder (Phase 5, FR-G) and connectivity/offline UX (Phase 3).
- Any change to the `IFileStorage` interface shape or to Cloud/MinIO behavior beyond leaving it intact.

## Edge Cases (Critical only)
- Base folder missing at startup → created on first use (as the MinIO impl auto-creates its bucket).
- Path-traversal safety: storage keys are opaque/sanitized so a key can never resolve outside the base folder.
- Concurrent uploads must not collide → keys are unique (guid-based) as MinIO's are.
