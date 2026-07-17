using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Queries;

public class GetDentalRecordsQuery : IRequest<Result<IEnumerable<DentalRecordDto>>>
{
    public Guid PatientId { get; set; }
}

public class GetDentalRecordsQueryHandler : IRequestHandler<GetDentalRecordsQuery, Result<IEnumerable<DentalRecordDto>>>
{
    private readonly IDentalRecordRepository _dentalRecordRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetDentalRecordsQueryHandler(
        IDentalRecordRepository dentalRecordRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver)
    {
        _dentalRecordRepository = dentalRecordRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<IEnumerable<DentalRecordDto>>> Handle(GetDentalRecordsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // Verify the owning patient belongs to the caller's clinic before returning any records.
            // DentalRecord is a child entity with no ClinicId of its own and is not covered by the global
            // query filter, so this explicit check is the sole tenant guard for this read (AC-1).
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<IEnumerable<DentalRecordDto>>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<IEnumerable<DentalRecordDto>>.Failure("Patient not found");
            }

            var records = await _dentalRecordRepository.GetByPatientIdAsync(request.PatientId, cancellationToken);

            var dtos = records.Select(dr => new DentalRecordDto
            {
                Id = dr.Id,
                PatientId = dr.PatientId,
                InterventionDate = dr.InterventionDate,
                ProcedureType = dr.ProcedureType,
                Cost = dr.Cost,
                AmountPaid = dr.AmountPaid,
                Balance = dr.Cost - dr.AmountPaid,
                Notes = dr.Notes.ToList(),
                ImportantNotes = dr.ImportantNotes.ToList(),
                IsAdultTeeth = dr.IsAdultTeeth,
                ToothNumbers = dr.Teeth.Select(t => t.ToothNumber).OrderBy(t => t).ToList(),
                CreatedAt = dr.CreatedAt,
                UpdatedAt = dr.UpdatedAt
            });

            return Result<IEnumerable<DentalRecordDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<DentalRecordDto>>.Failure($"Error retrieving dental records: {ex.Message}");
        }
    }
}

