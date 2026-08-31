using System.Net;
using System.Net.Sockets;
using ClinicManagement.Domain.Common;

namespace ClinicManagement.Infrastructure.Http;

/// <summary>
/// Refuses an outbound connection whose <b>resolved</b> address is private, at the moment the socket is opened.
///
/// <para><b>The half that was owed.</b> <see cref="OutboundEndpoint"/> checks literals: it refuses
/// <c>localhost</c>, a single-label container name and an IP literal in a private range. But
/// <c>IPAddress.TryParse</c> returns false for <i>any</i> hostname, so a hostname passed unconditionally — and
/// a clinic admin on the hosted deployment (which public signup lets anyone become) could point
/// <c>smtpHost</c> at <c>127.0.0.1.nip.io</c>, or at a name they control that answers with <c>10.0.0.5</c>.
/// The API container would then dial its own compose network on the tenant's behalf. That is a server-side
/// request primitive, and the settings screen is its user interface.</para>
///
/// <para>⚠️ <b>Validation cannot close this on its own, however careful it is.</b> DNS is mutable: a name that
/// resolves publicly when the admin saves the form can resolve to <c>169.254.169.254</c> when the job dials it
/// an hour later — the classic rebind, and no amount of checking at save time sees it. The check has to happen
/// where the address is finally known, which is the connect callback. The literal check stays worth having: it
/// refuses the direct form with a French message the admin can act on, and it refuses <c>http://</c>.</para>
///
/// <para>⚠️ <b>Every address is checked, not just the first.</b> A name can answer with several records — one
/// public, one private — and connecting to whichever the resolver happens to order first is not a decision
/// anybody made. A single private answer refuses the whole connection.</para>
/// </summary>
public static class PublicEgressGuard
{
    /// <summary>
    /// Builds the <see cref="SocketsHttpHandler.ConnectCallback"/> that enforces the rule.
    ///
    /// <para>When <paramref name="allowPrivateNetwork"/> is true — a clinic's own machine relaying through a box
    /// on the practice's own LAN — this returns <c>null</c> and the handler keeps the framework's default
    /// behaviour. There is no tenant boundary to defend on an install that serves one practice from its own
    /// reception desk, and refusing would break the normal case.</para>
    /// </summary>
    public static Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>>? ConnectCallback(
        bool allowPrivateNetwork)
    {
        if (allowPrivateNetwork)
        {
            return null;
        }

        return async (context, cancellationToken) =>
        {
            var host = context.DnsEndPoint.Host;
            var addresses = await ResolveAsync(host, cancellationToken);

            EnsureAllPublic(host, addresses);

            // Connect by ADDRESS, not by name: re-resolving here would open a window between the check and the
            // connect in which the answer can change, which is the very race this guard exists to close.
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

            try
            {
                await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };
    }

    /// <summary>
    /// Resolves a host to the addresses a connection would actually use, then refuses the lot if any one of
    /// them is private. Used by the SMTP senders, which do not go through an <see cref="HttpClient"/> and so
    /// have no connect callback to hang this on.
    /// </summary>
    public static async Task EnsureHostResolvesPublicAsync(
        string host, bool allowPrivateNetwork, CancellationToken cancellationToken = default)
    {
        if (allowPrivateNetwork)
        {
            return;
        }

        EnsureAllPublic(host, await ResolveAsync(host, cancellationToken));
    }

    private static async Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        // An IP literal is not a DNS name, and asking a resolver about one is both pointless and, on some
        // stacks, a reverse lookup nobody asked for.
        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal))
        {
            return new[] { literal };
        }

        return await Dns.GetHostAddressesAsync(host, cancellationToken);
    }

    private static void EnsureAllPublic(string host, IPAddress[] addresses)
    {
        // No answer at all is refused rather than passed through. `Socket.ConnectAsync` on an empty array
        // throws something far less legible, and « the name did not resolve » is worth saying plainly.
        if (addresses.Length == 0)
        {
            throw new HttpRequestException($"Le nom « {host} » ne résout vers aucune adresse.");
        }

        foreach (var address in addresses)
        {
            if (!OutboundEndpoint.IsPublicAddress(address))
            {
                // ⚠️ The message names the host but NOT the address it resolved to. Reporting the resolved
                // address back to the tenant who chose the name turns a refusal into a DNS-based scanner of the
                // operator's private network: « which of my names came back internal » is exactly the question
                // this refusal must not answer.
                throw new HttpRequestException(
                    $"Le nom « {host} » désigne une adresse interne : la connexion est refusée.");
            }
        }
    }
}
