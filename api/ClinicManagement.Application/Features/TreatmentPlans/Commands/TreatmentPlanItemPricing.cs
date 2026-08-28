using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// Seeds a plan line's planned cost from its chosen procedure's default fee
/// (<see cref="Domain.Entities.ProcedureType.DefaultCost"/>) when the caller left the cost blank, so a devis
/// built from the catalog does not require re-typing fees the app already knows. A user-entered positive cost is
/// never overwritten; free-text lines (no procedure) are untouched.
/// Shared by <see cref="CreateTreatmentPlanCommandHandler"/> and <see cref="UpdateTreatmentPlanCommandHandler"/>.
///
/// <para>⚠️ <b>The source used to be the DCH catalog</b> (<c>DentalActCode.DefaultFee</c>), and a comment here
/// said <c>ProcedureTypeId</c> was deliberately NOT used to seed a cost — because « reseeding it from the menu's
/// current default would rewrite what the patient agreed to ». That argument was about *overwriting*, and it
/// still holds: this only ever fills a cost the caller left at zero, and never touches a positive one. With the
/// DCH catalog gone from the devis, the procedure's own default is the only fee the app knows, and dropping the
/// prefill entirely would mean typing a price on every line of every devis.</para>
/// </summary>
internal static class TreatmentPlanItemPricing
{
    /// <summary>
    /// Resolves each request line into the <see cref="TreatmentPlanItemInput"/> that
    /// <see cref="TreatmentPlan.SetItems(IEnumerable{TreatmentPlanItemInput}, bool)"/> expects, filling
    /// <c>PlannedCost</c> from the chosen procedure's default cost when the caller sent a non-positive cost.
    /// Only procedures belonging to <paramref name="clinicId"/> are trusted (defense-in-depth over the query
    /// filter).
    /// <para>
    /// Line ids are dropped, so every line is created fresh — correct on the create path, where there is no
    /// prior identity to preserve. The update path must use <see cref="ResolveWithIdsAsync"/>.
    /// </para>
    /// </summary>
    public static async Task<List<TreatmentPlanItemInput>> ResolveAsync(
        IEnumerable<TreatmentPlanItemRequest> items,
        Guid clinicId,
        IProcedureTypeRepository procedureTypeRepository,
        CancellationToken cancellationToken)
        => (await ResolveWithIdsAsync(items, clinicId, procedureTypeRepository, cancellationToken))
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
        IProcedureTypeRepository procedureTypeRepository,
        CancellationToken cancellationToken)
    {
        var resolved = new List<TreatmentPlanItemInput>();

        // Cache per procedure so a plan with several lines of the same act does one lookup.
        var feeCache = new Dictionary<Guid, decimal?>();

        foreach (var item in items)
        {
            var plannedCost = item.PlannedCost;

            if (plannedCost <= 0m && item.ProcedureTypeId is Guid procedureTypeId)
            {
                if (!feeCache.TryGetValue(procedureTypeId, out var fee))
                {
                    var procedure = await procedureTypeRepository.GetByIdAsync(procedureTypeId, cancellationToken);
                    fee = procedure != null && procedure.ClinicId == clinicId ? procedure.DefaultCost : null;
                    feeCache[procedureTypeId] = fee;
                }

                if (fee is decimal defaultFee && defaultFee > 0m)
                {
                    plannedCost = defaultFee;
                }
            }

            resolved.Add(new TreatmentPlanItemInput(
                item.Id,
                item.DesignationFr,
                plannedCost,
                item.ProcedureTypeId,
                item.ToothNumbers));
        }

        return resolved;
    }
}
