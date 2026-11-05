# Context — public demo cabinet (pointers, not contents)

**Last verified:** 2026-11-03 @ `66ecc657` + stories 1–5 working tree (uncommitted).
**Staleness check:** `git diff --stat 66ecc657..HEAD -- api/ClinicManagement.Infrastructure/Deployment api/ClinicManagement.API/Program.cs api/ClinicManagement.API/Controllers/AuthController.cs web/lib/demo web/lib/auth web/app/demo`

## Where things live
| Question | Pointer |
|---|---|
| The demo's capabilities | `api/ClinicManagement.Infrastructure/Deployment/DeploymentProfile.cs` (`PublicDemo` arm in `For()`) |
| Demo seam for Application | `Application/Common/Interfaces/IDemoPolicy.cs` → `Infrastructure/Services/DemoPolicy.cs` |
| Refusal codes + sentences | `Application/Features/Demo/DemoRefusals.cs` |
| Which cabinet a visitor enters | `Application/Features/Demo/DemoCabinetLocator.cs` |
| Fixed identity of the cabinet | `Application/Features/Demo/DemoScenario.cs` |
| Session mint | `Application/Features/Auth/Commands/StartDemoSessionCommand.cs` (precedent: `TradeRelayAssertionCommand.cs`) |
| 2 h cap | `RefreshTokenCommand.cs` (demo block) + `LocalAuthService.GenerateRefreshToken(…, notAfterUtc)` |
| Build the cabinet | `API/BackgroundJobs/DemoCabinetRebuilder.cs` (enqueued in `Program.cs`, search `IssuesDemoSessions`) |
| The scenario | `Application/Features/Demo/DemoCabinetBuilder.cs` (part A) + `DemoCabinetBuilder.Treatments.cs` (part B, same partial class) + `DemoScenario.cs` (team, hours) + `DemoScenarioPatients.cs`; assets `Features/Demo/Assets/*.png` (embedded) |
| Act as a member | `Application/Features/Demo/IDemoSeedContext.cs` → `API/BackgroundJobs/DemoSeedContext.cs` (one scope per command: journal author is first-read-wins per scope) |
| Builder tests | `UnitTests/Features/Demo/DemoCabinetBuilderTests.cs` (recording seed; odd-day anchors; no raw clock) |
| Read-only gate precedent | `API/Middleware/SubscriptionGateMiddleware.cs` + `UnitTests/Api/SubscriptionExemptionCoverageTests.cs` |
| Web demo flag | `web/lib/demo/demo-context.tsx` (`useDemo`, closed doors) · `web/lib/demo/demo-cookie.ts` |
| Entry page / BFF | `web/app/demo/page.tsx` · `web/app/bff/auth/demo-session/route.ts` |
| Read-only gate | `API/Middleware/DemoReadOnlyMiddleware.cs` + `[AllowedInDemo]`/`[RefusedInDemo]` (`Application/Common/Authorization/`) + `UnitTests/Api/DemoExemptionCoverageTests.cs` |
| Web refusal | `web/lib/demo/demo-refusal.ts` (sentence, signup URL) → `client.ts` swap → `ui/form-error-banner.tsx` (+ `DemoRefusalNotice`) + `lib/errors.ts`; pre-request refusal `web/lib/demo/refuse-in-demo.ts`; band `web/components/demo/demo-band.tsx` |
| QA driver (story 4) | `qa/walk-4a.mjs` opens a patient by id via the API (rows are not links; list is paged) |
| Demo build vs jobs | `API/BackgroundJobs/DemoCabinetBuilds` — a cabinet being rebuilt is skipped by `AppointmentProgressJob`; any new job that advances past visits must ask it too |
| Demo PDFs | `Infrastructure/Services/SpecimenStamp.cs` (the mark) + `SpecimenPdfGenerationService.cs` (wrap, chosen in `Extensions.cs`); limit `API/Startup/RateLimiting.cs` `DemoPdfPolicy` + `DemoSessionPartitionKey` (reads the bearer: the limiter runs before auth) |
| Nightly rebuild | order `API/BackgroundJobs/DemoCabinetRebuild.cs` (steps interface + verdict); real steps `DemoCabinetRebuilder.cs` (`Run`); which cabinets go `Application/Features/Demo/DemoCabinetRetirement.cs`; delete `DemoCabinetPurge.cs` (console path); `demoBuiltOn` = `DemoCabinetBuilds.CurrentBuiltOn` → `GET /api/auth/mode` |
| Rebuild on demand (local) | restart the API with `Demo__RebuildOnStart=true` (the start script has it); `scratchpad/demo6.sh` holds the real-stack helpers |
| Demo beside production | overlay `deploy/docker-compose.demo.yml`; site `deploy/demo/Caddyfile.demo` (mounted only by the overlay, imported by `deploy/Caddyfile`); runbook `deploy/demo/README.md`; deploy wiring `.github/workflows/deploy-hosted.yml` (search `DEMO_DOMAIN`); guard `UnitTests/Api/DemoDeployWiringTests.cs` |
| QA driver (story 5) | `qa/walk-5.mjs` — a PDF is stamped when its `/Keywords` is the mark; eye pass renders page 1 with pdf.js from cdnjs |
| QA driver (part C) | `qa/walk-4c.mjs` — patient tabs addressed by `[role=tabpanel][id$="-content-<value>"]` (the page nests tabs) |
| QA driver | `features/demo-cabinet/qa/walk.mjs` (story 1), `qa/walk-2.mjs` (story 2) — need `playwright-core` in the scratchpad; story 1's B4 needs `Demo__SessionMinutes=2` |

## Gates that work here
- API tests: `dotnet build api/ClinicManagement.UnitTests/... -c Release -p:BaseOutputPath=<temp>\` then `dotnet vstest <temp>\Release\net8.0\ClinicManagement.UnitTests.dll` (~25 s).
- Web: `cd web && npm run check:responsive && npx tsc --noEmit && npm run build` — stop `next dev` on the worktree first.
- Isolated stack: DB `clinic_demo_local` (shared postgres container `clinic-postgres`); API `env … Deployment__Profile=PublicDemo Hosting__Urls=http://localhost:5099 Console__Port=0 FrontendUrl=http://localhost:3099 ConnectionStrings__DefaultConnection=… DataProtection__KeyRingPath=%TEMP%\demo-keyring MinIO__BucketName=clinic-demo-local dotnet run --no-launch-profile` from `api/ClinicManagement.API`; web `.env.local` → `:5099`, `npx next dev -p 3099`.

- Money / schema on the demo DB: run the built API exe (`$TEMP/demo-run/Debug/net8.0/ClinicManagement.API.exe`) with the start script's env and the verb as argument (`verify-schema`, `reconcile-money`). Locally `verify-schema` reports one expected drift, `key-ring-protection` (no certificate on a dev box).

## Gates that do NOT exist in this repo
- No web test runner, no working ESLint. No unit test touches a database.

## ⚠️ Volatile
- `site/` Umami listener lives on `feature/security-remediation` (`587ad9ea`), not on `main` — story 8 depends on it.
- `FreeSlotFinderTests` date bomb fixed in this branch only (DEV-3).
