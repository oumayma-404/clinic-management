# Feature Specification: Appointment → Google Calendar Sync on Create + Offline Gating

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-21
**Scope:** BE
**Feature:** Push newly created appointments to Google Calendar automatically, and skip sync attempts when the server is offline.

## Overview
`CreateAppointmentCommand` never triggers `IGoogleCalendarSyncService.SyncAppointmentToGoogleCalendarAsync` — only the update path and the manual controller do. So a new appointment stays "non synchronisé" forever unless someone edits it or clicks Push manually, contradicting the documented "syncs on create/update." Separately, the update-path sync fires unconditionally even in offline Local mode, wastefully attempting an OAuth refresh. This adds post-commit sync on create (mirroring update) and gates both on server internet reachability.

## What Changes
- `CreateAppointmentCommand` triggers App→Google sync post-commit for patient appointments, mirroring the update path — best-effort, never fails/rolls back the create; patient-less "busy slot" appointments are skipped (the sync service already skips them).
- The create- and update-path Google sync is gated on server internet reachability (`IInternetProbe`), so no OAuth refresh is attempted when the server is offline.

## Acceptance Criteria
- **AC-1:** Creating a patient appointment with Google Calendar configured and the server online pushes the event to Google and reports `IsSyncedToGoogle = true` (badge clears) without a manual push.
- **AC-2:** A Google failure during create sync never fails or rolls back appointment creation.
- **AC-3:** When the server has no internet, create/update sync is skipped (no OAuth attempt) and resumes automatically when connectivity returns; the manual "Push to Google" remains available.
- **AC-4:** Patient-less "busy slot" appointments are not synced.

## Out of Scope
- The disabled Google→App direction and its recurring job.
- Frontend sync-badge UI (already present).
