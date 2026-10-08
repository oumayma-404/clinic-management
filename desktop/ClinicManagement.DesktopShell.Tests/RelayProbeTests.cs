using System.Net;
using System.Security.Cryptography;
using ClinicManagement.DesktopShell;
using Xunit;

namespace ClinicManagement.DesktopShell.Tests;

/// <summary>
/// <c>relayProbe()</c> (<c>clinic-pc-copy</c> AC-6.2): what the shell agrees to try, and what counts as the PC. The
/// probe itself needs a PC on a LAN and is the rig's job; what is decided before and after it is here.
/// </summary>
public class RelayProbeTests
{
    private static readonly string Fingerprint = new('A', 64);

    [Fact]
    public void A_Well_Formed_Request_Keeps_Only_Private_Addresses()
    {
        var request = RelayProbe.Parse(
            $$"""{"addresses":["192.168.1.20","8.8.8.8","10.0.0.5","not-an-ip","fd00::5"],"port":5001,"fingerprint":"{{Fingerprint.ToLowerInvariant()}}"}""");

        Assert.NotNull(request);
        Assert.Equal(new[] { "192.168.1.20", "10.0.0.5", "fd00::5" }, request!.Addresses.Select(a => a.ToString()));
        Assert.Equal(5001, request.Port);
        Assert.Equal(Fingerprint, request.Fingerprint);
    }

    // The shell must not become a way for a page to make a clinic PC call arbitrary hosts.
    [Theory]
    [InlineData("""{"addresses":["8.8.8.8"],"port":5001,"fingerprint":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""")]
    [InlineData("""{"addresses":["192.168.1.20"],"port":0,"fingerprint":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""")]
    [InlineData("""{"addresses":["192.168.1.20"],"port":70000,"fingerprint":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""")]
    [InlineData("""{"addresses":["192.168.1.20"],"port":5001,"fingerprint":"ABC"}""")]
    [InlineData("""{"addresses":[],"port":5001,"fingerprint":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""")]
    [InlineData("""{"port":5001}""")]
    [InlineData("""not json""")]
    public void Anything_Else_Is_Refused_And_The_Page_Sends_No_Report(string json)
    {
        Assert.Null(RelayProbe.Parse(json));
    }

    [Theory]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.1", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("192.168.0.10", true)]
    [InlineData("169.254.10.1", true)]
    [InlineData("::ffff:192.168.1.5", true)]
    [InlineData("fe80::1", true)]
    [InlineData("fd12:3456::1", true)]
    [InlineData("41.230.1.2", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("127.0.0.1", false)]
    public void Only_A_Lan_Address_Is_Tried(string address, bool expected)
    {
        Assert.Equal(expected, RelayProbe.IsPrivate(IPAddress.Parse(address)));
    }

    // D21: the PC is its certificate. Another machine answering at that address is not the PC.
    [Fact]
    public void The_Pc_Is_Recognised_By_Its_Certificate_Never_By_Its_Address()
    {
        var der = new byte[] { 0x30, 0x82, 0x01, 0x0A, 0x02, 0x01 };
        var pinned = Convert.ToHexString(SHA256.HashData(der));

        Assert.True(RelayProbe.IsThePcsCertificate(der, pinned));
        Assert.False(RelayProbe.IsThePcsCertificate(new byte[] { 0x30, 0x82 }, pinned));
        Assert.False(RelayProbe.IsThePcsCertificate(null, pinned));
        Assert.False(RelayProbe.IsThePcsCertificate(der, pinned.ToLowerInvariant()));
    }
}
