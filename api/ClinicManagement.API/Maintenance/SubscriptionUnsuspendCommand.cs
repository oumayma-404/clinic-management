using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Subscriptions.Commands;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.API.Startup;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// Lifts a suspension, clearing its whole trail (FR-7).
///
/// <code>
///   ClinicManagement.API.exe subscription-unsuspend --clinic &lt;id|email&gt;
/// </code>
///
/// <para><b>⚠️ Lifting a suspension grants no time.</b> The cabinet then stands on its own end date, which may
/// itself already be in the past — so a cabinet suspended while lapsed is still read-only afterwards and needs a
/// <c>subscription-grant</c> as well. The output says so rather than leaving the operator to discover it.</para>
///
/// <para>No motif: an unsuspension has nothing to explain to the practice, which simply stops being told it is
/// suspended. Why it was lifted is the vendor's own record, and the audit ledger carries who and when.</para>
/// </summary>
public static class SubscriptionUnsuspendCommand
{
    public const string CommandName = "subscription-unsuspend";

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        try
        {
            var configuration = InstallConfiguration.BuildForConsoleVerb();
            if (!MaintenanceDatabase.HasConnectionString(configuration, "Lifting a cabinet's suspension"))
            {
                return SubscriptionVerbs.Failed;
            }

            await using var provider = SubscriptionVerbs.BuildProvider(configuration);
            using var scope = provider.CreateScope();

            var actor = SubscriptionVerbs.DeclareActor(scope.ServiceProvider, CommandName);

            var clinicId = await SubscriptionVerbs.ResolveCabinetAsync(args, scope.ServiceProvider, cancellationToken);
            if (clinicId is null)
            {
                Console.Error.WriteLine("Usage: subscription-unsuspend --clinic <identifiant|email>");
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
                    Suspend = false,
                    ActedBy = actor,
                },
                cancellationToken);

            if (result.IsFailure)
            {
                Console.Error.WriteLine($"Unsuspension failed: {result.Error}");
                return SubscriptionVerbs.Failed;
            }

            var lifted = result.Value!;
            Console.WriteLine();
            Console.WriteLine("Suspension lifted.");
            Console.WriteLine($"  Clinic id:      {lifted.ClinicId}");
            Console.WriteLine($"  End date:       {SubscriptionVerbs.Day(lifted.EndsOn)}");

            if (lifted.EndsOn is { } endsOn && endsOn.Date < DateTime.UtcNow.Date)
            {
                Console.WriteLine();
                Console.WriteLine("⚠️  That date is in the past, so this cabinet is still read-only — lifting a");
                Console.WriteLine("    suspension grants no time. Record a payment with subscription-grant.");
            }

            Console.WriteLine();
            return SubscriptionVerbs.Success;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unsuspension failed: {ex.Message}");
            return SubscriptionVerbs.Failed;
        }
    }
}
