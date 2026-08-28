# Feature Specification: Copie automatique de l'archive sur le poste

**Status:** APPROVED
**Type:** Small
**Created:** 2026-08-28
**Scope:** Full
**Feature:** The desktop shell pulls the cabinet's own archive on a schedule into a folder the admin chooses, unattended, so a copy of the whole record lives on the practice's own hardware instead of only on the vendor's server.

## Overview

`ClinicRecoveryPointJob` already nags a practice when no archive has left the building
(`Clinic.LastArchiveDownloadedAtUtc` → `NotificationCategory.ArchiveStale`), and the only remedy today is an
admin clicking « Télécharger l'archive » into `~/Downloads` and remembering to do it again next month. This is
the remedy: the Windows shell fetches `GET /api/backup/archive` on a cadence and writes it to a chosen folder.

The archive is already the right payload — it carries the manifest, one JSON per table **and the blobs**, through
the same tenant filter as every CSV export, and it deliberately excludes credentials because *« credentials do not
travel in a file on a laptop »*. It was designed for this destination.

Unattended is only possible with a new credential: archive downloads require a step-up confirmation that lives
5 minutes and is spent on use. So this feature adds a **device grant** — admin-issued, named, revocable, scoped to
`download-clinic-archive` and nothing else.

## What Changes

- An admin issues a named **device grant** from « Paramètres », sees its secret **once**, and can revoke it.
- `GET /api/backup/archive` accepts a valid device grant **in place of** a step-up confirmation.
- The desktop shell stores a grant, a destination folder and a cadence, and pulls the archive when due.
- A grant-authorised pull stamps `LastArchiveDownloadedAtUtc`, so `ArchiveStale` clears on its own.
- Every grant-authorised pull is recorded in `ArchiveAccessLedger`, distinguishable from a human download.

## Acceptance Criteria

- **AC-1:** A grant authorises `download-clinic-archive` only. Presented to the **restore** endpoint it is refused
  exactly as an absent confirmation is — one credential cannot become the other.
- **AC-2:** The secret is shown once at issue and never again; the row stores a SHA-256 hash
  (`ClinicSignup`'s shape). A lost secret is replaced by revoking and re-issuing, never by reading it back.
- **AC-3:** Revoking a grant takes effect on the next request. A revoked or unknown grant is refused with the same
  French sentence as a missing confirmation — a caller learns nothing about which grants exist.
- **AC-4:** A grant is clinic-scoped. Presented against another cabinet's data it refuses, and a test asserts this
  directly rather than relying on the ambient tenant filter.
- **AC-5:** The shell writes to `<folder>/archive-<clinic>-<yyyy-MM-dd>.zip` via a `.part` file renamed only on a
  complete, verified stream. An interrupted pull never replaces the previous good copy.
- **AC-6:** The shell keeps the N most recent copies (default 4) and deletes older ones only **after** a new one
  has landed.
- **AC-7:** The destination folder is ACL-hardened on first use through the existing `DirectoryAclHardener` policy
  — inheritance broken, `Administrators` + the running account only, `Users`/`Everyone` removed.
- **AC-8:** The shell states in French, at setup, that the folder will hold the cabinet's whole medical record
  **unencrypted**, and says whether the destination drive is BitLocker-protected. It does not refuse either way.
- **AC-9:** A failed pull (offline, refused grant, disk full) never deletes an existing copy, is surfaced in the
  shell rather than silently swallowed, and is retried at the next due time.
- **AC-10:** With no grant configured the shell behaves exactly as it does today — no scheduler, no folder, no
  prompt. The feature is absent, not broken.

## API Contract

### `POST /api/backup/archive-grants` — admin only
Request: `{ "label": string }`
Response 201: `{ "id": guid, "label": string, "secret": string, "createdAt": iso }` — `secret` returned **once**
Errors: `403 forbidden` (non-admin)

### `GET /api/backup/archive-grants` — admin only
Response 200: `[{ "id": guid, "label": string, "createdAt": iso, "lastUsedAt": iso|null, "revokedAt": iso|null }]`

### `DELETE /api/backup/archive-grants/{id}` — admin only
Response 204

### `GET /api/backup/archive` — modified
Now accepts header `X-Archive-Grant: <secret>` as an **alternative** to `X-Step-Up-Confirmation`.
Errors: unchanged — a bad grant refuses with the existing sentence and code.

## Data / Schema Changes

- **New entity `ClinicArchiveGrant`** — `Id`, `ClinicId`, `Label`, `SecretHash` (SHA-256), `CreatedByUserId`,
  `CreatedAt`, `LastUsedAtUtc` (nullable), `RevokedAtUtc` (nullable). Clinic-owned, so it joins the EF tenant
  filter and `TenantScopeFilterTests`' derived set.
- **`ClinicArchiveScope.Excluded`** for that entity — a grant is a credential and must not travel inside an
  archive that a restore would then re-create.

## Device Behaviour

- **Leading device:** desk. The grants list is an admin surface in « Paramètres », beside the archive card.
- **Narrow width (< 640):** the grants list renders as cards (`CARDS_ONLY`/`TABLE_ONLY`), each row's revoke
  action in the row's own menu. The one-time secret is shown in a `mobile="bottom"` dialog with a copy control —
  never a value the reader must transcribe from a truncated cell.
- **Touch:** revoke and copy are ≥ 44 px on a coarse pointer, grown rather than overlaid (they sit in a row).
- Inherits `~/.claude/skills/DEVICE-CONTRACT.md`.

## Out of Scope

- **The incremental browsable file mirror** (per-patient folders, live) — a separate spec. This feature delivers
  the restorable ZIP only.
- Restoring *from* a local copy: the existing « Restaurer une archive » upload already covers it, unchanged.
- Any change to the vendor's own `deploy/` backup sidecar, to recovery points, or to `BacksUpItsOwnData`.
- Encryption of the copy at rest — deliberately declined; the drive's own encryption is the right layer, and AC-8
  states it rather than reimplementing it.
- macOS/Linux: there is one desktop shell and it is Windows-only.

## Edge Cases (Critical only)

- **Clock or cadence missed while the app was closed** — due-ness is « the newest copy is older than the cadence »,
  never a wall-clock alarm, so a laptop that was off for a week pulls once on next launch rather than not at all.
- **The archive outgrows the disk.** A cabinet with years of radiographs is gigabytes. The shell checks free space
  against the previous copy's size before starting, and refuses with a French sentence naming the figure — it does
  not begin a write it cannot finish.
- **Two shells sharing one grant.** Permitted, and each keeps its own folder; `LastUsedAtUtc` reflects the most
  recent of them. Nothing about a grant is per-machine except where the admin chose to paste it.
