using System.Net;
using ClinicManagement.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure;

/// <summary>
/// Which peers' <c>X-Forwarded-For</c> is believed (multi-tenant-cloud review finding 1 — the Critical one).
///
/// <para><b>What the defect was.</b> <see cref="ClientIp"/> trusted the header only from a <b>loopback</b> peer.
/// Behind a reverse proxy that is never true — every request's peer is the proxy container — so every
/// address-keyed partition in the product collapsed to one bucket for the whole deployment: the auth limiter's
/// ceiling, the API limiter's fall-back, and the per-source login lockout. The tests below are therefore mostly
/// about the *default* still being loopback-only (nothing may change where nothing is configured) and about the
/// header still being refused from an untrusted peer, which is the property that stops a client inventing a
/// partition.</para>
/// </summary>
public class TrustedProxiesTests
{
    private static HttpContext Request(string? peer, string? forwardedFor = null)
    {
        var context = new DefaultHttpContext();
        if (peer is not null)
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        }

        if (forwardedFor is not null)
        {
            context.Request.Headers[ClientIp.ForwardedForHeader] = forwardedFor;
        }

        return context;
    }

    private static TrustedProxies From(params string[] ranges)
    {
        var values = new Dictionary<string, string?>();
        for (var i = 0; i < ranges.Length; i++)
        {
            values[$"{TrustedProxies.ConfigurationKey}:{i}"] = ranges[i];
        }

        return TrustedProxies.FromConfiguration(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }

    [Fact]
    public void No_configuration_is_loopback_only()
    {
        var proxies = From();

        Assert.True(proxies.IsTrusted(IPAddress.Loopback));
        Assert.False(proxies.IsTrusted(IPAddress.Parse("172.18.0.3")));
    }

    [Fact]
    public void Loopback_stays_trusted_even_with_a_configured_list()
    {
        // The BFF hop exists in every profile, so a configured range only ever ADDS to loopback.
        Assert.True(From("172.16.0.0/12").IsTrusted(IPAddress.Loopback));
    }

    [Theory]
    [InlineData("172.18.0.3", true)]
    [InlineData("172.31.255.254", true)]
    [InlineData("172.15.0.1", false)]   // just below the /12
    [InlineData("172.32.0.1", false)]   // just above it
    [InlineData("10.0.0.1", false)]
    public void A_cidr_range_matches_only_inside_itself(string peer, bool expected)
    {
        Assert.Equal(expected, From("172.16.0.0/12").IsTrusted(IPAddress.Parse(peer)));
    }

    [Fact]
    public void A_bare_address_is_one_host()
    {
        var proxies = From("172.18.0.3");

        Assert.True(proxies.IsTrusted(IPAddress.Parse("172.18.0.3")));
        Assert.False(proxies.IsTrusted(IPAddress.Parse("172.18.0.4")));
    }

    [Fact]
    public void An_ipv4_mapped_ipv6_peer_matches_an_ipv4_range()
    {
        // Kestrel reports ::ffff:172.18.0.3 on a dual-stack socket, which is the form a hosted deployment
        // actually sees — matching it against the IPv4 CIDR the operator wrote is the whole point.
        Assert.True(From("172.16.0.0/12").IsTrusted(IPAddress.Parse("::ffff:172.18.0.3")));
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("172.16.0.0/99")]
    [InlineData("172.16.0.0/-1")]
    [InlineData("")]
    public void An_unparseable_entry_narrows_trust_rather_than_failing_startup(string entry)
    {
        // A typo in a proxy list must never take the deployment off the air, and must never widen trust either.
        var proxies = From(entry);

        Assert.True(proxies.IsTrusted(IPAddress.Loopback));
        Assert.False(proxies.IsTrusted(IPAddress.Parse("172.18.0.3")));
    }

    [Fact]
    public void A_null_peer_is_not_trusted()
    {
        Assert.False(From("172.16.0.0/12").IsTrusted(null));
    }

    // ---- What the two Resolve overloads do with the same request ----

    [Fact]
    public void A_proxy_peer_is_believed_only_by_the_trusting_overload()
    {
        var context = Request("172.18.0.3", forwardedFor: "41.229.0.1");

        Assert.Equal("172.18.0.3", ClientIp.Resolve(context));
        Assert.Equal("41.229.0.1", ClientIp.Resolve(context, From("172.16.0.0/12")));
    }

    [Fact]
    public void An_untrusted_peers_header_is_still_refused()
    {
        // The property that stops a client escaping its own bucket: only OUR hop may relabel a request.
        var context = Request("41.229.0.9", forwardedFor: "10.0.0.1");

        Assert.Equal("41.229.0.9", ClientIp.Resolve(context, From("172.16.0.0/12")));
    }

    [Fact]
    public void Two_clinics_behind_one_proxy_resolve_to_different_addresses()
    {
        // The Critical finding in one assertion: before this, both of these were the proxy's own address.
        var proxies = From("172.16.0.0/12");

        Assert.NotEqual(
            ClientIp.Resolve(Request("172.18.0.3", forwardedFor: "41.229.0.1"), proxies),
            ClientIp.Resolve(Request("172.18.0.3", forwardedFor: "197.0.0.9"), proxies));
    }
}
