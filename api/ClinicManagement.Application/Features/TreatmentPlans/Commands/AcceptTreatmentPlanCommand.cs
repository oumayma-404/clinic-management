using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// Accept a draft plan (devis): assign the per-clinic-per-year number (<c>AAAA-NNNN</c>, separate from
/// invoices) and freeze it. Numbering is gapless and concurrency-safe (unique index + recompute-and-retry).
/// </summary>
public class AcceptTreatmentPlanCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid Id { get; set; }
}

public class AcceptTreatmentPlanCommandHandler : IRequestHandler<AcceptTreatmentPlanCommand, Result<TreatmentPlanDto>>
{
    private const int MaxNumberingAttempts = 5;

    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<AcceptTreatmentPlanCommandHandler> _logger;

    public AcceptTreatmentPlanCommandHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<AcceptTreatmentPlanCommandHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
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

            // The clinic's fiscal year, not the UTC one (AC-P6.8) — same defect and same reasoning as the
            // invoice sequence in `IssueInvoiceCommand`.
            var year = ClinicClock.ClinicYear();

            for (var attempt = 1; attempt <= MaxNumberingAttempts; attempt++)
            {
                var nextSequence = await _planRepository.GetMaxSequenceForYearAsync(clinicId, year, cancellationToken) + 1;
                var number = $"{year}-{nextSequence:D4}";

                if (attempt == 1)
                {
                    plan.Accept(number);
                }
                else
                {
                    plan.SetAcceptedNumber(number);
                }

                await _planRepository.UpdateAsync(plan, cancellationToken);

                try
                {
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("Accepted treatment plan {PlanId} as {Number}", plan.Id, plan.Number);
                    var patient = await _patientRepository.GetByIdAsync(plan.PatientId, cancellationToken);
                    return Result<TreatmentPlanDto>.Success(plan.ToDto(patient?.GetFullName()));
                }
                catch (DbUpdateException) when (attempt < MaxNumberingAttempts)
                {
                    _logger.LogWarning("Devis number {Number} collided on accept attempt {Attempt}; recomputing", number, attempt);
                }
            }

            return Result<TreatmentPlanDto>.Failure("Impossible d'attribuer un numéro de devis unique. Veuillez réessayer.");
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
