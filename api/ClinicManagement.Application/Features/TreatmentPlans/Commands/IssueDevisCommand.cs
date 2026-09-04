using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// « Éditer le devis » — give a treatment its number, because the patient is being handed a document.
///
/// <para>
/// This is the <b>only</b> place a devis number is taken. Numbering used to happen at creation, which meant
/// the act of saying « cet acte prend plusieurs séances » spent a gapless per-clinic-per-year number on a
/// document nobody had asked for — and a spent number can only be released by a cancellation carrying a motif.
/// Splitting the two is the whole change: <see cref="StartTreatmentCommand"/> follows the work,
/// this one produces the paper.
/// </para>
///
/// <para>
/// ⚠️ <b>Idempotent by design.</b> A treatment that already has a number is returned unchanged rather than
/// refused — the dentist pressing « Éditer le devis » twice wants the devis, not an error, and the second
/// press must not mint a second number for the same work.
/// </para>
///
/// <para>
/// ⚠️ It also raises the lump-sum échéance <c>Accept</c> has always raised when no schedule was supplied, and
/// that is correct <i>here</i>: this is the moment the money becomes a claim the patient has seen. What was
/// wrong was doing it at creation.
/// </para>
/// </summary>
public class IssueDevisCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid Id { get; set; }

    /// <summary>Optimistic-concurrency token from the read this action was offered on. 0 skips the check.</summary>
    public uint Version { get; set; }
}

public class IssueDevisCommandHandler : IRequestHandler<IssueDevisCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<IssueDevisCommandHandler> _logger;

    public IssueDevisCommandHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        IProcedureTypeRepository procedureTypeRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<IssueDevisCommandHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _procedureTypeRepository = procedureTypeRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(IssueDevisCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var plan = await _planRepository.GetByIdAsync(request.Id, cancellationToken);
            if (plan == null || plan.ClinicId != clinicId)
            {
                return Result<TreatmentPlanDto>.Failure("Plan de traitement introuvable.");
            }

            var patient = await _patientRepository.GetByIdAsync(plan.PatientId, cancellationToken);

            // Already numbered — hand it back. See the idempotence note above.
            if (plan.Number != null)
            {
                return Result<TreatmentPlanDto>.Success(plan.ToDto(patient?.GetFullName()));
            }

            _unitOfWork.SetExpectedVersion(plan, request.Version);

            var issued = await DevisNumbering.AcceptAndSaveAsync(
                plan, clinicId, _planRepository, _procedureTypeRepository, _unitOfWork,
                // Already persisted — the plan is being promoted, not inserted.
                ct => _planRepository.UpdateAsync(plan, ct),
                /*
                 * The steps the Draft already carries, echoed back BY POSITION (the helper indexes acts, not
                 * ids). They are the confirmed sequence: the treatment was cut into séances when it started,
                 * so the catalogue protocol must not be laid over them a second time — and echoing each step's
                 * own id is what keeps its réalisé date and its fiche link through the promotion.
                 */
                plan.Items
                    .OrderBy(i => i.SequenceNumber)
                    .Select(i => (IReadOnlyList<TreatmentPlanItemStepInput>?)i.Steps
                        .OrderBy(s => s.SequenceNumber)
                        .Select(s => new TreatmentPlanItemStepInput(
                            s.Id, s.Label, s.EstimatedDurationMinutes, s.MinDaysAfterPrevious))
                        .ToList())
                    .ToList(),
                _logger, cancellationToken);
            if (issued.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(issued.Error!);
            }

            _logger.LogInformation("Issued devis {Number} for treatment {PlanId}", plan.Number, plan.Id);
            return Result<TreatmentPlanDto>.Success(plan.ToDto(patient?.GetFullName()));
        }
        catch (InvalidOperationException ex)
        {
            return Result<TreatmentPlanDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error issuing devis for treatment {PlanId}", request.Id);
            return Result<TreatmentPlanDto>.Failure("Erreur lors de l'édition du devis.");
        }
    }
}
