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
/// PC's moment is always at least 30 s after the cloud's — on any line, losing requests, answers or both. The cost is a
/// cloud that locks up to ~10 s sooner after a cut (the confirmation trails the newest ack by one heartbeat).</para>
///
/// <para>⚠️ <b>Disarming is two-phase (D14)</b>: an ack saying « pas armé » counts only once the PC has confirmed it,
/// because until then the PC may still hold the armed one before it.</para>
/// </summary>
public static class ClinicWriteLease
{
    public static readonly TimeSpan CloudFencesAfter = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan PcTakesOverAfter = TimeSpan.FromSeconds(90);

    /// <summary>A cloud clock this far behind an ack it already sent has gone back, not merely jittered.</summary>
    public static readonly TimeSpan ClockStepTolerance = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The cloud refuses the cabinet's saves. The clock starts at the earliest « armé » ack the PC may hold: the one it
    /// confirmed when that one was armed, else the first armed one sent since. ⚠️ A cloud clock that went back behind an
    /// ack it already sent counts as silence: the safe answer to « how long since? » is « too long », never « not yet ».
    /// </summary>
    public static bool IsCloudFenced(ClinicRelay? relay, DateTime nowUtc)
    {
        if (relay is null || relay.Status == ClinicRelayStatus.Retired || !relay.MayBeArmed)
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
    /// The PC may take the cabinet's saves: its last ack said « armé », that ack is <see cref="PcTakesOverAfter"/> old on
    /// the PC's own clock, and the cabinet's internet box still answers — a PC cut off from the box never takes over.
    /// </summary>
    public static bool PcMayTakeOver(TimeSpan sinceLastAckReceived, bool lastAckArmed, bool boxAnswers) =>
        lastAckArmed && boxAnswers && sinceLastAckReceived >= PcTakesOverAfter;

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
