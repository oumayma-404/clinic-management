using System.Globalization;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure;
using ClinicManagement.API.Startup;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// What the five <c>subscription-*</c> vendor verbs share: the container they run in, how they name a cabinet, and
/// how they print a date (<c>clinic-subscription</c> Part F, FR-6).
///
/// <para><b>⚠️ The tenant-scope declaration is deliberately NOT here.</b> Each verb declares its own —
/// <c>UseClinic</c> for the four that act on one cabinet, <c>UseSystemWide</c> for the report — because
/// <c>SystemWideCallerCoverageTests</c> reads the declaration out of each <c>Maintenance/*Command.cs</c> file. A
/// declaration hidden in a helper would make all five look silent to the one guard that exists to catch a path
/// reading nothing and reporting success.</para>
///
/// <para><b>Gated on the connection string, never on the deployment profile</b> (amendment M3): these verbs run no
/// PostgreSQL binary, and the deployment they exist for above all is the hosted one, which has no local DB tooling.</para>
/// </summary>
internal static class SubscriptionVerbs
{
    /// <summary>Grant / cancel / suspend / unsuspend all report the same two outcomes.</summary>
    public const int Success = 0;
    public const int Failed = 1;

    /// <summary>Builds the verb's container: <c>AddInfrastructure</c> only, never <c>AddApplication</c>.</summary>
    /// <remarks>
    /// <c>AddApplication</c> would register the claims-reading <c>AuditActorProvider</c> over the process one, so a
    /// verb's writes would be attributed to a token that does not exist — and its <c>IClinicContext</c> needs an
    /// <c>IHttpContextAccessor</c> there is none of. The floor <c>ITenantScope</c> and
    /// <c>ICurrentClinicProvider</c> live in <c>AddInfrastructure</c> precisely so a verb can declare itself.
    /// </remarks>
    public static ServiceProvider BuildProvider(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Resolves the cabinet from <c>--clinic</c> (an id <i>or</i> an e-mail) and <c>--email</c>, through the same
    /// <see cref="SubscriptionCabinetLookup"/> the commands use — so a verb and its command cannot disagree about
    /// what identifies a practice.
    ///
    /// <para>⚠️ <b>Run before the tenant scope is declared, and that is safe for one specific reason:</b> the lookup
    /// touches <c>Clinics</c> and <c>Users</c> only, and both are deliberately unfiltered. It has to happen first —
    /// <c>ITenantScope</c> is single-assignment, so the id must be known before <c>UseClinic</c> can name it.</para>
    /// </summary>
    public static async Task<Guid?> ResolveCabinetAsync(
        string[] args, IServiceProvider scope, CancellationToken cancellationToken)
    {
        var supplied = ConsoleArgs.ReadOption(args, "--clinic");
        var email = ConsoleArgs.ReadOption(args, "--email");

        Guid? clinicId = null;
        if (Guid.TryParse(supplied, out var parsed))
        {
            clinicId = parsed;
        }
        else if (!string.IsNullOrWhiteSpace(supplied))
        {
            // « --clinic owner@cabinet.tn » is the form the plan's own usage line uses; refusing it because the
            // flag is called --clinic would be pedantry about our own vocabulary.
            email ??= supplied;
        }

        var result = await SubscriptionCabinetLookup.ResolveAsync(
            clinicId,
            email,
            scope.GetRequiredService<IClinicRepository>(),
            scope.GetRequiredService<IUserRepository>(),
            cancellationToken);

        if (result.IsSuccess)
        {
            return result.Value;
        }

        Console.Error.WriteLine(result.Error);
        return null;
    }

    /// <summary>
    /// Attributes the verb's writes in the audit ledger as <c>job|&lt;command&gt;</c> (FR-12).
    ///
    /// <para>The string is built by <see cref="AuditActor.Process"/> rather than by a local <c>$"job|…"</c>: that
    /// prefix has a named authority which also trims and substitutes « unknown », so a hardcoded copy is how the
    /// actor stamped on <c>RecordedBy</c> silently diverges from the one the audit interceptor writes for the
    /// same run.</para>
    /// </summary>
    public static string DeclareActor(IServiceProvider scope, string commandName)
    {
        scope.GetRequiredService<IAuditActorProvider>().RunAs(commandName);
        return AuditActor.Process(commandName).UserId;
    }

    /// <summary>
    /// Is this inclusive end day already behind the cabinet? <b>The clinic's day, never the server's</b> —
    /// <c>EndsOn</c> is a Tunisian calendar day, so comparing it against <c>DateTime.UtcNow.Date</c> printed or
    /// omitted the « date is in the past » warning against yesterday for the first hour of every clinic day. One
    /// helper so a third verb needing the question cannot get a third answer.
    /// </summary>
    public static bool IsInThePast(DateTime? endsOn) =>
        endsOn is { } day && day.Date < ClinicClock.ClinicToday().Date;

    /// <summary>Reads <c>--flag value</c> from a verb's arguments. See <see cref="ConsoleArgs.ReadOption"/>.</summary>
    public static string? ReadOption(string[] args, string flag) => ConsoleArgs.ReadOption(args, flag);

    /// <summary>An inclusive end day, or the words for having none — never a far-future date (AC-2.5).</summary>
    public static string Day(DateTime? day) =>
        day is { } value
            ? value.ToString(SubscriptionRefusals.DateFormat, CultureInfo.InvariantCulture)
            : "sans échéance";

    /// <summary>Reads <c>--flag N</c> as a positive whole number; null when absent, false when unusable.</summary>
    public static bool TryReadPositiveInt(string[] args, string flag, out int? value)
    {
        value = null;
        var raw = ConsoleArgs.ReadOption(args, flag);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            Console.Error.WriteLine($"'{raw}' n'est pas un nombre valide pour {flag} (entier positif attendu).");
            return false;
        }

        value = parsed;
        return true;
    }
}
