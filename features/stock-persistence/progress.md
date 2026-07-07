# Progress: Stock Persistence

**Started:** 2026-06-26
**Type:** Small
**Branch:** feature/stock-persistence

## Status
- [x] Implementation
- [x] Quality checks (build, typecheck, migration applies)
- [x] Tests (xUnit handler tests — see Test Plan / Tests Run)

## Test Plan
No integration harness in this repo; used the existing `ClinicManagement.UnitTests` xUnit+Moq project (new file `Features/Stock/StockHandlersTests.cs`, 4 handler test classes). Frontend ACs (UI states) not automated — no FE harness. No Newman/Postman.

| AC | Action | Target | Notes |
|----|--------|--------|-------|
| AC-1 | New test class | StockHandlersTests (GetStockItems) | Clinic-scoped list mapped to DTO; IsLowStock computed |
| AC-2 | New test class | StockHandlersTests (Create) | Creates item scoped to user's clinic; persists |
| AC-3 | New test class | StockHandlersTests (Update) | Updates own-clinic item incl. quantity |
| AC-4 | New test class | StockHandlersTests (Delete) | Deletes own-clinic item |
| AC-5 | New test class | StockHandlersTests (Create) | Blank name / negative quantity rejected, no save |
| AC-6 | New test class | StockHandlersTests (GetStockItems) | lowStockOnly passed through to repo |
| AC-7 | New test class | StockHandlersTests (Update + Delete) | Cross-clinic update/delete fails, no save; no-token list fails |
| Edge | New test class | StockHandlersTests (Create) | MaximumStockLevel defaults to MinimumStockLevel when omitted |

## Tests Run
| Suite | Filter | Result |
|-------|--------|--------|
| Unit | ClinicManagement.UnitTests (Stock + Dashboard) | 18 passed, 0 failed, 0 skipped |

(13 stock tests + 5 dashboard tests after /apply-review-fixes added the negative-UnitPrice test and the ICurrentClinicResolver refactor.)

## Review fixes applied (see reviews/feature-review.md)
Fixed #1 (preserve max on edit), #2 (backend negative-price guard), #4 (extracted ICurrentClinicResolver used by all stock handlers). Deferred #3 (migration empty-table — PR note). Skipped #5/#6 (suggestions). Build 0 errors/0 new warnings; 18/18 tests pass.

## Working tree note (start of session)
Branched from a baseline commit (oumayma-404) that captured all prior uncommitted WIP + the live-dashboard feature, and untracked bin/obj/node_modules/.next. Working tree is otherwise clean; all files below belong to this feature.

## Files Changed
Backend:
- api/ClinicManagement.Domain/Entities/StockItem.cs — add ClinicId + ctor param; add SetCurrentStock; UpdateInfo now takes unit
- api/ClinicManagement.Domain/Repositories/IStockItemRepository.cs — add GetByClinicIdAsync(lowStockOnly)
- api/ClinicManagement.Infrastructure/Repositories/StockItemRepository.cs — impl
- api/ClinicManagement.Infrastructure/Persistence/Configurations/StockItemConfiguration.cs — ClinicId required + FK→Clinic (cascade) + index
- api/ClinicManagement.Infrastructure/Migrations/20260626082529_AddStockItemClinicId.* (new migration; applied to dev DB)
- api/ClinicManagement.Application/DTOs/StockItemDto.cs (new; + ToDto mapping extension)
- api/ClinicManagement.Application/Features/Stock/Queries/GetStockItemsQuery.cs (new)
- api/ClinicManagement.Application/Features/Stock/Commands/{Create,Update,Delete}StockItemCommand.cs (new)
- api/ClinicManagement.API/Controllers/StockController.cs (new)

Frontend:
- web/lib/api/types.ts — add StockItemDto
- web/lib/api/stock.ts (new) — stockApi + StockItemPayload
- web/components/stock-table.tsx — real API load, search/category/low-stock filters, low-stock badge, delete via API + AlertDialog, loading/empty/error
- web/components/stock-item-form-modal.tsx — entity-aligned fields, inline validation, toasts, create/update via API
- web/app/stock/page.tsx — refreshKey + typed editingItem + onSaved

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Added `SetCurrentStock(int)` + `unit` param on `UpdateInfo` to StockItem | Entity only exposed AddStock/RemoveStock; spec called for a directly-editable quantity and the form edits Unit. Internal domain methods; no external callers (stock was never wired). |
| `StockItemDto.ToDto()` mapping extension | DRY across 3 handlers; keeps handlers thin. Single new helper in the DTO file. |
| POST returns `Ok` (not `CreatedAtAction`) | No GET-by-id route was specced/needed; avoids adding one purely for the Location header. |

## Significant Deviations
(none — the two schema/field decisions were resolved via questions in /define-small-feature: per-clinic ClinicId + drop Item Code.)

## Quality
- `dotnet build ClinicManagement.sln`: 0 errors, 0 new warnings in changed files (pre-existing CS8632/CS8618 conventions unchanged).
- `npx tsc --noEmit`: clean for all stock feature files. (Lint not available in repo — no eslint installed.)
- Migration `AddStockItemClinicId` applied to the running dev DB successfully.
