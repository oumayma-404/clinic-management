using ClinicManagement.Application.DTOs;
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
    /// The séances the dentist confirmed on the form, by <b>position</b> in the request list — the shape
    /// <see cref="ApplyAsync"/>'s <c>confirmedByPosition</c> takes. <c>null</c> for a line the client said
    /// nothing about, which then takes its procedure's catalogue protocol; an empty list is « one séance ».
    /// <para>
    /// ⚠️ <b>One projection, because two disagreed.</b> The create path built this and the amend path called
    /// <see cref="ApplyAsync"/> without it — so unticking every séance on an act added by amendment sent
    /// <c>steps: []</c>, the server discarded it, and the full catalogue protocol was applied instead, under a
    /// « Devis modifié » success toast. The tri-state was honoured on creation only.
    /// </para>
    /// </summary>
    public static List<IReadOnlyList<TreatmentPlanItemStepInput>?> ConfirmedByPosition(
        IEnumerable<TreatmentPlanItemRequest> items) =>
        items
            .Select(i => i.Steps?
                .Select(step => new TreatmentPlanItemStepInput(
                    step.Id, step.Label, step.EstimatedDurationMinutes, step.MinDaysAfterPrevious))
                .ToList() as IReadOnlyList<TreatmentPlanItemStepInput>)
            .ToList();

    /// <summary>
    /// Applies each candidate act's protocol in place. No-op when nothing is a candidate, when the acts name
    /// no procedure, or when the procedures carry no protocol — which is the common case, since only prosthetic
    /// work is seeded with one.
    /// </summary>
    public static async Task ApplyAsync(
        TreatmentPlan plan,
        Guid clinicId,
        IProcedureTypeRepository procedureTypeRepository,
        CancellationToken cancellationToken,
        IReadOnlyList<IReadOnlyList<TreatmentPlanItemStepInput>?>? confirmedByPosition = null)
    {
        /*
         * ⚠️ A confirmed act is a candidate even with NO procedure, because the dentist may have cut a
         * hand-typed devis line into séances — the catalogue path needs a `ProcedureTypeId` to look a protocol
         * up, the confirmed path does not.
         */
        var candidates = plan.Items
            .Where(i => !i.HasSteps && i.Status == TreatmentPlanItemStatus.Planned)
            .Where(i => i.ProcedureTypeId.HasValue || ConfirmedFor(confirmedByPosition, i) != null)
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        // Cache per procedure so a devis with four crowns does one lookup, as the pricing twin does.
        var protocols = new Dictionary<Guid, IReadOnlyList<ProcedureStepTemplate>>();

        foreach (var item in candidates)
        {
            /*
             * The dentist's own answer wins over the catalogue, and an EMPTY confirmed list is an answer:
             * « this act is one séance ». Applying the protocol over it would re-propose exactly what they
             * just unticked.
             */
            var confirmed = ConfirmedFor(confirmedByPosition, item);
            if (confirmed != null)
            {
                if (confirmed.Count > 0)
                {
                    plan.SetItemSteps(item.Id, confirmed);
                }
                continue;
            }

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
                protocol.Select(s => new TreatmentPlanItemStepInput(
                    null, s.Label, s.DurationMinutes, s.MinDaysAfterPrevious)));
        }
    }

    /// <summary>
    /// What the caller confirmed for this act, or <c>null</c> when it said nothing about it.
    ///
    /// <para>⚠️ Matched on <c>SequenceNumber</c>, which is the act's <b>position in the request list</b>:
    /// <c>SetItems</c> numbers the rebuilt items 0..n-1 in the order they arrive, so index and sequence are the
    /// same thing. Matching on the designation instead would break on a devis with two identical lines — two
    /// « Couronne 26 » is an ordinary devis — and ids do not exist yet on the create path.</para>
    /// </summary>
    private static IReadOnlyList<TreatmentPlanItemStepInput>? ConfirmedFor(
        IReadOnlyList<IReadOnlyList<TreatmentPlanItemStepInput>?>? confirmedByPosition,
        TreatmentPlanItem item)
    {
        if (confirmedByPosition == null) return null;
        var position = item.SequenceNumber;
        return position >= 0 && position < confirmedByPosition.Count ? confirmedByPosition[position] : null;
    }
}
