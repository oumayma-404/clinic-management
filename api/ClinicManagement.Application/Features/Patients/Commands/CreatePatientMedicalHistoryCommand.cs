using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Commands;

public class CreatePatientMedicalHistoryCommand : IRequest<Result<PatientMedicalHistoryDto>>
{
    public Guid PatientId { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime? Date { get; set; }
    public string? Notes { get; set; }
}

public class CreatePatientMedicalHistoryCommandHandler : IRequestHandler<CreatePatientMedicalHistoryCommand, Result<PatientMedicalHistoryDto>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public CreatePatientMedicalHistoryCommandHandler(
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PatientMedicalHistoryDto>> Handle(CreatePatientMedicalHistoryCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Description))
            {
                return Result<PatientMedicalHistoryDto>.Failure("La description est requise.");
            }

            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<PatientMedicalHistoryDto>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<PatientMedicalHistoryDto>.Failure("Patient introuvable.");
            }

            var entry = new PatientMedicalHistory(
                Guid.NewGuid(),
                request.PatientId,
                request.Description,
                request.Date,
                request.Notes);

            // Update patient's UpdatedAt timestamp
            patient.AddMedicalHistoryEntry(entry);
            
            // Add the entry directly to the repository (adds to DbSet)
            await _patientRepository.AddMedicalHistoryEntryAsync(entry, cancellationToken);
            
            // Update only the patient's UpdatedAt property
            await _patientRepository.UpdateAsync(patient, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var dto = new PatientMedicalHistoryDto
            {
                Id = entry.Id,
                PatientId = entry.PatientId,
                Description = entry.Description,
                Date = entry.Date,
                Notes = entry.Notes,
                CreatedAt = entry.CreatedAt
            };

            return Result<PatientMedicalHistoryDto>.Success(dto);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PatientMedicalHistoryDto>.Failure($"Error creating medical history entry: {ex.Message}");
        }
    }
}


