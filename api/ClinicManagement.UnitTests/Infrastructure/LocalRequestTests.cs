using System.Net;
using ClinicManagement.Infrastructure;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure;

/// <summary>
/// The shared loopback check (R-8). Mirrors the original <c>AuthController.IsLocalRequest</c> cases so the
/// behavior is provably preserved after the extraction: loopback / same-machine / null-remote ⇒ true;
/// a distinct LAN IP ⇒ false. Backs both the first-run setup gate (AC-1.2a) and the Hangfire lockdown.
/// </summary>
public class LocalRequestTests
{
    private static HttpContext Context(IPAddress? remote, IPAddress? local = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remote;
        context.Connection.LocalIpAddress = local;
        return context;
    }

    [Fact]
    public void Null_remote_ip_is_NOT_loopback() // fail closed (security-hardening P3.0)
    {
        // Deliberately reversed. This gate opens the first-run setup endpoint and the Hangfire dashboard, so
        // it must deny on missing or ambiguous information rather than assume "must be local". Kestrel over
        // TCP always populates the peer, so a null here means an unexpected hosting topology — exactly where
        // guessing is wrong. Previously returned true.
        Assert.False(LocalRequest.IsLoopback(Context(remote: null)));
    }

    [Fact]
    public void A_forwarded_header_cannot_make_a_lan_request_look_local() // must read the raw TCP peer only
    {
        // The rate limiter resolves its client address from X-Forwarded-For (via ClientIp) when the peer is
        // loopback. This gate must NOT — otherwise a LAN client sends `X-Forwarded-For: 127.0.0.1` and walks
        // through the setup endpoint and /hangfire. Keeping the two separate is why UseForwardedHeaders is
        // not used anywhere in this codebase.
        var context = Context(remote: IPAddress.Parse("192.168.1.50"), local: IPAddress.Parse("192.168.1.10"));
        context.Request.Headers[ClinicManagement.Infrastructure.ClientIp.ForwardedForHeader] = "127.0.0.1";

        Assert.False(LocalRequest.IsLoopback(context));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void Loopback_ip_is_loopback(string ip)
    {
        Assert.True(LocalRequest.IsLoopback(Context(IPAddress.Parse(ip))));
    }

    [Fact]
    public void Remote_equal_to_local_is_loopback() // same machine reaching itself over its LAN NIC
    {
        var context = Context(remote: IPAddress.Parse("192.168.1.10"), local: IPAddress.Parse("192.168.1.10"));
        Assert.True(LocalRequest.IsLoopback(context));
    }

    [Fact]
    public void Distinct_lan_ip_is_not_loopback()
    {
        var context = Context(remote: IPAddress.Parse("192.168.1.50"), local: IPAddress.Parse("192.168.1.10"));
        Assert.False(LocalRequest.IsLoopback(context));
    }
}
