using ClinicManagement.Domain.Services;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Recall;

/// <summary>One reason a patient is on the worklist, with the date that made it due.</summary>
/// <param name="Kind">Which reason.</param>
/// <param name="DueSince">
/// The moment the reason became actionable — an échéance's due date, the acceptance date of a stalled devis, the
/// recall due date. Drives « en retard de N jours » and the list's ordering.
/// </param>
/// <param name="Detail">
/// Short factual context for the row (a devis number, an amount). Never a French sentence — the frontend owns the
/// wording, per the standing English-key/French-label convention.
/// </param>
public sealed record RecallReason(RecallReasonKind Kind, DateTime DueSince, string? Detail = null);

/// <summary>
/// The pure rules deciding why — and whether — a patient belongs on the « à rappeler » worklist.
///
/// <para>Deliberately pure and static, like <c>PlanBillingRules</c> and <c>RecallDueRule</c>: no repositories, no
/// clock. The handler gathers facts, these rules judge them. That is what lets the same rules be reused by a future
/// dashboard count or job without either re-deriving them.</para>
///
/// <para><b>Reasons are aggregated per patient, not per row.</b> Snooze state lives on the patient
/// (<c>Patient.RecallSnoozedUntil</c>), so a per-reason row would let « Reporter » on one reason silently hide
/// another — and staff make <i>one</i> call covering everything anyway. One patient, one row, all their reasons.
/// </para>
/// </summary>
public static class RecallWorklistRules
{
    /// <summary>
    /// How long after acceptance a devis with unfinished acts and nothing booked counts as stalled. A grace period
    /// exists so a plan accepted this morning, whose next séance has simply not been booked yet, is not chased.
    /// </summary>
    public const int StalledPlanGraceDays = 14;

    /// <summary>How long a devis may sit unanswered before it is worth chasing.</summary>
    public const int UnansweredDevisGraceDays = 14;

    /// <summary>
    /// Every reason applying to one patient, most urgent first. Empty ⇒ the patient does not belong on the list.
    /// </summary>
    /// <param name="recallAnchorUtc">Last completed visit, else registration date.</param>
    /// <param name="recallIntervalMonths">The clinic's interval.</param>
    /// <param name="plans">This patient's plan facts.</param>
    /// <param name="oldestOverdueInstallmentUtc">From the installment-outstanding read; null when nothing is overdue.</param>
    /// <param name="outstandingAmount">Total outstanding for the patient, for the row's detail text.</param>
    public static IReadOnlyList<RecallReason> ReasonsFor(
        DateTime recallAnchorUtc,
        int recallIntervalMonths,
        IEnumerable<RecallPlanFact> plans,
        DateTime? oldestOverdueInstallmentUtc,
        decimal outstandingAmount,
        DateTime nowUtc)
    {
        var reasons = new List<RecallReason>();

        // 1. Money already owed. The most concrete reason to call, and the cheapest to detect — the
        //    installment-outstanding read already computes the oldest overdue due date for « Créances ».
        if (oldestOverdueInstallmentUtc.HasValue)
        {
            reasons.Add(new RecallReason(
                RecallReasonKind.OverdueInstallment,
                oldestOverdueInstallmentUtc.Value,
                outstandingAmount > 0m ? outstandingAmount.ToString("0.000") : null));
        }

        foreach (var plan in plans)
        {
            // 2. An accepted devis with acts left and nothing booked. The population this runs over already
            //    excludes patients with a future appointment, so "nothing booked" needs no separate check —
            //    a patient who is coming in is not stalled, and staff will handle it in the chair.
            if (IsStalled(plan, nowUtc))
            {
                reasons.Add(new RecallReason(
                    RecallReasonKind.StalledPlan,
                    plan.AcceptedDate ?? plan.CreatedAt,
                    plan.Number ?? $"{plan.DoneItems}/{plan.TotalItems}"));
            }

            // 3. A quote nobody answered.
            if (IsUnanswered(plan, nowUtc))
            {
                reasons.Add(new RecallReason(RecallReasonKind.UnansweredDevis, plan.CreatedAt, plan.Number));
            }
        }

        // 4. The original rule, kept as one reason among several rather than the whole feature.
        if (RecallDueRule.IsDue(recallAnchorUtc, recallIntervalMonths, nowUtc))
        {
            reasons.Add(new RecallReason(
                RecallReasonKind.OverdueVisit,
                RecallDueRule.DueDate(recallAnchorUtc, recallIntervalMonths)));
        }

        // Most urgent kind first (enum order); within a kind, the longest-waiting first.
        return reasons
            .OrderBy(r => (int)r.Kind)
            .ThenBy(r => r.DueSince)
            .ToList();
    }

    /// <summary>
    /// A treatment still running, with unfinished acts, past its grace period.
    ///
    /// <para>
    /// ⚠️ <b>« Still running » is <see cref="TreatmentPlanLifecycle.IsLive"/> and used to be a hand-written
    /// <c>Accepted || InProgress</c>.</b> « Suivre ce traitement » makes an un-numbered <c>Draft</c> a live
    /// treatment, and <c>AdvanceAfterWorkRecorded</c> deliberately keeps it one however many séances it records —
    /// so a patient who stopped coming halfway through a bridge followed this way was in the exact state this
    /// worklist exists to catch and was never listed. Silently: the plan is real, the acts are unfinished, and no
    /// screen said anything.
    /// </para>
    /// </summary>
    public static bool IsStalled(RecallPlanFact plan, DateTime nowUtc) =>
        TreatmentPlanLifecycle.IsLive(plan.Status)
        && plan.DoneItems < plan.TotalItems
        && (plan.AcceptedDate ?? plan.CreatedAt).AddDays(StalledPlanGraceDays) <= nowUtc;

    /// <summary>
    /// A devis presented to a patient and never answered, past its grace period.
    ///
    /// <para>
    /// ⚠️ <b>It yields to <see cref="IsStalled"/>, which now claims most of what it used to.</b> Both tests match
    /// a <c>Draft</c> past fourteen days, so without this a followed treatment would be reported twice, under two
    /// reasons that contradict each other — « au point mort » and « jamais répondu » about one plan.
    /// </para>
    /// <para>
    /// ⚠️ <b>Its premise is doubtful and deliberately left in place rather than quietly retired.</b> « Presented »
    /// meant « a numbered quote the patient is holding » — and <c>Accept</c> is the only writer of
    /// <c>Number</c>, so a Draft has never carried one, and since « Suivre ce traitement » a Draft is a treatment
    /// nobody was quoted for at all (the label is « Sans devis »). Whether this reason should survive at all is a
    /// product decision about the recall taxonomy, not a consequence of the multi-séance work, so the false half
    /// is removed by the yield above and the kind is left reachable.
    /// </para>
    /// </summary>
    public static bool IsUnanswered(RecallPlanFact plan, DateTime nowUtc) =>
        plan.Status == TreatmentPlanStatus.Draft
        && !IsStalled(plan, nowUtc)
        && plan.CreatedAt.AddDays(UnansweredDevisGraceDays) <= nowUtc;
}
