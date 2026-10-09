using System.Net;
using System.Text;
using System.Text.Json;
using ClinicManagement.Infrastructure.Relay;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Relay;

/// <summary>
/// D21 / FR-7 — the cabinet's apps find the PC de secours after its address changed. The PC answers only the relay it
/// was paired as, only on the cabinet's own networks, and says nothing the cloud does not already give every device.
/// </summary>
public class RelayDiscoveryTests
{
    private static readonly Guid Relay = Guid.Parse("4a0f6d3e-6c2b-4a7e-9e57-1c3b2f6a8d10");
    private const string Fingerprint = "DD12C3785C42A1B2C3D4E5F60718293A4B5C6D7E8F90A1B2C3D4E5F60718293A";
    private static readonly IPAddress Desk = IPAddress.Parse("192.168.1.20");
    private static readonly string[] Addresses = { "192.168.1.35" };

    [Fact]
    public void A_Request_Names_The_Relay_It_Looks_For()
    {
        Assert.Equal(Relay, RelayDiscovery.ParseRequest(RelayDiscovery.BuildRequest(Relay)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("APEXA-RELAY-DISCOVER/1 not-a-guid")]
    [InlineData("APEXA-RELAY-DISCOVER/2 4a0f6d3e-6c2b-4a7e-9e57-1c3b2f6a8d10")]
    [InlineData("hello")]
    public void Anything_Else_Is_Not_A_Request(string text)
    {
        Assert.Null(RelayDiscovery.ParseRequest(Encoding.ASCII.GetBytes(text)));
    }

    [Fact]
    public void An_Oversized_Datagram_Is_Not_A_Request()
    {
        var padded = Encoding.ASCII.GetBytes(RelayDiscovery.RequestPrefix + Relay.ToString("D") + new string(' ', 200));
        Assert.Null(RelayDiscovery.ParseRequest(padded));
    }

    [Fact]
    public void The_Pc_Answers_Where_It_Is_And_Which_Certificate_It_Shows()
    {
        var answer = RelayDiscovery.Answer(Relay, Relay, Desk, 5096, Fingerprint, Addresses);

        Assert.NotNull(answer);
        using var json = JsonDocument.Parse(answer!);
        Assert.Equal(Relay.ToString("D"), json.RootElement.GetProperty("relayId").GetString());
        Assert.Equal(5096, json.RootElement.GetProperty("port").GetInt32());
        Assert.Equal(Fingerprint, json.RootElement.GetProperty("fingerprint").GetString());
        Assert.Equal("192.168.1.35", json.RootElement.GetProperty("addresses")[0].GetString());
    }

    // Silence, never a refusal: a stranger learns nothing about whether a PC is here.
    [Fact]
    public void Another_Relay_An_Unpaired_Pc_Or_No_Certificate_Gets_No_Answer()
    {
        Assert.Null(RelayDiscovery.Answer(Guid.NewGuid(), Relay, Desk, 5096, Fingerprint, Addresses));
        Assert.Null(RelayDiscovery.Answer(Relay, null, Desk, 5096, Fingerprint, Addresses));
        Assert.Null(RelayDiscovery.Answer(Relay, Relay, Desk, 5096, null, Addresses));
        Assert.Null(RelayDiscovery.Answer(null, Relay, Desk, 5096, Fingerprint, Addresses));
    }

    [Theory]
    [InlineData("8.8.8.8", false)]
    [InlineData("41.226.10.5", false)]
    [InlineData("10.0.0.7", true)]
    [InlineData("172.16.4.2", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("169.254.10.1", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("fd00::35", true)]
    [InlineData("2001:4860::1", false)]
    public void Only_The_Cabinets_Own_Networks_Are_Answered(string from, bool answered)
    {
        var answer = RelayDiscovery.Answer(Relay, Relay, IPAddress.Parse(from), 5096, Fingerprint, Addresses);
        Assert.Equal(answered, answer is not null);
    }
}
