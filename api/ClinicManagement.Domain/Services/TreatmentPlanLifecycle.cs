using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Services;

/// <summary>
/// « Ce traitement est-il encore en cours ? » — the clinical question, asked in one place so that every screen,
/// every count and every SQL filter answer it identically.
///
/// <para>
/// The sibling of <see cref="PlanBillingRules"/>, and deliberately <b>not</b> part of it: that type answers
/// « does this plan carry money », which is a different question with a different answer for the same status.
/// A <c>Draft</c> is live work and carries no debt; conflating the two is what produced this type.
/// </para>
///
/// <para>
/// ⚠️ <b>Phrased as « not finished » rather than « accepted or in progress ».</b> The states that end a
/// treatment are the closed ones (<c>Cancelled</c>, <c>Completed</c>), and enumerating the open ones is exactly
/// what left <c>Draft</c> out when « Suivre ce traitement » made an un-numbered plan a live treatment: the test
/// was written by hand in five places, four in the browser and one in SQL, and only the browser's were updated.
/// The frontend twin is <c>plan-next-action.ts</c>'s <c>isPlanLive</c>, held by <c>check:responsive</c>'s N23;
/// <c>TreatmentPlanLifecycleTests</c> holds this side, including the SQL writer that N23's <c>.tsx</c> scan
/// structurally cannot reach.
/// </para>
///
/// <para>
/// The symptom of the copy that was missed had no error anywhere: « Traitements en cours » simply did not list
/// a treatment the dentist had started minutes earlier, and pressing « Éditer le devis » — which promotes the
/// plan to <c>Accepted</c> — appeared to fix it, hiding the cause behind a plausible workaround.
/// </para>
/// </summary>
public static class TreatmentPlanLifecycle
{
    /// <summary>
    /// The statuses a treatment is still running in. Materialised as a collection because the « Traitements en
    /// cours » projection filters on it <b>in SQL</b> (<c>Contains</c> translates to <c>IN</c>), so the rule is
    /// stated once and used in memory and in the database alike.
    /// </summary>
    public static readonly IReadOnlyCollection<TreatmentPlanStatus> LiveStatuses = new[]
    {
        TreatmentPlanStatus.Draft,
        TreatmentPlanStatus.Accepted,
        TreatmentPlanStatus.InProgress
    };

    /// <summary>
    /// True when a plan in this status is still being carried out — so its acts may be booked, recorded, and
    /// listed under « Traitements en cours ».
    /// <para>
    /// ⚠️ <b>Reads the positive list, and used to be written as « not Cancelled and not Completed ».</b> That
    /// phrasing is safe only while the closed statuses are the ones that exist: appending
    /// <see cref="TreatmentPlanStatus.Stopped"/> made a stopped treatment read as <b>live</b> — bookable,
    /// listed under « Traitements en cours », ringed on the odontogramme — with no error anywhere. The
    /// collection above is the single statement of the rule; this is a lookup into it, never a second copy.
    /// </para>
    /// </summary>
    public static bool IsLive(TreatmentPlanStatus status) => LiveStatuses.Contains(status);
}
