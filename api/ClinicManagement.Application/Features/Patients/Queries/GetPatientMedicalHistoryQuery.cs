using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Queries;

public class GetPatientMedicalHistoryQuery : IRequest<Result<IEnumerable<PatientMedicalHistoryDto>>>
{
    public Guid PatientId { get; set; }
}

public class GetPatientMedicalHistoryQueryHandler : IRequestHandler<GetPatientMedicalHistoryQuery, Result<IEnumerable<PatientMedicalHistoryDto>>>
{
    private readonly IPatientRepository _patientRepository;

    public GetPatientMedicalHistoryQueryHandler(IPatientRepository patientRepository)
    {
        _patientRepository = patientRepository;
    }

    public async Task<Result<IEnumerable<PatientMedicalHistoryDto>>> Handle(GetPatientMedicalHistoryQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null)
            {
                return Result<IEnumerable<PatientMedicalHistoryDto>>.Failure("Patient not found");
            }

            var dtos = patient.MedicalHistoryEntries.Select(mh => new PatientMedicalHistoryDto
            {
                Id = mh.Id,
                PatientId = mh.PatientId,
                Description = mh.Description,
                Date = mh.Date,
                Notes = mh.Notes,
                CreatedAt = mh.CreatedAt
            }).OrderByDescending(mh => mh.Date ?? mh.CreatedAt);

            return Result<IEnumerable<PatientMedicalHistoryDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<PatientMedicalHistoryDto>>.Failure($"Error retrieving medical history: {ex.Message}");
        }
    }
}










