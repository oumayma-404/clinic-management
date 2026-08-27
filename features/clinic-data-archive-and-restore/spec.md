# Feature Specification: Clinic Data Archive & Restore

**Status:** APPROVED
**Type:** Small
**Created:** 2026-08-11
**Scope:** Full
**Feature:** A cabinet downloads a complete archive of its own data to its own PC and can put it back — whether it lost one week or everything.

## Overview

On `HostedMultiTenant` the clinic's data lives in a database it does not administer, and `pg_dump` cannot serve it: one
database holds every cabinet and the tool has no tenant predicate. This adds a **per-clinic archive** — the cabinet's own
records and files, tenant-filtered, as one file it keeps on its own PC — and a **restore** that puts missing records back.

The restore is **additive and keyed on the original ids**. Every primary key in this product is a GUID minted in the domain
constructor, so a row that still exists is matched and left alone, and a row that is gone is re-inserted *with its own id
and its own document number*. That is what makes total loss and partial loss the **same operation**: total loss is the case
where every row is a gap. It is also why money documents are safe — the gapless `AAAA-NNNN` sequences break only if new
numbers are minted, and nothing here mints one.

## What Changes

- A cabinet admin can **download** an archive (`.zip`: a JSON manifest, one file per entity type, and the blobs).
- A cabinet admin can **restore** an archive: missing records are re-inserted, records still present are left untouched and
  counted, and the result is reported per entity type.
- The vendor can restore an archive **from the platform console**, re-provisioning a cabinet that no longer exists — the only
  path that works when the accounts are gone too.
- The « Sauvegarde » card on `/settings` becomes these two actions instead of the current « rien à lancer ici » statement.

## Acceptance Criteria

- **AC-1:** The archive contains the cabinet's clinical and financial records + their blobs, and **nothing belonging to
  another cabinet** — asserted by a test that builds an archive with two cabinets seeded and finds no foreign id in it.
- **AC-2:** Restoring an archive into a cabinet that still has all of it changes nothing and reports every row as « déjà
  présent » (so a double restore is a no-op).
- **AC-3:** Restoring after rows were deleted re-inserts exactly the missing rows, **with their original ids and original
  invoice/devis/avoir numbers**, and the next number issued afterwards continues the sequence without a gap or a collision.
- **AC-4:** A record that exists but **differs** from the archive is **skipped, never overwritten**, and counted separately
  from « déjà présent » — so work done after the backup cannot be rolled back.
- **AC-5:** Restored files are readable: a `PatientFile` re-inserted from the archive downloads its original bytes, because
  the blob is written back at its **original storage key**.
- **AC-6:** An archive whose clinic id does not match the target cabinet is refused, in French, naming the mismatch —
  except on the console path, which **re-creates the cabinet with the archive's own clinic id**.
- **AC-7:** An archive whose manifest schema version this build does not understand is refused before anything is written,
  naming both versions.
- **AC-8:** The download works on an **expired** cabinet, and so does the restore — both carry
  `[AllowsWithoutSubscription]`: recovering records that already existed is not recording new work (AC-4.2's argument).
- **AC-9:** Every row the restore writes is attributed to a distinct audit actor, so the practice's « Journal d'activité »
  reads as a restore rather than as mass data entry.
- **AC-10:** At 320 px the download and restore actions are full-width stacked controls with 44 px targets on a coarse
  pointer, the restore confirmation is a bottom sheet, and the per-entity result renders as a list (never a table).

## API Contract

### GET /api/backup/archive
Response 200: `application/zip` (streamed; `Content-Disposition` names the cabinet + date)
Errors: `403` non-admin · `404` where the deployment has no per-clinic archive

### POST /api/backup/archive/restore
Request: `multipart/form-data` — `archive` (.zip, cap stated in config)
Response 200: `{ restored: { entity: count }, alreadyPresent: { entity: count }, conflicts: { entity: count }, warnings: string[] }`
Errors: `400 archive_invalid` · `400 archive_clinic_mismatch` · `400 archive_schema_unsupported` · `403` non-admin

### POST /api/platform/clinics/restore
Request: `multipart/form-data` — `archive`. Re-provisions the cabinet at the archive's own clinic id, then applies the same restore.
Response 200: same body as above. Errors: `400 archive_*` as above · `409 clinic_exists` when the cabinet is still live

## Data / Schema Changes

- **None.** No new table, no new column: the archive is a file and the restore writes existing rows.

## Device Behaviour

- **Leading device:** desk (the desktop app), but the card must work on a tablet at the chair.
- **Narrow width (< 640):** the two actions stack full-width; the restore confirmation and its per-entity result are a
  bottom sheet in `dvh`; the result is a card list, not a table.
- **Touch:** both actions are ≥ 44 px on a coarse pointer; the file input clears its own `value` before the upload runs so a
  failed restore can be retried with the same file. Inherits `~/.claude/skills/DEVICE-CONTRACT.md`.

## Out of Scope

- **Encryption of the archive.** It is a full copy of the cabinet's medical records: the screen must say so in French, and
  the operator guidance says to keep it somewhere safe. Encrypting it is a separate decision.
- **Unattended/scheduled downloads onto the PC.** The desktop shell has a `WebMessageReceived` channel but no
  `window.__clinicShell` bridge and no scheduler, so an automatic nightly copy is shell work, not this.
- **Repairing a corrupted record** (AC-4 skips it) and **restoring staff accounts** — password hashes are credentials and do
  not travel in a file on a laptop; the console path re-provisions the admin.
- **The cloud-side copy**, which stays `deploy/`'s off-server `backup` sidecar.

## Edge Cases (Critical only)

- **`ClinicSubscription` / `SubscriptionPeriod` are excluded from the archive.** They are the *vendor's* money (FR-2), and
  including them would let a cabinet restore its own entitlement from a file it controls.
- **Operational rows are excluded**: the reminder/push/email outboxes, `StaffNotification`, `DeviceRegistration`,
  `BackupRun`, `AuditEntry`. They are transient machine state, and re-inserting a due outbox row would re-send messages
  about visits that already happened.
- **Encrypted per-clinic reminder secrets are excluded** — the Data Protection key ring that decrypts them is not in the
  archive, so they would restore as undecryptable and each channel would silently read « non configuré ».
- **A pre-`multi-tenant-cloud`-US-5 flat blob key** (no `clinics/{id}/` prefix) is restored verbatim rather than re-prefixed,
  matching `DownloadAsync`'s own contract — otherwise historical files restore to keys nothing resolves.
