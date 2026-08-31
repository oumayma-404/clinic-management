using System.Net;
using System.Net.Sockets;

namespace ClinicManagement.Domain.Common;

/// <summary>
/// The one rule for an outbound endpoint a <b>tenant</b> is allowed to name.
///
/// <para>
/// Per-clinic integration endpoints (the SMS gateway URL, the WhatsApp Graph base, the SMTP host) are typed in by
/// a clinic admin and then dialled by a background job running <i>inside</i> the API container. Without this
/// rule they were accepted verbatim — <c>Trim()</c> was the whole of it — which made every one of them a
/// server-side request primitive pointed at whatever the tenant chose: the container's own loopback (where the
/// Hangfire dashboard trusts <c>LocalRequest.IsLoopback</c>), a sibling service on the compose network, or a
/// cloud metadata address.
/// </para>
///
/// <para>
/// ⚠️ <b>This checks literals, not resolution.</b> A hostname that resolves to a private address still passes
/// here, because the domain does no I/O and DNS can change between validation and use. The complete defence is
/// this rule <i>plus</i> a <c>SocketsHttpHandler.ConnectCallback</c> on the outbound client re-checking the
/// resolved address. <b>That half now exists</b> — <c>PublicEgressGuard</c>, which calls
/// <see cref="IsPublicAddress"/> so the two halves can never disagree about which ranges are private. This half
/// is still worth having, and is not redundant: it refuses the direct, obvious form with a French message an
/// admin can act on, at the moment they type it rather than hours later inside a background job — and it
/// refuses <c>http://</c>, which is what stops a credential travelling in clear text.
/// </para>
///
/// <para>
/// ⚠️ Refusals are French and name the field, because the only caller is an admin editing a settings screen.
/// </para>
/// </summary>
public static class OutboundEndpoint
{
    /// <summary>
    /// Validates an absolute URL a clinic supplied. Returns the trimmed value, or null when nothing was given
    /// (clearing an endpoint is legitimate — it hands the channel back to the install default).
    /// </summary>
    /// <param name="value">The raw user input.</param>
    /// <param name="fieldLabel">French label used in the refusal, e.g. « L'URL de la passerelle SMS ».</param>
    /// <param name="allowPrivateNetwork">
    /// True only where a private endpoint is the normal case — an offline LAN install relaying through a box on
    /// the practice's own network. Never true on a hosted deployment, where the private range reachable from the
    /// container is the operator's infrastructure rather than the clinic's.
    /// </param>
    public static string? ValidateUrl(string? value, string fieldLabel, bool allowPrivateNetwork)
    {
        var trimmed = Trimmed(value);
        if (trimmed is null)
        {
            return null;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException($"{fieldLabel} doit être une adresse absolue (https://…).");
        }

        var isHttps = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var isHttp = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);

        if (!isHttps && !(isHttp && allowPrivateNetwork))
        {
            throw new ArgumentException($"{fieldLabel} doit utiliser https://.");
        }

        EnsureHostIsRoutable(uri.Host, fieldLabel, allowPrivateNetwork);
        return trimmed;
    }

    /// <summary>
    /// Validates a bare host name (SMTP has no scheme). Same rule, minus the scheme check.
    /// </summary>
    public static string? ValidateHost(string? value, string fieldLabel, bool allowPrivateNetwork)
    {
        var trimmed = Trimmed(value);
        if (trimmed is null)
        {
            return null;
        }

        // A host must not smuggle a scheme, a port, a path or credentials — those would each change what is
        // actually dialled while looking like a host name in the settings screen.
        if (trimmed.Contains("://", StringComparison.Ordinal)
            || trimmed.Contains('/', StringComparison.Ordinal)
            || trimmed.Contains('@', StringComparison.Ordinal)
            || trimmed.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException($"{fieldLabel} doit être un nom d'hôte seul, sans schéma ni port.");
        }

        EnsureHostIsRoutable(trimmed, fieldLabel, allowPrivateNetwork);
        return trimmed;
    }

    private static void EnsureHostIsRoutable(string host, string fieldLabel, bool allowPrivateNetwork)
    {
        if (allowPrivateNetwork)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(host)
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
            // A single-label name is a container or LAN name (`minio`, `api`, `postgres`), never a public host.
            || !host.Contains('.', StringComparison.Ordinal))
        {
            throw new ArgumentException($"{fieldLabel} doit désigner un serveur public, pas une adresse interne.");
        }

        if (IPAddress.TryParse(host.Trim('[', ']'), out var address) && !IsPublic(address))
        {
            throw new ArgumentException($"{fieldLabel} doit désigner un serveur public, pas une adresse interne.");
        }
    }

    /// <summary>
    /// Is this a routable, public address? Public because the <b>connect-time</b> half of this rule needs the
    /// identical predicate: <c>PublicEgressGuard</c> re-checks the addresses a host actually resolved to, and
    /// two copies of « which ranges are private » would drift on the first range somebody adds.
    /// </summary>
    public static bool IsPublicAddress(IPAddress address) => IsPublic(address);

    private static bool IsPublic(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var octets = address.GetAddressBytes();
            return octets[0] switch
            {
                0 => false,                                   // "this network"
                10 => false,                                  // RFC1918
                127 => false,                                 // loopback
                169 when octets[1] == 254 => false,            // link-local, incl. cloud metadata
                172 when octets[1] >= 16 && octets[1] <= 31 => false, // RFC1918
                192 when octets[1] == 168 => false,            // RFC1918
                100 when octets[1] >= 64 && octets[1] <= 127 => false, // CGNAT
                >= 224 => false,                              // multicast + reserved
                _ => true,
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
            {
                return false;
            }

            // Unique-local (fc00::/7), and any IPv4-mapped address re-checked as IPv4 so ::ffff:127.0.0.1
            // cannot walk through the v4 rules above.
            var bytes = address.GetAddressBytes();
            if ((bytes[0] & 0xFE) == 0xFC)
            {
                return false;
            }

            if (address.IsIPv4MappedToIPv6)
            {
                return IsPublic(address.MapToIPv4());
            }
        }

        return true;
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
