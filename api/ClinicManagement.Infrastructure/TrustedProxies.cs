using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;

namespace ClinicManagement.Infrastructure;

/// <summary>
/// The set of peers whose <c>X-Forwarded-For</c> may be believed (multi-tenant-cloud review finding 1).
///
/// <para><b>Why this exists.</b> <see cref="ClientIp"/> used to trust the header only from a <b>loopback</b> peer,
/// which was exactly right while the only hop in front of the API was the co-located Next BFF. In a hosted
/// deployment it is never true: browser traffic arrives from the <c>caddy</c> container and BFF traffic from the
/// <c>web</c> container, both ordinary bridge addresses. The header was therefore sent and ignored, so every
/// address-keyed partition — the auth limiter's ceiling, the API limiter's fall-back and the per-source login
/// lockout — collapsed to <b>one bucket for the whole service</b>: ordinary load 429s, and one unauthenticated
/// caller could 429 every clinic out of logging in.</para>
///
/// <para><b>⚠️ Empty means loopback-only, so nothing changes where nothing is configured.</b> A
/// <see cref="SelfHostedLan"/> install and the committed defaults resolve <see cref="LoopbackOnly"/>, which
/// reproduces the previous rule exactly. Trust is opt-in per deployment because only the operator knows what sits
/// in front of the API.</para>
///
/// <para><b>⚠️ Since hosted-security-hardening Part 2 this set ALSO bounds <c>UseForwardedHeaders</c></b>, which
/// the codebase previously refused to register at all. The reason it refused was sound and has not gone away —
/// that middleware overwrites <c>Connection.RemoteIpAddress</c>, which is what
/// <see cref="LocalRequest.IsLoopback"/> reads to gate the first-run <c>setup</c> endpoint and the Hangfire
/// dashboard — so the two gates now read <see cref="OriginalPeer"/>, captured <i>before</i> the substitution.
/// The loopback guarantee stays a property of the real TCP peer; what changed is that the scheme and the client
/// address are now available to everything else, which is what makes the OAuth state cookie's <c>Secure</c>
/// flag and the API's own HSTS header correct behind a TLS-terminating proxy.</para>
///
/// <para>⚠️ <b>One parse, one authority.</b> <see cref="Networks"/> exposes the ranges so the API can hand the
/// same set to <c>ForwardedHeadersOptions</c> rather than re-reading the key — two parsers of one setting is how
/// the limiter and the header middleware end up trusting different hops.</para>
/// </summary>
public sealed class TrustedProxies
{
    /// <summary>Config key: an array of CIDR ranges (or bare addresses) whose forwarded header is believed.</summary>
    public const string ConfigurationKey = "Security:TrustedProxies";

    /// <summary>The previous behaviour, and the default: only a loopback peer is believed.</summary>
    public static readonly TrustedProxies LoopbackOnly = new(Array.Empty<Range>(), configuredEntryCount: 0);

    private readonly IReadOnlyList<Range> _ranges;

    private TrustedProxies(IReadOnlyList<Range> ranges, int configuredEntryCount)
    {
        _ranges = ranges;
        ConfiguredEntryCount = configuredEntryCount;
    }

    /// <summary>
    /// How many raw entries <see cref="ConfigurationKey"/> held, parseable or not. It separates "the operator
    /// configured nothing" from "the operator configured three ranges and none of them parsed" — identical in
    /// effect, opposite in what the startup log should say.
    /// </summary>
    public int ConfiguredEntryCount { get; }

    /// <summary>
    /// The parsed ranges, empty when the setting was absent or held nothing parseable. Loopback is trusted
    /// regardless and is deliberately not in here: it is not a configured range, it is the BFF hop that exists
    /// in every profile.
    /// </summary>
    public IReadOnlyList<ProxyNetwork> Networks =>
        _ranges.Select(r => new ProxyNetwork(r.NetworkAddress, r.PrefixLength)).ToList();

    /// <summary>
    /// One trusted range, in a shape that carries no ASP.NET Core type — this project has no framework
    /// reference, so the API maps these onto <c>ForwardedHeadersOptions.KnownNetworks</c> itself.
    /// </summary>
    public sealed record ProxyNetwork(IPAddress Network, int PrefixLength);

    /// <summary>
    /// Reads <see cref="ConfigurationKey"/>. Unparseable entries are **skipped**, never fatal: a typo in a proxy
    /// list must narrow trust, never take the deployment off the air.
    /// </summary>
    public static TrustedProxies FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configured = configuration.GetSection(ConfigurationKey).Get<string[]>();
        if (configured is null || configured.Length == 0)
        {
            return LoopbackOnly;
        }

        var ranges = configured
            .Select(Range.TryParse)
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();

        return ranges.Count == 0
            ? new TrustedProxies(Array.Empty<Range>(), configured.Length)
            : new TrustedProxies(ranges, configured.Length);
    }

    /// <summary>
    /// True when <paramref name="peer"/> is our own hop. Loopback always qualifies — the BFF hop still exists in
    /// every profile — so a configured list only ever *adds* to it.
    /// </summary>
    public bool IsTrusted(IPAddress? peer)
    {
        if (peer is null)
        {
            return false;
        }

        if (IPAddress.IsLoopback(peer))
        {
            return true;
        }

        foreach (var range in _ranges)
        {
            if (range.Contains(peer))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>One CIDR range, matched by comparing the first <c>PrefixLength</c> bits of the address.</summary>
    private sealed class Range
    {
        private readonly byte[] _network;
        private readonly int _prefixLength;
        private readonly AddressFamily _family;

        private Range(byte[] network, int prefixLength, AddressFamily family)
        {
            _network = network;
            _prefixLength = prefixLength;
            _family = family;
        }

        public int PrefixLength => _prefixLength;

        public IPAddress NetworkAddress => new(_network);

        public static Range? TryParse(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var text = value.Trim();
            var slash = text.IndexOf('/');

            // A bare address is a /32 (or /128) — one host, which is what an operator naming a single proxy means.
            if (slash < 0)
            {
                return IPAddress.TryParse(text, out var single)
                    ? new Range(Normalize(single), Bits(single), single.AddressFamily)
                    : null;
            }

            if (!IPAddress.TryParse(text[..slash], out var address)
                || !int.TryParse(text[(slash + 1)..], out var prefixLength)
                || prefixLength < 0
                || prefixLength > Bits(address))
            {
                return null;
            }

            return new Range(Normalize(address), prefixLength, address.AddressFamily);
        }

        public bool Contains(IPAddress candidate)
        {
            // Kestrel reports an IPv4-mapped IPv6 peer (::ffff:172.18.0.5) on a dual-stack socket; compare it as
            // the IPv4 address the operator actually wrote in the CIDR.
            var address = candidate.IsIPv4MappedToIPv6 ? candidate.MapToIPv4() : candidate;
            if (address.AddressFamily != _family)
            {
                return false;
            }

            var bytes = address.GetAddressBytes();
            if (bytes.Length != _network.Length)
            {
                return false;
            }

            var fullBytes = _prefixLength / 8;
            for (var i = 0; i < fullBytes; i++)
            {
                if (bytes[i] != _network[i])
                {
                    return false;
                }
            }

            var remainingBits = _prefixLength % 8;
            if (remainingBits == 0)
            {
                return true;
            }

            var mask = (byte)(0xFF << (8 - remainingBits));
            return (bytes[fullBytes] & mask) == (_network[fullBytes] & mask);
        }

        private static byte[] Normalize(IPAddress address) => address.GetAddressBytes();

        private static int Bits(IPAddress address) =>
            address.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
    }
}
