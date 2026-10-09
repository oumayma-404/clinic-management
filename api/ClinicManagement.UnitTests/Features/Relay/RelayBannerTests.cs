using ClinicManagement.Application.Features.Relay.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Relay;

/// <summary>
/// The cut's strip on every screen (<c>clinic-pc-copy</c> AC-3.4, AC-4.1, AC-5.2, EC-20, D20). What is held: the PC says
/// which of the two causes it is (the cabinet's internet, or the cloud), the cloud says « lecture seule » with the
/// moment, and nothing at all is shown while the cloud records the cabinet's work.
/// </summary>
public class RelayBannerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime T0 = new(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc); // 10:00 in Tunis

    private static RelayHeartbeat Beat(bool holding = false, DateTime? since = null, long underAck = 0) =>
        new(10, 100, true, 0, 0, null, false, "build-1", null, null, null, null, null, false,
            Holding: holding, HoldingSinceUtc: since, HoldingUnderAckSeq: underAck);

    private static ClinicRelay ArmedThenSilent()
    {
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", T0.AddDays(-1));
        relay.Pair("PC-ACCUEIL", "key", null, null, "build-1", T0.AddDays(-1));
        relay.RecordHeartbeat(Beat(), 10, T0);
        relay.RecordAckConfirmation(relay.IssueAck(armed: true, T0), armed: true);
        return relay;
    }

    // ---- on the PC de secours (AC-3.4, EC-20) -----------------------------------------------------------------------

    [Fact]
    public void The_Pc_Says_Internet_Coupe_When_The_Internet_Is_Down()
    {
        var banner = RelayBanners.OnThePc(handingBack: false, cutCause: "internet");

        Assert.Equal(RelayBanners.PcHolding, banner.Kind);
        Assert.Equal("Internet coupé", banner.Title);
        Assert.Contains("au retour d'internet", banner.Detail);
        Assert.True(banner.Warning);
    }

    [Fact]
    public void The_Pc_Says_The_Cloud_Is_Unreachable_When_The_Internet_Answers()
    {
        var banner = RelayBanners.OnThePc(handingBack: false, cutCause: "cloud");

        Assert.Equal(RelayBanners.PcHolding, banner.Kind);
        Assert.Equal("Le cloud est injoignable", banner.Title);
        Assert.Contains("à son retour", banner.Detail);
    }

    // Not judged yet (no probe, or the first judgment is still out): the internet is the likelier cause by far.
    [Fact]
    public void An_Unknown_Cause_Reads_As_Internet_Coupe()
    {
        Assert.Equal("Internet coupé", RelayBanners.OnThePc(handingBack: false, cutCause: null).Title);
    }

    [Fact]
    public void Returning_Outranks_The_Cause_And_Is_Not_A_Warning()
    {
        var banner = RelayBanners.OnThePc(handingBack: true, cutCause: "cloud");

        Assert.Equal(RelayBanners.PcReturning, banner.Kind);
        Assert.Equal("Retour au cloud en cours", banner.Title);
        Assert.False(banner.Warning);
    }

    // ---- on the cloud (AC-4.1) ----------------------------------------------------------------------------------------

    [Fact]
    public void Nothing_Is_Shown_While_The_Cloud_Records_The_Cabinets_Work()
    {
        Assert.Null(RelayBanners.OnTheCloud(null, T0));

        var relay = ArmedThenSilent();
        Assert.Null(RelayBanners.OnTheCloud(relay, T0.AddSeconds(30)));
    }

    [Fact]
    public void A_Pc_That_Said_It_Holds_Is_Named_With_The_Moment()
    {
        var relay = ArmedThenSilent();
        relay.RecordHeartbeat(Beat(holding: true, since: T0.AddMinutes(2), underAck: relay.LastAckSeq), 10, T0.AddMinutes(3));

        var banner = RelayBanners.OnTheCloud(relay, T0.AddMinutes(4));

        Assert.NotNull(banner);
        Assert.Equal(RelayBanners.CloudOnRelay, banner!.Kind);
        Assert.Equal("Le cabinet travaille sur le PC de secours depuis 10:02", banner.Title);
        Assert.Equal("ici, lecture seule. Données arrêtées à 10:02.", banner.Detail);
        Assert.True(banner.Warning);
    }

    [Fact]
    public void A_Silent_Pc_Is_Named_With_The_Lock_And_The_Way_Back()
    {
        var relay = ArmedThenSilent();
        var now = T0.AddMinutes(5);
        Assert.True(ClinicWriteLease.IsCloudFenced(relay, now));

        var banner = RelayBanners.OnTheCloud(relay, now);

        Assert.NotNull(banner);
        Assert.Equal(RelayBanners.CloudSilent, banner!.Kind);
        Assert.Equal("Le PC de secours ne répond plus depuis 10:01", banner.Title);
        Assert.Contains("Reprendre la main", banner.Detail);
    }

    [Fact]
    public void A_Cloud_Recovering_A_Gap_Says_So_Without_A_Warning()
    {
        var relay = ArmedThenSilent();
        relay.NoteFollowedEpoch("old", "new", pcAppliedSeq: 40, T0);

        var banner = RelayBanners.OnTheCloud(relay, T0.AddSeconds(10));

        Assert.NotNull(banner);
        Assert.Equal(RelayBanners.CloudRestoring, banner!.Kind);
        Assert.False(banner.Warning);
    }

    [Fact]
    public void A_Returning_Cut_Reads_Retour_Au_Cloud()
    {
        var relay = ArmedThenSilent();
        relay.RecordHeartbeat(Beat(holding: true, since: T0.AddMinutes(2), underAck: relay.LastAckSeq), 10, T0.AddMinutes(3));
        relay.RecordHandbackApplied(Guid.NewGuid(), T0.AddMinutes(2), T0.AddMinutes(20));

        var banner = RelayBanners.OnTheCloud(relay, T0.AddMinutes(20));

        Assert.NotNull(banner);
        Assert.Equal(RelayBanners.CloudReturning, banner!.Kind);
        Assert.False(banner.Warning);
    }
}
