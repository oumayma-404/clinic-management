using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Queries;

/// <summary>List the clinic's treatment plans, filtered by patient / status / created-date range.</summary>
public class GetTreatmentPlansQuery : IRequest<Result<List<TreatmentPlanDto>>>
{
    public Guid? PatientId { get; set; }
    public string? Status { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public class GetTreatmentPlansQueryHandler : IRequestHandler<GetTreatmentPlansQuery, Result<List<TreatmentPlanDto>>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetTreatmentPlansQueryHandler> _logger;

    public GetTreatmentPlansQueryHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetTreatmentPlansQueryHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<List<TreatmentPlanDto>>> Handle(GetTreatmentPlansQuery request, CancellationToken cancellationToken)
    {
        try
        {
            TreatmentPlanStatus? status = null;
            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                if (!Enum.TryParse<TreatmentPlanStatus>(request.Status, ignoreCase: true, out var parsed))
                {
                    return Result<List<TreatmentPlanDto>>.Failure("Statut de plan invalide.");
                }
                status = parsed;
            }

            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<List<TreatmentPlanDto>>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var plans = (await _planRepository.GetFilteredAsync(clinicId, request.PatientId, status, request.From, request.To, cancellationToken)).ToList();

            // Resolve patient names once per distinct patient (small N for a clinic's plan list).
            var names = new Dictionary<Guid, string?>();
            foreach (var patientId in plans.Select(p => p.PatientId).Distinct())
            {
                var patient = await _patientRepository.GetByIdAsync(patientId, cancellationToken);
                names[patientId] = patient?.GetFullName();
            }

            var dtos = plans
                .Select(p => p.ToDto(names.TryGetValue(p.PatientId, out var name) ? name : null))
                .ToList();

            return Result<List<TreatmentPlanDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing treatment plans");
            return Result<List<TreatmentPlanDto>>.Failure("Erreur lors du chargement des plans de traitement.");
        }
    }
}
