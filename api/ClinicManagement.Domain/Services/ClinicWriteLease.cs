using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Services;

/// <summary>
/// Who may record a cabinet's work: the cloud, or its PC de secours (<c>clinic-pc-copy</c> FR-3, D13, D14).
///
/// <para>⚠️ <b>Manual takeover since 2026-10-12 (owner)</b>: the PC takes over only when somebody in the cabinet presses
/// « Travailler sur le PC de secours », which is offered only while the cloud does not answer. The cloud no longer locks
/// on silence — a laptop asleep or a closed lid locked the whole cabinet's cloud overnight. It refuses saves only once
/// the PC has said it holds them (and during a restore's gap). A save made on the cloud during a cut is never lost: at
/// the return the cloud's version is kept and the PC's is listed « À vérifier ». Numbers stay unique because only the
/// cloud gives them: the PC numbers a cut's notes, devis and avoirs provisionally and the return renumbers them
/// (<see cref="PcMustConfirmNumbers"/>).</para>
///
/// <para><b>The fence is a predicate, never a flag a job flips</b>, so nothing can be late. Each side measures elapsed
/// time on its own clock from one exchange, so the two wall clocks never need to agree:</para>
/// <list type="bullet">
///   <item>the cloud stops accepting the cabinet's saves <see cref="CloudFencesAfter"/> after it <b>sent</b> the last
///   ack the PC has confirmed receiving;</item>
///   <item>the PC takes over <see cref="PcTakesOverAfter"/> after it <b>received</b> its last ack, only if that ack
///   said « armé » — box or no box (a power cut darkens the box too).</item>
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
        // Silence alone never locks the cloud any more (manual takeover): a PC asleep is not a PC in charge.
        return relay.PcHoldingSinceUtc is not null;
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

        return relay!.PcHoldingSinceUtc ?? relay.GapPendingSinceUtc ?? nowUtc;
    }

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
    /// D16, retired 2026-10-13 (owner): the cloud never waits for its PC before making a note, devis or avoir number
    /// final. It used to, whenever the PC could take over — and since the cloud no longer locks on silence, a laptop
    /// asleep with its last ack « armé » made every numbered save on the cloud wait 3 s and fail, all night, with no way
    /// out but retiring the PC. Numbers stay unique the other way round: <b>the PC de secours never gives a legal
    /// number</b> — what it numbers during a cut is provisional (<c>AAAA-P…</c>) and the cloud gives the real one at the
    /// return. The cloud cannot tell a PC asleep from a PC holding a cut, so no time window could have kept both.
    /// </summary>
    public static bool PcMustConfirmNumbers(ClinicRelay? relay) => false;

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
