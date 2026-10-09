using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Relay;

/// <summary>
/// Which PC de secours rows the admins' bell carries (<c>clinic-pc-copy</c> AC-2.2, EC-9, EC-10). Every instant is
/// fixed and stated in Tunisian time (UTC+1): 2026-10-07 is a Wednesday.
/// </summary>
public class RelayAlertRulesTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // Mon–Fri 08:30–18:00 with a 12:00–14:00 break; closed at the week-end.
    private const string Hours =
        "[{\"day\":\"Monday\",\"enabled\":true,\"from\":\"08:30\",\"to\":\"18:00\",\"breakFrom\":\"12:00\",\"breakTo\":\"14:00\"},"
        + "{\"day\":\"Tuesday\",\"enabled\":true,\"from\":\"08:30\",\"to\":\"18:00\",\"breakFrom\":\"12:00\",\"breakTo\":\"14:00\"},"
        + "{\"day\":\"Wednesday\",\"enabled\":true,\"from\":\"08:30\",\"to\":\"18:00\",\"breakFrom\":\"12:00\",\"breakTo\":\"14:00\"},"
        + "{\"day\":\"Thursday\",\"enabled\":true,\"from\":\"08:30\",\"to\":\"18:00\",\"breakFrom\":\"12:00\",\"breakTo\":\"14:00\"},"
        + "{\"day\":\"Friday\",\"enabled\":true,\"from\":\"08:30\",\"to\":\"18:00\",\"breakFrom\":\"12:00\",\"breakTo\":\"14:00\"},"
        + "{\"day\":\"Saturday\",\"enabled\":false,\"from\":\"\",\"to\":\"\"},"
        + "{\"day\":\"Sunday\",\"enabled\":false,\"from\":\"\",\"to\":\"\"}]";

    private static readonly RelayAlert[] None = Array.Empty<RelayAlert>();

    /// <summary>A Tunisian wall-clock moment as the UTC instant the job would see.</summary>
    private static DateTime Tunis(int day, int hour, int minute = 0) =>
        new DateTime(2026, 10, day, hour, minute, 0, DateTimeKind.Utc).AddHours(-1);

    private static RelayHeartbeat Beat(
        long applied = 10, DateTime? pcClock = null, long? diskFree = 100L * 1024 * 1024 * 1024,
        IReadOnlyList<string>? mismatch = null, bool stopped = false) =>
        new(applied, 100, true, 0, 0, diskFree, false, "1.0.0", pcClock, null, mismatch, null, null, stopped);

    /// <summary>A PC whose first copy finished early on Monday, last heard at <paramref name="lastBeat"/>.</summary>
    private static ClinicRelay Pc(DateTime lastBeat, RelayHeartbeat? beat = null, long highWater = 10, DateTime? lastReady = null)
    {
        var start = Tunis(5, 7);
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", start);
        relay.Pair("PC-ACCUEIL", "key", null, null, null, start);
        relay.RecordHeartbeat(Beat(), 10, lastReady ?? start.AddMinutes(1));
        relay.RecordHeartbeat(beat ?? Beat(), highWater, lastBeat);
        return relay;
    }

    private static IReadOnlyList<RelayAlert> Alerts(
        ClinicRelay? relay, DateTime now, IReadOnlyCollection<RelayAlert>? shown = null, string? hours = Hours) =>
        RelayAlertRules.Wanted(relay, hours, shown ?? None, now).Select(r => r.Alert).ToList();

    [Fact]
    public void A_Ready_Pc_And_No_Pc_Raise_Nothing()
    {
        var now = Tunis(7, 10);

        Assert.Empty(Alerts(Pc(now.AddSeconds(-5)), now));
        Assert.Empty(Alerts(null, now));
    }

    // AC-2.2 / FR-2: « après 15 min pendant les heures d'ouverture ».
    [Fact]
    public void A_Pc_Off_Inside_Opening_Hours_Is_Told_After_Fifteen_Minutes()
    {
        var off = Tunis(7, 10);

        Assert.Empty(Alerts(Pc(off), off + RelayAlertRules.Grace - TimeSpan.FromMinutes(1)));
        Assert.Equal(new[] { RelayAlert.Off }, Alerts(Pc(off), off + RelayAlertRules.Grace));
    }

    // Switched off at closing last night: the receptionist who switches it on five minutes after opening is not told.
    [Fact]
    public void The_Wait_Counts_From_The_Opening_Not_From_Last_Night()
    {
        var pc = Pc(Tunis(6, 18, 5));

        Assert.Empty(Alerts(pc, Tunis(7, 8, 44)));
        Assert.Equal(new[] { RelayAlert.Off }, Alerts(pc, Tunis(7, 8, 45)));
    }

    [Fact]
    public void After_The_Break_The_Wait_Counts_From_The_Break_End()
    {
        var pc = Pc(Tunis(7, 12, 30));

        Assert.Empty(Alerts(pc, Tunis(7, 13, 0)));      // during the break
        Assert.Empty(Alerts(pc, Tunis(7, 14, 10)));
        Assert.Equal(new[] { RelayAlert.Off }, Alerts(pc, Tunis(7, 14, 15)));
    }

    // A PC off at night or at the week-end is ordinary; telling it every evening teaches admins to ignore the bell.
    [Theory]
    [InlineData(7, 19, 0)]  // Wednesday evening
    [InlineData(10, 10, 0)] // Saturday, closed
    [InlineData(7, 7, 0)]   // before opening
    public void A_New_Problem_Outside_Opening_Hours_Is_Not_Told(int day, int hour, int minute)
    {
        var now = Tunis(day, hour, minute);

        Assert.Empty(Alerts(Pc(now.AddHours(-3)), now));
    }

    // Withdrawing a still-true row at closing would make the bell lie.
    [Fact]
    public void A_Row_Already_Shown_Stays_After_Closing()
    {
        var now = Tunis(7, 19);

        Assert.Equal(new[] { RelayAlert.Off }, Alerts(Pc(now.AddHours(-3)), now, new[] { RelayAlert.Off }));
    }

    // Late → Off: one problem that changed kind is told at once, not after a second fifteen minutes.
    [Fact]
    public void A_Shown_Problem_Changes_Kind_Without_A_Second_Wait()
    {
        var now = Tunis(7, 10);

        Assert.Equal(new[] { RelayAlert.Off }, Alerts(Pc(now.AddMinutes(-3)), now, new[] { RelayAlert.Late }));
    }

    [Fact]
    public void A_Copy_Behind_For_Fifteen_Minutes_Is_Late()
    {
        var now = Tunis(7, 10);
        var pc = Pc(now.AddSeconds(-5), Beat(applied: 10), highWater: 20, lastReady: now - RelayAlertRules.Grace);

        Assert.Equal(ClinicRelayState.Late, ClinicRelayHealth.Read(pc, now).State);
        Assert.Equal(new[] { RelayAlert.Late }, Alerts(pc, now));
    }

    // Neither of these comes and goes: there is nothing to wait out.
    [Fact]
    public void A_Full_Disk_And_A_Mismatch_Are_Told_At_Once_Inside_Opening_Hours()
    {
        var now = Tunis(7, 10);

        Assert.Equal(new[] { RelayAlert.DiskNearlyFull },
            Alerts(Pc(now.AddSeconds(-5), Beat(diskFree: ClinicRelayHealth.DiskNearlyFullBytes - 1)), now));
        Assert.Equal(new[] { RelayAlert.Mismatch },
            Alerts(Pc(now.AddSeconds(-5), Beat(mismatch: new[] { "Patients" })), now));
        Assert.Empty(Alerts(Pc(Tunis(10, 10, 0), Beat(mismatch: new[] { "Patients" })), Tunis(10, 10, 0).AddSeconds(5)));
    }

    // AC-9.4 and EC-9 are not the ordinary state of a PC at night, so they are told at any hour — Sunday 03:00 here.
    [Fact]
    public void A_Stopped_Copy_And_An_Abandoned_Setup_Are_Told_At_Any_Hour()
    {
        var sunday = Tunis(11, 3);

        var stopped = Pc(sunday.AddSeconds(-5), Beat(applied: 30, stopped: true));
        Assert.Equal(ClinicRelayState.Stopped, ClinicRelayHealth.Read(stopped, sunday).State);
        Assert.Equal(new[] { RelayAlert.Stopped }, Alerts(stopped, sunday));

        var (setup, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", sunday.AddDays(-2));
        setup.Pair("PC-ACCUEIL", "key", null, null, null, sunday.AddDays(-2));
        var abandoned = RelayAlertRules.Wanted(setup, Hours, None, sunday);
        Assert.Equal(RelayAlert.Abandoned, Assert.Single(abandoned).Alert);
        Assert.Contains("Installation abandonnée sur PC-ACCUEIL", abandoned[0].Message);
    }

    // EC-10 at the next contact; D20b's 30 s threshold.
    [Theory]
    [InlineData(31, true)]
    [InlineData(-45, true)]
    [InlineData(30, false)]
    public void A_Clock_Off_By_More_Than_Thirty_Seconds_Is_Told(int skewSeconds, bool told)
    {
        var sunday = Tunis(11, 3);
        var beatAt = sunday.AddSeconds(-5);
        var pc = Pc(beatAt, Beat(pcClock: beatAt.AddSeconds(skewSeconds)));

        var rows = RelayAlertRules.Wanted(pc, Hours, None, sunday);

        Assert.Equal(told, rows.Any(r => r.Alert == RelayAlert.ClockWrong));
        if (told)
        {
            Assert.Contains("L'horloge de PC-ACCUEIL est fausse", rows.Single().Message);
        }
    }

    // « No hours » means « no booking restriction » to the agenda, but it must not mean « never open » here.
    [Theory]
    [InlineData(null)]
    [InlineData("pas du json")]
    public void A_Cabinet_Without_Readable_Hours_Is_Watched_Monday_To_Saturday_Eight_To_Six(string? hours)
    {
        var saturday = Tunis(10, 10);
        Assert.Equal(new[] { RelayAlert.Off }, Alerts(Pc(saturday.AddHours(-1)), saturday, hours: hours));

        var sunday = Tunis(11, 10);
        Assert.Empty(Alerts(Pc(sunday.AddHours(-1)), sunday, hours: hours));
    }

    [Fact]
    public void Every_Row_Names_The_Pc()
    {
        var now = Tunis(7, 10);
        var row = Assert.Single(RelayAlertRules.Wanted(Pc(now.AddHours(-1)), Hours, None, now));

        Assert.Equal("PC de secours éteint", row.Title);
        Assert.Contains("PC-ACCUEIL est éteint depuis 09:00", row.Message);
    }
}
