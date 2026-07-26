# Progress: Adoption QA — Batch D (data hygiene)

**Started:** 2026-07-24
**Type:** Small
**Branch:** feature/windows-desktop-app

## Status
- [x] Implementation
- [x] Quality checks (dotnet build 0/0; web tsc 0; next build ok)
- [ ] Tests (handled by /test-small-feature)

## Files Changed (by finding)
- **#11 emergency contact** — `Create/UpdatePatientCommand.cs` (fields + `UpdateEmergencyContact`); `web/components/edit-patient-dialog.tsx` (2 inputs + load/reset + payloads); `web/lib/api/patients.ts` (create type).
- **#12 ordonnance DCI** — `Infrastructure/Services/PdfGenerationService.cs` (`MedicationData.Dci` + append "(DCI : …)"). FE already sends `dci`.
- **#13 honoraires €→DT + dead-end** — `web/components/document-editor-content.tsx` (`€`→`DT`, cited lines 283/315); `web/app/patients/[id]/page.tsx` (legacy honoraires doc routes to `/factures`, not the retired editor).
- **#14 stock** — `ConsumeStockCommand.cs` + `RestockStockItemCommand.cs` (new, use `RemoveStock`/`AddStock`); `CreateStockItemCommand.cs` (low-on-create `LowStockAsync`); `StockController.cs` (`consume`/`restock`); `web/lib/api/stock.ts`; `web/components/stock-table.tsx` (Sortie/Entrée dialog); `web/app/stock/page.tsx` (`useClinicRealtime(Stock)`).
- **#15 delete patient** — `DeletePatientCommand.cs` (new); `PatientsController.cs` (`DELETE {id}`); `web/lib/api/patients.ts` (`delete`); `web/components/patients-table.tsx` (admin-gated delete + confirm).
- **#16 onboarding hours** — `CreateClinicCommand.cs` (`WorkingHoursJson` + `SetWorkingHours` both branches); `CreateClinicRequest.cs` (DTO) + `SetupRequest.cs`; `ClinicsController.cs` + `AuthController.cs` (map through); `web/lib/api/clinics.ts`; `web/components/setup-wizard.tsx` (serialize + send).
- **#17 governorate dropdown** — `web/lib/tunisia.ts` (new shared list); `edit-patient-dialog.tsx` (Select); `setup-wizard.tsx` (import shared list).
- **#19 odontogram surfaces** — `web/components/odontogram.tsx` (MODVL toggle + `surfaces` in payload).

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| `StockHandlersTests.cs` ctor helper updated (add `INotificationGenerator` mock) | Build-required compile fix in test infra (src ctor changed); new low-on-create scenario deferred to /test-small-feature. |
| #13: only the 2 cited `€` lines converted to DT; deeper honoraires-editor `€` internals left | That editor is a retired dead-end now routed away; the primary path (invoice launcher) is unaffected. |
| Governorate list lifted to `web/lib/tunisia.ts`; setup-wizard now imports it | De-dup per spec; identical 24 names. |

## Deferred (report marked "harmless at UTC+1 today")
- **#20 latent:** waiting-list "Promouvoir" now benefits from the A1 overlap guard (shared create-appointment dialog). Reminder-channel backfill of already-booked appointments and the caisse date-boundary UTC normalization are **not** done here — lower priority, own pass.
