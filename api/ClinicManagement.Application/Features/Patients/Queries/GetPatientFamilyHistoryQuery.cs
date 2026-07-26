using MediatR;
using ClinicManagement.Application.Common.Interfaces;
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
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetPatientFamilyHistoryQueryHandler(IPatientRepository patientRepository, ICurrentClinicResolver clinicResolver)
    {
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<IEnumerable<PatientFamilyHistoryDto>>> Handle(GetPatientFamilyHistoryQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // Authoritative tenant guard: resolve the caller's clinic from the DB and verify the patient
            // belongs to it before returning family-history PHI — defense-in-depth, independent of the
            // fail-open global filter (cloud-security-and-tenant-isolation #6).
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<IEnumerable<PatientFamilyHistoryDto>>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<IEnumerable<PatientFamilyHistoryDto>>.Failure("Patient introuvable.");
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










