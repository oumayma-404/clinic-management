using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Commands;

public class UpdatePatientMedicalHistoryCommand : IRequest<Result<PatientMedicalHistoryDto>>
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string? Description { get; set; }
    public DateTime? Date { get; set; }
    public string? Notes { get; set; }
}

public class UpdatePatientMedicalHistoryCommandHandler : IRequestHandler<UpdatePatientMedicalHistoryCommand, Result<PatientMedicalHistoryDto>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public UpdatePatientMedicalHistoryCommandHandler(
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PatientMedicalHistoryDto>> Handle(UpdatePatientMedicalHistoryCommand request, CancellationToken cancellationToken)
    {
        try
        {
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

            var entry = patient.MedicalHistoryEntries.FirstOrDefault(e => e.Id == request.Id);
            if (entry == null)
            {
                return Result<PatientMedicalHistoryDto>.Failure("Antécédent médical introuvable.");
            }

            var description = request.Description ?? entry.Description;
            var date = request.Date ?? entry.Date;
            var notes = request.Notes ?? entry.Notes;

            entry.Update(description, date, notes);
            // No write to the patient row. A history entry is a child, and on this entity `UpdatedAt` shares
            // its row with the concurrency token — stamping it here refused the user’s own next save.
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
            return Result<PatientMedicalHistoryDto>.Failure($"Error updating medical history entry: {ex.Message}");
        }
    }
}


