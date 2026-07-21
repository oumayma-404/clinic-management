# Feature Specification: Patient-File Tenant Isolation & Delete Integrity

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-21
**Scope:** BE
**Feature:** Close the cross-clinic access hole on patient files/folders and harden folder deletion.

## Overview
The patient file/folder read, list, download, and delete handlers never verify the caller's clinic. Because `PatientFile`/`PatientFolder` are deliberately excluded from the EF global clinic filter, a user in clinic A who knows a clinic-B file/patient GUID can list metadata, download the actual bytes, and delete clinic B's medical files. This adds the explicit per-handler clinic check every other feature already does, and fixes a folder-delete path that can orphan rows or lose blob-delete errors.

## What Changes
- `GetPatientFilesQuery`, `GetPatientFoldersQuery`, `DownloadPatientFileQuery` resolve the caller's clinic and verify the target patient/file/folder belongs to it before returning anything.
- `DeletePatientFileCommand`, `DeletePatientFolderCommand` perform the same clinic check before deleting.
- `DeletePatientFolderCommand` no longer swallows blob-delete errors to `Debug.WriteLine` and no longer leaves a file row pointing at a deleted folder when a blob delete fails (real `ILogger`; DB deletes and folder removal stay consistent).

## Acceptance Criteria
- **AC-1:** A read/list/download/delete request for a file or folder whose patient belongs to a different clinic returns the standard not-found failure (mapped to 404) and never returns metadata, streams bytes, or deletes data.
- **AC-2:** `GetPatientFilesQuery`/`GetPatientFoldersQuery` verify the patient exists and is in the caller's clinic before returning results (no results for an arbitrary/foreign `patientId`).
- **AC-3:** Deleting a folder never commits a state where a file row references a removed folder; a blob-storage failure is logged via `ILogger` and does not silently produce a partial delete.
- **AC-4:** Same-clinic read/list/download/delete behavior is unchanged for legitimate callers.

## Out of Scope
- Adding a `ClinicId` column to `PatientFile`/`PatientFolder` or changing the EF global query filter.
- The already-protected write paths (`UploadPatientFileCommand`, `CreatePatientFolderCommand`, `InitializeDefaultFoldersCommand`).

## Edge Cases (Critical only)
- Clinic resolution follows the existing per-handler pattern (`IClinicContext.GetUserId()` → `IUserRepository` → `User.ClinicId`); an unauthenticated/no-clinic caller gets the not-found failure, not a 500.
