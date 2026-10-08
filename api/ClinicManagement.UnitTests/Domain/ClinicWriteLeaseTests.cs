using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// Who may record a cabinet's work (<c>clinic-pc-copy</c> FR-3, D13, D14). The simulation comes first because it is
/// the property the whole of Part 2 rests on — the cloud and the PC are never both writable — tried over hundreds of
/// lossy lines; the cases after it pin each rule the property is made of.
/// </summary>
public class ClinicWriteLeaseTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime T0 = new(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc);

    private static ClinicRelay NewRelay()
    {
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", T0.AddDays(-1));
        relay.Pair("PC-ACCUEIL", "key", null, null, "build-1", T0.AddDays(-1));
        return relay;
    }

    private static bool Fenced(ClinicRelay relay, DateTime now) => ClinicWriteLease.IsCloudFenced(relay, now);

    // ---- never both writable --------------------------------------------------------------------------------------

    /// <summary>How the line behaves after the cut: nothing, the answers lost, the requests lost, or coin flips.</summary>
    public enum Line { Cut, AnswersLost, RequestsLost, Flaky }

    public static IEnumerable<object[]> Lines() =>
        from line in Enum.GetValues<Line>()
        from seed in Enumerable.Range(0, 60)
        select new object[] { line, seed };

    /// <summary>
    /// The PC heartbeats every ~10 s; each request and each answer may be lost or delayed (≤ 2 s each way). The cloud
    /// confirms and acks as the real handler does, arming most acks. From a random moment the line degrades. Every
    /// 100 ms: the cloud is writable unless fenced; the PC holds once its takeover rule fires (and, holding, stops
    /// talking — a PC in charge is in a cut). They are never both writable.
    /// </summary>
    [Theory]
    [MemberData(nameof(Lines))]
    public void The_Cloud_And_The_Pc_Are_Never_Both_Writable(Line line, int seed)
    {
        var random = new Random(seed * 31 + (int)line);
        var relay = NewRelay();
        var degradeAt = TimeSpan.FromSeconds(30 + random.Next(0, 600));
        var end = degradeAt + TimeSpan.FromMinutes(5);

        long pcAckSeq = 0;
        var pcAckArmed = false;
        TimeSpan? pcAckReceivedAt = null;
        var pcHolds = false;
        var nextHeartbeat = TimeSpan.Zero;

        // Exchanges in flight: (cloud receives at, PC receives the answer at or null when lost, what the PC confirmed).
        var inFlight = new List<(TimeSpan AtCloud, TimeSpan? AnswerAt, long Confirmed, bool ConfirmedArmed, int Id)>();
        var answers = new Dictionary<int, (long Seq, bool Armed)>();
        var exchange = 0;

        for (var t = TimeSpan.Zero; t <= end; t += TimeSpan.FromMilliseconds(100))
        {
            var degraded = t >= degradeAt;

            if (!pcHolds && t >= nextHeartbeat)
            {
                var requestArrives = !degraded
                    ? random.NextDouble() < 0.95
                    : line switch { Line.AnswersLost => true, Line.Flaky => random.NextDouble() < 0.5, _ => false };
                var answerArrives = !degraded
                    ? random.NextDouble() < 0.95
                    : line == Line.Flaky && random.NextDouble() < 0.5;
                if (requestArrives)
                {
                    var atCloud = t + TimeSpan.FromMilliseconds(random.Next(0, 2000));
                    TimeSpan? answerAt = answerArrives ? atCloud + TimeSpan.FromMilliseconds(random.Next(0, 2000)) : null;
                    inFlight.Add((atCloud, answerAt, pcAckSeq, pcAckArmed, ++exchange));
                }

                nextHeartbeat = t + TimeSpan.FromMilliseconds(10_000 + random.Next(-500, 500));
            }

            foreach (var x in inFlight.Where(x => x.AtCloud <= t && !answers.ContainsKey(x.Id)).ToList())
            {
                relay.RecordAckConfirmation(x.Confirmed, x.ConfirmedArmed);
                var armed = random.NextDouble() < 0.9;
                answers[x.Id] = (relay.IssueAck(armed, T0 + x.AtCloud), armed);
            }

            foreach (var x in inFlight.Where(x => x.AnswerAt is { } at && at <= t && answers.ContainsKey(x.Id)).ToList())
            {
                inFlight.Remove(x);
                if (!pcHolds && answers[x.Id].Seq > pcAckSeq)
                {
                    (pcAckSeq, pcAckArmed, pcAckReceivedAt) = (answers[x.Id].Seq, answers[x.Id].Armed, x.AnswerAt);
                }
            }

            inFlight.RemoveAll(x => x.AnswerAt is null && answers.ContainsKey(x.Id));

            if (!pcHolds && pcAckReceivedAt is { } received
                && ClinicWriteLease.PcMayTakeOver(t - received, pcAckArmed, boxAnswers: true))
            {
                pcHolds = true;
            }

            var cloudWritable = !Fenced(relay, T0 + t);
            Assert.False(cloudWritable && pcHolds, $"Both writable at {t} on a {line} line (seed {seed}).");
        }

        // Not trivially safe: on a line that dies after an armed answer, the PC does take over.
        if (line == Line.Cut && pcAckArmed)
        {
            Assert.True(pcHolds, $"An armed PC on a dead line never took over (seed {seed}).");
        }
    }

    // [AC-6.1, D14] A PC that stood down properly and then went quiet never locks the cabinet.
    [Fact]
    public void A_Pc_That_Stood_Down_Leaves_The_Cloud_Writable_However_Long_It_Is_Gone()
    {
        var relay = NewRelay();
        relay.RecordAckConfirmation(relay.IssueAck(armed: true, T0), armed: true);
        var ask = relay.IssueAck(armed: false, T0.AddSeconds(10));
        relay.RecordAckConfirmation(ask, armed: false);
        relay.IssueAck(armed: false, T0.AddSeconds(11));

        Assert.False(Fenced(relay, T0.AddHours(3)));
    }

    // [D14] Until the PC has CONFIRMED the disarm, it may still hold the armed ack before it.
    [Fact]
    public void A_Disarm_Counts_Only_Once_The_Pc_Confirmed_It()
    {
        var relay = NewRelay();
        relay.RecordAckConfirmation(relay.IssueAck(armed: true, T0), armed: true);
        relay.IssueAck(armed: false, T0.AddSeconds(10));

        Assert.True(Fenced(relay, T0.AddSeconds(61)));
    }

    // ---- the cloud's clock ----------------------------------------------------------------------------------------

    [Fact]
    public void The_Cloud_Fences_Sixty_Seconds_After_It_Sent_The_Ack_The_Pc_Confirmed()
    {
        var relay = NewRelay();
        relay.RecordAckConfirmation(relay.IssueAck(armed: true, T0), armed: true);
        // Heartbeats keep arriving, but every answer is lost: the PC keeps confirming the same ack.
        relay.IssueAck(armed: true, T0.AddSeconds(10));
        relay.IssueAck(armed: true, T0.AddSeconds(20));

        Assert.False(Fenced(relay, T0.AddSeconds(60)));
        Assert.True(Fenced(relay, T0.AddSeconds(60.1)));
    }

    [Fact]
    public void Nothing_Fences_A_Cabinet_Whose_Pc_Was_Never_Armed_Or_Is_Retired_Or_Absent()
    {
        var neverArmed = NewRelay();
        neverArmed.RecordAckConfirmation(neverArmed.IssueAck(armed: false, T0), armed: false);
        var retired = NewRelay();
        retired.RecordAckConfirmation(retired.IssueAck(armed: true, T0), armed: true);
        retired.Retire(ClinicRelayRetirement.Retired, "local|admin", T0.AddSeconds(5));

        Assert.False(Fenced(neverArmed, T0.AddHours(1)));
        Assert.False(Fenced(retired, T0.AddHours(1)));
        Assert.False(ClinicWriteLease.IsCloudFenced(null, T0.AddHours(1)));
    }

    // A PC coming back after a stand-down is armed again with no false fence: the clock starts at the first armed ack
    // sent since what it confirmed, not at that (hours-old) confirmation.
    [Fact]
    public void A_Pc_Rearmed_After_A_Stand_Down_Is_Not_Fenced_By_Its_Old_Confirmation()
    {
        var relay = NewRelay();
        relay.RecordAckConfirmation(relay.IssueAck(armed: false, T0), armed: false);

        relay.IssueAck(armed: true, T0.AddHours(2));

        Assert.False(Fenced(relay, T0.AddHours(2).AddSeconds(5)));
        Assert.True(Fenced(relay, T0.AddHours(2).AddSeconds(61)));
    }

    // The silence clock may only move forward, and only to an ack the cloud really sent.
    [Fact]
    public void An_Older_Or_Unsent_Ack_Changes_Nothing()
    {
        var relay = NewRelay();
        var first = relay.IssueAck(armed: true, T0);
        var second = relay.IssueAck(armed: true, T0.AddSeconds(10));
        relay.RecordAckConfirmation(second, armed: true);

        relay.RecordAckConfirmation(first, armed: false);
        relay.RecordAckConfirmation(second + TimeSpan.TicksPerHour, armed: false);

        Assert.Equal(second, relay.ConfirmedAckSeq);
        Assert.True(relay.ConfirmedAckArmed);
    }

    // Each ack's id is its send instant, strictly increasing even when two land in one tick or the clock steps back.
    [Fact]
    public void Ack_Ids_Only_Ever_Increase()
    {
        var relay = NewRelay();
        var a = relay.IssueAck(armed: true, T0);
        var b = relay.IssueAck(armed: true, T0);
        var c = relay.IssueAck(armed: true, T0.AddMinutes(-5));

        Assert.Equal(T0.Ticks, a);
        Assert.True(b > a && c > b);
    }

    // A cloud clock that went back behind an ack it already sent counts as silence — « too long », never « not yet ».
    [Fact]
    public void A_Cloud_Clock_That_Went_Back_Fences_And_A_Jitter_Does_Not()
    {
        var relay = NewRelay();
        relay.RecordAckConfirmation(relay.IssueAck(armed: true, T0), armed: true);

        Assert.False(Fenced(relay, T0.AddMilliseconds(-500)));
        Assert.True(Fenced(relay, T0.AddSeconds(-30)));
    }

    // ---- the PC's rule and the arming rule ------------------------------------------------------------------------

    [Theory]
    [InlineData(90, true, true, true)]
    [InlineData(89.9, true, true, false)]
    [InlineData(600, false, true, false)]   // AC-3.8: its last ack did not arm it
    [InlineData(600, true, false, false)]   // AC-6.6: cut off from the box, it never takes over
    public void The_Pc_Takes_Over_Only_Armed_On_Time_And_Still_Reaching_The_Box(
        double seconds, bool armed, bool box, bool expected)
    {
        Assert.Equal(expected, ClinicWriteLease.PcMayTakeOver(TimeSpan.FromSeconds(seconds), armed, box));
    }

    private static ClinicRelay ReadyRelay(DateTime now)
    {
        var relay = NewRelay();
        relay.RecordHeartbeat(new RelayHeartbeat(10, 100, true, 0, 0, null, false, "build-1", null, null, null, null), 10, now);
        return relay;
    }

    [Fact]
    public void Only_A_Ready_Pc_On_The_Clouds_Build_That_Did_Not_Ask_To_Stand_Down_Is_Armed()
    {
        var now = T0.AddMinutes(1);

        Assert.True(ClinicWriteLease.ShouldArm(ReadyRelay(now), sameBuild: true, wantsToStandDown: false, now));
        Assert.False(ClinicWriteLease.ShouldArm(ReadyRelay(now), sameBuild: false, wantsToStandDown: false, now));
        Assert.False(ClinicWriteLease.ShouldArm(ReadyRelay(now), sameBuild: true, wantsToStandDown: true, now));

        var behind = ReadyRelay(now);
        behind.RecordHeartbeat(new RelayHeartbeat(10, 100, true, 0, 0, null, false, "build-1", null, null, null, null), 500, now.AddMinutes(5));
        Assert.False(ClinicWriteLease.ShouldArm(behind, true, false, now.AddMinutes(5)));

        var seeding = NewRelay();
        Assert.False(ClinicWriteLease.ShouldArm(seeding, true, false, now));
    }
}
