using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Commands;

public class UpdateDentalRecordCommand : IRequest<Result<DentalRecordDto>>
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public DateTime InterventionDate { get; set; }
    public decimal AmountPaid { get; set; }
    public bool IsAdultTeeth { get; set; }
    public List<DentalActInput> Acts { get; set; } = new();
    public List<string> Notes { get; set; } = new();
    public List<string> ImportantNotes { get; set; } = new();
}

public class UpdateDentalRecordCommandHandler : IRequestHandler<UpdateDentalRecordCommand, Result<DentalRecordDto>>
{
    private readonly IDentalRecordRepository _dentalRecordRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IToothStateRepository _toothStateRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateDentalRecordCommandHandler(
        IDentalRecordRepository dentalRecordRepository,
        IPatientRepository patientRepository,
        IToothStateRepository toothStateRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _dentalRecordRepository = dentalRecordRepository;
        _patientRepository = patientRepository;
        _toothStateRepository = toothStateRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<DentalRecordDto>> Handle(UpdateDentalRecordCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.Acts.Count == 0)
            {
                return Result<DentalRecordDto>.Failure("Au moins un acte est requis.");
            }

            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<DentalRecordDto>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var dentalRecord = await _dentalRecordRepository.GetByIdAsync(request.Id, cancellationToken);
            if (dentalRecord == null || dentalRecord.PatientId != request.PatientId)
            {
                return Result<DentalRecordDto>.Failure("Dental record not found");
            }

            var patient = await _patientRepository.GetByIdAsync(dentalRecord.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<DentalRecordDto>.Failure("Dental record not found");
            }

            var parsed = DentalRecordActParser.Parse(request.Acts, request.IsAdultTeeth);
            if (parsed.IsFailure)
            {
                return Result<DentalRecordDto>.Failure(parsed.Error!);
            }

            dentalRecord.Update(request.InterventionDate, request.AmountPaid, request.Notes, request.ImportantNotes);
            dentalRecord.SetActs(parsed.Value!.Select(p =>
                (p.Input.ProcedureTypeId, p.Input.ProcedureName, p.Input.Cost,
                 (IReadOnlyList<int>)p.Input.ToothNumbers, p.Condition, p.Input.Surfaces, p.Input.Note)));

            await _dentalRecordRepository.UpdateAsync(dentalRecord, cancellationToken);

            // Replace this record's odontogram entries (delete old, re-add from the new acts).
            var existingStates = await _toothStateRepository.GetByDentalRecordIdAsync(dentalRecord.Id, cancellationToken);
            foreach (var state in existingStates)
            {
                await _toothStateRepository.DeleteAsync(state.Id, cancellationToken);
            }
            foreach (var toothState in DentalRecordActParser.BuildToothStates(parsed.Value!, dentalRecord.PatientId, request.InterventionDate, dentalRecord.Id))
            {
                await _toothStateRepository.AddAsync(toothState, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            dentalRecord = await _dentalRecordRepository.GetByIdAsync(request.Id, cancellationToken);
            return Result<DentalRecordDto>.Success(dentalRecord!.ToDto());
        }
        catch (ArgumentException ex)
        {
            return Result<DentalRecordDto>.Failure(ex.Message);
        }
        catch (Exception ex)
        {
            return Result<DentalRecordDto>.Failure($"Error updating dental record: {ex.Message}");
        }
    }
}
