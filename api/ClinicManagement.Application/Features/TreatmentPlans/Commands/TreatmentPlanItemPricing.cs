using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// Seeds a plan line's planned cost from its linked dental act's suggested fee
/// (<see cref="Domain.Entities.DentalActCode.DefaultFee"/>) when the caller left the cost blank,
/// so a devis built from the odontogram/catalog does not require re-typing fees the app already knows.
/// A user-entered positive cost is never overwritten; free-text lines (no act code) are untouched.
/// Shared by <see cref="CreateTreatmentPlanCommandHandler"/> and <see cref="UpdateTreatmentPlanCommandHandler"/>.
/// </summary>
internal static class TreatmentPlanItemPricing
{
    /// <summary>
    /// Resolves each request line into the <see cref="TreatmentPlanItemInput"/> that
    /// <see cref="TreatmentPlan.SetItems(IEnumerable{TreatmentPlanItemInput}, bool)"/> expects, filling
    /// <c>PlannedCost</c> from the linked act's default fee when the caller sent a non-positive cost.
    /// Only acts belonging to <paramref name="clinicId"/> are trusted (defense-in-depth over the query filter).
    /// <para>
    /// Line ids are dropped, so every line is created fresh — correct on the create path, where there is no
    /// prior identity to preserve. The update path must use <see cref="ResolveWithIdsAsync"/>.
    /// </para>
    /// </summary>
    public static async Task<List<TreatmentPlanItemInput>> ResolveAsync(
        IEnumerable<TreatmentPlanItemRequest> items,
        Guid clinicId,
        IDentalActCodeRepository dentalActRepository,
        CancellationToken cancellationToken)
        => (await ResolveWithIdsAsync(items, clinicId, dentalActRepository, cancellationToken))
            .Select(i => i with { Id = null })
            .ToList();

    /// <summary>
    /// Same resolution, keeping each line's echoed-back <c>Id</c> so <c>SetItems</c> can preserve the identity
    /// of an unchanged act — without which a draft edit re-issues every id and orphans any appointment or
    /// dental-record link pointing at those acts.
    /// </summary>
    public static async Task<List<TreatmentPlanItemInput>> ResolveWithIdsAsync(
        IEnumerable<TreatmentPlanItemRequest> items,
        Guid clinicId,
        IDentalActCodeRepository dentalActRepository,
        CancellationToken cancellationToken)
    {
        var resolved = new List<TreatmentPlanItemInput>();
        // Cache per act code so a plan with several lines of the same act does one lookup.
        var feeCache = new Dictionary<Guid, decimal?>();

        foreach (var item in items)
        {
            var plannedCost = item.PlannedCost;

            if (plannedCost <= 0m && item.DentalActCodeId is Guid actCodeId)
            {
                if (!feeCache.TryGetValue(actCodeId, out var fee))
                {
                    var act = await dentalActRepository.GetByIdAsync(actCodeId, cancellationToken);
                    fee = act != null && act.ClinicId == clinicId ? act.DefaultFee : null;
                    feeCache[actCodeId] = fee;
                }

                if (fee is decimal defaultFee && defaultFee > 0m)
                {
                    plannedCost = defaultFee;
                }
            }

            // ProcedureTypeId is carried through verbatim and deliberately NOT used to seed the cost. It says
            // which service the act is performed as, so booking it can preselect the procedure; the devis fee
            // is the negotiated number, and reseeding it from the menu's current default would rewrite what
            // the patient agreed to.
            resolved.Add(new TreatmentPlanItemInput(
                item.Id,
                item.DesignationFr,
                plannedCost,
                item.DentalActCodeId,
                item.CodeActe,
                item.ProcedureTypeId,
                item.ToothNumbers));
        }

        return resolved;
    }
}
