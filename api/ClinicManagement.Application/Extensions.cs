using System.Reflection;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using ClinicManagement.Application.Common.Behaviors;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;

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

        return services;
    }
}

