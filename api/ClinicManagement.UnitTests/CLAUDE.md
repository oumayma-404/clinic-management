# ClinicManagement.UnitTests — Test Suite Guide

xUnit + Moq unit tests for the whole `api/` solution. ~90 test classes, one folder per layer, mirroring the source tree. **Fast, isolated, mock-based** — no database, no HTTP server, no external calls. (There is no separate integration/E2E project; these are the backend's only automated tests.)

> Sub-guide under the root `CLAUDE.md`. This maps the test project; read the layer guides for the code under test.

## Stack

- **xUnit 2.5.3** (`[Fact]`, `[Theory]`), **Moq 4.20.72**, coverlet for coverage. .NET 8, nullable + implicit usings on; `Xunit` is a global `using` (`.csproj`).
- References **Application, Infrastructure, AND API** projects. The API reference exists so reflection tests can enumerate controller types; `FrameworkReference Microsoft.AspNetCore.App` is pulled in for `DefaultHttpContext` / MVC `ControllerBase` / authorization-options types used by the Phase-4 auth-gate + controller-coverage tests.

## Layout (mirrors the solution)

```
Api/            → controller/base + startup/job/maintenance tests (thin API layer)
Common/         → cross-cutting: MediatR behaviors, exception middleware, clinic provider, authz policies, admin recovery
Domain/         → pure entity/value-object/calculator rules (no mocks needed)
Features/       → CQRS handler tests, grouped by feature area (the bulk of the suite)
Hubs/           → SignalR realtime: ClinicHub, ClinicGroups, SignalRRealtimeNotifier
Infrastructure/ → service/repo/persistence tests: renderers, senders, e-invoice, backup, cert, storage, seeds
```

## Conventions (match these when adding tests)

- **Moq harness pattern.** Handler tests build a small private `Harness`/`Handler()` helper that wires mocked repositories + `ICurrentClinicResolver`/`IClinicContext` + `IUnitOfWork` and returns the real handler. See `Features/Notifications/NotificationGenerationTests.cs` for the canonical shape (nested `GeneratorHarness`/`StockHarness`, `NullLogger<T>.Instance` for loggers).
- **Spec-ID traceability.** Class-level XML `<summary>` and per-test `//` comments cite the spec item they cover (`[US-2]`, `[AC-4]`, `[FR-E3]`, `[R-3]`). Preserve this — it's how tests map back to feature specs.
- **Tenant isolation is a first-class guard.** Every clinic-scoped feature has a `*TenantIsolationTests` proving another clinic's row reads as "not found" for get/update/cancel/delete and lists are caller-scoped (fixed GUIDs `aaaa…`/`bbbb…`; e.g. `Features/Invoices/InvoiceTenantIsolationTests.cs`, and the same for Appointments/Documents/Files/Notifications/ProcedureTypes/**TreatmentPlans**).
- **Best-effort side effects assert non-failure.** Notification/reminder generators are tested to *swallow* persistence failures without breaking the core op (`Generator_Swallows_Persistence_Failure`).
- Fixed UTC `DateTime`s and deterministic GUIDs — no `DateTime.Now`/`Guid.NewGuid()` in assertions that must be stable.

## Release-gate / guard tests (fail loud when someone regresses a hardening decision)

- **`Api/ControllerAuthorizationCoverageTests.cs`** — reflection scan of every controller/action; the set of `[AllowAnonymous]` endpoints must *exactly* match a hard-coded allow-list (currently `Auth.GetMode/Login/Setup/Register`, `Connectivity.Get`, `GoogleCalendar.Callback`). Adding any new anonymous endpoint fails the build until reviewed — this is the Local-mode fail-closed guarantee (FR-E3).
- **`Common/Authorization/AuthorizationPoliciesTests.cs`** — the `FallbackPolicy` is installed only in Local mode.
- **`Common/Behaviors/RealtimeBroadcastBehavior*` + `Common/Behaviors/RealtimeResourceResolverTests.cs`** — the MediatR pipeline behavior that auto-broadcasts SignalR resource-changed events.
- **`Api/TreatmentPlansControllerAuthorizationTests.cs`** — pins `CancelPlan` to `AdminOrDoctor` (altering a numbered financial document) and every other action to *no* method-level policy. Carries a **drift guard** (`Every_Action_Is_Classified_By_This_Test`) that fails when a new action is added without deciding its policy — deliberate, so slice B's `amend`/`revise-installments`/`items/order` cannot land unclassified.
- **`Features/Common/ConcurrencyConflictTests.cs`** — the optimistic-concurrency contract. Reflection-based where it can be, so a new entity or DTO is covered without editing the test: every `Entity<>` carries the token, the six round-tripped DTOs and their update commands expose it, a `ConflictException` **escapes** the handler catch-alls rather than being flattened, and the handler actually calls `SetExpectedVersion` (without which the whole feature is inert while looking present).
- **`Features/Invoices/CreditNoteReadTests.cs`** — avoirs are readable, and « Total encaissé » nets them in **both** branches of the revenue read (the no-period branch is the one `/factures` actually loads).
- **`Features/Patients/PatientContactOptionalTests.cs`** — contact is optional, the tri-state clears, no sentinel is written, and one phone-less patient no longer 500s the patient list.
- **`Features/Billing/MoneyReadConsistencyTests.cs`** — « Solde patient », « Créances » and the dashboard KPI must report the same outstanding figure for one shared fixture. Its repository mocks intentionally reimplement `TreatmentPlanRepository`/`InvoiceRepository`'s SQL filters, so the test targets the *handlers* feeding `Domain/Services/PlanBillingRules` the same rule. Paired with `Domain/PlanBillingRulesTests.cs` (the rule itself).
- **`Infrastructure/Persistence/*SeedTests.cs`** — CNAM + medication catalog seed integrity.
- **`Infrastructure/Services/`** e-invoicing depth: `TeifXmlGeneratorTests` (TTN TEIF XML), `XadesEInvoiceSignerTests` (XAdES signature), `QrCodeGeneratorTests`, `SandboxTtnClientTests`; reminders: `ReminderChannelSenderTests`/`ReminderScheduler`/`ReminderSettingsProvider`/`ReminderPhone`/`ReminderSchedule`; plus `CertificateProvisionerTests`, `PgDumpBackupServiceTests`, `InternetProbeTests`, `CnamBs1BulletinRendererTests`, document renderers (`Certificat`/`Liaison`/`Generic`/`PractitionerRenderSnapshot`).

## Gotchas

- **`Features/Patients/DentalRecordPostVisitCompletionTests.cs.deferred`** — the `.deferred` extension deliberately excludes it from compilation (parked, not deleted). Don't rename it back without checking why it was parked.
- **Running the suite on this machine.** `dotnet test` fails at assembly-load with `0x800711C7` because Windows **Smart App Control** is ON and blocks freshly-built DLLs — environmental, not a test defect. See the user's `smart-app-control-blocks-tests` memory. Workaround: `dotnet build <UnitTests.csproj> -p:OutDir=<scratch>/` then `dotnet vstest <scratch>/ClinicManagement.UnitTests.dll`. (SAC's verdict is **time-varying** — the full suite has run clean through this workaround; do not attribute a red run to it without first clearing `bin/`+`obj/` and running `dotnet build-server shutdown`.)
- **Nothing here touches a database — so migrations are outside this suite's reach entirely.** An index can be missing, an exclusion constraint can be non-partial, a data backfill can cover zero rows, and a model change can have no applied migration at all, while every test in this project passes. That class of change is gated by the **`verify-schema` console verb** instead (`Application/Common/Maintenance/SchemaVerificationService` + `Infrastructure/Persistence/SchemaVerificationReader`), run before and after a migration batch and diffed. `SchemaVerificationServiceTests` covers the assertions against a **mocked reader** — which is why the reader seam exists at all. Do **not** add a database-touching test here to cover a migration; extend `verify-schema` and its service tests.
- **A failing test here has three times been a stale fixture, not a defect.** `data-and-money-integrity` inherited
  an "8-failure baseline" that turned out to be exactly that, in all three cases with the production code correct
  and the test drifted behind it: `ReminderSchedulerTests` stubbed `ResolveEnabledChannelsAsync` while the
  scheduler reads the full `ResolveAsync`; `DoctorCachetTests` uploaded three arbitrary bytes after the handler
  grew a **magic-byte** check (so the guard itself had no coverage); `DocumentTypeAndFilenameTests` left
  `ICurrentClinicResolver` unconfigured after the tenant guard moved ahead of the patient lookup (so it passed
  its main assertion for the wrong reason). Diagnose before assuming environmental.
