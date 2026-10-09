using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Services;

/// <summary>
/// Who may record a cabinet's work: the cloud, or its PC de secours — never both (<c>clinic-pc-copy</c> FR-3, D13, D14).
///
/// <para><b>The fence is a predicate, never a flag a job flips</b>, so nothing can be late. Each side measures elapsed
/// time on its own clock from one exchange, so the two wall clocks never need to agree:</para>
/// <list type="bullet">
///   <item>the cloud stops accepting the cabinet's saves <see cref="CloudFencesAfter"/> after it <b>sent</b> the last
///   ack the PC has confirmed receiving;</item>
///   <item>the PC takes over <see cref="PcTakesOverAfter"/> after it <b>received</b> its last ack, only if that ack
///   said « armé », and only while the cabinet's internet box answers (AC-6.6).</item>
/// </list>
/// <para>An ack is received after it is sent, and a confirmed ack is never newer than the last one received, so the
/// PC's moment is always at least 15 s after the cloud's — on any line, losing requests, answers or both. The cost is a
/// cloud that locks up to ~10 s sooner after a cut (the confirmation trails the newest ack by one heartbeat).</para>
///
/// <para>⚠️ <b>Disarming is two-phase (D14)</b>: an ack saying « pas armé » counts only once the PC has confirmed it,
/// because until then the PC may still hold the armed one before it.</para>
/// </summary>
public static class ClinicWriteLease
{
    // 45 s / 60 s since 2026-10-09 (owner: « 60 s, not 90 »). The margin is what keeps the two from both accepting
    // saves, and any positive one does (an ack is received after it is sent); 15 s leaves room for a slow answer.
    public static readonly TimeSpan CloudFencesAfter = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan PcTakesOverAfter = TimeSpan.FromSeconds(60);

    /// <summary>A cloud clock this far behind an ack it already sent has gone back, not merely jittered.</summary>
    public static readonly TimeSpan ClockStepTolerance = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The cloud refuses the cabinet's saves. The clock starts at the earliest « armé » ack the PC may hold: the one it
    /// confirmed when that one was armed, else the first armed one sent since. ⚠️ A cloud clock that went back behind an
    /// ack it already sent counts as silence: the safe answer to « how long since? » is « too long », never « not yet ».
    /// </summary>
    public static bool IsCloudFenced(ClinicRelay? relay, DateTime nowUtc)
    {
        if (relay is null || relay.Status == ClinicRelayStatus.Retired)
        {
            return false;
        }

        // AC-9.4: a restored cloud, until its PC has sent back what the restore lost (capped, so it can never last).
        if (relay.IsRecoveringGap(nowUtc))
        {
            return true;
        }

        // The PC said it holds the cabinet's saves: nothing about the acks can make the cloud writable again until the
        // PC hands the cut's work back (D18). A heartbeat arriving once the line heals is no evidence the PC let go.
        if (relay.PcHoldingSinceUtc is not null)
        {
            return true;
        }

        if (!relay.MayBeArmed)
        {
            return false;
        }

        // « Reprendre la main » (D19): no ack sent up to the reclaim arms the PC any more.
        var armedFrom = relay.ConfirmedAckArmed ? relay.ConfirmedAckSeq : relay.PendingArmedAckSeq;
        if (armedFrom <= relay.ReclaimedAtAckSeq)
        {
            return false;
        }

        if (nowUtc.Ticks < relay.LastAckSeq - ClockStepTolerance.Ticks)
        {
            return true;
        }

        var anchor = relay.ConfirmedAckArmed ? relay.ConfirmedAckSeq : relay.PendingArmedAckSeq;
        return nowUtc.Ticks - anchor > CloudFencesAfter.Ticks;
    }

    /// <summary>
    /// Since when the cabinet's saves have been refused here: the PC's own takeover moment once it said so, else 60 s
    /// after the ack the silence clock runs from. Null while the cloud is writable.
    /// </summary>
    public static DateTime? LockedSinceUtc(ClinicRelay? relay, DateTime nowUtc)
    {
        if (!IsCloudFenced(relay, nowUtc))
        {
            return null;
        }

        if (relay!.PcHoldingSinceUtc is { } holding)
        {
            return holding;
        }

        var anchor = relay.ConfirmedAckArmed ? relay.ConfirmedAckSeq : relay.PendingArmedAckSeq;
        var since = new DateTime(Math.Min(anchor + CloudFencesAfter.Ticks, nowUtc.Ticks), DateTimeKind.Utc);
        return since;
    }

    /// <summary>
    /// The PC may take the cabinet's saves: its last ack said « armé », that ack is <see cref="PcTakesOverAfter"/> old on
    /// the PC's own clock, and the cabinet's internet box still answers — a PC cut off from the box never takes over.
    /// </summary>
    public static bool PcMayTakeOver(TimeSpan sinceLastAckReceived, bool lastAckArmed, bool boxAnswers) =>
        lastAckArmed && boxAnswers && sinceLastAckReceived >= PcTakesOverAfter;

    /// <summary>
    /// How long ago the PC received its last ack, never over-counted. An ack received by this process is timed on the
    /// monotonic clock (immune to a wall-clock step); one received before the PC restarted can only be timed on the wall
    /// clock, so it is capped by how long this process has run — a wall clock that jumped forward must never make the PC
    /// take over early. Under-counting only makes the takeover later, which is the safe direction.
    /// </summary>
    public static TimeSpan SinceLastAckReceived(
        DateTime wallNowUtc, DateTime? wallReceivedAtUtc, TimeSpan? monotonicSinceReceipt, TimeSpan monotonicSinceStart)
    {
        if (monotonicSinceReceipt is { } measured)
        {
            return measured;
        }

        if (wallReceivedAtUtc is not { } received)
        {
            return TimeSpan.Zero;
        }

        var wall = wallNowUtc - received;
        if (wall < TimeSpan.Zero)
        {
            wall = TimeSpan.Zero;
        }

        return wall < monotonicSinceStart ? wall : monotonicSinceStart;
    }

    /// <summary>
    /// D16: a note, devis or avoir number numbered on the cloud must be held by the PC before it is final — whenever the
    /// PC could take over with it: it may hold an « armé » ack no reclaim overruled, and it has not taken over (then the
    /// cloud is fenced and numbers nothing). A PC that stood down (AC-6.1), was never armed or is retired is not waited for.
    /// </summary>
    public static bool PcMustConfirmNumbers(ClinicRelay? relay)
    {
        if (relay is null || relay.Status == ClinicRelayStatus.Retired || relay.PcHoldingSinceUtc is not null || !relay.MayBeArmed)
        {
            return false;
        }

        var armedFrom = relay.ConfirmedAckArmed ? relay.ConfirmedAckSeq : relay.PendingArmedAckSeq;
        return armedFrom > relay.ReclaimedAtAckSeq;
    }

    /// <summary>
    /// Whether this ack arms the PC: it is « Prêt » (FR-2 — the one predicate the card, the bell and the console read),
    /// it runs the cloud's build, and it has not asked to stand down (a clean shutdown, an update). A PC that was not
    /// ready at its last contact never takes over (AC-3.8).
    /// </summary>
    public static bool ShouldArm(ClinicRelay relay, bool sameBuild, bool wantsToStandDown, DateTime nowUtc) =>
        !wantsToStandDown
        && sameBuild
        && relay.Status == ClinicRelayStatus.Active
        && ClinicRelayHealth.Read(relay, nowUtc).State == ClinicRelayState.Ready;
}
