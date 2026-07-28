using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Commands;

public class CreateDentalRecordCommand : IRequest<Result<DentalRecordDto>>
{
    public Guid PatientId { get; set; }
    public DateTime InterventionDate { get; set; }
    public decimal AmountPaid { get; set; }
    public bool IsAdultTeeth { get; set; }
    /// <summary>The acts performed this session — the record's procedure summary + cost are derived from these.</summary>
    public List<DentalActInput> Acts { get; set; } = new();
    public List<string> Notes { get; set; } = new();
    public List<string> ImportantNotes { get; set; } = new();
    /// <summary>Optional treatment plan whose step this record completes (required when <see cref="TreatmentPlanItemId"/> is set).</summary>
    public Guid? TreatmentPlanId { get; set; }
    /// <summary>Optional plan step this record carries out — marked "réalisé" and linked to this record on save.</summary>
    public Guid? TreatmentPlanItemId { get; set; }
    /// <summary>Optional appointment this record documents — completing it and dismissing its post-visit review
    /// prompt (finding #10), so recording the dental work (not only a medical document) closes the loop.</summary>
    public Guid? AppointmentId { get; set; }
}

public class CreateDentalRecordCommandHandler : IRequestHandler<CreateDentalRecordCommand, Result<DentalRecordDto>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IDentalRecordRepository _dentalRecordRepository;
    private readonly IToothStateRepository _toothStateRepository;
    private readonly ITreatmentPlanRepository _treatmentPlanRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationGenerator _notificationGenerator;
    private readonly IStockConsumptionService _stockConsumption;
    private readonly IRealtimeNotifier _realtimeNotifier;
    private readonly ILogger<CreateDentalRecordCommandHandler> _logger;

    public CreateDentalRecordCommandHandler(
        IPatientRepository patientRepository,
        IDentalRecordRepository dentalRecordRepository,
        IToothStateRepository toothStateRepository,
        ITreatmentPlanRepository treatmentPlanRepository,
        IAppointmentRepository appointmentRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        INotificationGenerator notificationGenerator,
        IStockConsumptionService stockConsumption,
        IRealtimeNotifier realtimeNotifier,
        ILogger<CreateDentalRecordCommandHandler> logger)
    {
        _patientRepository = patientRepository;
        _dentalRecordRepository = dentalRecordRepository;
        _toothStateRepository = toothStateRepository;
        _treatmentPlanRepository = treatmentPlanRepository;
        _appointmentRepository = appointmentRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _notificationGenerator = notificationGenerator;
        _stockConsumption = stockConsumption;
        _realtimeNotifier = realtimeNotifier;
        _logger = logger;
    }

    public async Task<Result<DentalRecordDto>> Handle(CreateDentalRecordCommand request, CancellationToken cancellationToken)
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

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<DentalRecordDto>.Failure("Patient introuvable.");
            }

            var parsed = DentalRecordActParser.Parse(request.Acts);
            if (parsed.IsFailure)
            {
                return Result<DentalRecordDto>.Failure(parsed.Error!);
            }

            var record = new DentalRecord(
                Guid.NewGuid(),
                request.PatientId,
                request.InterventionDate,
                request.AmountPaid,
                request.IsAdultTeeth,
                request.Notes,
                request.ImportantNotes);

            record.SetActs(parsed.Value!);

            await _dentalRecordRepository.AddAsync(record, cancellationToken);

            var toothStates = DentalRecordActParser
                .BuildToothStates(parsed.Value!, request.PatientId, request.InterventionDate, record.Id)
                .ToList();

            // Treating a tooth closes any open diagnosis charted on it (AC-5).
            await DentalRecordLinker.ClearDiagnosesForTreatedTeethAsync(
                _toothStateRepository, request.PatientId, toothStates, cancellationToken);

            foreach (var toothState in toothStates)
            {
                await _toothStateRepository.AddAsync(toothState, cancellationToken);
            }

            // Completing a scheduled plan step: mark it "réalisé" and link it to this record (AC-4).
            if (request.TreatmentPlanItemId.HasValue)
            {
                var link = await DentalRecordLinker.LinkPlanItemAsync(
                    _treatmentPlanRepository, request.TreatmentPlanId, request.TreatmentPlanItemId.Value,
                    request.PatientId, clinicResult.Value, record.Id, request.InterventionDate, cancellationToken);
                if (link.IsFailure)
                {
                    return Result<DentalRecordDto>.Failure(link.Error!);
                }
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // If this record documents a scheduled appointment, close its post-visit-review loop
            // (finding #10) — best-effort, never rolls back the committed record.
            if (request.AppointmentId.HasValue)
            {
                await CompleteReviewedAppointmentAsync(request.AppointmentId.Value, clinicResult.Value, cancellationToken);
            }

            // AC-P4.10 — draw each recorded act's material list out of stock. Post-commit and best-effort, so a
            // stock failure can never lose the clinical record (AC-P4.13). One entry per act performance, not
            // per distinct procedure: two composites really do use two capsules.
            await _stockConsumption.ConsumeForDentalRecordAsync(
                clinicResult.Value,
                record.Id,
                record.Acts.Where(a => a.ProcedureTypeId.HasValue).Select(a => a.ProcedureTypeId!.Value).ToList(),
                cancellationToken);

            return Result<DentalRecordDto>.Success(record.ToDto());
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
            _logger.LogError(ex, "Unhandled failure creating a dental record");
            return Result<DentalRecordDto>.Failure("Erreur lors de l'enregistrement de la fiche de soins. Veuillez réessayer.");
        }
    }

    // Marks the documented appointment Completed (if it resolves in the caller's clinic) and removes its
    // pending post-visit review — mirrors CreateMedicalDocumentCommand.CompleteReviewedAppointmentAsync.
    // A cross-clinic/missing id is a silent no-op. Wrapped so any failure only logs — never rolls back the
    // already-committed dental record.
    private async Task CompleteReviewedAppointmentAsync(Guid appointmentId, Guid clinicId, CancellationToken cancellationToken)
    {
        try
        {
            var appointment = await _appointmentRepository.GetByIdAsync(appointmentId, cancellationToken);
            if (appointment == null || appointment.ClinicId != clinicId)
            {
                return; // cross-clinic or unknown id → leave everything unchanged
            }

            // AC-P1.12: three outcomes, not two. `Contradicted` means the fiche documents a visit the schedule
            // says was cancelled or missed — logged as a Warning so it is discoverable, and the appointment is
            // deliberately NOT reopened (silently un-cancelling a visit is the invisible state change this part
            // exists to remove).
            var outcome = appointment.MarkVisitCompleted();
            if (outcome == VisitCompletionOutcome.Contradicted)
            {
                _logger.LogWarning(
                    "Fiche de soins recorded against appointment {AppointmentId}, which is {Status}. The "
                    + "appointment was left unchanged; its post-visit review is cleared regardless.",
                    appointmentId, appointment.Status);
            }

            // Only Completed actually changed the row; the other two outcomes have nothing to persist, so the
            // save is skipped rather than issuing an UPDATE that sets nothing.
            if (outcome == VisitCompletionOutcome.Completed)
            {
                // Change-tracked from GetByIdAsync, so SaveChanges persists MarkVisitCompleted().
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            // The review is fulfilled either way — including on AlreadyCompleted (idempotent) and on
            // Contradicted, where leaving the prompt up would nag about a visit that is not going to happen.
            await _notificationGenerator.CancelPostVisitReviewAsync(appointment.ClinicId, appointmentId, cancellationToken);

            // This command broadcasts "patients"; also tell "appointments" consumers so the calendar reflects
            // the now-Completed status.
            await _realtimeNotifier.NotifyEntityChangedAsync(appointment.ClinicId, "appointments", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Post-visit completion side-effect failed for appointment {AppointmentId}", appointmentId);
        }
    }
}
