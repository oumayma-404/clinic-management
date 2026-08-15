# Progress: Fournisseurs (suppliers) for the stockroom

**Started:** 2026-08-15
**Type:** Small (forced — see DEV-1)
**Branch:** feature/windows-desktop-app (user chose the current branch)

## Status
- [x] Implementation
- [x] Quality checks (build, typecheck, build, responsive)
- [ ] Eye pass at 320/390/820/1180/1440 px — **owed, and blocked**; see « Device gate » below
- [ ] Tests (handled by /test-small-feature)

## Working tree note

**At the start of this session** four files were dirty and unrelated (in-flight
`feature/windows-desktop-app` UI work): `web/app/globals.css`, `web/components/dashboard-sidebar.tsx`,
`web/components/ui/badge.tsx`, `web/components/ui/button.tsx`, plus untracked `.playwright-mcp/`.

⚠️ **The tree changed under this session.** By the end those four were no longer dirty (committed or reverted
elsewhere) and a *different* set of unrelated changes had appeared — someone else's dashboard work:

- `web/app/page.tsx`
- `web/components/dashboard/day/day-ribbon.tsx`
- `web/lib/dashboard/day-phrases.ts`
- `web/lib/dashboard/day-summary.ts`
- `.playwright-mcp/` (untracked)
- `0.006` — a stray untracked file with a non-printing character in its name; not this feature's

None of them mentions `supplier` / `Supplier` / `fournisseur`; verified by grep. **They are not part of this
feature and must be excluded from its commits.** Stage by path only — never `git add -A` or `git add .`.

`npx tsc --noEmit` was re-run against the final tree (with that work present) and is still clean, so nothing
here is blocked by it.

## Quality checks

| Gate | Result |
|---|---|
| `dotnet build ClinicManagement.sln -c Release` | **0 errors**, 0 new warnings (scoped to changed files; the CS8618 hits on `StockItem.cs` / `LabWorkOrder.cs` are the pre-existing private-EF-ctor baseline shared by every entity — my edits only moved their line numbers) |
| `npx tsc --noEmit` (web) | **0 errors** |
| `npm run check:responsive` | **17/17 passed** |
| `npm run build` (web) | succeeded; `/fournisseurs` present in the route manifest |
| Unit suite (`dotnet vstest`, Release, scratch OutDir) | **3362 passed / 0 failed** — incl. `RealtimeResourceResolverTests` (both directions, with the new `suppliers` key), `TenantScopeFilterTests` (Supplier enrolled automatically), `ClinicArchiveScopeTests` (parents-before-children ordering with two new FKs), `SchemaVerificationServiceTests` |
| `npm run lint` | not run — `eslint` is in the script but not in `devDependencies`; per `web/CLAUDE.md` the real gate is tsc + check:responsive + build |

### Device gate — the eye pass is owed, and why it was not done
`check:responsive` passed 17/17 and the diff was re-read against `DEVICE-CONTRACT.md` § 1–13 (dialog widths are
`md:`-prefixed, the picker popover is `w-[min(22rem,calc(100vw-2rem))]`, controls in rows grow their own box with
`coarse:size-11`/`coarse:py-3` rather than `.touch-target`, the list has a card tree below `md:`, empty /
filtered-empty / failed are three distinct states).

**The manual walk was not performed.** The API was not running and starting it applies this feature's migration to
the developer's live PostgreSQL — which **drops `StockItems.Supplier`**. That is a destructive, effectively
one-way change to the user's dev data and is their call to make, not something to do in passing. Postgres and
MinIO are up; only the API and a working web dev server are missing.

## Files Changed

### Domain
- `Services/CategoryFolding.cs` **(new)** — the shared fold for all three open category sets
- `Services/SupplierCategories.cs` **(new)** · `Services/StockCategories.cs` **(new)**
- `Services/ProcedureTypeCategories.cs` — its private `Fold` now delegates (see auto-approved deviations)
- `Entities/Supplier.cs` **(new)**
- `Entities/StockItem.cs` — `Supplier` string → `SupplierId`; category normalised on write
- `Entities/LabWorkOrder.cs` — `SupplierId` added beside `Prosthetist`
- `Repositories/ISupplierRepository.cs` **(new)** (+ `SupplierUsage`)
- `Repositories/IStockItemRepository.cs` — `GetSupplierLinksAsync`

