using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Repositories;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Logging;

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

        // Services
        var fileStoragePath = configuration["FileStorage:BasePath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "Files");
        services.AddSingleton<IFileStorageService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<LocalFileStorageService>>();
            return new LocalFileStorageService(fileStoragePath, logger);
        });

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

        return services;
    }
}

