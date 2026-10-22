# Codebase Context: PC de secours

**Feature:** [features/clinic-pc-copy/](../)
**Written:** 2026-10-07 at commit `b618a1d8` (worktree `.claude/worktrees/clinic-pc-copy`, branch `feature/clinic-pc-copy`)
**Last verified:** 2026-10-07 at commit `ee88ed66`
**Scope:** pointers only. ⚠️ rows are volatile.

## Staleness check

```bash
git diff --stat b618a1d8..HEAD -- api/ClinicManagement.Infrastructure/Deployment api/ClinicManagement.API/Program.cs \
  api/ClinicManagement.Infrastructure/Persistence/ApplicationDbContext.cs api/ClinicManagement.Infrastructure/Persistence/ClinicArchiveScope.cs \
  api/ClinicManagement.API/Controllers/BackupController.cs web/lib/api/client.ts web/components/clinic-settings.tsx \
  desktop/ClinicManagement.DesktopShell/MainWindow.xaml.cs packaging/setup/clinic-setup.iss
```

## Gate commands (verified)

| Gate | Command | Verified | Notes |
|------|---------|----------|-------|
| Backend build+tests | `cd api && dotnet test ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj -c Release -p:BaseOutputPath=<scratchpad>/bo/` | 2026-10-07 | baseline 4935 pass / 6 skip; cold build ~6 min, tests ~2 min. Out-of-repo output = SAC workaround |
| Frontend | `cd web && npm run check:responsive && npx tsc --noEmit && npm run build` | — | 77 checks; next free N-number is N50 |
| Console | `cd console && npx tsc --noEmit` + its own `scripts/check-responsive.mjs` | — | |
| Schema | `dotnet run -- verify-schema` (needs a DB) | — | exit 0/1/2 |

**Gates that do NOT exist:** no web test runner, no working ESLint, no DB in UnitTests, nothing runs the iOS shell.

## Where the authorities live

| Question | Pointer |
|---|---|
| Deployment kinds + capabilities | `api/ClinicManagement.Infrastructure/Deployment/DeploymentProfile.cs` · tests `UnitTests/Infrastructure/Deployment/DeploymentProfileTests.cs` |
| Other direct kind switches | `Infrastructure/Services/OutboundEndpointPolicy.cs`, `Infrastructure/Security/LocalDataProtection.cs` |
| Recurring jobs + middleware order + console verbs | `API/Program.cs` (jobs ~1162, middleware ~850–1077, verbs 23–222) |
| A write gate with a `{error, code}` body | `API/Middleware/SubscriptionGateMiddleware.cs` + `Application/Features/Subscriptions/SubscriptionRefusals.cs` |
| Realtime area keys | `Application/Common/Behaviors/RealtimeResourceResolver.cs` (`ExcludedAreas`) ↔ `web/lib/realtime/clinic-hub.ts` |
| Save path, audit chain, transactions | `Infrastructure/Persistence/ApplicationDbContext.cs` (`SaveChangesAsync`), `AuditChainAppender.cs`, `AuditSaveChangesInterceptor.cs` (`ExcludedEntityTypes`) |
| Model-derived per-clinic table plan | `Infrastructure/Persistence/ClinicArchiveScope.cs` (`Excluded`, `Redacted`, `BlobProperties`) |
| Device-grant credential pattern | `Domain/Entities/ClinicArchiveGrant.cs`, `Infrastructure/Auth/ArchiveGrantAuthorizer.cs`, `API/Controllers/BackupController.cs` (token exchange), `API/Startup/RateLimiting.cs`, `API/Authorization/ScopedTokenFilter.cs`, `Infrastructure/Auth/LocalAuthClaims.cs` |
| Token minting | `Infrastructure/Auth/LocalAuthService.cs` (`GenerateScopedToken`) |
| Secret protection | `Infrastructure/Security/UserSecretProtector.cs` |
| Bell | `Domain/Entities/StaffNotification.cs`, `Infrastructure/Repositories/StaffNotificationRepository.cs` (`VisibleQuery`, `UnreadQuery`), `Application/Common/Services/NotificationGenerator.cs`, `Infrastructure/Services/PushNotificationGeneratorDecorator.cs`, `Application/Common/Services/StaffNotificationRules.cs` |
| Step-up | `Application/Features/Auth/Commands/StepUpCommand.cs`, `Infrastructure/Security/StepUpConfirmations.cs`, controller use `ClinicsController.ShowMoney` / `BackupController.RequireStepUp`; web `components/security/step-up-dialog.tsx` |
| Custom journal rows | `Application/Features/Backup/ArchiveAccessLedger.cs` (pattern), labels `Application/Features/Audit/AuditLabels.cs` |
| Opening hours | `Application/Common/Services/WorkingHoursResolver.cs` |
| Console portfolio column | `Infrastructure/Repositories/ClinicActivityRepository.cs` (`PortfolioQuery`, `Projection`) → `PlatformClinicRow` → `PlatformClinicRowMapper` → `PlatformReadShape.AllowedLeafNames` → `console/components/clinic-portfolio.tsx` |
| Email | `Application/Common/Interfaces/ITransactionalEmailSender.cs` |
| Web settings cards / banner / client | `web/components/clinic-settings.tsx` (~1508), `web/components/backup/archive-grants-card.tsx`, `web/components/subscription/subscription-banner.tsx` (mounted `app-shell.tsx:102`), `web/lib/api/client.ts` (`ApiErrorCode`, `apiHeaders`) |
| Desktop shell bridge / nav failure / update | `desktop/ClinicManagement.DesktopShell/VaultBridge.cs`, `MainWindow.xaml.cs`, `ServerConfig.cs`, `ShellUpdater.cs`; contract `mobile/shared/bridge.md`, `web/types/clinic-shell.d.ts` |
| Android shell | `mobile/android/app/src/main/java/com/clinicmanagement/shell/MainActivity.kt`, `ServerConfig.kt`, `ShellBridge.kt` |
| Installer | `packaging/setup/clinic-setup.iss` (roles `InitializeWizard`, `WriteInstallConfig`, `CurStepChanged`) |
| CI | `.github/workflows/ci.yml` (`local-mode` job is the boot template) |

## ⚠️ Volatile

| Fact | State 2026-10-07 |
|---|---|
| Vendor alert email channel (`server-loss-recovery` Part 3) | uncommitted in sibling worktree `.claude/worktrees/server-loss-recovery` (`BackupHealthJob.cs`, `IPlatformAccountRepository.GetActiveEmailsAsync`) — not in this branch |
| Main checkout | ~230 dirty files from peers; nothing here depends on them |
