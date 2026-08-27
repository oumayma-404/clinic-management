# Progress: Misc Data-Integrity Cleanups

**Started:** 2026-07-21
**Type:** Small
**Branch:** feature/windows-desktop-app (user declined a dedicated branch)

## Status
- [x] Implementation
- [x] Quality checks — `dotnet build ClinicManagement.Application.csproj` → 0 errors, 0 new warnings (44 pre-existing CS8618, none in changed files). Handler deletion compiles clean; `AppointmentCreatedEventHandler` had no references outside its own file + docs.
- [x] Tests — new `UpdateInvoiceLinkPreservationTests.cs` (1, #10: editing a draft invoice with only patient+lines preserves its existing dental-record/appointment links). #16 (dead-handler removal) = build gate (solution compiles green without it; no references). Green.

## Working tree note (start of session)
Unrelated in-flight work EXCLUDED from staging: `medication-catalog-picker`; the other `features/fix-*` folders.

## Files Changed
- `api/.../Features/Invoices/Commands/UpdateInvoiceCommand.cs` — #10: preserve the invoice's existing header dental-record / appointment links when the edit request omits them (`?? invoice.<Link>`).
- `api/.../Features/Appointments/EventHandlers/AppointmentCreatedEventHandler.cs` — #16: **deleted** (dead code; never-dispatched handler that would enqueue an unsendable `NotificationType.Both` reminder).
- `api/ClinicManagement.Application/CLAUDE.md` — updated the Appointments event-handler references to reflect the removal + that no domain-event dispatch is wired.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| #16 removed the handler (spec-chosen) and updated the two `CLAUDE.md` references | Keeps the nearest doc accurate per the repo guide; no runtime behavior change (events were never dispatched). |

## Significant Deviations
- **DEV-1 — #10 preserves HEADER links only, not per-line dental-record links.** The spec AC-1 mentions per-line links too, but the edit contract (`InvoiceLineRequest`: designation/qty/unitPrice/dentalRecordId) carries no stable line identity, and `SetLines` rebuilds all lines from scratch — so per-line links can't be reliably matched/preserved across an edit without a contract change. The header `DentalRecordId` is the documented "already invoiced" guard driver (`InvoiceDto.cs`), and the investigation found no active consumer of the per-line link. **Impact:** the substantive data-loss (header link) is fixed; per-line links still follow the request. Preserving per-line links would require adding line ids to the edit contract (a larger change) — flagging for the user to decide if needed.

## Deferred to /test-small-feature
- New scenarios: editing a draft invoice (patient + lines only) keeps its header dental-record/appointment links; build stays green without the removed handler.
