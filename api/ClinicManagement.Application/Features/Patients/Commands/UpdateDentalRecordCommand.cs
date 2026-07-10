using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Commands;

public class UpdateDentalRecordCommand : IRequest<Result<DentalRecordDto>>
{
    public Guid Id { get; set; }
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

public class UpdateDentalRecordCommandHandler : IRequestHandler<UpdateDentalRecordCommand, Result<DentalRecordDto>>
{
    private readonly IDentalRecordRepository _dentalRecordRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateDentalRecordCommandHandler(
        IDentalRecordRepository dentalRecordRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _dentalRecordRepository = dentalRecordRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<DentalRecordDto>> Handle(UpdateDentalRecordCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.ProcedureType))
            {
                return Result<DentalRecordDto>.Failure("Procedure type is required");
            }

            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<DentalRecordDto>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var dentalRecord = await _dentalRecordRepository.GetByIdAsync(request.Id, cancellationToken);
            if (dentalRecord == null)
            {
                return Result<DentalRecordDto>.Failure("Dental record not found");
            }

            if (dentalRecord.PatientId != request.PatientId)
            {
                return Result<DentalRecordDto>.Failure("Dental record does not belong to the specified patient");
            }

            // Verify the owning patient belongs to the caller's clinic before mutating.
            var patient = await _patientRepository.GetByIdAsync(dentalRecord.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<DentalRecordDto>.Failure("Dental record not found");
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

            // Remove all existing teeth
            var existingTeeth = dentalRecord.Teeth.ToList();
            foreach (var tooth in existingTeeth)
            {
                dentalRecord.RemoveTooth(tooth.ToothNumber);
            }

            // Update record
            dentalRecord.Update(
                request.InterventionDate,
                request.ProcedureType,
                request.Cost,
                request.AmountPaid,
                request.Notes,
                request.ImportantNotes);

            // Add new teeth
            foreach (var toothNumber in request.ToothNumbers)
            {
                dentalRecord.AddTooth(toothNumber);
            }

            await _dentalRecordRepository.UpdateAsync(dentalRecord, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Reload to get updated data
            dentalRecord = await _dentalRecordRepository.GetByIdAsync(request.Id, cancellationToken);

            var dto = new DentalRecordDto
            {
                Id = dentalRecord!.Id,
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
            return Result<DentalRecordDto>.Failure($"Error updating dental record: {ex.Message}");
        }
    }
}

