using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Relay;

/// <summary>
/// AC-3.7 / D24 (<c>clinic-pc-copy</c>): a plain browser opens the PC de secours from the address on its card, and
/// « Préparer ce navigateur » sends it to the PC's own trust page. Both come from the PC's last heartbeat; a retired PC,
/// or one not heard from yet, offers neither.
/// </summary>
public class RelayBrowserAccessTests
{
    private static readonly Guid ClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTime T0 = new(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc);

    private static ClinicRelay Paired(string? lanAddresses = "192.168.1.35", int? httpsPort = 5096)
    {
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", T0.AddDays(-1));
        relay.Pair("PC-ACCUEIL", "key", null, null, "build-1", T0.AddDays(-1));
        relay.RecordHeartbeat(
            new RelayHeartbeat(10, 100, true, 0, 0, null, false, "build-1", null, lanAddresses, null, null,
                new string('A', 64), false, HttpsPort: httpsPort),
            10, T0);
        return relay;
    }

    [Fact]
    public void The_Card_Opens_The_Pc_And_Its_Trust_Page()
    {
        var urls = RelayBrowserAccess.For(Paired());

        Assert.NotNull(urls);
        Assert.Equal("https://192.168.1.35:5096/", urls!.Open);
        Assert.Equal("http://192.168.1.35:5080/api/trust", urls.Prepare);
    }

    [Fact]
    public void An_Ipv6_Address_Is_Bracketed()
    {
        var urls = RelayBrowserAccess.For(Paired("fd00::35"));

        Assert.Equal("https://[fd00::35]:5096/", urls!.Open);
        Assert.Equal("http://[fd00::35]:5080/api/trust", urls.Prepare);
    }

    [Fact]
    public void Nothing_Is_Offered_Without_An_Address_A_Port_Or_For_A_Retired_Pc()
    {
        Assert.Null(RelayBrowserAccess.For(null));
        Assert.Null(RelayBrowserAccess.For(Paired(lanAddresses: null)));
        Assert.Null(RelayBrowserAccess.For(Paired(httpsPort: null)));

        var retired = Paired();
        retired.Retire(ClinicRelayRetirement.Retired, "local|admin", T0.AddHours(1));
        Assert.Null(RelayBrowserAccess.For(retired));
    }

    // One port, written in three places: the PC's trust gate, the installer, and this card.
    [Fact]
    public void The_Trust_Port_Is_The_One_The_Pc_Listens_On()
    {
        Assert.Equal(ClinicManagement.API.Startup.TrustPortGate.DefaultPort, RelayBrowserAccess.TrustPort);
    }
}
