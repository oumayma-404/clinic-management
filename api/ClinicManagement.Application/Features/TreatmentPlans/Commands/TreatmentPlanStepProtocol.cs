using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// Copies a procedure's step protocol (<see cref="ProcedureType.DefaultSteps"/>) onto the devis acts that
/// chose it, so « Couronne / bridge » arrives on the devis already cut into préparation · empreinte ·
/// scellement instead of the dentist retyping the same three steps on every devis.
///
/// <para>
/// This is the <b>only</b> consumer of <c>DefaultSteps</c>, and it is the twin of
/// <see cref="TreatmentPlanItemPricing"/>: the catalogue holds the default, the devis holds the fact, and the
/// server fills only what is blank. Without it the catalogue's step editor and the three protocols in
/// <c>ProcedureTypeCatalogSeed</c> were authored and read by nobody — the exact failure this repo keeps
/// producing, and one that shows up as an empty « Étapes » strip rather than as an error.
/// </para>
///
/// <para>
/// ⚠️ <b>It runs at acceptance, not at creation</b>, and that is forced rather than chosen:
/// <c>TreatmentPlan.SetItemSteps</c> refuses a Draft (« Le devis doit être accepté pour définir les étapes
/// d'un acte ») while <c>SetItems</c> is Draft-only, so there is no instant at which a Draft may hold steps.
/// It therefore lives inside <see cref="DevisNumbering.AcceptAndSaveAsync"/> — acceptance itself — so neither
/// acceptance path can apply the number and forget the protocol, and both land in one save.
/// </para>
///
/// <para>
/// ⚠️ <b>Fills only a blank act.</b> An act is a candidate when it names a procedure, has no steps of its own
/// and is still <c>Planned</c>. Re-applying over a stepped act would discard steps the dentist edited by hand;
/// applying to an act already <c>Done</c> or <c>InProgress</c> would make <c>SetSteps</c> throw (it refuses to
/// cut finished work into steps) and take the whole amendment down with it.
/// </para>
///
/// <para>
/// <c>public</c> rather than <c>internal</c> because this solution has no <c>InternalsVisibleTo</c>, and the
/// « fills only a blank act » rule above is the whole of the safety here — the reason
/// <c>DashboardProcedureMixReader</c> and <c>DirectoryAclHardener.ComposeGrantArguments</c> made the same
/// choice. Its twin <see cref="TreatmentPlanItemPricing"/> stayed internal and untested, which is not a
/// precedent worth following for a method that can discard a dentist's hand-edited steps if the predicate
/// ever loosens.
/// </para>
/// </summary>
public static class TreatmentPlanStepProtocol
{
    /// <summary>
    /// Applies each candidate act's protocol in place. No-op when nothing is a candidate, when the acts name
    /// no procedure, or when the procedures carry no protocol — which is the common case, since only prosthetic
    /// work is seeded with one.
    /// </summary>
    public static async Task ApplyAsync(
        TreatmentPlan plan,
        Guid clinicId,
        IProcedureTypeRepository procedureTypeRepository,
        CancellationToken cancellationToken)
    {
        // Materialised before the loop: SetItemSteps mutates the act it names, and iterating the live
        // collection while doing so is the kind of thing that works until a protocol has two acts on one plan.
        var candidates = plan.Items
            .Where(i => i.ProcedureTypeId.HasValue
                        && !i.HasSteps
                        && i.Status == TreatmentPlanItemStatus.Planned)
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        // Cache per procedure so a devis with four crowns does one lookup, as the pricing twin does.
        var protocols = new Dictionary<Guid, IReadOnlyList<ProcedureStepTemplate>>();

        foreach (var item in candidates)
        {
            var procedureTypeId = item.ProcedureTypeId!.Value;

            if (!protocols.TryGetValue(procedureTypeId, out var protocol))
            {
                var procedureType = await procedureTypeRepository.GetByIdAsync(procedureTypeId, cancellationToken);
                // Only this clinic's catalogue is trusted, defence-in-depth over the query filter — the same
                // check TreatmentPlanItemPricing makes before reading a fee.
                protocol = procedureType != null && procedureType.ClinicId == clinicId
                    ? procedureType.DefaultSteps
                    : Array.Empty<ProcedureStepTemplate>();
                protocols[procedureTypeId] = protocol;
            }

            if (protocol.Count == 0)
            {
                continue;
            }

            plan.SetItemSteps(
                item.Id,
                protocol.Select(s => new TreatmentPlanItemStepInput(null, s.Label, s.DurationMinutes)));
        }
    }
}
