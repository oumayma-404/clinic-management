using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Application.Features.Relay.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Relay;

/// <summary>
/// The card during and after a cut (<c>clinic-pc-copy</c> FR-2 « En relève », « Retour au cloud »; AC-6.2 / US-7's
/// « repris »). What is held: a PC that said it holds is never « Éteint », however long it is silent; the bell rings
/// for neither state (the banners say it); the vendor still hears of a PC silent for a day; an ack while the PC holds
/// never arms it; and the card says who took the saves back until that PC speaks again.
/// </summary>
public class RelayCardStatesTests
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

    private static ClinicRelay Holding()
    {
        var relay = ArmedThenSilent();
        relay.RecordHeartbeat(Beat(holding: true, since: T0.AddMinutes(2), underAck: relay.LastAckSeq), 10, T0.AddMinutes(3));
        return relay;
    }

    // ---- « En relève » ------------------------------------------------------------------------------------------

    [Fact]
    public void A_Pc_That_Said_It_Holds_Is_In_Charge_And_Says_Since_When()
    {
        var relay = Holding();

        var reading = ClinicRelayHealth.Read(relay, T0.AddMinutes(4));

        Assert.Equal(ClinicRelayState.InCharge, reading.State);
        Assert.False(reading.IsProblem);
        Assert.Equal("in-charge", RelayLabels.Key(reading.State));
        Assert.Equal("Le cabinet travaille sur ce PC depuis 10:02", RelayLabels.Sentence(reading, relay, T0.AddMinutes(4)));
        Assert.Equal("En relève depuis 10:02", RelayLabels.Short(reading, T0.AddMinutes(4)));
    }

    // Silent since (the cabinet's internet dropped again): still in charge, never « Éteint » — the lock says the rest.
    [Fact]
    public void A_Pc_In_Charge_Is_Never_Read_As_Off_However_Silent()
    {
        var reading = ClinicRelayHealth.Read(Holding(), T0.AddHours(3));

        Assert.Equal(ClinicRelayState.InCharge, reading.State);
    }

    [Fact]
    public void The_Bell_Rings_For_Neither_State()
    {
        var holding = Holding();
        Assert.DoesNotContain(RelayAlertRules.Wanted(holding, null, Array.Empty<RelayAlert>(), T0.AddHours(1)),
            r => RelayAlertRules.IsNotReady(r.Alert));
    }

    [Fact]
    public void The_Vendor_Still_Hears_Of_A_Pc_In_Charge_Silent_For_A_Day()
    {
        var holding = Holding();

        Assert.DoesNotContain(RelayIncidentKind.Unseen,
            RelayVendorAlertRules.Due(holding, null, new HashSet<RelayIncidentKind>(), T0.AddHours(2)));
        Assert.Contains(RelayIncidentKind.Unseen,
            RelayVendorAlertRules.Due(holding, null, new HashSet<RelayIncidentKind>(), T0.AddHours(25)));
    }

    // While it holds, the PC is not « Prêt »: an ack then is never « armé » — it is already in charge.
    [Fact]
    public void An_Ack_While_The_Pc_Holds_Never_Arms_It()
    {
        var holding = Holding();

        Assert.False(ClinicWriteLease.ShouldArm(holding, sameBuild: true, wantsToStandDown: false, T0.AddMinutes(4)));
    }

    // ---- « Retour au cloud » ------------------------------------------------------------------------------------

    [Fact]
    public void A_Cut_Handed_Back_Reads_Retour_Au_Cloud_Until_The_Pc_Lets_Go()
    {
        var relay = Holding();
        relay.RecordHandbackApplied(Guid.NewGuid(), T0.AddMinutes(2), T0.AddMinutes(20));

        var reading = ClinicRelayHealth.Read(relay, T0.AddMinutes(20));

        Assert.Equal(ClinicRelayState.Returning, reading.State);
        Assert.False(reading.IsProblem);
        Assert.Equal("Retour au cloud en cours", RelayLabels.Sentence(reading, relay, T0.AddMinutes(20)));
    }

    // ---- « repris » ---------------------------------------------------------------------------------------------

    [Fact]
    public void The_Card_Says_The_Cabinets_Devices_Took_The_Saves_Back()
    {
        var relay = ArmedThenSilent();
        relay.Reclaim(ClinicRelay.ReclaimedByDevices, T0.AddMinutes(3));

        Assert.Equal("Repris par les appareils du cabinet à 10:03 : ils joignaient le cloud, plus le PC de secours.",
            RelayLabels.Reclaimed(relay, T0.AddMinutes(5)));
        Assert.Equal(RelayLabels.Reclaimed(relay, T0.AddMinutes(5)),
            GetRelayStatusQueryHandler.ToDto(relay, T0.AddMinutes(5)).ReclaimSentence);
    }

    [Fact]
    public void The_Card_Says_An_Administrator_Took_The_Saves_Back()
    {
        var relay = ArmedThenSilent();
        relay.Reclaim("local|admin", T0.AddMinutes(10));

        Assert.Equal("Un administrateur a repris la main à 10:10.", RelayLabels.Reclaimed(relay, T0.AddMinutes(11)));
    }

    // Once the PC is heard from again, the line goes: its own state (« Prêt », « Copie arrêtée ») says what follows.
    [Fact]
    public void The_Line_Goes_Once_The_Pc_Speaks_Again()
    {
        var relay = ArmedThenSilent();
        relay.Reclaim(ClinicRelay.ReclaimedByDevices, T0.AddMinutes(3));
        relay.RecordHeartbeat(Beat(), 10, T0.AddMinutes(30));

        Assert.Null(RelayLabels.Reclaimed(relay, T0.AddMinutes(31)));
        Assert.Null(RelayLabels.Reclaimed(ArmedThenSilent(), T0.AddMinutes(31)));
    }
}
