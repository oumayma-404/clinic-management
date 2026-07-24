using ClinicManagement.Application.DTOs;
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
    /// Resolves each request line into the tuple <see cref="Domain.Entities.TreatmentPlan.SetItems"/> expects,
    /// filling <c>PlannedCost</c> from the linked act's default fee when the caller sent a non-positive cost.
    /// Only acts belonging to <paramref name="clinicId"/> are trusted (defense-in-depth over the query filter).
    /// </summary>
    public static async Task<List<(string DesignationFr, decimal PlannedCost, Guid? DentalActCodeId, string? CodeActe, IReadOnlyList<int> ToothNumbers)>> ResolveAsync(
        IEnumerable<TreatmentPlanItemRequest> items,
        Guid clinicId,
        IDentalActCodeRepository dentalActRepository,
        CancellationToken cancellationToken)
    {
        var resolved = new List<(string, decimal, Guid?, string?, IReadOnlyList<int>)>();
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

            resolved.Add((item.DesignationFr, plannedCost, item.DentalActCodeId, item.CodeActe, (IReadOnlyList<int>)item.ToothNumbers));
        }

        return resolved;
    }
}
