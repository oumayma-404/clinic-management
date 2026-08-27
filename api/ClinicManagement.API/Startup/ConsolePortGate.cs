using Microsoft.AspNetCore.Http;

namespace ClinicManagement.API.Startup;

/// <summary>
/// Makes « the console's routes answer on the console's port, and nothing else does » a property of the request
/// pipeline (<c>platform-console</c> FR-2, AC-1.7, risk R-3).
///
/// <para><b>Why this type has to exist at all.</b> A Kestrel listener is <b>not scoped to a subset of routes</b>:
/// every endpoint the application maps answers on <i>every</i> port it binds. So binding a second port for the
/// console publishes the entire clinic API on it, and leaves every console route answering on the public one —
/// the second half being the whole exposure FR-2 exists to prevent. The bind is not the boundary; this is.</para>
///
/// <para><b>⚠️ Two-way, where <see cref="TrustPortGate"/> is one-way, and the difference is deliberate.</b> The
/// trust page serves only public material, so it stays reachable on the normal front door as a convenience. A
/// console path on the public address is the highest-privilege surface in the product published to the internet,
/// so this refuses <i>both</i> directions: a console path anywhere but the console port, and anything but a
/// console path on it.</para>
///
/// <para><b>⚠️ The refusal keys on the real <c>Connection.LocalPort</c>, never on a header.</b> Behind the hosted
/// front door every request reaches the API from the same proxy, so a proxy-set header is the only other way to
/// tell the two apart — and that would make the spec's refusal a <i>configuration rule</i>, which is exactly what
/// FR-2 rejects. A port cannot be forged by a client.</para>
///
/// <para><b>⚠️ Off means every console path is refused, not that nothing is refused.</b> With
/// <paramref name="consolePort"/> at <c>0</c> — the capability off, or an operator who has not set
/// <c>Console:Port</c> — <c>/api/platform/*</c> must 404 everywhere rather than fall through to the public
/// listener. That asymmetry with <see cref="TrustPortGate"/>, whose <c>0</c> refuses nothing, is AC-1.8: the
/// console is <b>absent</b> when off, never present-and-refusing.</para>
///
/// <para>Kept as a static predicate over primitives so it is unit-testable without a host — the interesting cases
/// are boundary ones and each is a plain assertion.</para>
/// </summary>
public static class ConsolePortGate
{
    /// <summary>
    /// The route prefix the console's endpoints live under. Inside <c>/api</c> so it sits behind the same
    /// exception, rate-limiting and client-version middleware as everything else; the console's <b>pages</b> are a
    /// separate Next application and never reach this process.
    /// </summary>
    public const string ConsolePathPrefix = "/api/platform";

    /// <summary>
    /// True when the request must be refused outright, in either direction.
    /// </summary>
    /// <param name="localPort">The local port the connection was accepted on.</param>
    /// <param name="consolePort">The configured console port; <c>0</c> or less means the console is off.</param>
    /// <param name="path">The request path.</param>
    public static bool ShouldRefuse(int localPort, int consolePort, PathString path)
    {
        var isConsolePath = IsConsolePath(path);

        if (consolePort <= 0)
        {
            // Off ⇒ the console is absent. Clinic traffic is untouched; console paths exist nowhere.
            return isConsolePath;
        }

        return localPort == consolePort
            ? !isConsolePath      // on the console port: only console paths
            : isConsolePath;      // anywhere else: never a console path
    }

    /// <summary>
    /// ⚠️ <c>StartsWithSegments</c>, not <c>StartsWith</c>: <c>/api/platform-ish</c> shares the prefix as text
    /// while being a different endpoint, and letting it through would reopen the whole hole by typo — the same
    /// trap <see cref="TrustPortGate"/> already documents.
    /// </summary>
    public static bool IsConsolePath(PathString path) =>
        path.StartsWithSegments(ConsolePathPrefix, StringComparison.OrdinalIgnoreCase);
}
