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
- **`Features/Billing/MoneyReadConsistencyTests.cs`** — « Solde patient », « Créances » and the dashboard KPI must report the same outstanding figure for one shared fixture. Its repository mocks intentionally reimplement `TreatmentPlanRepository`/`InvoiceRepository`'s SQL filters, so the test targets the *handlers* feeding `Domain/Services/PlanBillingRules` the same rule. Paired with `Domain/PlanBillingRulesTests.cs` (the rule itself).
- **`Infrastructure/Persistence/*SeedTests.cs`** — CNAM + medication catalog seed integrity.
- **`Infrastructure/Services/`** e-invoicing depth: `TeifXmlGeneratorTests` (TTN TEIF XML), `XadesEInvoiceSignerTests` (XAdES signature), `QrCodeGeneratorTests`, `SandboxTtnClientTests`; reminders: `ReminderChannelSenderTests`/`ReminderScheduler`/`ReminderSettingsProvider`/`ReminderPhone`/`ReminderSchedule`; plus `CertificateProvisionerTests`, `PgDumpBackupServiceTests`, `InternetProbeTests`, `CnamBs1BulletinRendererTests`, document renderers (`Certificat`/`Liaison`/`Generic`/`PractitionerRenderSnapshot`).

## Gotchas

- **`Features/Patients/DentalRecordPostVisitCompletionTests.cs.deferred`** — the `.deferred` extension deliberately excludes it from compilation (parked, not deleted). Don't rename it back without checking why it was parked.
- **Running the suite on this machine.** `dotnet test` fails at assembly-load with `0x800711C7` because Windows **Smart App Control** is ON and blocks freshly-built DLLs — environmental, not a test defect. See the user's `smart-app-control-blocks-tests` memory.
