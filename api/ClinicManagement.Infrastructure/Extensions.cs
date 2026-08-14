using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Infrastructure.Auth;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Repositories;
using ClinicManagement.Infrastructure.Security;
using ClinicManagement.Infrastructure.Services;
using ClinicManagement.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using Minio;

namespace ClinicManagement.Infrastructure;

public static class Extensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // ⚠️ **The console verbs have no host builder, so nothing else registers this.** Five Infrastructure
        // types take `IConfiguration` in their constructor (`LocalAuthService`, `LocalAuthConfig`,
        // `ConnectivityConfig`, `Auth0ManagementService`), and a verb that builds a bare
        // `new ServiceCollection()` + `AddInfrastructure(configuration)` could not activate any of them:
        // `provision-clinic` died on « Unable to resolve service for type
        // 'Microsoft.Extensions.Configuration.IConfiguration' while attempting to activate 'LocalAuthService' ».
        //
        // That broke the **only** two doors a hosted deployment has — `provision-clinic` is how a clinic is
        // created where self-registration is closed and `/setup` is loopback-gated, and `reset-admin-password`
        // is the documented recovery for a locked-out admin. Registering it here rather than in each verb is
        // deliberate: four verbs building their own container is exactly where a per-call-site fix rots.
        //
        // `TryAdd`, so the API host's own registration keeps winning and this is a no-op there.
        services.TryAddSingleton(configuration);

        // Resolved once: every branch below asks a named capability of it rather than re-reading Auth:Mode.
        var profile = DeploymentProfile.Resolve(configuration);

        // Registered so a service can ask a capability at request time. It is immutable and derived from
        // configuration read at startup, so a singleton is the honest lifetime — and the alternative (each
        // consumer calling Resolve again) would re-parse the key and could throw from inside a request.
        services.AddSingleton(profile);

        // Which peers' X-Forwarded-For may be believed. Same lifetime reasoning as the profile, and a singleton so
        // the login-lockout tracker gets the deployment's real answer rather than the loopback-only default.
        services.AddSingleton(TrustedProxies.FromConfiguration(configuration));

        // Database
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        // The audit ledger's writer (I6). Scoped, because its actor and its collected rows belong to one request.
        //
        // ⚠️ It is resolved from the provider inside `AddDbContext` rather than registered with
        // `AddInterceptors(...)` at configuration time: the overload that takes instances would need a singleton,
        // and this interceptor holds per-request state (the actor, and the rows collected between the two save
        // phases). The `IServiceProvider` overload of `AddDbContext` gives it the *scope's* provider, so each
        // request's context observes that request's interceptor.
        //
        // The actor seam gets a **floor** here, not a registration: `AddApplication` runs first in `Program.cs` and
        // registers the real claims-reading `AuditActorProvider`, so `TryAdd` is a no-op in the API. It matters for
        // the console verbs, which build their container from this method alone — see `ProcessAuditActorProvider`.
        services.TryAddScoped<IAuditActorProvider, ProcessAuditActorProvider>();

        // Whose rows this scope may read (multi-tenant-cloud US-2). Registered here rather than in
        // `AddApplication` for one reason: the console verbs build their container from *this* method alone, and
        // the filters now refuse an unset scope — so without these two a verb would read nothing at all instead
        // of declaring itself cross-clinic. `ICurrentClinicProvider` gets the same **floor** treatment as the
        // audit actor above (`AddApplication` runs first in `Program.cs`, so TryAdd is a no-op in the API).
        services.AddScoped<ITenantScope, TenantScope>();
        services.TryAddScoped<ICurrentClinicProvider, CurrentClinicProvider>();

        // Built through an explicit factory rather than `AddScoped<T>()` so its dependencies are resolved
        // explicitly. The clinic provider used to be resolved with `GetService` because a console verb had none;
        // the floor above means every container that can produce a DbContext can produce one, so a missing
        // registration is now a loud startup failure rather than a silent null on a non-nullable parameter.
        services.AddScoped(provider => new AuditSaveChangesInterceptor(
            provider.GetRequiredService<IAuditActorProvider>(),
            provider.GetRequiredService<ICurrentClinicProvider>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ILogger<AuditSaveChangesInterceptor>>()));

        // The audit chain's key (hosted-security-hardening FR-4.1). A **singleton** because it is immutable and
        // resolved from startup configuration, the same lifetime reasoning as the deployment profile it reads —
        // and because resolving it per request would re-read (or, on a clinic's own PC, re-generate) a file on
        // every save. Registered here rather than in `AddApplication` so the console verbs, whose container is
        // this method alone, can write the ledger too.
        //
        // ⚠️ Registered as a FACTORY, not an instance, and the difference matters twice. `AddInfrastructure` is
        // called by the console verbs and by several test fixtures, so constructing it here would make a missing
        // key throw while the container is being *built* — surfacing as an unrelated resolution failure rather
        // than as the operator sentence it carries. A missing key must still be a **startup** failure and not a
        // 500 on whichever clinical save happens to be first, so `Program.cs` resolves it once at startup
        // (beside TransportAssurance) and the refusal lands there, loud and named.
        services.AddSingleton<IAuditChainKeyProvider>(_ => new AuditChainKeyProvider(configuration));

        services.AddDbContext<ApplicationDbContext>((provider, options) =>
            options
                .UseNpgsql(connectionString)
                .AddInterceptors(provider.GetRequiredService<AuditSaveChangesInterceptor>()));

        // Unit of Work
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Repositories
        services.AddScoped<IPatientRepository, PatientRepository>();
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IStockItemRepository, StockItemRepository>();
        services.AddScoped<IStockMovementRepository, StockMovementRepository>();
        services.AddScoped<IProcedureTypeRepository, ProcedureTypeRepository>();
        services.AddScoped<IDentalRecordRepository, DentalRecordRepository>();
        services.AddScoped<IPatientFolderRepository, PatientFolderRepository>();
        services.AddScoped<IPatientFileRepository, PatientFileRepository>();
        services.AddScoped<IMedicalDocumentRepository, MedicalDocumentRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IClinicRepository, ClinicRepository>();
        services.AddScoped<IDoctorRepository, DoctorRepository>();
        services.AddScoped<IStaffNotificationRepository, StaffNotificationRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<ICreditNoteRepository, CreditNoteRepository>();
        services.AddScoped<IClinicReminderSettingsRepository, ClinicReminderSettingsRepository>();
        services.AddScoped<IDocumentEmailRepository, DocumentEmailRepository>();
        services.AddScoped<IUserDashboardPreferenceRepository, UserDashboardPreferenceRepository>();
        services.AddScoped<ICnamCatalogRepository, CnamCatalogRepository>();
        services.AddScoped<IMedicationCatalogRepository, MedicationCatalogRepository>();
        services.AddScoped<IDentalActCodeRepository, DentalActCodeRepository>();
        services.AddScoped<IToothStateRepository, ToothStateRepository>();
        services.AddScoped<ITreatmentPlanRepository, TreatmentPlanRepository>();
        // Clinical-workflow-depth repositories (caisse expenses, waiting list, dental-lab work orders).
        services.AddScoped<IExpenseRepository, ExpenseRepository>();
        services.AddScoped<IAuditEntryRepository, AuditEntryRepository>();
        // L4d — the backup ledger. Registered beside the other repositories rather than only for the job,
        // because « Paramètres » reads it too (« Dernière sauvegarde réussie ») and so does the history endpoint.
        services.AddScoped<IBackupRunRepository, BackupRunRepository>();
        services.AddScoped<IWaitingListRepository, WaitingListRepository>();
        services.AddScoped<ILabWorkOrderRepository, LabWorkOrderRepository>();
        services.AddScoped<IRecurringAppointmentRepository, RecurringAppointmentRepository>();
        // Part 6 — the OS-push registry and its outbox.
        services.AddScoped<IDeviceRegistrationRepository, DeviceRegistrationRepository>();
        services.AddScoped<IPushDeliveryRepository, PushDeliveryRepository>();
        // clinic-self-signup — pending signups. Registered unconditionally like every other repository; the
        // capability gate lives on the endpoints, not on whether the table can be read.
        services.AddScoped<IClinicSignupRepository, ClinicSignupRepository>();
        // clinic-subscription — the entitlement and its ledger. ⚠️ Registered by **AddInfrastructure** and not
        // AddApplication, and that is load-bearing rather than conventional: `provision-clinic` builds its
        // container from this method alone, and it creates a cabinet — which must not come into existence without
        // an entitlement (FR-4), so it has to be able to resolve this and the policy below.
        services.AddScoped<IClinicSubscriptionRepository, ClinicSubscriptionRepository>();
        services.AddScoped<ISessionFamilyRepository, SessionFamilyRepository>();
        // vendor-whatsapp-messaging-quota — the allocation ledger and the per-month counters. Here for the same
        // load-bearing reason as the line above: `provision-clinic` creates a cabinet from a container built out of
        // this method alone, and a cabinet must not exist without an allowance (FR-3).
        services.AddScoped<IMessagingAllowanceRepository, MessagingAllowanceRepository>();

        // HttpClient for Auth0 Management API
        services.AddHttpClient();

        // Connectivity probe (Local-mode offline UX). Registered unconditionally — harmless in Cloud
        // (the ConnectivityController 404s there and the frontend never polls). Singleton + shared
        // IMemoryCache so N polling clients collapse to one outbound probe per TTL window (R-1).
        services.AddMemoryCache();
        services.AddSingleton<IInternetProbe, InternetProbe>();

        // File storage base path (used by the Local-mode disk backend below). Resolved against the install
        // directory (R-6) so it is stable whether launched from a console or as a Windows service.
        var fileStoragePath = LocalInstallPaths.Resolve(configuration["FileStorage:BasePath"] ?? "Files");

        // Local (offline) authentication service. Harmless in Cloud mode (only used by /api/auth/login).
        services.AddScoped<ILocalAuthService, LocalAuthService>();

        // Per-(account, source) failed-login tracking (security-hardening US-4 / AC-4.2). Backed by the
        // shared IMemoryCache registered above, so lockout state is transient by design — see the class.
        services.AddScoped<ILoginAttemptTracker, LoginAttemptTracker>();

        // The vendor's console identity population (platform-console Part 1). Registered UNCONDITIONALLY, not
        // behind ServesPlatformConsole: `platform-account` is a console verb that builds its container from this
        // method alone, and an operator has to be able to bootstrap the first account on a deployment whose
        // listener is not bound yet. Nothing here is reachable without that listener — ConsolePortGate 404s every
        // console route — so registering the services costs a few objects and buys a working verb.
        services.AddScoped<IPlatformAccountRepository, PlatformAccountRepository>();
        services.AddScoped<IPlatformAuthService, PlatformAuthService>();
        services.AddSingleton<ITotpService, TotpService>();
        // Singleton like IReminderSecretProtector, and for the same reason: an IDataProtector is thread-safe and
        // deriving one per request would re-run the key derivation on every sign-in.
        services.AddSingleton<IPlatformSecretProtector, PlatformSecretProtector>();
        // The clinic half of the same seam, with its own purpose string so a clinic ciphertext and a console
        // one are not interchangeable. Registered here — inside AddInfrastructure — so the `reset-user-totp`
        // verb, whose container is this method alone, can resolve it.
        services.AddSingleton<IUserSecretProtector, UserSecretProtector>();
        // The third sibling (FR-3.4): the Google Calendar refresh token, the last credential this database held
        // in the clear. Registered here for the same reason — `reprotect-secrets` resolves it from this method.
        services.AddSingleton<IGoogleTokenProtector, GoogleTokenProtector>();
        // ⚠️ SINGLETON, and the lifetime is load-bearing: a step-up confirmation is minted by one request and
        // consumed by another, so a scoped registration builds a fresh store per request, the confirmation is
        // never found, and EVERY guarded action refuses with a French « mot de passe incorrect » that is not
        // incorrect — silently. See IStepUpConfirmations' own note.
        services.AddSingleton<IStepUpConfirmations, StepUpConfirmations>();

        // The console's activity counters (platform-console Part 2). Registered unconditionally for the reason
        // above: the counter job runs on any deployment and its rows cost nothing where no console reads them,
        // whereas a job registered behind a capability would leave a deployment that later switches the console on
        // with a portfolio of « jamais mesuré » and no history to backfill from.
        services.AddScoped<IClinicActivityRepository, ClinicActivityRepository>();

        // The console's access ledger (platform-console Part 3). Unconditional for the same reason: the console
        // controllers are unreachable without the listener, and a ledger registered behind a capability would fail
        // to resolve on precisely the deployment where somebody switched the console on.
        services.AddScoped<IPlatformAccessEntryRepository, PlatformAccessEntryRepository>();

        // Auth0 Management Service — real where Auth0 owns identity, no-op where the product does (no Auth0 tenant).
        if (profile.UsesLocalAccounts)
        {
            services.AddScoped<IAuth0ManagementService, NoOpAuth0ManagementService>();
        }
        else
        {
            services.AddScoped<IAuth0ManagementService, Auth0ManagementService>();
        }

        // File storage backend: the clinic's own disk (no MinIO) where the deployment stores blobs locally,
        // MinIO everywhere else (unchanged).
        if (profile.UsesDiskStorage)
        {
            services.AddScoped<IFileStorage>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<LocalDiskFileStorage>>();
                return new LocalDiskFileStorage(fileStoragePath, logger);
            });
        }
        else
        {
            var minioEndpoint = configuration["MinIO:Endpoint"];
            var minioAccessKey = configuration["MinIO:AccessKey"];
            var minioSecretKey = configuration["MinIO:SecretKey"];
            var minioBucketName = configuration["MinIO:BucketName"] ?? "clinic-files";
            var minioUseSSL = configuration.GetValue<bool>("MinIO:UseSSL", false);

            // "Configured" means present AND not the published default — see MinioCredentials. Previously a
            // mere non-empty check, so a Cloud deploy that forgot its env vars authenticated with
            // minioadmin/minioadmin instead of failing loud (audit § 2, finding 11).
            var minioConfigured = MinioCredentials.IsConfigured(minioEndpoint, minioAccessKey, minioSecretKey);

            if (!minioConfigured)
            {
                var environmentName = configuration["ASPNETCORE_ENVIRONMENT"];

                if (!MinioCredentials.TolerateUnconfigured(environmentName))
                {
                    // Fail loud at startup, consistent with how an empty DB connection string is treated.
                    // Running on default credentials is the defect, not a warning.
                    throw new InvalidOperationException(
                        MinioCredentials.NotConfiguredMessage(minioAccessKey, minioSecretKey));
                }

                // Development only: the tracked appsettings.json is Cloud mode, docker-compose runs MinIO as
                // minioadmin, and Development.json has no override — so failing here would break `dotnet run`
                // on a fresh clone for every developer (AC-10.5). Warn once and carry on.
                Console.Error.WriteLine(
                    "[warn] MinIO is using default or missing credentials. Acceptable in Development only — "
                    + "a non-Development environment will refuse to start. "
                    + MinioCredentials.NotConfiguredMessage(minioAccessKey, minioSecretKey));
            }

            if (!string.IsNullOrWhiteSpace(minioEndpoint) &&
                !string.IsNullOrWhiteSpace(minioAccessKey) &&
                !string.IsNullOrWhiteSpace(minioSecretKey))
            {
                // The internal root the object store's leaf is verified against (hosted-security-hardening
                // Part 2, FR-2.2). Absent on SelfHostedLan and in dev, where MinIO:UseSSL is false anyway.
                var minioRootCertificate = configuration[InternalCertificate.MinioRootCertificateKey];

                services.AddSingleton<IMinioClient>(sp =>
                {
                    var minioClient = new MinioClient()
                        .WithEndpoint(minioEndpoint)
                        .WithCredentials(minioAccessKey, minioSecretKey);

                    if (minioUseSSL)
                    {
                        minioClient = minioClient.WithSSL();

                        // A CA minted for this deployment is in no system trust store, so without this the
                        // first upload fails validation. The handler VERIFIES against that root — it does not
                        // skip verification, which is why a name mismatch still refuses.
                        var trustedRoot = InternalCertificate.TryLoad(minioRootCertificate);
                        if (trustedRoot is not null)
                        {
                            minioClient = minioClient.WithHttpClient(
                                InternalRootTrust.CreateHttpClient(trustedRoot), disposeHttpClient: true);
                        }
                    }

                    return minioClient.Build();
                });

                services.AddScoped<IFileStorage>(sp =>
                {
                    var minioClient = sp.GetRequiredService<IMinioClient>();
                    var logger = sp.GetRequiredService<ILogger<MinioFileStorage>>();
                    return new MinioFileStorage(minioClient, minioBucketName, logger);
                });
            }
            else
            {
                // Cloud mode requires MinIO to be configured.
                services.AddScoped<IFileStorage>(sp =>
                    throw new InvalidOperationException("MinIO is not configured. Please set MinIO:Endpoint, MinIO:AccessKey, and MinIO:SecretKey in configuration."));
            }
        }

        // Per-clinic reminder secrets — and the Local install's DB credentials file — are encrypted at rest
        // via ASP.NET Core Data Protection. The key-ring configuration lives in ONE place
        // (LocalDataProtection) because the Local console verbs must configure the identical key ring from
        // outside this DI container; two definitions could drift and leave the installer writing ciphertext
        // the API cannot read. See LocalDataProtection for the mode-resolved path and at-rest protection.
        LocalDataProtection.AddConfiguredDataProtection(services, configuration);
        services.AddSingleton<IReminderSecretProtector, ReminderSecretProtector>();

        // SMS/WhatsApp appointment reminders (revives the dormant Notification outbox).
        //   - IReminderScheduler enqueues/voids reminder rows inline from the appointment command handlers
        //     (best-effort, post-commit). Config-aware, so it lives here rather than in Application.
        //   - IReminderSettingsProvider resolves the effective settings for a clinic (per-clinic override ??
        //     per-install RemindersConfig), consumed by the scheduler (channels) and dispatcher/senders.
        //   - The channel senders are registered as a set; the dispatcher (NotificationJob) routes each due
        //     row to the sender whose Channel matches the row's NotificationType.
        services.AddScoped<IReminderSettingsProvider, ReminderSettingsProvider>();

        // Whether a clinic may aim an integration endpoint at a private address. Singleton for the same reason
        // the profile it reads is one: immutable, derived from startup configuration.
        services.AddSingleton<IOutboundEndpointPolicy, OutboundEndpointPolicy>();

        // clinic-subscription — the two seams that carry a deployment fact and an operator setting into
        // Application, which references Domain alone and so cannot name DeploymentProfile. Singletons for the same
        // reason as the profile: both are derived from startup configuration and immutable.
        services.AddSingleton<ISubscriptionPolicy, SubscriptionPolicy>();
        // Same lifetime reasoning as the profile it reads: immutable and derived from startup configuration.
        services.AddSingleton<ISecondFactorPolicy, SecondFactorPolicy>();
        services.AddSingleton<ISubscriptionPricing, SubscriptionPricing>();

        // vendor-whatsapp-messaging-quota — the same two kinds of seam, for the same structural reason (Application
        // references Domain alone and cannot name DeploymentProfile), and registered here rather than in
        // AddApplication for the reason above it: `provision-clinic` builds its container from *this* method alone
        // and it creates a cabinet, which must not come into existence without an allowance (FR-3).
        services.AddSingleton<IVendorMessagingAvailability, VendorMessagingAvailability>();
        services.AddSingleton<IMessagingAllowancePolicy, MessagingAllowancePolicy>();
        services.AddScoped<IReminderScheduler, ReminderScheduler>();
        services.AddScoped<IReminderChannelSender, HttpSmsSender>();
        services.AddScoped<IReminderChannelSender, WhatsAppSender>();

        // OS push (mobile-native-shells Part 6).
        //   - IOsPushAvailability is the single « can this installation push to this platform? » — the AND of the
        //     profile's Kind-derived PermitsOsPush and the per-install credentials. Asked by the registration
        //     endpoint, the fan-out, the dispatcher and the settings surface, so all four agree.
        //   - The senders are a set, routed by platform, exactly as the reminder channels are by channel.
        //   - The fan-out DECORATES INotificationGenerator, which AddApplication registered before this method
        //     ran: one hook reaches every notification category rather than twelve edited call sites. The inner
        //     instance is built explicitly because there is no decoration helper in this solution (no Scrutor),
        //     and an explicit factory is clearer here than adding a package for one registration.
        services.AddSingleton<IOsPushAvailability, OsPushAvailability>();
        services.AddScoped<IPushSender, FcmPushSender>();
        services.AddScoped<IPushSender, ApnsPushSender>();
        services.AddScoped<INotificationGenerator>(provider => new PushNotificationGeneratorDecorator(
            new NotificationGenerator(
                provider.GetRequiredService<IStaffNotificationRepository>(),
                provider.GetRequiredService<IDoctorRepository>(),
                provider.GetRequiredService<IUnitOfWork>(),
                provider.GetRequiredService<IRealtimeNotifier>(),
                provider.GetRequiredService<ILogger<NotificationGenerator>>()),
            provider.GetRequiredService<IUserRepository>(),
            provider.GetRequiredService<IDoctorRepository>(),
            provider.GetRequiredService<IDeviceRegistrationRepository>(),
            provider.GetRequiredService<IPushDeliveryRepository>(),
            provider.GetRequiredService<IUnitOfWork>(),
            provider.GetRequiredService<IOsPushAvailability>(),
            configuration,
            provider.GetRequiredService<ILogger<PushNotificationGeneratorDecorator>>()));

        // Outbound document emails — the SMTP sender for the document-email outbox (DocumentEmailJob). It reads
        // its host/credentials/from-identity from the same IReminderSettingsProvider the two message channels
        // use, so a clinic configures every outbound channel in one place.
        services.AddScoped<IDocumentEmailSender, SmtpDocumentEmailSender>();

        // clinic-self-signup — the first email path in the product bound to NO clinic. It reads the per-install
        // `Notification:Smtp:*` section directly, and must keep doing so: its one caller runs before any clinic
        // exists, so there is nothing for IReminderSettingsProvider to resolve against.
        services.AddScoped<ITransactionalEmailSender, SmtpTransactionalEmailSender>();

        // Where a link in that email has to point. Reads FrontendUrl, the key the Google OAuth redirect already
        // uses, so an emailed link and a redirected browser cannot arrive at different hosts.
        services.AddSingleton<IPublicAppUrlProvider, PublicAppUrlProvider>();

        // WhatsApp Embedded-Signup onboarding (Cloud) — provisions a clinic's own WABA/phone via the Graph API.
        services.AddScoped<IWhatsAppOnboardingService, WhatsAppOnboardingService>();

        // The template this product submits on a cabinet's behalf, and the poll that reads its state back (FR-7,
        // FR-7a). Registered unconditionally like its onboarding sibling: with no Meta credentials the call fails
        // and is logged, and the cabinet stays « en attente de validation » rather than the container failing.
        services.AddScoped<IWhatsAppTemplateService, WhatsAppTemplateService>();

        // NOTE: CertificateProvisioner is intentionally NOT DI-registered (Finding 17) — it is constructed
        // manually pre-Build in Program.cs (Kestrel needs the cert before the DI container exists), so a
        // singleton registration here was dead. Program.cs passes it a real (Serilog-backed) logger.

        // One-click backup (US-8 / FR-G): pg_dump + file-storage copy. Safe to register unconditionally —
        // only exercised by the admin-gated backup endpoint; on Cloud (no bundled pg_dump) a call fails
        // with a clear "pg_dump introuvable" error rather than doing anything.
        // Directory-permission policy (security-hardening). Stateless, so a singleton. Shared by the Local
        // `harden-permissions` console verb and the backup service, which must not diverge — a backup that
        // is not access-restricted hands out an unprotected copy of everything the install protects.
        services.AddSingleton<DirectoryAclHardener>();
        services.AddScoped<IBackupService, PgDumpBackupService>();

        // The per-clinic archive (clinic-data-archive-and-restore) — what `pg_dump` cannot be on a shared
        // database, since it takes --dbname and has no tenant predicate. Scoped, because it shares the request's
        // DbContext: the restore stages rows the caller's own IUnitOfWork commits, so a second context would put
        // the rows and their audit rows in two different transactions.
        //
        // ⚠️ Registered HERE and not in AddApplication, alongside the tenant scope and the subscription seams
        // above, for the same reason they are: it resolves the EF model, which only this assembly can see.
        services.AddScoped<IClinicArchiveStore, ClinicArchiveStore>();

        // Per-clinic reference-catalog seeder (feature cloud-security-and-tenant-isolation, #5): clones the
        // shared default CNAM/medication/dental-act catalogs into each clinic on creation + a startup backfill.
        services.AddScoped<IClinicCatalogSeeder, ClinicCatalogSeeder>();
        services.AddScoped<IClinicAdminBackfill, ClinicAdminBackfill>();

        // Google Calendar refresh tokens are now stored PER CLINIC on the Clinic entity (feature
        // cloud-security-and-tenant-isolation, #4) — the former global file/singleton token store
        // (IGoogleTokenStore/FileGoogleTokenStore) was retired, so there is no shared account across clinics.

        // Google Calendar Service
        services.AddScoped<IGoogleCalendarService, GoogleCalendarService>();
        services.AddScoped<IGoogleCalendarSyncService, GoogleCalendarSyncService>();

        // PDF Generation Service
        services.AddScoped<IPdfGenerationService, PdfGenerationService>();

        // The QR renderer's live caller is TrustController, which renders the LAN trust page's QR from it.
        services.AddSingleton<IQrCodeGenerator, QrCodeGenerator>();

        return services;
    }
}

