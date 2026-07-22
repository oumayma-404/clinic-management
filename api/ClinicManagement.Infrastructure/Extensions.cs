using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Auth;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Repositories;
using ClinicManagement.Infrastructure.Services;
using ClinicManagement.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using Minio;

namespace ClinicManagement.Infrastructure;

public static class Extensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Database
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString));

        // Unit of Work
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Repositories
        services.AddScoped<IPatientRepository, PatientRepository>();
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IStockItemRepository, StockItemRepository>();
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
        services.AddScoped<IClinicReminderSettingsRepository, ClinicReminderSettingsRepository>();
        services.AddScoped<ICnamCatalogRepository, CnamCatalogRepository>();
        services.AddScoped<IMedicationCatalogRepository, MedicationCatalogRepository>();
        services.AddScoped<IDentalActCodeRepository, DentalActCodeRepository>();
        services.AddScoped<IToothStateRepository, ToothStateRepository>();
        services.AddScoped<ITreatmentPlanRepository, TreatmentPlanRepository>();

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

        // Auth0 Management Service — real in Cloud mode, no-op in Local mode (no Auth0 tenant).
        if (LocalAuthConfig.IsLocalMode(configuration))
        {
            services.AddScoped<IAuth0ManagementService, NoOpAuth0ManagementService>();
        }
        else
        {
            services.AddScoped<IAuth0ManagementService, Auth0ManagementService>();
        }

        // File storage backend, selected by auth mode:
        //   Local (offline) → local disk (no MinIO); Cloud → MinIO (unchanged).
        if (LocalAuthConfig.IsLocalMode(configuration))
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

            if (!string.IsNullOrWhiteSpace(minioEndpoint) &&
                !string.IsNullOrWhiteSpace(minioAccessKey) &&
                !string.IsNullOrWhiteSpace(minioSecretKey))
            {
                services.AddSingleton<IMinioClient>(sp =>
                {
                    var minioClient = new MinioClient()
                        .WithEndpoint(minioEndpoint)
                        .WithCredentials(minioAccessKey, minioSecretKey);

                    if (minioUseSSL)
                    {
                        minioClient = minioClient.WithSSL();
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

        // Per-clinic reminder secrets are encrypted at rest via ASP.NET Core Data Protection. The key ring is
        // persisted to a mode-resolved directory so ciphertext survives restarts: Local → the gitignored
        // per-install .local/ (via LocalInstallPaths); Cloud → an optional configured directory
        // (DataProtection:KeyRingPath). If Cloud leaves it unset, keys use the framework default location
        // (single-instance only — a multi-instance Cloud deployment must configure a shared key ring; ops note).
        var dataProtection = services.AddDataProtection().SetApplicationName("ClinicManagement");
        var isLocalMode = LocalAuthConfig.IsLocalMode(configuration);
        var keyRingPath = isLocalMode
            ? Path.Combine(LocalInstallPaths.LocalDir, "dataprotection-keys")
            : configuration["DataProtection:KeyRingPath"];
        if (!string.IsNullOrWhiteSpace(keyRingPath))
        {
            Directory.CreateDirectory(keyRingPath);
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));

            // Encrypt the key ring itself at rest: supplying a custom key repository disables the framework's
            // automatic key-at-rest protection, which would leave the master keys (that encrypt every clinic's
            // credentials) in cleartext on disk. On the Local Windows install, protect them with machine-scoped
            // DPAPI so a stolen/copied key-ring folder is useless off the host. DPAPI is Windows-only; a Cloud
            // key ring at DataProtection:KeyRingPath relies on that directory's ACLs (ops responsibility).
            if (isLocalMode && OperatingSystem.IsWindows())
            {
                dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
            }
        }
        services.AddSingleton<IReminderSecretProtector, ReminderSecretProtector>();

        // SMS/WhatsApp appointment reminders (revives the dormant Notification outbox).
        //   - IReminderScheduler enqueues/voids reminder rows inline from the appointment command handlers
        //     (best-effort, post-commit). Config-aware, so it lives here rather than in Application.
        //   - IReminderSettingsProvider resolves the effective settings for a clinic (per-clinic override ??
        //     per-install RemindersConfig), consumed by the scheduler (channels) and dispatcher/senders.
        //   - The channel senders are registered as a set; the dispatcher (NotificationJob) routes each due
        //     row to the sender whose Channel matches the row's NotificationType.
        services.AddScoped<IReminderSettingsProvider, ReminderSettingsProvider>();
        services.AddScoped<IReminderScheduler, ReminderScheduler>();
        services.AddScoped<IReminderChannelSender, HttpSmsSender>();
        services.AddScoped<IReminderChannelSender, WhatsAppSender>();

        // WhatsApp Embedded-Signup onboarding (Cloud) — provisions a clinic's own WABA/phone via the Graph API.
        services.AddScoped<IWhatsAppOnboardingService, WhatsAppOnboardingService>();

        // NOTE: CertificateProvisioner is intentionally NOT DI-registered (Finding 17) — it is constructed
        // manually pre-Build in Program.cs (Kestrel needs the cert before the DI container exists), so a
        // singleton registration here was dead. Program.cs passes it a real (Serilog-backed) logger.

        // One-click backup (US-8 / FR-G): pg_dump + file-storage copy. Safe to register unconditionally —
        // only exercised by the admin-gated backup endpoint; on Cloud (no bundled pg_dump) a call fails
        // with a clear "pg_dump introuvable" error rather than doing anything.
        services.AddScoped<IBackupService, PgDumpBackupService>();

        // Google OAuth refresh-token store (US-3 / FR-E3): persists the token to a gitignored per-install
        // file instead of rewriting appsettings.json. Singleton so the in-memory cache provides the
        // immediate live-refresh-without-restart behavior the old in-place config set had. Safe to
        // register unconditionally — only exercised when Google Calendar is used.
        services.AddSingleton<IGoogleTokenStore, FileGoogleTokenStore>();

        // Google Calendar Service
        services.AddScoped<IGoogleCalendarService, GoogleCalendarService>();
        services.AddScoped<IGoogleCalendarSyncService, GoogleCalendarSyncService>();

        // PDF Generation Service
        services.AddScoped<IPdfGenerationService, PdfGenerationService>();

        // TTN « El Fatoora » electronic invoicing (feature facturation-einvoicing-ttn):
        //   - TEIF XML generation + XAdES/XMLDSig signing (cert from .local/) + QR cachet rendering.
        //   - ITtnClient registered as a set (sandbox + production); EInvoiceService picks the one matching
        //     the clinic's configured environment (mirrors the reminder-sender pattern).
        //   - EInvoiceService orchestrates the whole dispatch; the outbox job + submit command call it.
        services.AddScoped<ITeifXmlGenerator, TeifXmlGenerator>();
        services.AddScoped<IEInvoiceSigner, XadesEInvoiceSigner>();
        services.AddSingleton<IQrCodeGenerator, QrCodeGenerator>();
        services.AddScoped<ITtnClient, SandboxTtnClient>();
        services.AddScoped<ITtnClient, HttpTtnClient>();
        services.AddScoped<IEInvoiceService, EInvoiceService>();

        // Hugging Face AI Service
        services.AddScoped<IHuggingFaceAIService, HuggingFaceAIService>();

        // AI Action Service
        services.AddScoped<IAIActionService, AIActionService>();

        return services;
    }
}

