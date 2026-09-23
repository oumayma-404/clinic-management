using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Appointments;

/// <summary>Shared validation for linking an appointment to a treatment-plan step (create + update).</summary>
public static class AppointmentPlanLink
{
    /// <summary>Confirm the plan item exists and belongs to the given clinic + patient before it is linked.</summary>
    public static async Task<Result> ValidateAsync(
        ITreatmentPlanRepository treatmentPlanRepository,
        Guid? treatmentPlanId,
        Guid treatmentPlanItemId,
        Guid clinicId,
        Guid? patientId,
        CancellationToken cancellationToken)
    {
        if (!patientId.HasValue)
        {
            return Result.Failure("Un rendez-vous sans patient ne peut pas être lié à un acte du plan.");
        }
        if (!treatmentPlanId.HasValue)
        {
            return Result.Failure("Le plan de traitement est requis pour lier l'acte.");
        }

        var plan = await treatmentPlanRepository.GetByIdAsync(treatmentPlanId.Value, cancellationToken);
        if (plan == null || plan.ClinicId != clinicId || plan.PatientId != patientId.Value)
        {
            return Result.Failure("Plan de traitement introuvable.");
        }
        var item = plan.Items.FirstOrDefault(i => i.Id == treatmentPlanItemId);
        if (item == null)
        {
            return Result.Failure("Acte du plan introuvable.");
        }
        // Only ever asked for a NEW link (the caller skips an unchanged one), so it must be bookable.
        if (!TreatmentPlanLifecycle.IsLive(plan.Status))
        {
            return Result.Failure(ClosedPlanRefusal);
        }
        if (item.IsWithdrawn)
        {
            return Result.Failure(WithdrawnItemRefusal);
        }

        return Result.Success();
    }

    /// <summary>
    /// Confirm that <b>every</b> act of a grouped séance belongs to the same plan, clinic and patient — and that
    /// every named step belongs to the act it is named with. One plan load for the whole set.
    /// <para>
    /// A batch rather than a loop over <see cref="ValidateAsync"/> because grouping is the normal case now (« ces
    /// trois actes en une séance »), and per-item validation would re-read the same aggregate once per act. It also
    /// makes the shared-plan rule explicit: the acts of one séance must come from one devis, since the appointment
    /// carries a single <c>TreatmentPlanId</c> and a mixed set would have no coherent one to record.
    /// </para>
    /// </summary>
    /// <returns>
    /// The validated steps' <b>désignations</b>, keyed by id. Returned rather than discarded because a plan act
    /// with no catalog procedure behind it (a hand-typed devis line) still needs a name on the appointment row
    /// that carries its link — and the plan aggregate is already loaded here, so fetching it again downstream
    /// would be a second read of the same rows.
    /// </returns>
    public static async Task<Result<Dictionary<Guid, string>>> ValidateManyAsync(
        ITreatmentPlanRepository treatmentPlanRepository,
        Guid? treatmentPlanId,
        IReadOnlyCollection<(Guid ItemId, Guid? StepId)> treatmentPlanLinks,
        Guid clinicId,
        Guid? patientId,
        CancellationToken cancellationToken,
        IReadOnlyCollection<Guid>? alreadyHeldItemIds = null)
    {
        if (treatmentPlanLinks.Count == 0)
        {
            return Result<Dictionary<Guid, string>>.Success(new Dictionary<Guid, string>());
        }
        if (!patientId.HasValue)
        {
            return Result<Dictionary<Guid, string>>.Failure(
                "Un rendez-vous sans patient ne peut pas être lié à un acte du plan.");
        }
        if (!treatmentPlanId.HasValue)
        {
            return Result<Dictionary<Guid, string>>.Failure("Le plan de traitement est requis pour lier l'acte.");
        }

        var plan = await treatmentPlanRepository.GetByIdAsync(treatmentPlanId.Value, cancellationToken);
        if (plan == null || plan.ClinicId != clinicId || plan.PatientId != patientId.Value)
        {
            return Result<Dictionary<Guid, string>>.Failure("Plan de traitement introuvable.");
        }

        var byId = plan.Items.ToDictionary(i => i.Id, i => i);
        if (treatmentPlanLinks.Any(l => !byId.ContainsKey(l.ItemId)))
        {
            return Result<Dictionary<Guid, string>>.Failure("Acte du plan introuvable.");
        }

        // ⚠️ A step is validated against the step list of ITS OWN act, not against the plan's steps as a set.
        // Both checks pass for a step id that exists on a different line of the same devis, and that request is
        // exactly what a stale browser sends after the acts are reordered or amended — it would book « le
        // scellement » of one act as a séance of another, and every screen would then read the progress of the
        // wrong bridge. The plan aggregate is already loaded here, so this costs nothing.
        foreach (var link in treatmentPlanLinks)
        {
            if (link.StepId.HasValue && byId[link.ItemId].Steps.All(s => s.Id != link.StepId.Value))
            {
                return Result<Dictionary<Guid, string>>.Failure("Étape du devis introuvable.");
            }
        }

        // A link the visit ALREADY carries is accepted whatever became of its devis since: the séance was booked
        // on it, and refusing it made every later edit of the visit (its time, its notes) impossible once the act
        // was recorded or the devis closed. Only a NEW link has to point at work that can still be booked.
        var held = alreadyHeldItemIds ?? Array.Empty<Guid>();
        foreach (var link in treatmentPlanLinks.Where(l => !held.Contains(l.ItemId)))
        {
            if (!TreatmentPlanLifecycle.IsLive(plan.Status))
            {
                return Result<Dictionary<Guid, string>>.Failure(ClosedPlanRefusal);
            }
            if (byId[link.ItemId].IsWithdrawn)
            {
                return Result<Dictionary<Guid, string>>.Failure(WithdrawnItemRefusal);
            }
        }

        return Result<Dictionary<Guid, string>>.Success(
            treatmentPlanLinks
                .Select(l => l.ItemId)
                .Distinct()
                .ToDictionary(id => id, id => byId[id].DesignationFr));
    }

