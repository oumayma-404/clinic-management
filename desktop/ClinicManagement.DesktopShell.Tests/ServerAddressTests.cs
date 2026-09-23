using ClinicManagement.DesktopShell;
using Xunit;

namespace ClinicManagement.DesktopShell.Tests;

/// <summary>
/// <see cref="ServerConfigStore.IsIpLiteral"/> — what decides whether the address panel advises against what is
/// typed. The rule is narrow on purpose: it is an advisory shown live under a field a clinic depends on, so a
/// false positive nags somebody who did the right thing, and a false negative lets the one failure this exists
/// for through.
/// </summary>
public class ServerAddressTests
{
    [Theory]
    [InlineData("192.168.1.10")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.4.2")]
    [InlineData("  192.168.1.10  ")] // The field is not trimmed before this is asked.
    public void A_lan_ipv4_literal_is_advised_against(string host) =>
        Assert.True(ServerConfigStore.IsIpLiteral(host));

    [Theory]
    [InlineData("clinic-server")]
    [InlineData("DESKTOP-4KJ2P1")]
    [InlineData("app.apexa.tn")]
    [InlineData("serveur.cabinet.lan")]
    public void A_name_is_not(string host) =>
        Assert.False(ServerConfigStore.IsIpLiteral(host));

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    public void Loopback_is_not_advised_against(string host) =>
        // AC-2.5: this is what the server PC's own shell is configured with, and it cannot be improved on.
        // Nagging the one machine that is definitionally right would make the advisory noise everywhere.
        Assert.False(ServerConfigStore.IsIpLiteral(host));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_empty_address_is_not(string? host) =>
        // An empty field is the first-run state. « Veuillez saisir une adresse » is that panel's own message.
        Assert.False(ServerConfigStore.IsIpLiteral(host));

    [Theory]
    [InlineData("192.168.1.10:5001")]
    [InlineData("https://192.168.1.10:5001")]
    [InlineData("http://192.168.1.10/")]
    public void The_advisory_survives_a_port_or_a_scheme(string typed)
    {
        // The panel asks about ParseAddress(...).Host, never the raw text — typing a full URL is the same
        // mistake as typing the bare address and must be advised about identically.
        var host = ServerConfigStore.ParseAddress(typed).Host;
        Assert.True(ServerConfigStore.IsIpLiteral(host));
    }

    [Fact]
    public void An_ipv6_literal_is_not_advised_against() =>
        // Out of scope rather than approved: nothing in this product hands out IPv6 on a clinic LAN, and the
        // bracketed form does not survive ParseAddress's last-colon port split anyway. Stated so the narrow
        // AddressFamily test reads as a decision rather than an oversight.
        Assert.False(ServerConfigStore.IsIpLiteral("fe80::1"));
}
