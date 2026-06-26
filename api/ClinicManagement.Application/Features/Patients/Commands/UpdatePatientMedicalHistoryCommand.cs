using MediatR;
using ClinicManagement.Application.Common.Models;
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
    private readonly IUnitOfWork _unitOfWork;

    public UpdatePatientMedicalHistoryCommandHandler(IPatientRepository patientRepository, IUnitOfWork unitOfWork)
    {
        _patientRepository = patientRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PatientMedicalHistoryDto>> Handle(UpdatePatientMedicalHistoryCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null)
            {
                return Result<PatientMedicalHistoryDto>.Failure("Patient not found");
            }

            var entry = patient.MedicalHistoryEntries.FirstOrDefault(e => e.Id == request.Id);
            if (entry == null)
            {
                return Result<PatientMedicalHistoryDto>.Failure("Medical history entry not found");
            }

            var description = request.Description ?? entry.Description;
            var date = request.Date ?? entry.Date;
            var notes = request.Notes ?? entry.Notes;

            entry.Update(description, date, notes);
            // Update only the patient's UpdatedAt property (entry changes are automatically tracked)
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
        catch (Exception ex)
        {
            return Result<PatientMedicalHistoryDto>.Failure($"Error updating medical history entry: {ex.Message}");
        }
    }
}


