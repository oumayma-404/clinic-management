using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Commands;

public class CreateDentalRecordCommand : IRequest<Result<DentalRecordDto>>
{
    public Guid PatientId { get; set; }
    public DateTime InterventionDate { get; set; }
    public string ProcedureType { get; set; } = string.Empty;
    public decimal Cost { get; set; }
    public decimal AmountPaid { get; set; }
    public bool IsAdultTeeth { get; set; }
    public List<int> ToothNumbers { get; set; } = new();
    public List<string> Notes { get; set; } = new();
    public List<string> ImportantNotes { get; set; } = new();
}

public class CreateDentalRecordCommandHandler : IRequestHandler<CreateDentalRecordCommand, Result<DentalRecordDto>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IDentalRecordRepository _dentalRecordRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateDentalRecordCommandHandler(
        IPatientRepository patientRepository,
        IDentalRecordRepository dentalRecordRepository,
        IUnitOfWork unitOfWork)
    {
        _patientRepository = patientRepository;
        _dentalRecordRepository = dentalRecordRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<DentalRecordDto>> Handle(CreateDentalRecordCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.ProcedureType))
            {
                return Result<DentalRecordDto>.Failure("Procedure type is required");
            }

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null)
            {
                return Result<DentalRecordDto>.Failure("Patient not found");
            }

            // Validate tooth numbers match the teeth type (adult vs child)
            foreach (var toothNumber in request.ToothNumbers)
            {
                var isAdultTooth = DentalRecordTooth.IsAdultTooth(toothNumber);
                if (isAdultTooth != request.IsAdultTeeth)
                {
                    return Result<DentalRecordDto>.Failure(
                        $"Tooth number {toothNumber} is {(isAdultTooth ? "an adult" : "a child")} tooth, but the record is marked for {(request.IsAdultTeeth ? "adult" : "child")} teeth");
                }
            }

            var dentalRecord = new DentalRecord(
                Guid.NewGuid(),
                request.PatientId,
                request.InterventionDate,
                request.ProcedureType,
                request.Cost,
                request.AmountPaid,
                request.IsAdultTeeth,
                request.Notes,
                request.ImportantNotes);

            // Add teeth
            foreach (var toothNumber in request.ToothNumbers)
            {
                dentalRecord.AddTooth(toothNumber);
            }

            await _dentalRecordRepository.AddAsync(dentalRecord, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var dto = new DentalRecordDto
            {
                Id = dentalRecord.Id,
                PatientId = dentalRecord.PatientId,
                InterventionDate = dentalRecord.InterventionDate,
                ProcedureType = dentalRecord.ProcedureType,
                Cost = dentalRecord.Cost,
                AmountPaid = dentalRecord.AmountPaid,
                Notes = dentalRecord.Notes.ToList(),
                ImportantNotes = dentalRecord.ImportantNotes.ToList(),
                IsAdultTeeth = dentalRecord.IsAdultTeeth,
                ToothNumbers = dentalRecord.Teeth.Select(t => t.ToothNumber).OrderBy(t => t).ToList(),
                CreatedAt = dentalRecord.CreatedAt,
                UpdatedAt = dentalRecord.UpdatedAt
            };

            return Result<DentalRecordDto>.Success(dto);
        }
        catch (Exception ex)
        {
            return Result<DentalRecordDto>.Failure($"Error creating dental record: {ex.Message}");
        }
    }
}

