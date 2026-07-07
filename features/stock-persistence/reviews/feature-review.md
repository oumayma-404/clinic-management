# Feature Review: stock-persistence

**Status:** RESOLVED
**Challenged:** Yes (all 6 findings challenged before fixing)

## Resolution (2026-06-26)
- **Fixed #1** — the stock form now sends the item's existing `maximumStockLevel` on edit (create still defaults to min), so editing no longer silently resets the maximum.
- **Fixed #2** — `Create`/`Update` stock handlers now reject a negative `UnitPrice` (parity with the UI); added a unit test.
- **Fixed #4** — extracted the duplicated clinic-resolution into `ICurrentClinicResolver` / `CurrentClinicResolver` (registered in `AddApplication`); all four stock handlers now depend on it instead of `IClinicContext` + `IUserRepository`. Tests updated to mock the resolver.
- **Deferred #3** — migration empty-table assumption: no code change (table empty in all known envs; a backfill step for non-existent rows would be over-engineering). PR note instead.
- **Skipped #5, #6** — client-side low-stock filter (intentional) and no optimistic concurrency (consistent with the whole app); both conscious choices, no defect.

Quality after fixes: `dotnet build` 0 errors / 0 new warnings (the new `clinic.Error ?? ...` coalesce and `result.Value!` test assertions keep CS8604/CS8602 out of changed files); unit tests 18/18 pass; frontend typecheck clean for stock files.


**Date:** 2026-06-26
**Parent Branch:** baseline commit 0e4d343 (feature/stock-persistence working tree)
**Merge Base:** 0e4d343
**Files Reviewed:** ~17 files (+368/−327 in tracked files, plus new controller, DTO, 4 handlers, migration, client, tests)

> **Agents skipped — reviewed inline.** Small diff fully held in working memory. This skill's four agents are hard-coded with ROP/`Extensions.ROP` and Anakin bounded-context mandates that don't exist in this project (MediatR `Result<T>`, EF repos, `IClinicContext`). Running them would yield false findings, so all four mandates (Code Quality, error-handling/Result, Business Logic, Breaking Changes) were applied inline against the real conventions.

## Findings

### Finding 1
- **Severity:** Minor
- **Category:** Business Logic
- **File:** web/components/stock-item-form-modal.tsx
- **Line:** ~88-96 (payload build) / form has no Maximum field
- **Comment:** The form never collects or sends `maximumStockLevel`, so on every **update** the handler defaults `maximum` to `minimumStockLevel` (`UpdateStockItemCommand` → `UpdateStockLevels(min, max=min)`). Any previously-set MaximumStockLevel is silently reset to the minimum each edit. Impact is low today (max isn't used for low-stock, which keys off minimum), but it's silent data mutation. Fix: either add a "Maximum stock level" field to the form, send the item's existing `maximumStockLevel` back on update, or drop max from the editable path and document it as unmanaged.

### Finding 2
- **Severity:** Minor
- **Category:** Code Quality
- **File:** api/ClinicManagement.Application/Features/Stock/Commands/CreateStockItemCommand.cs (and UpdateStockItemCommand.cs)
- **Line:** validation block (~46-56)
- **Comment:** Backend validates name/category/unit/min/quantity but **not** `UnitPrice`. The frontend rejects a negative price, but a direct API call (or the AI action layer) could persist a negative `UnitPrice`. Add `if (request.UnitPrice.HasValue && request.UnitPrice.Value < 0) return Result<StockItemDto>.Failure("Unit price cannot be negative");` to both create and update handlers for parity with the UI.

### Finding 3
- **Severity:** Minor
- **Category:** Breaking Change
- **File:** api/ClinicManagement.Infrastructure/Migrations/20260626082529_AddStockItemClinicId.cs
- **Line:** Up() — AddColumn ClinicId (non-nullable, default empty Guid) + FK to Clinics
- **Comment:** The migration adds a required `ClinicId` defaulting to `00000000-...-000000000000` and a cascade FK to `Clinics`. On an **empty** `StockItems` table (the case here, since stock was never persisted) this is fine. But in any environment that already had stock rows, every row would get the empty Guid, which has no matching `Clinics.Id` → the FK creation fails and the migration aborts. Acceptable given current reality; note it in the PR, or guard with a data step if a populated environment is possible.

### Finding 4
- **Severity:** Suggestion
- **Category:** Code Quality
- **File:** api/ClinicManagement.Application/Features/Stock/**/*.cs
- **Line:** clinic-resolution block in all 4 handlers
- **Comment:** The `GetUserId()` → `GetByAuth0SubAsync` → `user.ClinicId` block is duplicated across all four stock handlers (and matches the same duplication in appointments/dashboard handlers). It works and matches the existing codebase convention, but a small shared helper (e.g. an `IClinicContext` extension or a `CurrentClinicResolver` service returning `Result<Guid>`) would remove the repetition project-wide. Out of scope to refactor app-wide here; noting for a future cleanup.

### Finding 5
- **Severity:** Suggestion
- **Category:** Business Logic
- **File:** web/components/stock-table.tsx
- **Line:** ~64-71 (client-side low-stock filter)
- **Comment:** The low-stock filter is applied client-side (`item.isLowStock`), so the server-side `GET /api/stock?lowStockOnly=true` path is never exercised by the UI (the client always fetches all and filters in memory). That's fine for typical clinic volumes and keeps the toggle instant, but the server param is currently dead from the FE's perspective. Either use it (refetch with the flag) or keep client-side and treat the server flag as an API convenience — just be intentional.

### Finding 6
- **Severity:** Suggestion
- **Category:** Business Logic
- **File:** api/ClinicManagement.Application/Features/Stock/Commands/UpdateStockItemCommand.cs
- **Line:** ~70-72 (SetCurrentStock)
- **Comment:** Quantity is set directly with no optimistic concurrency, so two concurrent edits last-write-wins (one user's quantity change can clobber another's). This matches the rest of the app (no concurrency tokens anywhere), so it's consistent — flagging only so it's a conscious choice for an inventory feature where concurrent stock edits are plausible.

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 0 |
| Minor | 3 |
| Suggestion | 3 |
| **Total** | 6 |
