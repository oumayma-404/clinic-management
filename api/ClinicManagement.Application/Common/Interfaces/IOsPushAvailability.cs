using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// « Can this installation deliver an OS notification to this platform? » — asked in one place by everything that
/// needs the answer: the registration endpoint (refuse rather than queue, AC-42), the fan-out (write no row it
/// cannot drain), the dispatcher (park what it cannot send, AC-50) and the settings surface (state it per
/// platform, AC-51/AC-52).
///
/// <para>It is the <c>AND</c> of two questions that belong in different places: whether the <b>topology</b> permits
/// push (<c>DeploymentProfile.PermitsOsPush</c> — derived from the deployment kind and nothing else) and whether
/// the <b>credentials</b> for that platform are present (configuration). Keeping the second out of
/// <c>DeploymentProfile</c> is what lets <c>SelfHostedLan</c> stay ✗ however an operator configures it.</para>
///
/// <para>Deliberately <b>not</b> a per-clinic question. There is one mobile app per deployment, so one Firebase
/// project and one Apple team — a clinic cannot switch push on for itself, and modelling it per clinic would
/// promise an operator control they do not have.</para>
/// </summary>
public interface IOsPushAvailability
{
    /// <summary>Can a device on this platform be registered and delivered to?</summary>
    bool SupportsPush(DevicePlatform platform);

    /// <summary>
    /// True when at least one platform is sendable. What makes the whole registration route <b>absent</b> rather
    /// than present-and-always-refusing where neither is (AC-51).
    /// </summary>
    bool IsAvailableAtAll { get; }

    /// <summary>
    /// Why <paramref name="platform"/> cannot be pushed to, in French, or null when it can. One wording for the
    /// refusal a client sees, the reason a parked row records and the sentence a settings screen shows — three
    /// surfaces that would otherwise each invent their own explanation of the same fact.
    /// </summary>
    string? UnavailableReason(DevicePlatform platform);
}
