using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Commands;

public class UpdateDentalRecordCommand : IRequest<Result<DentalRecordDto>>
{
    /// <summary>
    /// The <c>Version</c> the client read. Round-tripped so the save is validated against the copy the user
    /// actually edited rather than the one the handler just loaded. Omit (0) to skip the check — the seam
    /// server-internal writers use; see <c>IUnitOfWork.SetExpectedVersion</c>.
    /// </summary>
    public uint Version { get; set; }

    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public DateTime InterventionDate { get; set; }
    public decimal AmountPaid { get; set; }
    public bool IsAdultTeeth { get; set; }
    public List<DentalActInput> Acts { get; set; } = new();
    public List<string> Notes { get; set; } = new();
    public List<string> ImportantNotes { get; set; } = new();
    /// <summary>Optional treatment plan whose step this record completes (required when <see cref="TreatmentPlanItemId"/> is set).</summary>
    public Guid? TreatmentPlanId { get; set; }
    /// <summary>Optional plan step this record carries out — marked "réalisé" and linked to this record on save.</summary>
    public Guid? TreatmentPlanItemId { get; set; }
}

public class UpdateDentalRecordCommandHandler : IRequestHandler<UpdateDentalRecordCommand, Result<DentalRecordDto>>
{
    private readonly IDentalRecordRepository _dentalRecordRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IToothStateRepository _toothStateRepository;
    private readonly ITreatmentPlanRepository _treatmentPlanRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IStockConsumptionService _stockConsumption;

    public UpdateDentalRecordCommandHandler(
        IDentalRecordRepository dentalRecordRepository,
        IPatientRepository patientRepository,
        IToothStateRepository toothStateRepository,
        ITreatmentPlanRepository treatmentPlanRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        IStockConsumptionService stockConsumption)
    {
        _dentalRecordRepository = dentalRecordRepository;
        _patientRepository = patientRepository;
        _toothStateRepository = toothStateRepository;
        _treatmentPlanRepository = treatmentPlanRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _stockConsumption = stockConsumption;
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
                return Result<DentalRecordDto>.Failure("Dossier dentaire introuvable.");
            }

            var patient = await _patientRepository.GetByIdAsync(dentalRecord.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<DentalRecordDto>.Failure("Dossier dentaire introuvable.");
            }

            var parsed = DentalRecordActParser.Parse(request.Acts);
            if (parsed.IsFailure)
            {
                return Result<DentalRecordDto>.Failure(parsed.Error!);
            }

            // AC-P4.10 on the EDIT path: consume only what this edit ADDS. A fiche is re-saved routinely (a
            // corrected note, one more tooth), and consuming the whole list again each time would draw stock for
            // materials already used — strictly worse than the under-consumption it replaces. Acts are counted
            // per procedure because SetActs regenerates act ids, so a before/after diff by id is impossible;
            // counting occurrences also keeps "two composites" meaning two capsules.
            var consumedBefore = CountByProcedure(dentalRecord.Acts.Select(a => a.ProcedureTypeId));

            dentalRecord.Update(request.InterventionDate, request.AmountPaid, request.Notes, request.ImportantNotes);
            dentalRecord.SetActs(parsed.Value!);

            var addedProcedureIds = PositiveDelta(
                consumedBefore, CountByProcedure(dentalRecord.Acts.Select(a => a.ProcedureTypeId)));

            // Validate the save against the version the USER was editing, not the one this
            // handler just loaded — that one always matches and would detect nothing.
            _unitOfWork.SetExpectedVersion(dentalRecord, request.Version);
            await _dentalRecordRepository.UpdateAsync(dentalRecord, cancellationToken);

            // Replace this record's odontogram entries (delete old, re-add from the new acts).
            var existingStates = await _toothStateRepository.GetByDentalRecordIdAsync(dentalRecord.Id, cancellationToken);
            foreach (var state in existingStates)
            {
                await _toothStateRepository.DeleteAsync(state.Id, cancellationToken);
            }

            var toothStates = DentalRecordActParser
                .BuildToothStates(parsed.Value!, dentalRecord.PatientId, request.InterventionDate, dentalRecord.Id)
                .ToList();

            // Treating a tooth closes any open diagnosis charted on it (AC-5).
            await DentalRecordLinker.ClearDiagnosesForTreatedTeethAsync(
                _toothStateRepository, dentalRecord.PatientId, toothStates, cancellationToken);

            foreach (var toothState in toothStates)
            {
                await _toothStateRepository.AddAsync(toothState, cancellationToken);
            }

            // Completing a scheduled plan step: mark it "réalisé" and link it to this record (AC-4).
            if (request.TreatmentPlanItemId.HasValue)
            {
                var link = await DentalRecordLinker.LinkPlanItemAsync(
                    _treatmentPlanRepository, request.TreatmentPlanId, request.TreatmentPlanItemId.Value,
                    dentalRecord.PatientId, clinicResult.Value, dentalRecord.Id, request.InterventionDate, cancellationToken);
                if (link.IsFailure)
                {
                    return Result<DentalRecordDto>.Failure(link.Error!);
                }
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Post-commit and best-effort, exactly as on create (AC-P4.13). Removing an act deliberately
            // consumes nothing and returns nothing: the material was physically used, so an automatic
            // put-back would invent stock that is not on the shelf. That correction is a manual Adjustment.
            await _stockConsumption.ConsumeForDentalRecordAsync(
                clinicResult.Value, dentalRecord.Id, addedProcedureIds, cancellationToken);

            dentalRecord = await _dentalRecordRepository.GetByIdAsync(request.Id, cancellationToken);
            return Result<DentalRecordDto>.Success(dentalRecord!.ToDto());
        }
        catch (ArgumentException ex)
        {
            return Result<DentalRecordDto>.Failure(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Result<DentalRecordDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<DentalRecordDto>.Failure($"Error updating dental record: {ex.Message}");
        }
    }

    /// <summary>Occurrences of each catalogued procedure among a record's acts (free-text acts have no id).</summary>
    private static Dictionary<Guid, int> CountByProcedure(IEnumerable<Guid?> procedureTypeIds)
    {
        var counts = new Dictionary<Guid, int>();
        foreach (var id in procedureTypeIds)
        {
            if (id.HasValue)
            {
                counts[id.Value] = counts.GetValueOrDefault(id.Value) + 1;
            }
        }

        return counts;
    }

    /// <summary>
    /// The procedure ids this edit ADDED, one entry per added performance. A procedure that stayed the same or
    /// was removed contributes nothing.
    /// </summary>
    private static List<Guid> PositiveDelta(Dictionary<Guid, int> before, Dictionary<Guid, int> after)
    {
        var added = new List<Guid>();
        foreach (var (procedureTypeId, count) in after)
        {
            var delta = count - before.GetValueOrDefault(procedureTypeId);
            for (var i = 0; i < delta; i++)
            {
                added.Add(procedureTypeId);
            }
        }

        return added;
    }
}
