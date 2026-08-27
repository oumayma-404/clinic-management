using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Subscriptions.Commands;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.API.Startup;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// Suspends a cabinet with a mandatory written reason (FR-7).
///
/// <code>
///   ClinicManagement.API.exe subscription-suspend --clinic &lt;id|email&gt; --reason "Usage frauduleux signalé"
/// </code>
///
/// <para><b>⚠️ Suspension is for abuse or fraud — non-payment is not suspension.</b> A cabinet that has not paid
/// simply has no grant covering today, and that expresses itself as expiry. Suspending a late payer instead would
/// tell them « Suspendu » on a screen whose whole point is to explain what to do, and paying would not lift it.</para>
/// </summary>
public static class SubscriptionSuspendCommand
{
    public const string CommandName = "subscription-suspend";

    private const string Usage = "Usage: subscription-suspend --clinic <identifiant|email> --reason <motif>";

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        try
        {
            var reason = SubscriptionVerbs.ReadOption(args, "--reason");
            if (string.IsNullOrWhiteSpace(reason))
            {
                Console.Error.WriteLine(Usage);
                return SubscriptionVerbs.Failed;
            }

            var configuration = InstallConfiguration.BuildForConsoleVerb();
            if (!MaintenanceDatabase.HasConnectionString(configuration, "Suspending a cabinet"))
            {
                return SubscriptionVerbs.Failed;
            }

            await using var provider = SubscriptionVerbs.BuildProvider(configuration);
            using var scope = provider.CreateScope();

            var actor = SubscriptionVerbs.DeclareActor(scope.ServiceProvider, CommandName);

            var clinicId = await SubscriptionVerbs.ResolveCabinetAsync(args, scope.ServiceProvider, cancellationToken);
            if (clinicId is null)
            {
                return SubscriptionVerbs.Failed;
            }

            // US-2: one cabinet, so UseClinic. See SubscriptionGrantCommand for why the lookup precedes it.
            scope.ServiceProvider.GetRequiredService<ITenantScope>().UseClinic(clinicId.Value);

            var handler = new SetSubscriptionSuspensionCommandHandler(
                scope.ServiceProvider.GetRequiredService<IClinicSubscriptionRepository>(),
                scope.ServiceProvider.GetRequiredService<IClinicRepository>(),
                scope.ServiceProvider.GetRequiredService<IUserRepository>(),
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
                scope.ServiceProvider
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger<SetSubscriptionSuspensionCommandHandler>());

            var result = await handler.Handle(
                new SetSubscriptionSuspensionCommand
                {
                    ClinicId = clinicId,
                    Suspend = true,
                    Reason = reason,
                    ActedBy = actor,
                },
                cancellationToken);

            if (result.IsFailure)
            {
                Console.Error.WriteLine($"Suspension failed: {result.Error}");
                return SubscriptionVerbs.Failed;
            }

            Console.WriteLine();
            Console.WriteLine("Cabinet suspended. It can still read and export everything; it cannot record work.");
            Console.WriteLine($"  Clinic id:      {result.Value!.ClinicId}");
            Console.WriteLine($"  Motif:          {reason}");
            Console.WriteLine($"  End date:       {SubscriptionVerbs.Day(result.Value.EndsOn)}  (unchanged)");
            Console.WriteLine();
            Console.WriteLine("Its « Abonnement » screen reads « Suspendu » with this motif — never « Expiré », so");
            Console.WriteLine("nobody there pays for something a payment will not unblock.");
            Console.WriteLine();
            return SubscriptionVerbs.Success;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Suspension failed: {ex.Message}");
            return SubscriptionVerbs.Failed;
        }
    }
}
