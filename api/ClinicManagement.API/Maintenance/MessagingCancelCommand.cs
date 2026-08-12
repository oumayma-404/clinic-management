using ClinicManagement.API.Startup;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Messaging.Commands;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// Strikes out one allocation with a written motif, correcting a mis-keyed forfait
/// (<c>vendor-whatsapp-messaging-quota</c> US-7, AC-9.1).
///
/// <code>
///   ClinicManagement.API.exe messaging-cancel --clinic &lt;id|email&gt; --entry &lt;id de l'allocation&gt; \
///       --reason "Complément enregistré sur le mauvais cabinet"
/// </code>
///
/// <para>The allocation id is printed by <c>messaging-grant</c> when it records one, and listed by
/// <c>messaging-report --clinic &lt;id|email&gt;</c> for everything recorded before that — which is the only other place
/// in the product that prints one.</para>
///
/// <para><b>⚠️ Unlike <c>subscription-cancel</c>, this reaches the CURRENT month</b> (AC-7.4/7.4a). A cancellation says
/// the allocation should never have existed, so it applies to every month the entry fed — where a genuine <i>lowering</i>
/// waits for the next month (AC-6.4). That is why a mis-keyed « +3000 » is correctable in the month it was keyed into,
/// and why the motif is mandatory rather than polite: the cabinet's forfait can fall <b>below what it has already
/// spent</b>, at which point the month reads « épuisé » and reminders are held from that moment. Nothing is unsent and
/// nothing is clawed back — consumption is untouched.</para>
/// </summary>
public static class MessagingCancelCommand
{
    public const string CommandName = "messaging-cancel";

    private const string Usage =
        "Usage: messaging-cancel --clinic <identifiant|email> --entry <identifiant de l'allocation> --reason <motif>";

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
            if (!MaintenanceDatabase.HasConnectionString(configuration, "Cancelling a WhatsApp reminder allowance"))
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

            // US-2: one cabinet, so UseClinic. See MessagingGrantCommand for why the lookup precedes it.
            scope.ServiceProvider.GetRequiredService<ITenantScope>().UseClinic(clinicId.Value);

            var handler = new CancelMessagingAllowanceCommandHandler(
                scope.ServiceProvider.GetRequiredService<IMessagingAllowanceRepository>(),
                scope.ServiceProvider.GetRequiredService<IClinicRepository>(),
                scope.ServiceProvider.GetRequiredService<IUserRepository>(),
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
                scope.ServiceProvider
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger<CancelMessagingAllowanceCommandHandler>());

            var result = await handler.Handle(
                new CancelMessagingAllowanceCommand
                {
                    ClinicId = clinicId,
                    EntryId = entryId,
                    Reason = reason,
                    CancelledBy = actor,
                },
                cancellationToken);

            if (result.IsFailure)
            {
                Console.Error.WriteLine($"Messaging allowance cancellation failed: {result.Error}");
                return SubscriptionVerbs.Failed;
            }

            var cancelled = result.Value!;
            Console.WriteLine();
            Console.WriteLine("Allocation cancelled. The row is kept and shown struck through, with its motif.");
            Console.WriteLine($"  Clinic id:        {cancelled.ClinicId}");
            Console.WriteLine($"  Allocation id:    {cancelled.EntryId}");
            Console.WriteLine($"  This month was:   {MessagingVerbs.Allowance(cancelled.PreviousAllowanceThisMonth)}");
            Console.WriteLine($"  This month is:    {MessagingVerbs.Allowance(cancelled.AllowanceThisMonth)}");
            Console.WriteLine($"  Already sent:     {MessagingVerbs.Count(cancelled.ConsumedThisMonth)}  (untouched)");

            if (cancelled.ExhaustedThisMonth)
            {
                Console.WriteLine();
                Console.WriteLine("⚠️  This cabinet's forfait is now spent for the current month: its WhatsApp");
                Console.WriteLine("    reminders are held from now on, consuming nothing, and its « Rappels » screen");
                Console.WriteLine("    says so with your contact details. Nothing already sent was clawed back. Its");
                Console.WriteLine("    SMS reminders, its agenda and its records are unaffected.");
            }

            Console.WriteLine();
            return SubscriptionVerbs.Success;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Messaging allowance cancellation failed: {ex.Message}");
            return SubscriptionVerbs.Failed;
        }
    }
}
