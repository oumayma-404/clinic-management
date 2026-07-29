using System.Reflection;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using ClinicManagement.Application.Common.Behaviors;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Dashboard.Readers;

namespace ClinicManagement.Application;

public static class Extensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));

        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        // Real-time: broadcasts an "entityChanged" signal to the caller's clinic after any successful
        // mutating command, so connected clients refetch. Registered after the handler runs (innermost),
        // i.e. after commit. The IRealtimeNotifier impl is registered in the API layer (Program.cs).
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RealtimeBroadcastBehavior<,>));

        // Register ClinicContext
        // Note: IHttpContextAccessor must be registered in the API layer (Program.cs)
        services.AddScoped<IClinicContext, ClinicContext>();
        services.AddScoped<ICurrentClinicResolver, CurrentClinicResolver>();
        // Best-effort writer for the in-app staff notification feed (generated inline from command handlers).
        services.AddScoped<INotificationGenerator, NotificationGenerator>();
        // AC-P4.10 — draws an act's material list out of stock when its fiche is saved. Scoped and called
        // post-commit from the dental-record handlers, like the notification generator beside it.
        services.AddScoped<IStockConsumptionService, StockConsumptionService>();
        // Fire-and-forget, connectivity-gated Google Calendar sync for appointment create/update.
        services.AddScoped<IAppointmentGoogleSyncDispatcher, AppointmentGoogleSyncDispatcher>();
        // Backstop tenant scoping: feeds the EF Core global query filter with the caller's clinic id.
        // Inactive (null) when no clinic is in scope so background jobs / CLI / anonymous flows are unaffected.
        services.AddScoped<ICurrentClinicProvider, CurrentClinicProvider>();
        // Indicative CNAM reimbursable/out-of-pocket split for invoices + devis (caches the catalog per request).
        services.AddScoped<ICnamBillingCalculator, CnamBillingCalculator>();
        // Dashboard section readers. One per section rather than a single handler doing all of it, so a new KPI
        // touches one reader and one test class instead of a 25-field god-query. GetDashboardQueryHandler composes
        // them sequentially — they share the request's DbContext, which is not thread-safe.
        // DashboardPeriod / PeriodComparison are pure records and deliberately NOT registered.
        services.AddScoped<IDashboardActivityReader, DashboardActivityReader>();
        services.AddScoped<IDashboardMoneyReader, DashboardMoneyReader>();
        services.AddScoped<IDashboardAlertsReader, DashboardAlertsReader>();
        services.AddScoped<IDashboardTrendReader, DashboardTrendReader>();

        return services;
    }
}

