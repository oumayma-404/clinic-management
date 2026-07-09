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
    public void Null_remote_ip_is_loopback() // in-process / no remote info ⇒ true (R-8)
    {
        Assert.True(LocalRequest.IsLoopback(Context(remote: null)));
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