### Infrastructure
- `Persistence/Configurations/SupplierConfiguration.cs` **(new)**
- `Persistence/Configurations/StockItemConfiguration.cs` · `LabWorkOrderConfiguration.cs` — FK `Restrict` + index
- `Persistence/ApplicationDbContext.cs` — `DbSet<Supplier>` + query filter
- `Repositories/SupplierRepository.cs` **(new)** · `StockItemRepository.cs` — search term became a correlated EXISTS
- `Persistence/SchemaVerificationReader.cs` — two new counts
- `Migrations/20260815110947_AddSuppliers.cs` (+ Designer + snapshot) — **hand-corrected**, see DEV-3
- `Extensions.cs` — DI

### Application
- `DTOs/SupplierDto.cs` **(new)** · `StockItemDto.cs` · `NotificationDto.cs` · `LabWorkOrderDto.cs`
- `Features/Suppliers/` **(new)** — `SupplierRefusals`, `SupplierLink`, `Queries/GetSuppliersQuery`,
  `Commands/{Create,Update,Delete}SupplierCommand`
- `Features/Stock/` — create/update/consume/restock commands + list query
- `Features/LabOrders/` — create/update/status commands + list query
- `Features/Notifications/Queries/GetNotificationsQuery.cs` — read-time supplier resolution (AC-6/AC-7)
- `Common/Csv/ExportTables.cs` — supplier name **and number** on the stock export
- `Common/Maintenance/SchemaVerificationService.cs` — `supplier-links-backfill`
- `Common/Interfaces/ISchemaVerificationReader.cs`

### API
- `Controllers/SuppliersController.cs` **(new)**

### Web
- `lib/whatsapp.ts` **(new)** — the single `wa.me` builder + the three French message bodies
- `lib/api/suppliers.ts` **(new)** · `lib/api/types.ts` · `lib/api/stock.ts` · `lib/api/lab-orders.ts`
- `app/fournisseurs/page.tsx` **(new)**
- `components/suppliers/{suppliers-table,supplier-form-dialog,supplier-picker,whatsapp-action}.tsx` **(new)**
- `components/ui/category-combobox.tsx` **(new)**
- `components/stock-item-form-modal.tsx` · `stock-table.tsx` · `notification-panel.tsx` ·
  `procedure-type-materials-dialog.tsx`
- `app/stock/page.tsx` · `app/lab-orders/page.tsx`
- `lib/nav.ts` · `lib/zones.ts` · `lib/realtime/clinic-hub.ts`

### Tests (compile-only fixes — see auto-approved deviations)
- `UnitTests/Features/Stock/StockHandlersTests.cs`
- `UnitTests/Features/Notifications/{NotificationQueryTests,NotificationGenerationTests}.cs`

