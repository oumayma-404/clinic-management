using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Application.Common.Interfaces;
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

        // HttpClient for Auth0 Management API
        services.AddHttpClient();

        // Services
        var fileStoragePath = configuration["FileStorage:BasePath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "Files");
        services.AddSingleton<IFileStorageService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<LocalFileStorageService>>();
            return new LocalFileStorageService(fileStoragePath, logger);
        });

        // Auth0 Management Service
        services.AddScoped<IAuth0ManagementService, Auth0ManagementService>();

        // MinIO File Storage
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
            // Fallback to local file storage if MinIO is not configured
            services.AddScoped<IFileStorage>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<MinioFileStorage>>();
                throw new InvalidOperationException("MinIO is not configured. Please set MinIO:Endpoint, MinIO:AccessKey, and MinIO:SecretKey in configuration.");
            });
        }

        services.AddScoped<INotificationService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<NotificationService>>();
            return new NotificationService(
                logger,
                smtpServer: configuration["Notification:Smtp:Server"],
                smtpPort: configuration.GetValue<int?>("Notification:Smtp:Port"),
                smtpUsername: configuration["Notification:Smtp:Username"],
                smtpPassword: configuration["Notification:Smtp:Password"],
                smsApiKey: configuration["Notification:Sms:ApiKey"],
                smsApiUrl: configuration["Notification:Sms:ApiUrl"]);
        });

        // Domain Services
        services.AddScoped<Domain.Services.IPatientSummaryService, PatientSummaryService>();

        // Google Calendar Service
        services.AddScoped<IGoogleCalendarService, GoogleCalendarService>();
        services.AddScoped<IGoogleCalendarSyncService, GoogleCalendarSyncService>();

        // PDF Generation Service
        services.AddScoped<IPdfGenerationService, PdfGenerationService>();

        // Hugging Face AI Service
        services.AddScoped<IHuggingFaceAIService, HuggingFaceAIService>();

        // AI Action Service
        services.AddScoped<IAIActionService, AIActionService>();

        return services;
    }
}

