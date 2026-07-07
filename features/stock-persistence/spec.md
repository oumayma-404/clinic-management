# Feature Specification: Stock Persistence

**Status:** APPROVED
**Type:** Small
**Created:** 2026-06-25
**Scope:** Full
**Feature:** Wire the Stock/inventory module to a real, clinic-scoped API so items persist (replacing the hardcoded sample data + console.log "save").

## Overview
The Stock page is currently a façade: `stock-table.tsx` holds a hardcoded array in `useState` and `stock-item-form-modal.tsx` "saves" via `console.log`, so adds/deletes vanish on refresh. The domain already has a `StockItem` entity + `IStockItemRepository`/`StockItemRepository`, but there is no controller, CQRS handlers, DTO, or API client. This feature adds a clinic-scoped Stock API and wires the UI to it (load/create/update/delete) with proper feedback and a low-stock indicator/filter.

## What Changes
- Add `ClinicId` to `StockItem` (+ EF migration) and scope all stock queries/mutations to the current clinic via `IClinicContext` (same pattern as patients/appointments).
- Add a `StockController` with list/create/update/delete, backed by MediatR commands/queries returning `Result<T>`, and a `StockItemDto`.
- Add `web/lib/api/stock.ts` typed client + `StockItemDto` in `types.ts`.
- `stock-table.tsx` loads items from the API (no hardcoded array); supports search, category filter, and a **low-stock** filter.
- `stock-item-form-modal.tsx` creates/updates via the API with **inline validation + `sonner` toasts** (remove `alert()` and `console.log`). Form fields align to the entity: Name, Category, Unit, Quantity (current stock), Minimum stock level (required), and optional Description / Unit price / Supplier. The fake "Item Code" field is removed.
- Delete uses the existing `AlertDialog` confirm and calls the API.
- Low-stock items (CurrentStock ≤ MinimumStockLevel) are visually indicated in the table.

## Acceptance Criteria
- **AC-1:** `GET /api/stock` returns only the current clinic's items; the table renders them with loading + empty states (no hardcoded data remains).
- **AC-2:** Submitting the "Add" form persists the item (`POST`); it is present after a page refresh.
- **AC-3:** Editing an item (including its quantity) persists via `PUT` and survives refresh.
- **AC-4:** Deleting an item (after `AlertDialog` confirm) removes it via `DELETE` and it stays gone after refresh.
- **AC-5:** The form shows inline validation for required fields and a success/error `toast`; no `alert()` or `console.log` remains in the stock components.
- **AC-6:** Items with CurrentStock ≤ MinimumStockLevel are visually flagged, and a "Low stock" filter shows only those.
- **AC-7:** Stock is clinic-scoped: a user cannot read, update, or delete another clinic's items.

## API Contract
All routes `[Authorize]`, clinic resolved server-side via `IClinicContext` → `IUserRepository` (like `GetAppointmentsQuery`). Failures map `Result.Failure` → `BadRequest`.

### GET /api/stock?lowStockOnly={bool}
Response 2XX: `StockItemDto[]`

### POST /api/stock
Request: `{ name, category, unit, currentStock, minimumStockLevel, maximumStockLevel?, description?, unitPrice?, supplier? }`
Response 2XX: `StockItemDto`

### PUT /api/stock/{id}
Request: same editable fields as POST (incl. `currentStock`)
Response 2XX: `StockItemDto`

### DELETE /api/stock/{id}
Response 2XX: empty

**StockItemDto:** `{ id, name, description?, category, unit, currentStock, minimumStockLevel, maximumStockLevel, unitPrice?, supplier?, isLowStock, createdAt, updatedAt? }`

## Data / Schema Changes
- **StockItem.ClinicId** — new `Guid`, required, FK → `Clinic`. New EF migration. (The stock table has never been populated in practice, so no existing-row backfill is needed.)
- Add a domain method to set/adjust `CurrentStock` directly on update (the entity currently only exposes `AddStock`/`RemoveStock`); `MaximumStockLevel` defaults to `MinimumStockLevel` when omitted (entity requires `max >= min`).
- Stock repository gains clinic-scoped reads (`GetByClinicIdAsync`, low-stock by clinic).

## Out of Scope
- Stock movements / audit history, supplier management, purchase orders.
- Quantity adjustment workflows beyond a direct editable value (no "receive/issue" ledger).
- Expiry-date / batch-number tracking in the form (entity fields exist but stay untouched here).

## Edge Cases (Critical only)
- Creating with quantity below the minimum level → item is created and immediately flagged low-stock.
- `maximumStockLevel` omitted → defaults to `minimumStockLevel` (keeps the entity's `max >= min` invariant).
- Update/delete of an item belonging to another clinic → returns a failure, not the data.
