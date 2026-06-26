using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Queries;

public class GetPatientFamilyHistoryQuery : IRequest<Result<IEnumerable<PatientFamilyHistoryDto>>>
{
    public Guid PatientId { get; set; }
}

public class GetPatientFamilyHistoryQueryHandler : IRequestHandler<GetPatientFamilyHistoryQuery, Result<IEnumerable<PatientFamilyHistoryDto>>>
{
    private readonly IPatientRepository _patientRepository;

    public GetPatientFamilyHistoryQueryHandler(IPatientRepository patientRepository)
    {
        _patientRepository = patientRepository;
    }

    public async Task<Result<IEnumerable<PatientFamilyHistoryDto>>> Handle(GetPatientFamilyHistoryQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null)
            {
                return Result<IEnumerable<PatientFamilyHistoryDto>>.Failure("Patient not found");
            }

            var dtos = patient.FamilyHistoryEntries.Select(fh => new PatientFamilyHistoryDto
            {
                Id = fh.Id,
                PatientId = fh.PatientId,
                Relationship = fh.Relationship,
                Condition = fh.Condition,
                Notes = fh.Notes,
                CreatedAt = fh.CreatedAt
            }).OrderBy(fh => fh.Relationship);

            return Result<IEnumerable<PatientFamilyHistoryDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<PatientFamilyHistoryDto>>.Failure($"Error retrieving family history: {ex.Message}");
        }
    }
}










