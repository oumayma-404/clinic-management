using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.TreatmentPlans;

/// <summary>
/// « Cet acte est-il déjà quoté ailleurs pour ce patient ? » (S7) — a <b>notice</b>, never a refusal.
///
/// <para>
/// ⚠️ <b>Nothing detected this and the balance counted the act twice.</b> Two live devis for one patient both
/// carry debt, and <c>PlanBillingRules.BilledPlanIds</c> de-duplicates per <i>plan</i>, not per act — so a
/// couronne on 16 quoted on a devis in March and quoted again on a devis in September puts its fee in
/// « Solde patient » twice, with no error, no badge and nothing on either screen saying so. It is the ordinary
/// shape of a treatment re-planned after a gap.
/// </para>
/// <para>
/// ⚠️ <b>A refusal would be wrong and was rejected.</b> A second opinion legitimately re-quotes the same act, a
/// patient may have two of the same act on different teeth, and a devis nobody accepted is a proposal rather
/// than a claim. The dentist is told and decides — which is also why the notice names the OTHER devis and its
/// number, so the answer (« annuler celui de mars ») is one click away rather than a hunt.
/// </para>
/// <para>
/// ⚠️ <b>Keyed on the procedure AND the tooth, and a tooth-less act matches only a tooth-less one.</b> Two
/// « Détartrage » with no tooth are genuinely the same act quoted twice; « Extraction » on 16 and on 26 are
/// two extractions. Matching on the désignation was rejected — a free-text line is typed by hand and two
/// spellings of one act would miss while two acts sharing a word would collide; prose-matching is the failure
/// this repository has already deleted once.
/// </para>
/// <para>
/// ⚠️ <b>Only DEBT-BEARING plans on both sides.</b> `PlanBillingRules.CarriesDebt` is the exact rule that
/// produces the double count, so an un-numbered treatment, a cancelled devis and a written-off one raise
/// nothing — there is no second claim to warn about.
/// </para>
/// </summary>
public static class DuplicateActDetection
{
    /// <summary>One act of <paramref name="plan"/> that another live devis of the same patient also quotes.</summary>
    /// <param name="ItemId">The act on THIS devis, so the notice can sit on its own row.</param>
    public readonly record struct DuplicateAct(
        Guid ItemId,
        string DesignationFr,
        IReadOnlyList<int> ToothNumbers,
        Guid OtherPlanId,
        string? OtherPlanNumber,
        string OtherPlanTitle);

    /// <summary>
    /// The acts of <paramref name="plan"/> that <paramref name="otherPlans"/> also quote.
    ///
    /// <para>
    /// Pure, and takes the other plans already loaded: the caller is a read that has the patient's plans in
    /// hand, and issuing a query from here would make this a second definition of « which plans count ».
    /// </para>
    /// </summary>
    public static IReadOnlyList<DuplicateAct> Find(
        TreatmentPlan plan,
        IEnumerable<TreatmentPlan> otherPlans)
    {
        if (!PlanBillingRules.CarriesDebt(plan.Status))
        {
            return Array.Empty<DuplicateAct>();
        }

        var candidates = otherPlans
            .Where(p => p.Id != plan.Id
                        && p.PatientId == plan.PatientId
                        && PlanBillingRules.CarriesDebt(p.Status))
            .ToList();

        if (candidates.Count == 0)
        {
            return Array.Empty<DuplicateAct>();
        }

        var found = new List<DuplicateAct>();

        foreach (var item in plan.ActiveItems)
        {
            // A hand-typed line carries no procedure, so there is nothing to match it on that is not prose.
            if (item.ProcedureTypeId is not Guid procedureTypeId)
            {
                continue;
            }

            var key = ToothKey(item);

            foreach (var other in candidates)
            {
                var clash = other.ActiveItems.FirstOrDefault(
                    o => o.ProcedureTypeId == procedureTypeId && ToothKey(o) == key);

                if (clash is null)
                {
                    continue;
                }

                found.Add(new DuplicateAct(
                    item.Id,
                    item.DesignationFr,
                    item.ToothNumbers.ToList(),
                    other.Id,
                    other.Number,
                    other.Title));

                // One notice per act: naming a third devis on the same row would not change what the dentist
                // does about it, and the row has one line to say it in.
                break;
            }
        }

        return found;
    }

    /// <summary>
    /// The act's teeth as a comparable key — ordered and de-duplicated, so « 16, 14 » and « 14, 16 » are one
    /// act. An empty key is a tooth-less act and matches only another tooth-less one.
    /// </summary>
    private static string ToothKey(TreatmentPlanItem item) =>
        string.Join(",", item.ToothNumbers.Distinct().OrderBy(t => t));
}
