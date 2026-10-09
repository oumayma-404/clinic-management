using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Relay;

/// <summary>
/// When the vendor is e-mailed about a PC de secours (<c>clinic-pc-copy</c> AC-9.2, AC-9.4), and what the e-mail says.
/// Instants are fixed and stated in Tunisian time; 2026-10-07 is a Wednesday, 2026-10-11 a Sunday.
/// </summary>
public class RelayVendorAlertRulesTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly RelayIncidentKind[] None = Array.Empty<RelayIncidentKind>();

    private static DateTime Tunis(int day, int hour, int minute = 0) =>
        new DateTime(2026, 10, day, hour, minute, 0, DateTimeKind.Utc).AddHours(-1);

    private static RelayHeartbeat Beat(long applied = 10, IReadOnlyList<string>? mismatch = null, bool stopped = false) =>
        new(applied, 100, true, 0, 0, 100L * 1024 * 1024 * 1024, false, "1.0.0", null, null, mismatch, null, null, stopped);

    private static ClinicRelay Pc(DateTime lastBeat, RelayHeartbeat? beat = null, long highWater = 10, DateTime? lastReady = null)
    {
        var start = Tunis(1, 7);
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", start);
        relay.Pair("PC-ACCUEIL", "key", null, null, null, start);
        relay.RecordHeartbeat(Beat(), 10, lastReady ?? start.AddMinutes(1));
        relay.RecordHeartbeat(beat ?? Beat(), highWater, lastBeat);
        return relay;
    }

    private static IReadOnlyList<RelayIncidentKind> Due(ClinicRelay? pc, DateTime now, params RelayIncidentKind[] open) =>
        RelayVendorAlertRules.Due(pc, null, open, now);

    [Fact]
    public void A_Ready_Pc_And_A_Pc_Off_For_An_Hour_Tell_The_Vendor_Nothing()
    {
        var now = Tunis(7, 10);

        Assert.Empty(Due(Pc(now.AddSeconds(-5)), now));
        Assert.Empty(Due(Pc(now.AddHours(-1)), now));
        Assert.Empty(Due(null, now));
    }

    // AC-9.2: « unseen for 24 h » — at any hour, Sunday night included.
    [Fact]
    public void A_Pc_Unseen_For_24_Hours_Is_Told_At_Any_Hour()
    {
        var now = Tunis(11, 3);

        Assert.Empty(Due(Pc(now - RelayVendorAlertRules.UnseenAfter + TimeSpan.FromMinutes(1)), now));
        Assert.Equal(new[] { RelayIncidentKind.Unseen }, Due(Pc(now - RelayVendorAlertRules.UnseenAfter), now));
    }

    // « behind by more than 15 min during opening hours » — the admins' rule; an episode already open lasts past closing.
    [Fact]
    public void A_Late_Copy_Is_Told_Inside_Opening_Hours_And_Kept_After_Closing()
    {
        var inHours = Tunis(7, 10);
        var late = Pc(inHours.AddSeconds(-5), Beat(applied: 10), highWater: 20, lastReady: inHours.AddMinutes(-15));
        Assert.Equal(new[] { RelayIncidentKind.Late }, Due(late, inHours));

        var evening = Tunis(7, 20);
        var lateAtNight = Pc(evening.AddSeconds(-5), Beat(applied: 10), highWater: 20, lastReady: evening.AddHours(-1));
        Assert.Empty(Due(lateAtNight, evening));
        Assert.Equal(new[] { RelayIncidentKind.Late }, Due(lateAtNight, evening, RelayIncidentKind.Late));
    }

    // « not matching after its own repair », and AC-9.4's stopped copy — both at any hour.
    [Fact]
    public void A_Mismatch_And_A_Stopped_Copy_Are_Told_At_Any_Hour()
    {
        var sunday = Tunis(11, 3);

        Assert.Equal(new[] { RelayIncidentKind.Mismatch },
            Due(Pc(sunday.AddSeconds(-5), Beat(mismatch: new[] { "Patients" })), sunday));
        Assert.Equal(new[] { RelayIncidentKind.Stopped },
            Due(Pc(sunday.AddSeconds(-5), Beat(applied: 30, stopped: true)), sunday));
    }

    [Fact]
    public void The_Email_Names_The_Cabinet_And_The_Problem()
    {
        var now = Tunis(11, 3);
        var subject = RelayVendorAlertEmail.Subject("Cabinet Ben Ali", RelayIncidentKind.Stopped);
        var content = RelayVendorAlertEmail.Compose(
            "Cabinet Ben Ali", ClinicId, RelayIncidentKind.Stopped, now.AddMinutes(-5), now);

        Assert.Equal("PC de secours — Cabinet Ben Ali : copie arrêtée", subject);
        Assert.Contains(content.Intro, line => line.Contains("Cabinet Ben Ali") && line.Contains("ne rien perdre"));
        Assert.Contains(content.Outro, line => line.Contains("n'effacez pas"));
        Assert.Contains(content.Details, d => d.Label == "Depuis" && d.Value == "11/10/2026 à 02:55");
        Assert.Contains(content.Details, d => d.Label == "Identifiant du cabinet" && d.Value == ClinicId.ToString("D"));

        // Found live: « du cabinet Cabinet sans nom » — most cabinets are NAMED « Cabinet … », so the text never says it.
        foreach (var kind in Enum.GetValues<RelayIncidentKind>())
        {
            var mail = RelayVendorAlertEmail.Compose("Cabinet Ben Ali", ClinicId, kind, null, now);
            Assert.False(string.IsNullOrWhiteSpace(mail.Intro[0]));
            Assert.DoesNotContain("cabinet Cabinet", string.Join(" ", mail.Intro.Append(mail.Preheader)));
        }
    }

    // Found live: a cabinet with a blank name produced « PC de secours —  : copie arrêtée », naming nobody.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_Cabinet_With_No_Name_Is_Still_Named(string? name)
    {
        Assert.Equal("PC de secours — Cabinet sans nom : copie arrêtée",
            RelayVendorAlertEmail.Subject(name, RelayIncidentKind.Stopped));
        Assert.Contains(RelayVendorAlertEmail.Compose(name, ClinicId, RelayIncidentKind.Stopped, null, Tunis(11, 3)).Details,
            d => d.Label == "Cabinet" && d.Value == RelayVendorAlertEmail.UnnamedClinic);
    }

    // One channel for every vendor alert: the addresses the operator named, else every active console account.
    [Fact]
    public async Task Named_Addresses_Win_Over_The_Console_Accounts()
    {
        var accounts = new Mock<IPlatformAccountRepository>();
        accounts.Setup(a => a.GetActiveEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "compte@apexa.tn" });

        Assert.Equal(new[] { "alertes@apexa.tn", "astreinte@apexa.tn" },
            await VendorAlertRecipients.ResolveAsync(" alertes@apexa.tn, astreinte@apexa.tn ,ALERTES@apexa.tn", accounts.Object));
        Assert.Equal(new[] { "compte@apexa.tn" }, await VendorAlertRecipients.ResolveAsync(null, accounts.Object));
        Assert.Equal(new[] { "compte@apexa.tn" }, await VendorAlertRecipients.ResolveAsync(" , ", accounts.Object));
    }
}
