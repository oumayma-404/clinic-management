using ClinicManagement.API.Startup;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Messaging.Commands;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// Records a cabinet's WhatsApp reminder forfait from a terminal
/// (<c>vendor-whatsapp-messaging-quota</c> US-9, AC-9.1).
///
/// <code>
///   ClinicManagement.API.exe messaging-grant --clinic owner@cabinet.tn --per-month 500 \
///       [--amount 45.000] [--method Transfer|Cash|Cheque|Card] [--reference VIR-2026-0413] [--note "..."]
///   ClinicManagement.API.exe messaging-grant --clinic &lt;id&gt; --top-up 300 --month 2026-08
/// </code>
///
/// <para><b>Why a verb and not an endpoint.</b> AC-9.3: a practice able to raise its own forfait does not have one, so
/// no controller anywhere references <c>GrantMessagingAllowanceCommand</c> and
/// <c>MessagingVendorCommandReachabilityTests</c> holds that in both directions — including that this branch is actually
/// dispatched by <c>Program.cs</c>, since a missing one boots the <b>web host</b> and reads as « the command did
/// nothing ».</para>
///
/// <para><b>⚠️ It produces the same record, the same journal attribution and the same refusals as the console</b>
/// (AC-9.2) because it runs the same command over the same shared pieces — <c>SubscriptionCabinetLookup</c>,
/// <c>MessagingAllowancePlan</c>, <c>MessagingAllowanceRefold</c>. What it deliberately does <b>not</b> produce is a
/// <c>PlatformAccessEntry</c>: that ledger records what a <i>console account</i> did, and a terminal run has none — its
/// attribution is <c>job|messaging-grant</c> in the cabinet's own audit ledger, exactly as the five
/// <c>subscription-*</c> verbs are.</para>
///
/// <para>⚠️ <b>There is no <c>--month</c> for a standing forfait, deliberately</b> (AC-6.4a): the server decides, from
/// the figure already in force — immediately for a raise, next month for a lowering. Offering the flag would be offering
/// a way to cut a practice off mid-afternoon.</para>
///
/// <para>Hosted invocation is
/// <c>docker exec clinic-api-prod dotnet ClinicManagement.API.dll messaging-grant …</c> — the container's environment is
/// inherited, so <c>AddInstallLayers()</c> resolves the same connection string as the running app.</para>
/// </summary>
public static class MessagingGrantCommand
{
    public const string CommandName = "messaging-grant";

