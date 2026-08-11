using System.Globalization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Subscriptions.Commands;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.API.Startup;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// Records a received payment against one cabinet and extends its entitlement (<c>clinic-subscription</c> US-5).
///
/// <code>
///   ClinicManagement.API.exe subscription-grant --clinic owner@cabinet.tn --months 12 \
///       [--plan Cabinet|Clinique|SurMesure] [--amount 1200.000] [--method Transfer|Cash|Cheque|Card] \
///       [--reference VIR-2026-0413] [--note "..."] [--complimentary]
///   ClinicManagement.API.exe subscription-grant --clinic &lt;id&gt; --days 15
///   ClinicManagement.API.exe subscription-grant --clinic &lt;id&gt; --until 2027-09-20
/// </code>
///
/// <para><b>Why a verb and not an endpoint.</b> FR-6: a cabinet able to extend its own entitlement over HTTP would
/// not have one. There is no controller anywhere in this feature that references the three vendor commands, and
/// <c>SubscriptionVendorCommandReachabilityTests</c> holds that.</para>
///
/// <para>Hosted invocation is
/// <c>docker exec clinic-api-prod dotnet ClinicManagement.API.dll subscription-grant …</c> — the container's
/// environment is inherited, so <c>AddInstallLayers()</c> resolves the same connection string as the running app.</para>
/// </summary>
public static class SubscriptionGrantCommand
{
    public const string CommandName = "subscription-grant";

    private const string Usage =
        "Usage: subscription-grant --clinic <identifiant|email> (--months <mois> | --days <jours> | "
        + "--until <AAAA-MM-JJ>) [--plan Cabinet|Clinique|SurMesure] [--amount <montant>] "
        + "[--method Transfer|Cash|Cheque|Card] [--reference <ref>] [--note <note>] [--complimentary]";

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        try
        {
            var configuration = InstallConfiguration.BuildForConsoleVerb();
            if (!MaintenanceDatabase.HasConnectionString(configuration, "Recording a subscription payment"))
            {
                return SubscriptionVerbs.Failed;
            }

            if (!SubscriptionVerbs.TryReadPositiveInt(args, "--months", out var months)
                || !SubscriptionVerbs.TryReadPositiveInt(args, "--days", out var days))
            {
                return SubscriptionVerbs.Failed;
            }

            if (!TryReadDay(args, "--until", out var until)
                || !TryReadAmount(args, out var amount)
                || !TryReadEnum<SubscriptionPlan>(args, "--plan", out var plan)
                || !TryReadEnum<SubscriptionPaymentMethod>(args, "--method", out var method))
            {
                return SubscriptionVerbs.Failed;
            }

            if (months is null && days is null && until is null)
            {
                Console.Error.WriteLine(Usage);
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

            // US-2: one cabinet, so UseClinic rather than UseSystemWide — SystemWide switches the query-filter
            // backstop off for the whole scope, and a grant is the narrowest work there is. The lookup above is what
            // makes naming the id possible here; ITenantScope is single-assignment, so this cannot be re-declared.
            scope.ServiceProvider.GetRequiredService<ITenantScope>().UseClinic(clinicId.Value);

            var handler = new GrantSubscriptionPeriodCommandHandler(
                scope.ServiceProvider.GetRequiredService<IClinicSubscriptionRepository>(),
                scope.ServiceProvider.GetRequiredService<IClinicRepository>(),
                scope.ServiceProvider.GetRequiredService<IUserRepository>(),
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
                scope.ServiceProvider
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger<GrantSubscriptionPeriodCommandHandler>());

            var result = await handler.Handle(
                new GrantSubscriptionPeriodCommand
                {
                    ClinicId = clinicId,
                    Kind = args.Contains("--complimentary", StringComparer.OrdinalIgnoreCase)
                        ? SubscriptionPeriodKind.Complimentary
                        : SubscriptionPeriodKind.Paid,
                    DurationMonths = months,
                    DurationDays = days,
                    ExplicitEndsOn = until,
                    Plan = plan,
                    Amount = amount,
                    Method = method,
                    Reference = SubscriptionVerbs.ReadOption(args, "--reference"),
                    Note = SubscriptionVerbs.ReadOption(args, "--note"),
                    RecordedBy = actor,
                },
                cancellationToken);

            if (result.IsFailure)
            {
                Console.Error.WriteLine($"Subscription grant failed: {result.Error}");
                return SubscriptionVerbs.Failed;
            }

            var granted = result.Value!;
            Console.WriteLine();
            Console.WriteLine("Subscription period recorded.");
            Console.WriteLine($"  Clinic id:      {granted.ClinicId}");
            Console.WriteLine($"  Period id:      {granted.EntryId}   (use this with subscription-cancel)");
            Console.WriteLine($"  Previous end:   {SubscriptionVerbs.Day(granted.PreviousEndsOn)}");
            Console.WriteLine($"  New end:        {SubscriptionVerbs.Day(granted.EndsOn)}");

            // A grant may only ever extend cover (the fold enforces it), so a date that did not move forward means
            // --until named a day the cabinet was already covered past. Said out loud rather than left to be read
            // off two dates: the operator typed a date and nothing happened, which reads as a silent success.
            if (granted.PreviousEndsOn is { } before && granted.EndsOn is { } after && after <= before)
            {
                Console.WriteLine();
                Console.WriteLine("⚠️  The end date did not move: this cabinet was already covered to that day or");
                Console.WriteLine("    beyond. Cover is never shortened by a grant — use subscription-cancel to void");
                Console.WriteLine("    a period, or grant a duration rather than a --until date.");
            }

            Console.WriteLine();
            Console.WriteLine("The cabinet's own app picks this up on its next subscription re-read (a few minutes");
            Console.WriteLine("at most) — nobody needs to sign out or restart. Its expiry notifications are");
            Console.WriteLine("withdrawn by the daily pass.");
            Console.WriteLine();
            return SubscriptionVerbs.Success;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Subscription grant failed: {ex.Message}");
            return SubscriptionVerbs.Failed;
        }
    }

    /// <summary>A bare calendar day. Parsed exactly, so a locale never decides what 03/04 means.</summary>
    private static bool TryReadDay(string[] args, string flag, out DateTime? value)
    {
        value = null;
        var raw = SubscriptionVerbs.ReadOption(args, flag);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (!DateTime.TryParseExact(
                raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            Console.Error.WriteLine($"'{raw}' n'est pas une date valide pour {flag} (format AAAA-MM-JJ attendu).");
            return false;
        }

        value = parsed.Date;
        return true;
    }

    /// <summary>Money, in the invariant culture: a command line is not localised and « 120.500 » is not 120500.</summary>
    private static bool TryReadAmount(string[] args, out decimal? value)
    {
        value = null;
        var raw = SubscriptionVerbs.ReadOption(args, "--amount");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
        {
            Console.Error.WriteLine($"'{raw}' n'est pas un montant valide (nombre positif attendu, ex. 1200.000).");
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryReadEnum<T>(string[] args, string flag, out T? value) where T : struct, Enum
    {
        value = null;
        var raw = SubscriptionVerbs.ReadOption(args, flag);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (!Enum.TryParse<T>(raw, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
        {
            Console.Error.WriteLine(
                $"'{raw}' n'est pas une valeur valide pour {flag}. Valeurs possibles : "
                + string.Join(" | ", Enum.GetNames<T>()));
            return false;
        }

        value = parsed;
        return true;
    }
}
