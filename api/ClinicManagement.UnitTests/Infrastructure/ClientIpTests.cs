using System.Net;
using ClinicManagement.Infrastructure;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure;

/// <summary>
/// Client-address resolution for rate limiting and per-source login lockout (security-hardening US-4, P3.0).
///
/// <b>This is the test that matters most in Part 3.</b> The failure mode it guards is silent and plausible: if
/// resolution is wrong, every browser login arrives as <c>127.0.0.1</c>, the limiter buckets the entire clinic
/// as one source, and it <i>looks</i> like a working limiter right up until it locks the whole clinic out. So
/// two distinct browser addresses must land in two distinct buckets, and a LAN client must never be able to
/// forge its way into another one.
/// </summary>
public class ClientIpTests
{
    private static HttpContext Context(string? peer, string? forwardedFor = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = peer is null ? null : IPAddress.Parse(peer);
        if (forwardedFor is not null)
        {
            context.Request.Headers[ClientIp.ForwardedForHeader] = forwardedFor;
        }

        return context;
    }

    [Fact]
    public void Two_browsers_behind_the_bff_resolve_to_two_different_buckets() // the whole point of P3.0
    {
        var first = ClientIp.Resolve(Context("127.0.0.1", forwardedFor: "192.168.1.42"));
        var second = ClientIp.Resolve(Context("127.0.0.1", forwardedFor: "192.168.1.43"));

        Assert.Equal("192.168.1.42", first);
        Assert.Equal("192.168.1.43", second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Without_the_forwarded_header_a_bff_call_collapses_to_loopback() // the bug being fixed
    {
        // Documents exactly what happens if the BFF stops forwarding: every login shares one bucket.
        var first = ClientIp.Resolve(Context("127.0.0.1"));
        var second = ClientIp.Resolve(Context("127.0.0.1"));

        Assert.Equal(first, second);
        Assert.Equal("127.0.0.1", first);
    }

    [Fact]
    public void A_lan_client_hitting_the_api_directly_keeps_its_real_address()
    {
        // /api/* is served in-process by Kestrel in Local mode, so this path exists alongside the BFF one.
        Assert.Equal("192.168.1.50", ClientIp.Resolve(Context("192.168.1.50")));
    }

    [Fact]
    public void A_lan_client_cannot_spoof_the_forwarded_header() // the header is only trusted from loopback
    {
        var resolved = ClientIp.Resolve(Context("192.168.1.50", forwardedFor: "10.9.9.9"));

        Assert.Equal("192.168.1.50", resolved);
    }

    [Fact]
    public void A_lan_client_cannot_claim_to_be_loopback() // would otherwise be a privilege escalation
    {
        var resolved = ClientIp.Resolve(Context("192.168.1.50", forwardedFor: "127.0.0.1"));

        Assert.Equal("192.168.1.50", resolved);
    }

    [Fact]
    public void The_left_most_entry_wins() // everything to its right is an intermediate hop
    {
        var resolved = ClientIp.Resolve(Context("127.0.0.1", forwardedFor: "192.168.1.42, 127.0.0.1"));

        Assert.Equal("192.168.1.42", resolved);
    }

    [Theory]
    [InlineData("192.168.1.42:51000", "192.168.1.42")] // proxies often append the source port
    [InlineData("[2001:db8::1]:51000", "2001:db8::1")]
    [InlineData("[2001:db8::1]", "2001:db8::1")]
    [InlineData("2001:db8::1", "2001:db8::1")]
    [InlineData("  192.168.1.42  ", "192.168.1.42")]
    public void Port_and_bracket_forms_are_normalised(string headerValue, string expected)
    {
        Assert.Equal(expected, ClientIp.Resolve(Context("127.0.0.1", headerValue)));
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",,,")]
    public void A_garbage_header_falls_back_to_the_peer_rather_than_inventing_a_bucket(string headerValue)
    {
        // A malformed header must not be usable as a partition key — otherwise an attacker varies the
        // garbage and gets a fresh bucket per request.
        Assert.Equal("127.0.0.1", ClientIp.Resolve(Context("127.0.0.1", headerValue)));
    }

    [Fact]
    public void A_garbage_first_entry_is_skipped_for_the_first_valid_one()
    {
        Assert.Equal("192.168.1.42", ClientIp.Resolve(Context("127.0.0.1", "junk, 192.168.1.42")));
    }

    [Fact]
    public void An_unattributable_request_shares_one_constrained_bucket_rather_than_being_exempt()
    {
        // No peer and no header: such requests are limited together. Being unattributable must not mean
        // being unlimited.
        Assert.Equal(ClientIp.Unknown, ClientIp.Resolve(Context(peer: null)));
    }

    [Fact]
    public void A_null_peer_does_not_trust_the_forwarded_header()
    {
        Assert.Equal(ClientIp.Unknown, ClientIp.Resolve(Context(peer: null, forwardedFor: "10.9.9.9")));
    }

    [Fact]
    public void Resolve_rejects_a_null_context()
    {
        Assert.Throws<ArgumentNullException>(() => ClientIp.Resolve(null!));
    }
}
