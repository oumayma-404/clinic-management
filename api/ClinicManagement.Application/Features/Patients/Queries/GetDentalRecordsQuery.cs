using MediatR;
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

    public GetDentalRecordsQueryHandler(IDentalRecordRepository dentalRecordRepository)
    {
        _dentalRecordRepository = dentalRecordRepository;
    }

    public async Task<Result<IEnumerable<DentalRecordDto>>> Handle(GetDentalRecordsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var records = await _dentalRecordRepository.GetByPatientIdAsync(request.PatientId, cancellationToken);

            var dtos = records.Select(dr => new DentalRecordDto
            {
                Id = dr.Id,
                PatientId = dr.PatientId,
                InterventionDate = dr.InterventionDate,
                ProcedureType = dr.ProcedureType,
                Cost = dr.Cost,
                AmountPaid = dr.AmountPaid,
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