    private const string Usage =
        "Usage: messaging-grant --clinic <identifiant|email> (--per-month <nombre> | --top-up <nombre> "
        + "--month <AAAA-MM>) [--amount <montant>] [--method Transfer|Cash|Cheque|Card] [--reference <ref>] "
        + "[--note <note>]";

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        try
        {
            var configuration = InstallConfiguration.BuildForConsoleVerb();
            if (!MaintenanceDatabase.HasConnectionString(configuration, "Recording a WhatsApp reminder allowance"))
            {
                return SubscriptionVerbs.Failed;
            }

            if (!MessagingVerbs.TryReadCount(args, "--per-month", out var perMonth)
                || !MessagingVerbs.TryReadCount(args, "--top-up", out var topUp)
                || !MessagingVerbs.TryReadAmount(args, out var amount)
                || !TryReadMethod(args, out var method))
            {
                return SubscriptionVerbs.Failed;
            }

            if (perMonth is null && topUp is null)
            {
                Console.Error.WriteLine(Usage);
                return SubscriptionVerbs.Failed;
            }

            // Read raw rather than defaulted to the current month: the handler refuses a month on a standing forfait
            // (AC-6.4a), and silently supplying one here would turn that refusal into a confusing success.
            var month = SubscriptionVerbs.ReadOption(args, "--month");

            await using var provider = SubscriptionVerbs.BuildProvider(configuration);
            using var scope = provider.CreateScope();

            var actor = SubscriptionVerbs.DeclareActor(scope.ServiceProvider, CommandName);

            var clinicId = await SubscriptionVerbs.ResolveCabinetAsync(args, scope.ServiceProvider, cancellationToken);
            if (clinicId is null)
            {
                return SubscriptionVerbs.Failed;
            }

            // US-2: one cabinet, so UseClinic rather than UseSystemWide — the narrowest scope for the narrowest work.
            // The lookup above is what makes naming the id possible here; ITenantScope is single-assignment.
            scope.ServiceProvider.GetRequiredService<ITenantScope>().UseClinic(clinicId.Value);

            var handler = new GrantMessagingAllowanceCommandHandler(
                scope.ServiceProvider.GetRequiredService<IMessagingAllowanceRepository>(),
                scope.ServiceProvider.GetRequiredService<IClinicRepository>(),
                scope.ServiceProvider.GetRequiredService<IUserRepository>(),
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
                scope.ServiceProvider
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger<GrantMessagingAllowanceCommandHandler>());

            var result = await handler.Handle(
                new GrantMessagingAllowanceCommand
                {
                    ClinicId = clinicId,
                    MessagesPerMonth = perMonth,
                    TopUpMessages = topUp,
                    AppliesToMonth = month,
                    AmountDt = amount,
                    Method = method,
                    Reference = SubscriptionVerbs.ReadOption(args, "--reference"),
                    Note = SubscriptionVerbs.ReadOption(args, "--note"),
                    RecordedBy = actor,
                },
                cancellationToken);

            if (result.IsFailure)
            {
                Console.Error.WriteLine($"Messaging allowance grant failed: {result.Error}");
                return SubscriptionVerbs.Failed;
            }

            var granted = result.Value!;
            Console.WriteLine();
            Console.WriteLine("WhatsApp reminder allowance recorded.");
            Console.WriteLine($"  Clinic id:        {granted.ClinicId}");
            Console.WriteLine($"  Allocation id:    {granted.EntryId}   (use this with messaging-cancel)");
            Console.WriteLine($"  Kind:             {granted.Kind}  ({granted.Messages} reminders)");
            Console.WriteLine($"  Effective month:  {granted.EffectiveMonth}");
            Console.WriteLine($"  This month was:   {MessagingVerbs.Allowance(granted.PreviousAllowanceThisMonth)}");
            Console.WriteLine($"  This month is:    {MessagingVerbs.Allowance(granted.AllowanceThisMonth)}");

            // AC-6.4 stated out loud rather than left to be read off two identical numbers. A lowering is the one
            // outcome here that looks like nothing happened, and the operator typed a figure — so silence would read as
            // a failed command and invite a second, larger attempt.
            if (granted.Kind == MessagingAllowanceKind.Standing
                && !string.Equals(granted.EffectiveMonth, ClinicClock.CurrentMonthKey(), StringComparison.Ordinal))
            {
                Console.WriteLine();
                Console.WriteLine("ℹ️  This lowers the cabinet's forfait, so it takes effect next month "
                    + $"({granted.EffectiveMonth}) — this month's figure is unchanged on purpose. A practice is never");
                Console.WriteLine("    cut off mid-afternoon by a change it had no warning of. Raising a forfait, by");
                Console.WriteLine("    contrast, applies immediately and releases any held reminders within a minute.");
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("Held reminders are released on the dispatcher's next tick (a minute at most) — but");
                Console.WriteLine("only those whose appointment has not yet started; the rest fail as obsolete. The");
                Console.WriteLine("cabinet's own « Rappels » screen picks the new figure up on its next read.");
            }

            Console.WriteLine();
            return SubscriptionVerbs.Success;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Messaging allowance grant failed: {ex.Message}");
            return SubscriptionVerbs.Failed;
        }
    }

    /// <summary>
    /// The <b>vendor's</b> payment methods. An unknown value is refused rather than ignored: it is a fact being written
    /// into a ledger nobody can edit afterwards, unlike a filter where a stale value should narrow nothing.
    /// </summary>
    private static bool TryReadMethod(string[] args, out SubscriptionPaymentMethod? value)
    {
        value = null;
        var raw = SubscriptionVerbs.ReadOption(args, "--method");

        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (!Enum.TryParse<SubscriptionPaymentMethod>(raw.Trim(), ignoreCase: true, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            Console.Error.WriteLine(
                $"'{raw}' n'est pas une valeur valide pour --method. Valeurs possibles : "
                + string.Join(" | ", Enum.GetNames<SubscriptionPaymentMethod>()));
            return false;
        }

        value = parsed;
        return true;
    }
}
