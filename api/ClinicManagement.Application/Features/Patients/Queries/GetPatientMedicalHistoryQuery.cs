using MediatR;
using ClinicManagement.Application.Common.Interfaces;
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
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetPatientMedicalHistoryQueryHandler(IPatientRepository patientRepository, ICurrentClinicResolver clinicResolver)
    {
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<IEnumerable<PatientMedicalHistoryDto>>> Handle(GetPatientMedicalHistoryQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // Authoritative tenant guard: resolve the caller's clinic from the DB and verify the patient
            // belongs to it before returning medical-history PHI — defense-in-depth, independent of the
            // fail-open global filter (cloud-security-and-tenant-isolation #6).
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<IEnumerable<PatientMedicalHistoryDto>>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
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










