using System;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// Settles which port a typed address means, when the user did not say.
///
/// <para>
/// The rule is identical in all three clients (desktop, Android, iOS) — see <c>mobile/CLAUDE.md</c> § « the port
/// rule ». An address with an explicit port is used verbatim and never probed. An address without one is tried
/// against <see cref="ServerConfig.CandidatePorts"/> in order, and the first port that **answers at all** wins.
/// </para>
///
/// <para>
/// ⚠️ « Answers » deliberately includes a TLS failure. An offline-LAN server presents a certificate signed by a CA
/// this PC may not have imported yet, so a handshake rejection is the *expected* outcome of probing a live clinic
/// server — treating it as "nothing here" would send every LAN install to the wrong port. What disqualifies a port
/// is a transport failure: no route, refused connection, timeout, or a name that does not resolve.
/// </para>
///
/// <para>
/// The mechanism is per-platform (here <see cref="HttpClient"/>, an <c>HttpURLConnection</c> on Android, a
/// <c>URLSession</c> on iOS) but the rule is not: what differs is how each runtime reports "the port answered",
/// never which port an address resolves to.
/// </para>
/// </summary>
public static class ServerProbe
{
    /// <summary>
    /// The route asked for. Anonymous and exempt from the client-version floor, so it answers a shell of any age —
    /// which is what makes it usable as a reachability probe rather than only as a version read.
    /// </summary>
    private const string ProbePath = "/api/meta/client-requirements";

    /// <summary>
    /// Per-candidate budget. Short on purpose: this runs before the first paint of the connecting screen's
    /// navigation, and the worst case is paid once per address, not once per launch.
    /// </summary>
    private static readonly TimeSpan CandidateTimeout = TimeSpan.FromSeconds(4);

    /// <summary>
    /// The config to actually connect with. Returns <paramref name="config"/> unchanged when its port is already
    /// explicit, so the common case costs no network at all.
    /// </summary>
    /// <remarks>
    /// When **no** candidate answers, the first candidate is returned rather than nothing: the address is simply
    /// wrong or the server is off, and that is diagnosed far better by the navigation that follows — which shows
    /// the unreachable screen naming the address — than by a second error screen of this probe's own.
    /// </remarks>
    public static async Task<ServerConfig> ResolveAsync(ServerConfig config)
    {
        if (config.PortIsExplicit || !config.IsConfigured)
        {
            return config;
        }

        foreach (var port in config.CandidatePorts)
        {
            if (await AnswersAsync(config.Host, port).ConfigureAwait(true))
            {
                return config.WithResolvedPort(port);
            }
        }

        return config.WithResolvedPort(config.CandidatePorts[0]);
    }

    private static async Task<bool> AnswersAsync(string host, int port)
    {
        using var client = new HttpClient { Timeout = CandidateTimeout };
        try
        {
            // Any status is an answer — 200, 404 on a server too old to have the route, even a 502 from a proxy
            // in front of a starting API. All of them prove something is listening on this port.
            using var response = await client
                .GetAsync($"https://{host}:{port}{ProbePath}", HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);
            return true;
        }
        catch (HttpRequestException ex) when (IsTlsFailure(ex))
        {
            // A certificate this PC does not trust — the offline-LAN install's normal state before its CA is
            // imported. Something is listening and speaking TLS, which is all this probe asks.
            return true;
        }
        catch (Exception)
        {
            // SocketException (refused / unreachable / DNS), TaskCanceledException (timeout), and anything else
            // unexpected: not a port to connect to.
            return false;
        }
    }

    private static bool IsTlsFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is AuthenticationException)
            {
                return true;
            }

            if (current is SocketException)
            {
                return false;
            }
        }

        return false;
    }
}