    public const string ClosedPlanRefusal =
        "Ce devis est clôturé : ses actes ne peuvent plus être planifiés. Reprenez le traitement ou choisissez un autre acte.";

    public const string WithdrawnItemRefusal =
        "Cet acte a été mis de côté sur le devis : remettez-le dans le traitement pour le planifier.";

    /// <summary>
    /// The requested acts of an EDIT, with every link the visit already held repaired against what its devis
    /// became — plus the plan id those links resolve to when the client sent none.
    /// <para>
    /// ⚠️ The visit is the agreement, and a devis changing under it must never make the visit unsaveable. So a
    /// held link whose act or step was since deleted is <b>dropped</b> (the act stays on the visit, the link goes),
    /// and the plan id is <b>derived</b> from the held act rather than required from a browser that only knows the
    /// bookable acts. A new link gets none of this — it is validated in full by <see cref="ValidateManyAsync"/>.
    /// </para>
    /// </summary>
    public static async Task<(List<AppointmentProcedureRequest> Requests, Guid? PlanId)> RepairHeldLinksAsync(
        ITreatmentPlanRepository treatmentPlanRepository,
        IReadOnlyCollection<AppointmentProcedureRequest> requested,
        Guid? treatmentPlanId,
        IReadOnlyCollection<Guid> heldItemIds,
        Guid clinicId,
        Guid? patientId,
        CancellationToken cancellationToken)
    {
        var plans = new Dictionary<Guid, TreatmentPlan?>();
        async Task<TreatmentPlan?> PlanHolding(Guid itemId)
        {
            if (treatmentPlanId.HasValue)
            {
                if (!plans.TryGetValue(treatmentPlanId.Value, out var byId))
                {
                    byId = await treatmentPlanRepository.GetByIdAsync(treatmentPlanId.Value, cancellationToken);
                    plans[treatmentPlanId.Value] = byId;
                }
                if (byId?.Items.Any(i => i.Id == itemId) == true)
                {
                    return byId;
                }
            }
            var cached = plans.Values.FirstOrDefault(p => p?.Items.Any(i => i.Id == itemId) == true);
            if (cached != null)
            {
                return cached;
            }
            var found = await treatmentPlanRepository.GetByItemIdAsync(itemId, cancellationToken);
            if (found != null)
            {
                plans[found.Id] = found;
            }
            return found;
        }

        Guid? resolvedPlanId = treatmentPlanId;
        var repaired = new List<AppointmentProcedureRequest>();
        foreach (var request in requested)
        {
            if (request.TreatmentPlanItemId is not { } itemId || !heldItemIds.Contains(itemId))
            {
                repaired.Add(request);
                continue;
            }

            var plan = await PlanHolding(itemId);
            var item = plan != null && plan.ClinicId == clinicId && plan.PatientId == patientId
                ? plan.Items.FirstOrDefault(i => i.Id == itemId)
                : null;
            if (item == null)
            {
                // The devis line is gone: keep the act, drop the link. A link-only row has nothing left to carry.
                if (request.ProcedureTypeId.HasValue)
                {
                    repaired.Add(new AppointmentProcedureRequest
                    {
                        ProcedureTypeId = request.ProcedureTypeId,
                        AgreedCost = request.AgreedCost,
                    });
                }
                continue;
            }

            resolvedPlanId ??= plan!.Id;
            repaired.Add(new AppointmentProcedureRequest
            {
                ProcedureTypeId = request.ProcedureTypeId,
                TreatmentPlanItemId = itemId,
                TreatmentPlanItemStepId = request.TreatmentPlanItemStepId is { } stepId
                                          && item.Steps.Any(s => s.Id == stepId)
                    ? stepId
                    : null,
                AgreedCost = request.AgreedCost,
            });
        }

        return (repaired, resolvedPlanId);
    }
}
