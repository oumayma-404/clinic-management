using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// Set the clinical steps of one act on a devis — « Préparation, Empreinte, Scellement définitif ».
/// <para>
/// Deliberately its own command rather than a field on <c>AmendTreatmentPlanCommand</c>, for the reason
/// <c>SetProcedureTypeMaterialsCommand</c> was split off <c>UpdateProcedureTypeCommand</c>: this list has
/// <b>replace</b> semantics (an empty list means « cet acte se fait en une séance », not « unchanged ») while
/// every field of the amend command is null-means-unchanged, and folding replace-semantics into a patch command
/// is how a list gets silently wiped by a partial body.
/// </para>
/// <para>
/// ⚠️ <b>It is not an amendment, and that is the load-bearing decision.</b> No money moves — the act's
/// <c>PlannedCost</c>, the devis total and the échéancier are all untouched — so it does not bump
/// <c>RevisionNumber</c> (nothing the patient signed for changes) and, unlike every amend path, it is
/// <b>not</b> blocked by a live invoice. A dentist must be able to correct the protocol of a bridge he is
/// halfway through, and the billed-plan guard would refuse exactly that.
/// </para>
/// </summary>
public class SetTreatmentPlanItemStepsCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid TreatmentPlanId { get; set; }
    public Guid ItemId { get; set; }

    /// <summary>
    /// The steps, in the order they are to be carried out. Each may echo back an existing
    /// <c>Id</c> — do so and that step keeps its identity, so its réalisé date, the fiche that evidences it and
    /// any séance already booked for it all survive the edit. Omit the id and it is a new step.
    /// </summary>
    public List<TreatmentPlanItemStepRequest> Steps { get; set; } = new();

    /// <summary>
    /// The <c>Version</c> the client read. Round-tripped so the save is checked against the copy the user was
    /// editing; <c>0</c> means « not supplied » and skips the check.
    /// </summary>
    public uint Version { get; set; }
}

/// <summary>One step as the client asks for it.</summary>
public class TreatmentPlanItemStepRequest
{
    /// <summary>The existing step this line stands for, or null for a new one.</summary>
    public Guid? Id { get; set; }

    public string Label { get; set; } = string.Empty;

    /// <summary>Chair time for this step, or null when nobody has estimated it.</summary>
    public int? EstimatedDurationMinutes { get; set; }
}

public class SetTreatmentPlanItemStepsCommandHandler
    : IRequestHandler<SetTreatmentPlanItemStepsCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SetTreatmentPlanItemStepsCommandHandler> _logger;

    public SetTreatmentPlanItemStepsCommandHandler(
        ITreatmentPlanRepository planRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<SetTreatmentPlanItemStepsCommandHandler> logger)
    {
        _planRepository = planRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(
        SetTreatmentPlanItemStepsCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<TreatmentPlanDto>.FailureFrom(clinicResult);
            }

            var plan = await _planRepository.GetByIdAsync(request.TreatmentPlanId, cancellationToken);
            if (plan == null || plan.ClinicId != clinicResult.Value)
            {
                return Result<TreatmentPlanDto>.Failure("Plan de traitement introuvable.");
            }

            try
            {
                plan.SetItemSteps(
                    request.ItemId,
                    request.Steps.Select(s => new TreatmentPlanItemStepInput(
                        s.Id, s.Label, s.EstimatedDurationMinutes)));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                // The aggregate's refusals are already French sentences naming the act or the step — the
                // status gate, the count cap, an unknown echoed id, and « une étape déjà réalisée ne peut pas
                // être retirée ». Re-writing them here would be a second copy free to drift from the rule.
                return Result<TreatmentPlanDto>.Failure(ex.Message);
            }

            _unitOfWork.SetExpectedVersion(plan, request.Version);
            await _planRepository.UpdateAsync(plan, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<TreatmentPlanDto>.Success(plan.ToDto());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Unhandled failure setting treatment plan item steps");
            return Result<TreatmentPlanDto>.Failure(
                "Erreur lors de l'enregistrement des étapes. Veuillez réessayer.");
        }
    }
}
