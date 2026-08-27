using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Subscriptions.Commands;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.API.Startup;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// Voids one ledger entry with a written reason, correcting a mis-keyed grant (AC-5.5, EC-4).
///
/// <code>
///   ClinicManagement.API.exe subscription-cancel --clinic &lt;id|email&gt; --entry &lt;id de la période&gt; \
///       --reason "Paiement enregistré sur le mauvais cabinet"
/// </code>
///
/// <para>The period id is printed by <c>subscription-grant</c> when it records one, and listed by
/// <c>subscription-report --clinic &lt;id|email&gt;</c> for everything recorded before that.</para>
///
/// <para><b>⚠️ The end date can move into the past</b>, at which point the cabinet is read-only again — which is the
/// correct outcome when the grant was never theirs, and the reason the motif is mandatory rather than polite.</para>
/// </summary>
public static class SubscriptionCancelCommand
{
    public const string CommandName = "subscription-cancel";

    private const string Usage =
        "Usage: subscription-cancel --clinic <identifiant|email> --entry <identifiant de la période> "
        + "--reason <motif>";

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        try
        {
            var entryValue = SubscriptionVerbs.ReadOption(args, "--entry");
            var reason = SubscriptionVerbs.ReadOption(args, "--reason");

            if (!Guid.TryParse(entryValue, out var entryId) || string.IsNullOrWhiteSpace(reason))
            {
                Console.Error.WriteLine(Usage);
                return SubscriptionVerbs.Failed;
            }

            var configuration = InstallConfiguration.BuildForConsoleVerb();
            if (!MaintenanceDatabase.HasConnectionString(configuration, "Cancelling a subscription period"))
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

            var handler = new CancelSubscriptionPeriodCommandHandler(
                scope.ServiceProvider.GetRequiredService<IClinicSubscriptionRepository>(),
                scope.ServiceProvider.GetRequiredService<IClinicRepository>(),
                scope.ServiceProvider.GetRequiredService<IUserRepository>(),
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
                scope.ServiceProvider
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger<CancelSubscriptionPeriodCommandHandler>());

            var result = await handler.Handle(
                new CancelSubscriptionPeriodCommand
                {
                    ClinicId = clinicId,
                    EntryId = entryId,
                    Reason = reason,
                    CancelledBy = actor,
                },
                cancellationToken);

            if (result.IsFailure)
            {
                Console.Error.WriteLine($"Subscription cancellation failed: {result.Error}");
                return SubscriptionVerbs.Failed;
            }

            var cancelled = result.Value!;
            Console.WriteLine();
            Console.WriteLine("Subscription period cancelled. The row is kept and shown struck through.");
            Console.WriteLine($"  Clinic id:      {cancelled.ClinicId}");
            Console.WriteLine($"  Period id:      {cancelled.EntryId}");
            Console.WriteLine($"  Previous end:   {SubscriptionVerbs.Day(cancelled.PreviousEndsOn)}");
            Console.WriteLine($"  New end:        {SubscriptionVerbs.Day(cancelled.EndsOn)}");

            if (SubscriptionVerbs.IsInThePast(cancelled.EndsOn))
            {
                Console.WriteLine();
                Console.WriteLine("⚠️  This date is in the past: the cabinet can still read and export everything,");
                Console.WriteLine("    but can no longer record new work until it is granted more time.");
            }

            Console.WriteLine();
            return SubscriptionVerbs.Success;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Subscription cancellation failed: {ex.Message}");
            return SubscriptionVerbs.Failed;
        }
    }
}
