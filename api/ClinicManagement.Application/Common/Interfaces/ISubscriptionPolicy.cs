namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Whether subscriptions are enforced on this deployment, and how many free days a new cabinet gets.
///
/// <para><b>⚠️ This seam is structurally required, not stylistic.</b> <c>DeploymentProfile</c> lives in
/// <b>Infrastructure</b> and <c>ClinicManagement.Application.csproj</c> references <b>Domain alone</b>, so no
/// Application type can name it. Every existing capability reaches this layer the same way — through an interface
/// (<see cref="IOsPushAvailability"/>) or by being asked in the API controller
/// (<c>AllowsPublicClinicSignup</c>). Nothing under <c>Features/Subscriptions/</c> may name a deployment profile.</para>
///
/// <para><b>⚠️ The two members answer different kinds of question, and mixing them is the defect this split
/// prevents.</b> <see cref="RequiresSubscription"/> is derived from the deployment's <i>kind</i> and from nothing
/// an operator can set (AC-7.3): a <c>Subscription:*</c> key must not be able to turn enforcement on or off, or a
/// clinic's own PC becomes one config edit away from refusing its own patient records.
/// <see cref="TrialDays"/> <i>is</i> operator configuration. That is the same line
/// <see cref="IOsPushAvailability"/> draws between <c>PermitsOsPush</c> and the credentials.</para>
/// </summary>
public interface ISubscriptionPolicy
{
    /// <summary>
    /// Is a cabinet's right to record new work a dated entitlement here? True for the hosted multi-tenant
    /// deployment only. Where it is false the entitlement is still created — <b>open-ended</b> — so FR-13 holds
    /// everywhere while nothing can expire.
    /// </summary>
    bool RequiresSubscription { get; }

    /// <summary>
    /// Free days a new cabinet arrives with, the creation day counting as day 1 (AC-1.1). Operator-configurable,
    /// and changing it later moves <b>no</b> existing cabinet's end date (AC-1.5, EC-12) — the trial is recorded as
    /// a ledger entry carrying its own duration, so a cabinet's date is fixed by what was recorded, not by what the
    /// setting says today.
    /// </summary>
    int TrialDays { get; }
}
