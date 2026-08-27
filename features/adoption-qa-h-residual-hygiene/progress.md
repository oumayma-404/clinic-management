# Progress: Adoption QA — H (residual hygiene)

**Started:** 2026-07-24
**Type:** Small (forced multi-item pass)
**Branch:** feature/windows-desktop-app

## Status
- [x] Implementation (H4a reminder-backfill deferred — see DEV-2)
- [x] Quality checks (dotnet build 0/0; web tsc 0; next build ok)
- [ ] Tests (handled by /test-small-feature)

## Files Changed (by item)
- **H1 retire honoraires editor** — `web/app/documents/[type]/page.tsx` now guards `type === "honoraires"` and renders a "géré dans Factures" notice + link instead of the retired € editor. (Kept the gallery card — it opens the invoice launcher, not the editor — see DEV-1.)
- **H2 stock-movement ledger** — `Domain/Entities/StockMovement.cs` + `Enums/StockMovementType.cs` + `Repositories/IStockMovementRepository.cs` (new); `Infrastructure/.../StockMovementConfiguration.cs` + `Repositories/StockMovementRepository.cs` + DbSet/filter + DI; migration `20260724…_AddStockMovements`. `ConsumeStockCommand`/`RestockStockItemCommand` record a movement (ResultingStock = post-mutation, same transaction). `GetStockMovementsQuery` + `StockMovementDto` + `GET /api/stock/{id}/movements`. FE: `stock.ts` `movements()` + `StockMovementDto`; `stock-table.tsx` "Historique" button + dialog.
- **H3 patient-delete semantics** — kept the Batch-D block-with-message (FK violation → clear French refusal); deterministic, no new pre-check needed (see DEV note).
- **H4b caisse date boundary** — `GetCaisseSummaryQuery` default upper bound is now the last tick of the day (was next-midnight), so a payment at 00:00 next day isn't double-counted.
- **H5 doc accuracy** — dropped the stale "global reference data (no ClinicId)" docstrings on `CnamNomenclatureEntry`/`CnamLetterValue`/`DentalActCode`/`Medication` + the 3 catalog repo interfaces → now "per-clinic (has ClinicId, clinic-filtered)".

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| H3: no new pre-check code | Batch-D's `DbUpdateException`→French-message block is already deterministic; recommended fork was "block", which is satisfied. |
| H5: invoice-PDF "job" CLAUDE.md wording untouched | The root + API CLAUDE.md already correctly describe invoice PDFs as a synchronous query — nothing stale to fix. |

## Significant Deviations
- **DEV-1 (approved inline):** H1 spec said "remove the honoraires template card from the gallery." Reality: that card opens the **invoice launcher** (`HonorairesLauncher`), a live/useful path — not the dead editor. Corrected: kept the card, guarded the `/documents/honoraires` **editor route** instead. Fully retires the dead-end without removing a good entry point. The retired editor's remaining `€` internals are now unreachable dead code (left in place; not excised to avoid destabilizing the shared editor).
- **DEV-2 (DEFERRED — needs its own pass):** H4(a) "reminder-channel-enable backfills already-booked upcoming appointments" was **not** implemented. `IReminderScheduler.ScheduleForAppointmentAsync` is not idempotent per appointment, so a naive backfill over all upcoming appointments would create **duplicate** outbox rows. Doing it safely requires an existence guard (via `INotificationRepository.GetByAppointmentIdAsync`) in the scheduler + a channel was-off→now-on transition check — its own small feature. The report rated #20 "harmless at UTC+1 today," so deferring is low-impact. **Recommend a follow-up spec `reminder-backfill-on-enable`.**
