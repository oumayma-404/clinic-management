using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Commands;

public class CreatePatientFamilyHistoryCommand : IRequest<Result<PatientFamilyHistoryDto>>
{
    public Guid PatientId { get; set; }
    public string Relationship { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class CreatePatientFamilyHistoryCommandHandler : IRequestHandler<CreatePatientFamilyHistoryCommand, Result<PatientFamilyHistoryDto>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public CreatePatientFamilyHistoryCommandHandler(
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PatientFamilyHistoryDto>> Handle(CreatePatientFamilyHistoryCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Relationship))
            {
                return Result<PatientFamilyHistoryDto>.Failure("Le lien de parenté est requis.");
            }

            if (string.IsNullOrWhiteSpace(request.Condition))
            {
                return Result<PatientFamilyHistoryDto>.Failure("L'affection est requise.");
            }

            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<PatientFamilyHistoryDto>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<PatientFamilyHistoryDto>.Failure("Patient introuvable.");
            }

            var entry = new PatientFamilyHistory(
                Guid.NewGuid(),
                request.PatientId,
                patient.ClinicId,
                request.Relationship,
                request.Condition,
                request.Notes);

            patient.AddFamilyHistoryEntry(entry);
            
            // Add the entry directly to the repository (adds to DbSet)
            await _patientRepository.AddFamilyHistoryEntryAsync(entry, cancellationToken);
            
            // No write to the patient row. A history entry is a child, and on this entity `UpdatedAt` shares
            // its row with the concurrency token — stamping it here refused the user’s own next save.
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var dto = new PatientFamilyHistoryDto
            {
                Id = entry.Id,
                PatientId = entry.PatientId,
                Relationship = entry.Relationship,
                Condition = entry.Condition,
                Notes = entry.Notes,
                CreatedAt = entry.CreatedAt
            };

            return Result<PatientFamilyHistoryDto>.Success(dto);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PatientFamilyHistoryDto>.Failure($"Error creating family history entry: {ex.Message}");
        }
    }
}