### Docs
- Root `CLAUDE.md` + Domain / Application / Infrastructure / API / web / web-components guides

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| `ProcedureTypeCategories.Fold` delegates to the new shared `CategoryFolding.Fold` (its two now-unused usings removed) | Byte-identical algorithm, private method, no public API or behaviour change. Two new open category sets need the same rule, and a third private copy is the `fixes-dont-propagate` shape this repo keeps finding. Pinned green by the full suite. |
| Test-infrastructure ctor fixes: `ISupplierRepository` mock added to the stock + notification fixtures | Build-required — the solution build compiles the test project, so 0-errors fails until they compile. Each original assertion is preserved (permissive/empty stubs; the fixtures' notifications are `AppointmentCreated`, so the new resolution short-circuits). **No new scenarios were written** — that is `/test-small-feature`'s job; see « Deferred » below. |
| `Consume`/`Restock`/`UpdateLabWorkOrderStatus` resolve the supplier for their response DTO | Those responses repaint the row client-side; without it a sortie or a stage change would drop the article's fournisseur (and its WhatsApp action) until the next refetch. One extra read only when a link exists. |
| `stockCategoryLabel` deleted along with the two constants the spec names | With the French label as the storage key the helper is the identity function; leaving it would be indirection asserting a translation that no longer happens. Its 3 call sites now render the stored value. |
| Stock CSV export gained a « Téléphone fournisseur » column | The reason to export stock is to order from it; a name with no number sends the reader back into the app. |

## Significant Deviations

### DEV-1 — Forced small pipeline on a ~37-file feature (approved)
**Original plan:** the skill escalates to the full pipeline past ~10 files.
**Discovered surface:** ~37 files.
**Actual:** asked via `AskUserQuestion` with the file count before writing code; the user chose the **full vertical
slice** (all 9 ACs) on the **current branch**.
**Justification:** the ACs are interdependent — AC-8's migration drops `StockItem.Supplier`, so a partial pass
leaves the product mid-migration with a stock form posting a field the server no longer accepts.
**Approved:** Y

### DEV-2 — Scope widened to laboratories and lab work orders (user-directed, mid-implementation)
**Spec said:** « Out of Scope — Suppliers for anything but stock (labo prothèse stays `LabWorkOrder`'s own free
text). »
**User said, mid-turn:** a fournisseur should also cover lab work, anapath and dental-equipment suppliers, and the
lab page should carry the WhatsApp contact too.
**Actual:** `SupplierCategories.Canonical` broadened to span laboratories and goods; `LabWorkOrder` gained a
nullable `SupplierId` mirroring the stock side, with its own backfill, DTO fields, command parameters, picker and
WhatsApp action. `Prosthetist` is **kept** — it is printed on the bon and a lab used once must be recordable
without filing a fiche — so the link is additive there, unlike on `StockItem`.
**Why linking rather than name-matching:** a WhatsApp action needs a *number*, which a free-text name has not got.
Matching the typed name against the supplier list at render time would be a second, fuzzy definition of « the same
laboratory » and would drift from the one the migration used.
**Impact:** ~10 files beyond the spec's scope; AC-4's refusal now counts two tables and names them separately.
**Approved:** Y (user-directed)

### DEV-3 — The scaffolded migration was hand-corrected in two places
**Original plan:** use `dotnet ef migrations add` (it worked here — the SAC/WDAC block did not bite).
**Actual:** the scaffold was wrong twice and both are silent failures:
1. `DropColumn("Supplier")` was emitted as the **first** statement. EF cannot know the backfill reads that column,
   so as generated it would have created **zero** suppliers and linked **zero** articles on every existing
   database, while reporting a clean migration. Every backfill now sits below the DDL it writes into and above the
   drop of what it reads.
2. The `CreateTable` block carried an **`xmin`** column (`Entity<T>.Version` maps onto PostgreSQL's *system*
   column). PostgreSQL refuses it outright — the same rejection that makes `AddConcurrencyToken`'s `Up()` empty.
**Also hand-written:** four backfills, each gated on « this row does not exist yet » so `Up()` re-runs safely, and
a `Down()` that restores the names and reverses the category rewrite before dropping the table.
**Not yet verified against a live database.** `verify-schema` should be run before and after and diffed, per the
repo's standing rule for a migration batch. Nothing in the unit suite touches a database.
**Approved:** implicit (correcting a scaffold defect), but the live verification is genuinely outstanding.

## Deferred to /test-small-feature
Genuinely **new** scenarios this change enables, none of which were written here:
- `Supplier` entity: category normalisation on ctor + `Update`, `SetActive` no-op returning false, name length guard
- `SupplierCategories` / `StockCategories` / `CategoryFolding`: folding case, accents, punctuation; `StockCategories`
  folding a **legacy English key**; `LegacyKeys` agreeing with `Canonical`
- `CreateSupplierCommand` / `UpdateSupplierCommand`: the duplicate refusal + its `supplier_duplicate` code, the
  accent/case-insensitive match, self-exclusion on rename, the `isActive` tri-state
- `DeleteSupplierCommand`: refusal naming **both** counts; a supplier referenced only by a bon; the success path
- `SupplierLink`: null id, foreign clinic id, unknown id, **and a deactivated supplier being accepted** (EC-4)
- `UpdateStockItemCommand`: the `SupplierId` tri-state (omitted = unchanged, explicit null = cleared)
- `GetNotificationsQuery`: a `LowStock` row resolving its supplier at read time; one whose article has no supplier;
  one whose article was deleted (EC-3); that non-`LowStock` rows resolve nothing and issue no reads
- Tenant isolation: `SupplierTenantIsolationTests` on the repo's standing pattern
- `SchemaVerificationService`: `supplier-links-backfill` red/green, and « not applicable » before the columns exist

## Follow-ups noted, not done
- `procedure-type-form-modal.tsx` still carries its own inline copy of the category-combobox pattern that
  `components/ui/category-combobox.tsx` now generalises. Folding it in is a refactor of working code that this
  feature did not otherwise touch; it is recorded in that component's docstring.
