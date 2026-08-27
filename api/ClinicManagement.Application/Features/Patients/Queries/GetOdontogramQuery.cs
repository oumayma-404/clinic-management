using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Queries;

/// <summary>Get a patient's odontogram (all recorded tooth treatments, many-per-tooth). Tenant-guarded via the patient.</summary>
public class GetOdontogramQuery : IRequest<Result<List<ToothStateDto>>>
{
    public Guid PatientId { get; set; }
}

public class GetOdontogramQueryHandler : IRequestHandler<GetOdontogramQuery, Result<List<ToothStateDto>>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IToothStateRepository _toothStateRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetOdontogramQueryHandler> _logger;

    public GetOdontogramQueryHandler(
        IPatientRepository patientRepository,
        IToothStateRepository toothStateRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetOdontogramQueryHandler> logger)
    {
        _patientRepository = patientRepository;
        _toothStateRepository = toothStateRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<List<ToothStateDto>>> Handle(GetOdontogramQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<List<ToothStateDto>>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<List<ToothStateDto>>.Failure("Patient introuvable.");
            }

            var states = await _toothStateRepository.GetByPatientIdAsync(request.PatientId, cancellationToken);

            var dtos = states
                .OrderBy(t => t.ToothNumber)
                .ThenBy(t => t.TreatmentDate)
                .Select(t => new ToothStateDto
                {
                    Id = t.Id,
                    ToothNumber = t.ToothNumber,
                    Condition = t.Condition.ToString(),
                    Source = t.Source.ToString(),
                    Surfaces = t.Surfaces,
                    Note = t.Note,
                    TreatmentDate = t.TreatmentDate,
                    DentalRecordId = t.DentalRecordId,
                    CreatedAt = t.CreatedAt
                })
                .ToList();

            return Result<List<ToothStateDto>>.Success(dtos);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error loading odontogram for patient {PatientId}", request.PatientId);
            return Result<List<ToothStateDto>>.Failure("Erreur lors du chargement de l'odontogramme.");
        }
    }
}
