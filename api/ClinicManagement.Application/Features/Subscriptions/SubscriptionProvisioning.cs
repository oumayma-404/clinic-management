using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.Subscriptions;

/// <summary>The entitlement and its opening ledger entry, for the caller to stage into its own single save.</summary>
public sealed record NewClinicEntitlement(ClinicSubscription Subscription, SubscriptionPeriod OpeningEntry);

/// <summary>
/// The <b>one</b> definition of what entitlement a brand-new cabinet starts with (FR-4, FR-13, AC-1.2, AC-1.2a).
///
/// <para><b>No I/O and no clock of its own.</b> It builds two entities and returns them; the caller stages them
/// into the <c>SaveChangesAsync</c> it was already going to make, which is what makes FR-4's « one indivisible
/// operation » true — a cabinet cannot come into existence without an entitlement, because both rows are in the
/// same transaction as the <c>Clinic</c>.</para>
///
/// <para>⚠️ <b>It takes primitives, never a deployment profile.</b> <c>DeploymentProfile</c> lives in
/// Infrastructure and this project references <b>Domain alone</b>, so naming it here would not compile — the
/// answer arrives through <c>ISubscriptionPolicy</c>, asked by the caller. Nothing under
/// <c>Features/Subscriptions/</c> may name a deployment profile.</para>
///
/// <para>⚠️ <b>The end date is not computed here.</b> Both branches build a ledger entry and let
/// <c>ClinicSubscription.RecomputeFrom</c> derive <c>EndsOn</c>, so the arithmetic exists in exactly one place. A
/// hand-written <c>clinicToday.AddDays(trialDays - 1)</c> would be off by one against the fold and would make
/// <c>verify-schema</c>'s <c>subscription-end-date-matches-ledger</c> red on <b>every</b> new cabinet — the shape
/// most likely to be dismissed as « the new check is noisy ».</para>
/// </summary>
public static class SubscriptionProvisioning
{
    /// <summary>
    /// A new cabinet's entitlement: a <b>trial</b> where subscriptions are enforced, <b>open-ended</b> where they
    /// are not (AC-1.2).
    ///
    /// <para>The open-ended branch is what makes FR-13 hold in all three topologies while nothing can ever expire
    /// in two of them — and it is why <c>every-clinic-has-an-entitlement</c> can be a flat count over every cabinet
    /// rather than a count qualified by deployment kind.</para>
    /// </summary>
    /// <param name="clinicToday">
    /// The clinic-local day, from <c>ClinicClock.ClinicToday()</c>. It becomes the opening entry's anchor and so
    /// <b>day 1</b> of the trial: 10 Aug + 30 days → the cabinet may work all of 8 Sep (AC-1.1).
    /// </param>
    /// <param name="trialDays">
    /// From <c>ISubscriptionPolicy.TrialDays</c>. Recorded as the entry's <i>duration</i>, which is what makes
    /// AC-1.5 / EC-12 true for free: changing the setting later moves no existing cabinet's date, because each
    /// cabinet's date is folded from what was recorded rather than from what the setting says today.
    /// </param>
    public static NewClinicEntitlement CreateForNewClinic(
        Guid clinicId,
        bool requiresSubscription,
        DateTime clinicToday,
        int trialDays,
        DateTime? nowUtc = null)
    {
        var recordedAtUtc = nowUtc ?? DateTime.UtcNow;

        var openingEntry = requiresSubscription
            ? SubscriptionPeriod.Trial(clinicId, clinicToday, trialDays, recordedAtUtc)
            : SubscriptionPeriod.OpenEnded(
                clinicId,
                SubscriptionPeriodKind.Complimentary,
                clinicToday,
                recordedAtUtc,
                note: "Cette installation n'applique pas d'abonnement : accès sans échéance.");

        var subscription = ClinicSubscription.For(clinicId, recordedAtUtc);
        subscription.RecomputeFrom(new[] { openingEntry }, recordedAtUtc);

        return new NewClinicEntitlement(subscription, openingEntry);
    }
}
