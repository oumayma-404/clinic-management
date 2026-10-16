using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using ClinicManagement.Infrastructure.Relay;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Relay;

/// <summary>
/// <c>clinic-pc-copy</c> D20b (FR-3, EC-10, AC-3.11): the PC de secours keeps Windows' clock on the cloud's — admins are
/// told it was wrong — or, when it cannot, is not ready and never takes over; and Windows Update's active hours cover the
/// cabinet's opening hours.
/// </summary>
public class RelayClockTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime T0 = new(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc);

    // ---- the rule --------------------------------------------------------------------------------------------------

    [Fact]
    public void The_Offset_Counts_Half_The_Round_Trip_And_A_Slow_Answer_Says_Nothing()
    {
        var offset = RelayClockRules.Offset(T0, T0.AddMinutes(-2), TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(121), offset);
        Assert.True(RelayClockRules.IsWrong(offset!.Value));

        Assert.False(RelayClockRules.IsWrong(TimeSpan.FromSeconds(30)));
        Assert.Null(RelayClockRules.Offset(T0, T0.AddDays(-1), TimeSpan.FromSeconds(6)));
    }

    [Theory]
    [InlineData(45, "45 s")]
    [InlineData(-180, "3 min")]
    [InlineData(3900, "1 h 05")]
    [InlineData(-86400, "1 j")]
    public void How_Far_Off_Reads_In_French(int seconds, string expected)
    {
        Assert.Equal(expected, RelayLabels.ClockOffset(seconds));
    }

    // The privilege call is real (it only asks for the right — it never moves the clock): it never throws, and when this
    // account lacks the right, or this is not Windows, it says why in French.
    [Fact]
    public void Asking_For_The_Right_To_Set_The_Clock_Never_Throws_And_Says_Why_It_Failed()
    {
        var granted = WindowsSystemClock.TryEnablePrivilege(out var error);

        Assert.True(granted ? error is null : !string.IsNullOrWhiteSpace(error));
    }

    // ---- the cloud -------------------------------------------------------------------------------------------------

    private static RelayHeartbeat Beat(DateTime? correctedAt = null, int? by = null, bool unfixable = false) =>
        new(10, 100, true, 0, 0, 100L * 1024 * 1024 * 1024, false, "1.4.0", null, null, null, null,
            ClockCorrectedAtUtc: correctedAt, ClockCorrectedBySeconds: by, ClockUnfixable: unfixable);

    private static ClinicRelay Ready()
    {
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", T0.AddDays(-1));
        relay.Pair("PC-ACCUEIL", "key", null, null, null, T0.AddDays(-1));
        relay.RecordHeartbeat(Beat(), 10, T0.AddMinutes(-1));
        return relay;
    }

    // [FR-3] A PC whose clock stays wrong is not ready: never « Prêt », never armed — so it never takes over — and the
    // admins are told why; the first heartbeat that says it is right again makes it ready.
    [Fact]
    public void A_Pc_That_Cannot_Set_Its_Clock_Is_Not_Ready_Nor_Armed_And_Admins_Are_Told()
    {
        var relay = Ready();
        relay.RecordHeartbeat(Beat(unfixable: true), 10, T0);

        Assert.Equal(ClinicRelayState.ClockWrong, ClinicRelayHealth.Read(relay, T0).State);
        Assert.False(ClinicWriteLease.ShouldArm(relay, sameBuild: true, wantsToStandDown: false, T0));
        var row = Assert.Single(RelayAlertRules.Wanted(relay, null, Array.Empty<RelayAlert>(), T0), r => r.Alert == RelayAlert.ClockWrong);
        Assert.Contains("ne prendra pas le relais", row.Message);
        Assert.Equal("clock-wrong", RelayLabels.Key(ClinicRelayState.ClockWrong));

        relay.RecordHeartbeat(Beat(), 10, T0.AddSeconds(10));
        Assert.Equal(ClinicRelayState.Ready, ClinicRelayHealth.Read(relay, T0.AddSeconds(10)).State);
    }

    // [EC-10] « L'horloge de PC-ACCUEIL est fausse » at the next contact — the PC has already set it, so admins are told,
    // for a day, not asked to do anything.
    [Fact]
    public void A_Clock_The_Pc_Set_Is_Told_For_A_Day()
    {
        var relay = Ready();
        relay.RecordHeartbeat(Beat(correctedAt: T0, by: -86400), 10, T0);

        Assert.Equal(ClinicRelayState.Ready, ClinicRelayHealth.Read(relay, T0).State);
        var row = Assert.Single(RelayAlertRules.Wanted(relay, null, Array.Empty<RelayAlert>(), T0.AddMinutes(5)),
            r => r.Alert == RelayAlert.ClockWrong);
        Assert.Equal("L'horloge de PC-ACCUEIL était fausse de 1 j : elle a été remise à l'heure du cloud (10:00).", row.Message);
        Assert.DoesNotContain(RelayAlertRules.Wanted(relay, null, Array.Empty<RelayAlert>(), T0 + RelayAlertRules.ClockCorrectionToldFor),
            r => r.Alert == RelayAlert.ClockWrong);
    }

    // ---- Windows Update (AC-3.11) ------------------------------------------------------------------------------------

    [Fact]
    public void Windows_Update_Is_Held_Over_The_Opening_Hours_With_An_Hour_Each_Side()
    {
        // A cabinet that never set its hours: 08:00–18:00, six days.
        Assert.Equal(new RelayActiveHours(7, 19), RelayUpdateHours.Window(null));

        var hours = """
            [{"day":"Monday","enabled":true,"from":"08:30","to":"20:15"},
             {"day":"Saturday","enabled":true,"from":"09:00","to":"13:00"},
             {"day":"Sunday","enabled":false,"from":"06:00","to":"23:00"}]
            """;
        Assert.Equal(new RelayActiveHours(7, 22), RelayUpdateHours.Window(hours));
    }

    [Fact]
    public void A_Window_Longer_Than_Windows_Allows_Is_Capped_And_A_Closed_Cabinet_Holds_Nothing()
    {
        var long_ = """[{"day":"Monday","enabled":true,"from":"05:00","to":"23:30"}]""";
        Assert.Equal(new RelayActiveHours(4, 22), RelayUpdateHours.Window(long_));

        var closed = """[{"day":"Monday","enabled":false,"from":"08:00","to":"18:00"}]""";
        Assert.Null(RelayUpdateHours.Window(closed));
    }
}
