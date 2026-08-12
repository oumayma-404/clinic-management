namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// « Does this deployment sell WhatsApp reminder capacity, and can it actually run the onboarding? » — asked in one
/// place by everything that needs the answer: the two clinic reads (404 rather than an empty section), the
/// enforcement gate (read nothing where the feature is off), the daily pass (do not register) and the connect card.
///
/// <para>It is the <c>AND</c> of two questions that belong in different places: whether the <b>topology</b> sells
/// vendor messaging (<c>DeploymentProfile.SellsVendorMessaging</c> — derived from the deployment kind and nothing
/// else) and whether the deployment's own <b>Meta credentials</b> are present (configuration). Keeping the second
/// out of <c>DeploymentProfile</c> is what lets the other two kinds stay ✗ however an operator configures them, and
/// it is <see cref="IOsPushAvailability"/>'s split for <see cref="IOsPushAvailability.SupportsPush"/>.</para>
///
/// <para>⚠️ <b>The seam is structurally required, not stylistic.</b> <c>DeploymentProfile</c> lives in
/// Infrastructure and <c>ClinicManagement.Application.csproj</c> references <b>Domain alone</b>, so no Application
/// type can name it — the same reason <see cref="ISubscriptionPolicy"/> exists.</para>
///
/// <para>Deliberately <b>not</b> a per-clinic question. The credit line, the Meta app and the bill are the
/// vendor's, one per deployment; a cabinet cannot switch this on for itself, and modelling it per clinic would
/// promise control nobody has.</para>
/// </summary>
public interface IVendorMessagingAvailability
{
    /// <summary>
    /// Does this deployment do vendor-purchased messaging at all? The <b>kind</b> half alone, and the answer FR-9
    /// asks for: where it is false every surface of the feature is absent rather than present-and-refusing (EC-16).
    /// </summary>
    bool SellsVendorMessaging { get; }

    /// <summary>
    /// Can a cabinet be walked through Meta's guided connection right now — the kind <b>and</b> the deployment's own
    /// Meta credentials (FR-9's last bullet).
    ///
    /// <para>⚠️ A separate answer from <see cref="SellsVendorMessaging"/> on purpose: an allowance a cabinet cannot
    /// yet spend is still a real allowance, so the section, the counting and the console stay present while only
    /// the connection offer goes away. Collapsing the two would make a missing <c>Meta:AppId</c> look like a
    /// deployment that does not sell messaging.</para>
    /// </summary>
    bool CanOnboardCabinets { get; }
}
