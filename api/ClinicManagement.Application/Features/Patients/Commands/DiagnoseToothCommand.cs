using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Commands;

/// <summary>
/// Chart a diagnosis directly on a patient's odontogram (existing pathology / "à traiter"), before any
/// treatment. Persisted as a <see cref="ToothStateSource.Diagnosis"/> tooth state with no source record.
/// </summary>
public class DiagnoseToothCommand : IRequest<Result<ToothStateDto>>
{
    public Guid PatientId { get; set; }
    public int ToothNumber { get; set; }
    public string Condition { get; set; } = string.Empty;
    public string? Surfaces { get; set; }
    public string? Note { get; set; }
}

public class DiagnoseToothCommandHandler : IRequestHandler<DiagnoseToothCommand, Result<ToothStateDto>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IToothStateRepository _toothStateRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public DiagnoseToothCommandHandler(
        IPatientRepository patientRepository,
        IToothStateRepository toothStateRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _patientRepository = patientRepository;
        _toothStateRepository = toothStateRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ToothStateDto>> Handle(DiagnoseToothCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (!Enum.TryParse<ToothCondition>(request.Condition, ignoreCase: true, out var condition))
            {
                return Result<ToothStateDto>.Failure("État de dent invalide.");
            }
            if (condition == ToothCondition.Sain)
            {
                return Result<ToothStateDto>.Failure("Un diagnostic « sain » n'est pas enregistré.");
            }

            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<ToothStateDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<ToothStateDto>.Failure("Patient introuvable.");
            }

            var state = new ToothState(
                Guid.NewGuid(),
                request.PatientId,
                patient.ClinicId,
                request.ToothNumber,
                condition,
                DateTime.UtcNow,
                request.Surfaces,
                request.Note,
                dentalRecordId: null,
                source: ToothStateSource.Diagnosis);

            await _toothStateRepository.AddAsync(state, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<ToothStateDto>.Success(new ToothStateDto
            {
                Id = state.Id,
                ToothNumber = state.ToothNumber,
                Condition = state.Condition.ToString(),
                Source = state.Source.ToString(),
                Surfaces = state.Surfaces,
                Note = state.Note,
                TreatmentDate = state.TreatmentDate,
                DentalRecordId = state.DentalRecordId,
                CreatedAt = state.CreatedAt
            });
        }
        catch (ArgumentException ex)
        {
            return Result<ToothStateDto>.Failure(ex.Message);
        }
    }
}
