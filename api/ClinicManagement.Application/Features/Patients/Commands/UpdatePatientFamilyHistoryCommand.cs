using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Commands;

public class UpdatePatientFamilyHistoryCommand : IRequest<Result<PatientFamilyHistoryDto>>
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string? Relationship { get; set; }
    public string? Condition { get; set; }
    public string? Notes { get; set; }
}

public class UpdatePatientFamilyHistoryCommandHandler : IRequestHandler<UpdatePatientFamilyHistoryCommand, Result<PatientFamilyHistoryDto>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public UpdatePatientFamilyHistoryCommandHandler(
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PatientFamilyHistoryDto>> Handle(UpdatePatientFamilyHistoryCommand request, CancellationToken cancellationToken)
    {
        try
        {
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

            var entry = patient.FamilyHistoryEntries.FirstOrDefault(e => e.Id == request.Id);
            if (entry == null)
            {
                return Result<PatientFamilyHistoryDto>.Failure("Antécédent familial introuvable.");
            }

            var relationship = request.Relationship ?? entry.Relationship;
            var condition = request.Condition ?? entry.Condition;
            var notes = request.Notes ?? entry.Notes;

            entry.Update(relationship, condition, notes);
            // Update only the patient's UpdatedAt property (entry changes are automatically tracked)
            await _patientRepository.UpdateAsync(patient, cancellationToken);
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
        catch (Exception ex)
        {
            return Result<PatientFamilyHistoryDto>.Failure($"Error updating family history entry: {ex.Message}");
        }
    }
}


