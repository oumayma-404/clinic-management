using System;
using System.Text;
using System.Text.Json;
using ClinicManagement.DesktopShell;
using Xunit;

namespace ClinicManagement.DesktopShell.Tests;

/// <summary>
/// D21 — the app finds the PC de secours after the box gave it a new address. An answer only says where to look: it must
/// name the same relay, port and certificate the app already holds, and only private addresses are kept.
/// </summary>
public class RelayDiscoveryTests
{
    private const string Fingerprint = "DD12C3785C42A1B2C3D4E5F60718293A4B5C6D7E8F90A1B2C3D4E5F60718293A";
    private static readonly Guid Relay = Guid.Parse("4a0f6d3e-6c2b-4a7e-9e57-1c3b2f6a8d10");
    private static readonly RelaySwitch.Target Known = new("192.168.1.35", 5096, Fingerprint, Relay);

    private static string Answer(Guid relay, int port, string fingerprint, params string[] addresses) =>
        JsonSerializer.Serialize(new { v = 1, relayId = relay.ToString("D"), port, fingerprint, addresses });

    // The request bytes are the server's (Infrastructure/Relay/RelayDiscovery.cs) — one format on both sides.
    [Fact]
    public void The_Request_Is_The_Servers_Format()
    {
        Assert.Equal("APEXA-RELAY-DISCOVER/1 " + Relay.ToString("D"), Encoding.ASCII.GetString(RelayDiscovery.BuildRequest(Relay)));
        Assert.Equal(47950, RelayDiscovery.Port);
    }

    [Fact]
    public void The_Pcs_New_Address_Is_Taken_From_A_Matching_Answer()
    {
        var addresses = RelayDiscovery.AddressesFrom(Answer(Relay, 5096, Fingerprint, "192.168.1.52", "8.8.8.8"), Known);

        Assert.Equal(new[] { "192.168.1.52" }, addresses); // a public address is never tried
    }

    [Fact]
    public void Another_Relay_Another_Port_Or_Another_Certificate_Is_Ignored()
    {
        Assert.Empty(RelayDiscovery.AddressesFrom(Answer(Guid.NewGuid(), 5096, Fingerprint, "192.168.1.52"), Known));
        Assert.Empty(RelayDiscovery.AddressesFrom(Answer(Relay, 5001, Fingerprint, "192.168.1.52"), Known));
        Assert.Empty(RelayDiscovery.AddressesFrom(Answer(Relay, 5096, new string('A', 64), "192.168.1.52"), Known));
        Assert.Empty(RelayDiscovery.AddressesFrom("not json", Known));
    }

    // A target saved before discovery existed has no relay id: it cannot be looked for, and nothing breaks.
    [Fact]
    public void A_Target_Saved_Before_Discovery_Loads_And_Is_Never_Looked_For()
    {
        var old = JsonSerializer.Deserialize<RelaySwitch.Target>($$"""{"Address":"192.168.1.35","Port":5096,"Fingerprint":"{{Fingerprint}}"}""");

        Assert.NotNull(old);
        Assert.Null(old!.RelayId);
        Assert.Empty(RelayDiscovery.AddressesFrom(Answer(Relay, 5096, Fingerprint, "192.168.1.52"), old));
    }

    [Fact]
    public void A_Prepare_Request_Carries_The_Relay_Id_When_The_Page_Sends_It()
    {
        var parsed = RelaySwitch.ParsePrepare(
            $$"""{"assertion":"ra1.a.b","relayId":"{{Relay}}","addresses":["192.168.1.35"],"port":5096,"fingerprint":"{{Fingerprint}}"}""");

        Assert.Equal(Relay, parsed!.RelayId);
    }
}
