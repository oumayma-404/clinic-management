using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace ClinicManagement.API.Startup;

/// <summary>What <c>Program.cs</c> must bind when the console is on: <b>both</b> ports, in one call.</summary>
public record ConsoleListenerPlan(int PublicPort, int ConsolePort);

/// <summary>
/// Resolves the two ports the hosted deployment listens on when the console is enabled, and <b>refuses a
/// collision</b> (<c>platform-console</c> EC-4, risk R-3a).
///
/// <para><b>⚠️ Binding the console listener can take the whole product offline, and that is why this type
/// exists.</b> In <c>HostedMultiTenant</c> there is no certificate file, so <c>Program.cs</c> takes the
/// <c>else</c> branch and <b>never calls <c>ConfigureKestrel</c> at all</b>: the only thing binding port 5000 is
/// <c>ASPNETCORE_URLS</c> from the compose file. Kestrel's explicit endpoints <b>override the URLs configuration
/// wholesale</b>, so a bare <c>ConfigureKestrel(k =&gt; k.ListenAnyIP(consolePort))</c> would unbind 5000, Caddy's
/// <c>/api/*</c> → <c>api:5000</c> would stop resolving, and the entire product would go dark while the console
/// itself worked perfectly. The failure is one line in Kestrel's log (« Overriding address(es)… ») and silent
/// everywhere an operator looks. So the public port is resolved here and bound in the <i>same</i> call.</para>
///
/// <para><b>⚠️ The collision check is derived from the ports actually resolved for binding</b>, never from
/// <c>Hosting:HttpPort</c> / <c>HttpsPort</c> / <c>WebPort</c>. None of those three keys is set in the hosted
/// compose file, so a check written against them passes cheerfully while the two listeners genuinely collide —
/// an EC-4 guard that cannot fire in the one profile the console exists on.</para>
///
/// <para>Pure and static over primitives, so every case above is a plain assertion in
/// <c>ConsoleListenerPlanTests</c> rather than something only a running host could show.</para>
/// </summary>
// The resolver is a separate static class from the record above because C# cannot give a record static members
// under the same name; both are part of one contract and share this file deliberately.
public static class ConsoleListenerPlanning
{
    /// <summary>The port a hosted deployment answers on when nothing says otherwise.</summary>
    public const int DefaultPublicPort = 5000;

    /// <summary>
    /// The public port this deployment is <i>already</i> answering on, in the order the host itself would honour:
    /// <c>Hosting:Urls</c> → <c>ASPNETCORE_URLS</c> → <c>Hosting:HttpPort</c> → <see cref="DefaultPublicPort"/>.
    ///
    /// <para>The first two are semicolon-separated URL lists; the first parsable port wins, which is what the
    /// host does with them too. A value nobody can parse falls through to the next source rather than throwing —
    /// this runs before anything is bound, and a malformed <c>ASPNETCORE_URLS</c> must not be the thing that stops
    /// a deployment starting when the console is merely being switched on.</para>
    /// </summary>
    public static int ResolvePublicPort(IConfiguration configuration)
    {
        foreach (var key in new[] { "Hosting:Urls", "ASPNETCORE_URLS" })
        {
            var port = FirstPortIn(configuration[key]);
            if (port is > 0)
            {
                return port.Value;
            }
        }

        var configured = configuration.GetValue<int?>("Hosting:HttpPort");
        return configured is > 0 ? configured.Value : DefaultPublicPort;
    }

    /// <summary>
    /// The plan, or <c>null</c> when the console is off (<paramref name="consolePort"/> at <c>0</c> or the
    /// capability ✗) — in which case <c>Program.cs</c> must touch none of the Kestrel configuration, so the other
    /// two profiles keep their current behaviour byte for byte.
    /// </summary>
    /// <exception cref="InvalidOperationException">The two ports collide (EC-4).</exception>
    public static ConsoleListenerPlan? Resolve(IConfiguration configuration, bool consoleEnabled, int consolePort)
    {
        if (!consoleEnabled || consolePort <= 0)
        {
            return null;
        }

        var publicPort = ResolvePublicPort(configuration);

        if (publicPort == consolePort)
        {
            throw new InvalidOperationException(
                $"Console:Port ({consolePort}) doit être un port distinct du port public de l'API ({publicPort}). "
                + "Le port public provient de Hosting:Urls, sinon de ASPNETCORE_URLS, sinon de Hosting:HttpPort, "
                + $"sinon {DefaultPublicPort} par défaut. Choisissez un autre Console:Port, ou mettez-le à 0 pour "
                + "désactiver la console éditeur.");
        }

        return new ConsoleListenerPlan(publicPort, consolePort);
    }

    /// <summary>The first parsable port in a semicolon-separated URL list, or null.</summary>
    private static int? FirstPortIn(string? urls)
    {
        if (string.IsNullOrWhiteSpace(urls))
        {
            return null;
        }

        foreach (var candidate in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Uri cannot parse the wildcard hosts Kestrel accepts (http://+:5000, http://*:5000), and those are
            // exactly what the hosted compose file sets — so the port is read off the last colon instead.
            var lastColon = candidate.LastIndexOf(':');
            if (lastColon < 0 || lastColon == candidate.Length - 1)
            {
                continue;
            }

            var tail = candidate[(lastColon + 1)..].TrimEnd('/');
            if (int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) && port > 0)
            {
                return port;
            }
        }

        return null;
    }
}
