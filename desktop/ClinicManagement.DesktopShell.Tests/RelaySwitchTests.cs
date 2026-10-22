using System;
using ClinicManagement.DesktopShell;
using Xunit;

namespace ClinicManagement.DesktopShell.Tests;

/// <summary>
/// The app following the PC de secours (<c>clinic-pc-copy</c> Part 3): what the page may hand the shell, what the PC's
/// answer must carry, what the waiting screen says, and that the PC's own origin stays inside the app.
/// </summary>
public class RelaySwitchTests
{
    private const string Fingerprint = "DD12C3785C42A1B2C3D4E5F60718293A4B5C6D7E8F90A1B2C3D4E5F60718293A";

    private static ServerConfig Hosted() => new() { Host = "app.apexa.tn", Port = 443, PortIsExplicit = true };

    private static readonly RelaySwitch.Target Pc = new("192.168.1.35", 5096, Fingerprint);

    [Fact]
    public void A_Prepare_Request_Carries_A_Ticket_And_Where_The_Pc_Is()
    {
        var parsed = RelaySwitch.ParsePrepare(
            $$"""{"assertion":"ra1.abc.def","addresses":["192.168.1.35","8.8.8.8"],"port":5096,"fingerprint":"{{Fingerprint}}"}""");

        Assert.NotNull(parsed);
        Assert.Equal("ra1.abc.def", parsed!.Assertion);
        Assert.Equal("192.168.1.35", Assert.Single(parsed.Where.Addresses).ToString()); // a public host is never tried
        Assert.Equal(5096, parsed.Where.Port);
    }

    [Theory]
    [InlineData("""{"assertion":"not-a-ticket","addresses":["192.168.1.35"],"port":5096,"fingerprint":"DD12C3785C42A1B2C3D4E5F60718293A4B5C6D7E8F90A1B2C3D4E5F60718293A"}""")]
    [InlineData("""{"addresses":["192.168.1.35"],"port":5096,"fingerprint":"DD12C3785C42A1B2C3D4E5F60718293A4B5C6D7E8F90A1B2C3D4E5F60718293A"}""")]
    [InlineData("""{"assertion":"ra1.a.b","addresses":["8.8.8.8"],"port":5096,"fingerprint":"DD12C3785C42A1B2C3D4E5F60718293A4B5C6D7E8F90A1B2C3D4E5F60718293A"}""")]
    [InlineData("""{"assertion":"ra1.a.b","addresses":["192.168.1.35"],"port":5096,"fingerprint":"short"}""")]
    [InlineData("not json")]
    public void Anything_Else_Is_Refused(string json)
    {
        Assert.Null(RelaySwitch.ParsePrepare(json));
    }

    [Fact]
    public void The_Pcs_Session_Is_Read_Inside_Or_Outside_The_Apis_Envelope()
    {
        var wrapped = RelaySwitch.ParseSession(
            """{"isSuccess":true,"value":{"accessToken":"a","refreshToken":"r-1","refreshExpiresAt":"2026-11-08T09:00:00Z","mustChangePassword":false}}""");
        var bare = RelaySwitch.ParseSession("""{"refreshToken":"r-2","mustChangePassword":true}""");

        Assert.Equal("r-1", wrapped!.Credential);
        Assert.Equal(new DateTime(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc), wrapped.ExpiresAtUtc);
        Assert.False(wrapped.MustChangePassword);
        Assert.Equal("r-2", bare!.Credential);
        Assert.True(bare.MustChangePassword);
        Assert.Null(RelaySwitch.ParseSession("""{"accessToken":"only"}"""));
        Assert.Null(RelaySwitch.ParseSession("<html>"));
    }

    // Manual takeover (owner, 2026-10-12): when the PC will be available, then the offer; « pas prêt » at once.
    [Fact]
    public void The_Waiting_Line_Counts_Down_Then_Offers_The_Button_And_Says_Not_Ready_At_Once()
    {
        var lost = new DateTime(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);
        Assert.Equal(25, RelaySwitch.SecondsLeft(lost, lost));
        Assert.Equal(20, RelaySwitch.SecondsLeft(lost, lost.AddSeconds(7)));
        Assert.Equal(0, RelaySwitch.SecondsLeft(lost, lost.AddSeconds(40)));

        Assert.Equal("Internet coupé — le PC de secours sera disponible dans 15 secondes.",
            RelaySwitch.WaitingLine(armed: true, canTakeOver: false, secondsLeft: 15));
        Assert.Equal("Internet coupé — le PC de secours sera disponible dans quelques secondes.",
            RelaySwitch.WaitingLine(armed: null, canTakeOver: false, secondsLeft: 0));
        Assert.Equal(RelaySwitch.OfferLine, RelaySwitch.WaitingLine(armed: true, canTakeOver: true, secondsLeft: 10));
        Assert.Equal(RelaySwitch.NotReadyLine, RelaySwitch.WaitingLine(armed: false, canTakeOver: false, secondsLeft: 20));
    }

    // Opened in the browser, the PC would be a login screen behind a certificate warning.
    [Fact]
    public void The_Pcs_Own_Origin_Stays_In_The_App_And_Nothing_Else_On_That_Machine_Does()
    {
        Assert.Equal(NavigationDisposition.LoadInShell,
            ExternalNavigation.DispositionFor("https://192.168.1.35:5096/patients", Hosted(), Pc));
        Assert.True(ExternalNavigation.IsClinicDocument("https://192.168.1.35:5096/", Hosted(), Pc));
        Assert.Equal(NavigationDisposition.OpenInBrowser,
            ExternalNavigation.DispositionFor("https://192.168.1.35:8080/", Hosted(), Pc));
        Assert.Equal(NavigationDisposition.OpenInBrowser,
            ExternalNavigation.DispositionFor("https://192.168.1.36:5096/", Hosted(), Pc));
    }

    [Fact]
    public void An_Ipv6_Pc_Is_Addressed_In_Brackets()
    {
        var pc = new RelaySwitch.Target("fd00::35", 5096, Fingerprint);

        Assert.Equal("https://[fd00::35]:5096", pc.Origin);
        Assert.True(RelaySwitch.IsPcOrigin(new Uri("https://[fd00::35]:5096/"), pc));
    }
}
