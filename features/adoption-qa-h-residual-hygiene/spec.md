# Spec: Adoption QA — H (residual hygiene)

**Status:** APPROVED
**Type:** Small (forced multi-item pass — low-value leftovers deferred from Batches B/D)
**Created:** 2026-07-24
**Scope:** Full
**Branch:** feature/windows-desktop-app (existing)
**Feature:** The low-priority leftovers from the adoption-QA pass — retire the dead honoraires editor, add a stock-movement audit trail, settle patient-delete semantics, close the two latent #20 items, and fix stale docs.

## What Changes
### H1 — Honoraires editor fully retired (#13)
- Remove the `honoraires` branch from `document-editor-content.tsx` and the `honoraires` template card from `app/documents/page.tsx` (the gallery already opens the invoice launcher). **Recommended over** converting its remaining ~7 `€` occurrences (608/667/941/1146/1156/2463/2476) to DT — the editor is a dead-end nothing routes to (Batch D already redirects legacy docs to `/factures`). Remove `DocumentTypes.Honoraires` only if no persisted rows depend on the token (else keep the token, drop the editor). *Fork: retire (recommended) vs. DT-convert-and-keep.*

### H2 — Stock-movement audit ledger (#14)
- New `StockMovement` child entity (`StockItemId`, `ClinicId`, `Type` = Consume|Restock, `Quantity`, `ResultingStock`, `CreatedAt`, optional `UserId`/`Reason`) written by `ConsumeStockCommand`/`RestockStockItemCommand`. Read via `GET /api/stock/{id}/movements`; a "Mouvements" history view/panel in the stock UI.

### H3 — Patient-delete semantics settled (#15)
- **Recommended:** keep the current **block-with-clear-message** behavior (Batch D already returns a French message on an FK violation) — formalize it (pre-check for linked invoices/appointments/records → explicit refusal instead of relying on `DbUpdateException`) so the message is deterministic. *Fork: block (recommended) vs. soft-delete (add `Patient.IsArchived` + filter).* 

### H4 — Latent #20 pair
- **Reminder backfill:** when an admin enables a reminder channel, enqueue reminders for already-booked upcoming appointments (a backfill in `UpdateClinicReminderSettingsCommand` over future active appointments via `IReminderScheduler`), so turning a channel on isn't retroactively silent.
- **Caisse date boundary:** normalize the caisse/revenue period bounds to UTC day edges with a consistent inclusive-start / exclusive-end rule in `GetCaisseSummaryQuery` + `GetInvoiceRevenueQuery` so a payment at local midnight lands in exactly one day.

### H5 — Doc accuracy
- Drop the stale "global reference data (no ClinicId)" docstrings on the per-clinic CNAM/medication/dental-act entities; fix the CLAUDE.md wording that frames invoice PDFs as a `PdfGenerationJob` (they're a synchronous query).

## Data / Schema Changes
- New table `StockMovements` (H2) — EF migration; indexed `(StockItemId, CreatedAt)`.
- H3 soft-delete *only if that fork is chosen* → `Patient.IsArchived` column + query filter.

## API Contract
### GET /api/stock/{id}/movements  (H2)
Response 2XX: `StockMovementDto[]` (newest-first)
Errors: `404 not found / other clinic`

## Out of Scope
- Reversing/editing a recorded stock movement (append-only).
- Re-sending reminders for *past* appointments on channel-enable (upcoming only).
- Any change to how reminders are dispatched (H4 only enqueues; the minutely job still sends).

## Edge Cases (Critical only)
- H2: the movement's `ResultingStock` must match the item's post-mutation `CurrentStock` (written in the same transaction).
- H4 backfill: don't double-enqueue if a reminder already exists for an appointment (idempotent per appointment).
- H1: a persisted legacy `honoraires` `MedicalDocument` must still open *somewhere* safe (Batch D routes it to `/factures`) — retiring the editor must not 404 those rows.
