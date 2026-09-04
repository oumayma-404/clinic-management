using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// Accept a draft plan (devis): assign the per-clinic-per-year number (<c>AAAA-NNNN</c>, separate from
/// invoices) and freeze it. Numbering is gapless and concurrency-safe (unique index + recompute-and-retry).
/// <para>
/// <b>Kept for the drafts that already exist.</b> New plans are accepted by
/// <c>CreateTreatmentPlanCommand</c>, so nothing reaches this path any more — but deleting it would strand every
/// pre-existing <c>Draft</c> row in a state with no way out, since <c>SetItems</c>/<c>Accept</c> are the only
/// Draft-legal operations and the workspace's « Accepter le devis » is what calls this. The numbering itself now
/// lives in the shared <see cref="DevisNumbering"/>.
/// </para>
/// </summary>
public class AcceptTreatmentPlanCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid Id { get; set; }
}

public class AcceptTreatmentPlanCommandHandler : IRequestHandler<AcceptTreatmentPlanCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<AcceptTreatmentPlanCommandHandler> _logger;

    public AcceptTreatmentPlanCommandHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        IProcedureTypeRepository procedureTypeRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<AcceptTreatmentPlanCommandHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _procedureTypeRepository = procedureTypeRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(AcceptTreatmentPlanCommand request, CancellationToken cancellationToken)
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

            var accepted = await DevisNumbering.AcceptAndSaveAsync(
                plan, clinicId, _planRepository, _procedureTypeRepository, _unitOfWork,
                ct => _planRepository.UpdateAsync(plan, ct),
                // Nothing confirmed: this path accepts a Draft that already exists, so its acts take their
                // procedures' catalogue protocols and the dentist edits them from the act rows afterwards.
                confirmedSteps: null,
                _logger, cancellationToken);
            if (accepted.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(accepted.Error!);
            }

            _logger.LogInformation("Accepted treatment plan {PlanId} as {Number}", plan.Id, plan.Number);
            var patient = await _patientRepository.GetByIdAsync(plan.PatientId, cancellationToken);
            return Result<TreatmentPlanDto>.Success(plan.ToDto(patient?.GetFullName()));
        }
        catch (InvalidOperationException ex)
        {
            return Result<TreatmentPlanDto>.Failure(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Result<TreatmentPlanDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error accepting treatment plan {PlanId}", request.Id);
            return Result<TreatmentPlanDto>.Failure("Erreur lors de l'acceptation du plan de traitement.");
        }
    }
}
