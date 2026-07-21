# Progress: WhatsApp Embedded Signup — Connect Flow (P1)

**Started:** 2026-07-21
**Type:** Small
**Branch:** feature/windows-desktop-app (reused — the project's working branch; reminder base code lives here, not on main)

## Status
- [x] Implementation
- [x] Quality checks (build, typecheck, next build)
- [ ] Tests (handled by /test-small-feature)

## Working tree note (start of session)
Pre-existing unrelated uncommitted files (excluded from this feature — not committed here anyway per manual-commit preference):
`appsettings.Development.json`, `ResolvedReminderSettings.cs`, `ReminderSettingsProvider.cs`, `RemindersConfig.cs`, `WhatsAppSender.cs` (in-progress per-clinic reminder edits), plus untracked `features/*/reviews/` and `.claude/worktrees/`.

## Files Changed
**Domain**
- `Enums/WhatsAppConnectionStatus.cs` (new — NotConnected/Connected/Error)
- `Entities/ClinicReminderSettings.cs` (+4 props, `ApplyWhatsAppConnection`, `ClearWhatsAppConnection`)

**Application**
- `Common/Interfaces/IWhatsAppOnboardingService.cs` (new — interface + `WhatsAppOnboardingError` enum + `WhatsAppOnboardingException`)
- `Features/Clinics/Commands/ConnectClinicWhatsAppCommand.cs` (new — atomic connect)
- `Features/Clinics/Commands/DisconnectClinicWhatsAppCommand.cs` (new — best-effort unsubscribe + clear)
- `DTOs/ReminderSettingsDto.cs` (+4 read-only fields; new `ConnectWhatsAppRequest`)
- `DTOs/ReminderSettingsMappings.cs` (map the 4 fields; status → enum name)

**Infrastructure**
- `Services/WhatsAppOnboardingService.cs` (new — Graph API: exchange/subscribe/register/unsubscribe, categorized errors)
- `Services/MetaConfig.cs` (new — `Meta:*` accessors, Graph base URL)
- `Persistence/Configurations/ClinicReminderSettingsConfiguration.cs` (+4 column mappings)
- `Migrations/20260721160000_AddWhatsAppConnectionFields.{cs,Designer.cs}` (new — hand-authored, see DEV-1)
- `Migrations/ApplicationDbContextModelSnapshot.cs` (+4 props in the ClinicReminderSettings block)
- `Extensions.cs` (register `IWhatsAppOnboardingService`)

**API**
- `Controllers/ClinicsController.cs` (+`POST`/`DELETE api/clinics/whatsapp/connect`, AdminOnly + Cloud-only 404; inject `IConfiguration`)
- `appsettings.json` (+documented `Meta` section; `AppSecret` intentionally not present — env only)

**Frontend**
- `lib/api/reminder-settings.ts` (+4 DTO fields, `WhatsAppConnectionStatus` type, `ConnectWhatsAppRequest`, `connectWhatsApp`/`disconnectWhatsApp`)
- `components/reminder-settings.tsx` (Cloud-only Embedded-Signup block: Meta JS SDK load, connect/disconnect, status badge + masked number + last error)

## Quality checks
- Backend: `dotnet build ClinicManagement.API.csproj -o <scratch>` → **0 errors, 0 new warnings** (13 warnings, all pre-existing files; the running app locks the normal `bin`, so built to a scratch OutDir per the skill).
- Frontend: `npx tsc --noEmit` → **0 errors**; `npx next build` → **success** (after `rm -rf .next` cleared a stale-cache PageNotFoundError on untouched `/bff/*` routes). ESLint is not installed in this repo (config imports `eslint` but the package is absent) — `tsc + build` is the real gate.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Connect/disconnect endpoints placed in `ClinicsController` (not a new `WhatsAppOnboardingController`) | Spec explicitly allowed either; reuses the existing reminder-settings home + `IConfiguration` pattern. Route still `api/clinics/whatsapp/connect`. |
| Backend `/connect` failure messages are in **French** | The approved spec's API Contract pins these exact French strings (AC-3). Differs from the repo's usual English-backend / FE-localizes convention — followed the spec (the contract for this feature). |
| Success returns `Ok(result)` (Result wrapper) but failure returns `HandleFailure` (`{ error }`) | Success mirrors `reminderSettingsApi` unwrap; failure uses the canonical `{ error }` contract so the FE client throws `ApiError` with the French message. |
| `Meta` config section added to base `appsettings.json` only, not `appsettings.Development.json` | Feature is Cloud-only; the Development file is Local (`Auth:Mode=Local`) where the endpoints 404. Avoids touching an already-modified file. |
| Registration PIN generated (`RandomNumberGenerator`) and not persisted | P1 scope; PIN persistence/re-register is out of scope (P2+). |

## Significant Deviations
**DEV-1 — EF migration hand-authored (tooling blocked).**
`dotnet ef migrations add` failed: the API app is running (locks the API `bin`, so the startup-project build fails) and Smart App Control blocks freshly-built design-time DLLs (`0x800711C7`, see memory). Per the skill's WDAC fallback, the additive migration was hand-authored as 3 files: `20260721160000_AddWhatsAppConnectionFields.cs` (Up: 4 `AddColumn`; `WhatsAppConnectionStatus` NOT NULL default 0 for existing rows; Down: 4 `DropColumn`), its `.Designer.cs` (mechanically derived from the updated snapshot — header transformed, `BuildModel`→`BuildTargetModel`, `[Migration]` attribute), and the 4 new props added to `ApplicationDbContextModelSnapshot.cs` (alphabetical). All three compile (verified via the scratch-OutDir build). **Action before merge:** regenerate/verify this migration with the EF tool in an unrestricted environment.
_Approved: pre-approved by the skill's WDAC-blocked fallback (environmental, not a code fault)._

**DEV-2 — Live Meta round-trip not exercised (external Phase-0 dependency).**
The connect flow needs Meta business verification + App Review + a `config_id` (external, days–weeks) to run end-to-end — surfaced and confirmed with the user at spec time. The Graph calls are structured behind `IWhatsAppOnboardingService` (real HTTP, categorized errors) so the code compiles and is mockable; no live round-trip was performed.
_Approved: user forced the small pipeline / P1 slice knowing this (spec Assumptions section)._

## Deferred to /test-small-feature
- `ConnectClinicWhatsAppCommandHandler`: happy path (exchange→subscribe→register→store, status Connected, token encrypted) + atomicity (each step failure → nothing persisted, distinct French message) using a mocked `IWhatsAppOnboardingService`.
- `DisconnectClinicWhatsAppCommandHandler`: clears connection; unsubscribe failure swallowed; idempotent when not connected.
- `WhatsAppOnboardingService.ClassifyGraphError` mapping (mocked `HttpMessageHandler`): already-registered / not-eligible / default-per-step.
- `ClinicReminderSettings.ApplyWhatsAppConnection` / `ClearWhatsAppConnection` domain unit tests.
- `ReminderSettingsMappings` mapping of the 4 new fields (status → enum name).
- (No Postman/Newman per user preference; FE has no test harness — coverage notes only.)
