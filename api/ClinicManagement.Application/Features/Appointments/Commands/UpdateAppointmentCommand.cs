using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Appointments;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Enums;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

namespace ClinicManagement.Application.Features.Appointments.Commands;

/// <summary>
/// Partially update an appointment.
///
/// <para>
/// <b>Every nullable field here is tri-state.</b> Omitting a property leaves it untouched; sending an explicit
/// <c>null</c> clears it. System.Text.Json only invokes a setter for a key physically present in the payload,
/// so each setter doubles as a "was this sent?" probe.
/// </para>
/// <para>
/// This used to be true of <see cref="TreatmentPlanItemId"/> alone, and the inconsistency was a data-loss bug:
/// <see cref="ProcedureTypeId"/> was compared against the stored value with no notion of "provided", so an
/// omitted key bound to <c>null</c>, read as "different from the current act", and wiped the procedure type,
/// its snapshot duration and its colour. Cancelling an appointment — which posts <c>{ status }</c> alone, from
/// the edit dialog and from the AI assistant — destroyed the act every time.
/// </para>
/// <para>
/// The mirror-image defect applied to <see cref="Notes"/>, <see cref="DoctorName"/> and <see cref="DoctorId"/>:
/// they treated <c>null</c> as "not provided", so those fields could never be <i>cleared</i> at all. Emptying
/// the notes box was a silent no-op and a practitioner could never be unassigned.
/// </para>
/// </summary>
public class UpdateAppointmentCommand : IRequest<Result<AppointmentDto>>
{
    /// <summary>
    /// The <c>Version</c> the client read. Round-tripped so the save is validated against the copy the user
    /// actually edited rather than the one the handler just loaded. Omit (0) to skip the check — the seam
    /// server-internal writers use; see <c>IUnitOfWork.SetExpectedVersion</c>.
    /// </summary>
    public uint Version { get; set; }

    public Guid Id { get; set; }
    public DateTime? AppointmentDateTime { get; set; }
    public int? DurationMinutes { get; set; }
    public string? Status { get; set; }

    /// <summary>
    /// Why the appointment was cancelled. Only read when <see cref="Status"/> moves to <c>Cancelled</c>.
    /// <para>
    /// The cancel path called <c>appointment.Cancel()</c> with no argument, so <c>CancellationReason</c> was
    /// **always null** on every cancellation made through the UI — even though the column exists, the entity
    /// accepts a reason, and `CancelRecurringSeriesCommand` records one. Now that cancelling a *completed*
    /// visit is possible (AC-P1.5), knowing why matters more, not less.
    /// </para>
    /// </summary>
    public string? CancellationReason { get; set; }

    /// <summary>
    /// Confirmed override for moving an appointment outside the practitioner's working hours (AC-P1.31).
    /// Recorded on the appointment rather than silently allowed.
    /// </summary>
    public bool AllowOutsideWorkingHours { get; set; }

    /// <summary>The plan the linked act belongs to — required whenever <see cref="TreatmentPlanItemId"/> is set.</summary>
    public Guid? TreatmentPlanId { get; set; }

    private string? _doctorName;
    private string? _notes;
    private Guid? _procedureTypeId;
    private Guid? _doctorId;
    private Guid? _treatmentPlanItemId;

    /// <summary>Free-text practitioner label. Explicit <c>null</c> clears it; omitting leaves it.</summary>
    public string? DoctorName
    {
        get => _doctorName;
        set { _doctorName = value; DoctorNameSpecified = true; }
    }

    /// <summary>Explicit <c>null</c> clears the notes; omitting leaves them.</summary>
    public string? Notes
    {
        get => _notes;
        set { _notes = value; NotesSpecified = true; }
    }

    /// <summary>
    /// The booked act. Explicit <c>null</c> clears it along with its snapshot duration and colour; omitting
    /// leaves all three untouched.
    /// </summary>
    public Guid? ProcedureTypeId
    {
        get => _procedureTypeId;
        set { _procedureTypeId = value; ProcedureTypeIdSpecified = true; }
    }

    /// <summary>Assigned practitioner (an FK to Doctor). Explicit <c>null</c> unassigns; omitting leaves it.</summary>
    public Guid? DoctorId
    {
        get => _doctorId;
        set { _doctorId = value; DoctorIdSpecified = true; }
    }

    /// <summary>Move — or clear — the treatment-plan act this appointment schedules.</summary>
    public Guid? TreatmentPlanItemId
    {
        get => _treatmentPlanItemId;
        set { _treatmentPlanItemId = value; TreatmentPlanItemIdSpecified = true; }
    }

    // "Was this property present in the payload?" — not part of the wire contract in either direction, so a
    // client can neither read nor forge them.
    [JsonIgnore] public bool DoctorNameSpecified { get; private set; }
    [JsonIgnore] public bool NotesSpecified { get; private set; }
    [JsonIgnore] public bool ProcedureTypeIdSpecified { get; private set; }
    [JsonIgnore] public bool DoctorIdSpecified { get; private set; }
    [JsonIgnore] public bool TreatmentPlanItemIdSpecified { get; private set; }
}

public class UpdateAppointmentCommandHandler : IRequestHandler<UpdateAppointmentCommand, Result<AppointmentDto>>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly ITreatmentPlanRepository _treatmentPlanRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAppointmentGoogleSyncDispatcher _googleSyncDispatcher;
    private readonly INotificationGenerator _notificationGenerator;
    private readonly IReminderScheduler _reminderScheduler;
    private readonly ILogger<UpdateAppointmentCommandHandler> _logger;

    public UpdateAppointmentCommandHandler(
        IAppointmentRepository appointmentRepository,
        IProcedureTypeRepository procedureTypeRepository,
        IDoctorRepository doctorRepository,
        IClinicRepository clinicRepository,
        ITreatmentPlanRepository treatmentPlanRepository,
        ICurrentClinicResolver clinicResolver,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        IAppointmentGoogleSyncDispatcher googleSyncDispatcher,
        INotificationGenerator notificationGenerator,
        IReminderScheduler reminderScheduler,
        ILogger<UpdateAppointmentCommandHandler> logger)
    {
        _appointmentRepository = appointmentRepository;
        _procedureTypeRepository = procedureTypeRepository;
        _doctorRepository = doctorRepository;
        _clinicRepository = clinicRepository;
        _treatmentPlanRepository = treatmentPlanRepository;
        _clinicResolver = clinicResolver;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
        _googleSyncDispatcher = googleSyncDispatcher;
        _notificationGenerator = notificationGenerator;
        _reminderScheduler = reminderScheduler;
        _logger = logger;
    }

    public async Task<Result<AppointmentDto>> Handle(UpdateAppointmentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<AppointmentDto>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var appointment = await _appointmentRepository.GetByIdAsync(request.Id, cancellationToken);
            if (appointment == null)
            {
                return Result<AppointmentDto>.Failure("Rendez-vous introuvable.");
            }

            // Explicit tenant check (defense-in-depth alongside the global query filter): an appointment
            // from another clinic reads as "not found".
            if (appointment.ClinicId != clinicResult.Value)
            {
                return Result<AppointmentDto>.Failure("Rendez-vous introuvable.");
            }

            // Capture pre-mutation state so we can tell (after commit) whether this update cancelled or
            // rescheduled the appointment, for in-app staff notifications (spec US-3).
            var oldStatus = appointment.Status;
            var oldDateTime = appointment.AppointmentDateTime;
            var oldDuration = appointment.Duration;
            var oldDoctorId = appointment.DoctorId;

            // Update appointment date/time if provided
            if (request.AppointmentDateTime.HasValue)
            {
                var appointmentDateTime = request.AppointmentDateTime.Value;
                if (appointmentDateTime.Kind == DateTimeKind.Unspecified)
                {
                    appointmentDateTime = DateTime.SpecifyKind(appointmentDateTime, DateTimeKind.Utc);
                }
                else if (appointmentDateTime.Kind == DateTimeKind.Local)
                {
                    appointmentDateTime = appointmentDateTime.ToUniversalTime();
                }

                if (appointment.AppointmentDateTime != appointmentDateTime)
                {
                    // A cancelled/completed appointment cannot be rescheduled directly (the domain guards it).
                    // If the caller is reactivating a cancelled appointment (status → Scheduled), un-cancel it
                    // as part of the move; if it stays cancelled, skip the date change so editing other fields
                    // (notes, doctor) doesn't 400 on the reschedule guard — e.g. when the sent start time
                    // differs only by zeroed seconds. Completed appointments are never rescheduled here.
                    if (appointment.Status == AppointmentStatus.Cancelled)
                    {
                        var reactivating = !string.IsNullOrWhiteSpace(request.Status)
                            && Enum.TryParse<AppointmentStatus>(request.Status, true, out var target)
                            && target == AppointmentStatus.Scheduled;
                        if (reactivating)
                        {
                            appointment.Reactivate(appointmentDateTime);
                        }
                    }
                    else if (appointment.Status != AppointmentStatus.Completed)
                    {
                        appointment.Reschedule(appointmentDateTime);
                    }
                }
            }

            // Update duration if provided. A non-positive duration is rejected rather than ignored — it used
            // to fall through this guard and return 200 having changed nothing.
            if (request.DurationMinutes.HasValue)
            {
                if (request.DurationMinutes.Value <= 0)
                {
                    return Result<AppointmentDto>.Failure("La durée doit être supérieure à 0 minute.");
                }

                var newDuration = TimeSpan.FromMinutes(request.DurationMinutes.Value);
                if (appointment.Duration != newDuration)
                {
                    appointment.UpdateDuration(newDuration);
                }
            }

            // Tri-state: only touch the name when the caller actually sent the field. An explicit null clears it.
            if (request.DoctorNameSpecified)
            {
                appointment.UpdateDoctorName(request.DoctorName);
            }

            // Tri-state. An explicit null unassigns the practitioner — previously unreachable, because null
            // was read as "not provided".
            if (request.DoctorIdSpecified && request.DoctorId != appointment.DoctorId)
            {
                if (request.DoctorId.HasValue)
                {
                    var doctor = await _doctorRepository.GetByIdAsync(request.DoctorId.Value, cancellationToken);
                    if (doctor == null || doctor.ClinicId != clinicResult.Value)
                    {
                        return Result<AppointmentDto>.Failure("Praticien introuvable.");
                    }
                }

                appointment.SetDoctorId(request.DoctorId);
            }

            // Tri-state: an explicit null clears the notes. Emptying the notes box used to be a silent no-op.
            if (request.NotesSpecified)
            {
                appointment.UpdateNotes(request.Notes);
            }

            // Tri-state — THE data-loss fix. Without the Specified guard an omitted key bound to null, compared
            // as "different from the current act", and wiped the procedure type plus its snapshot duration and
            // colour. Cancelling an appointment posts { status } alone, so every cancellation destroyed the act.
            if (request.ProcedureTypeIdSpecified && request.ProcedureTypeId != appointment.ProcedureTypeId)
            {
                if (request.ProcedureTypeId.HasValue)
                {
                    var procedureType = await _procedureTypeRepository.GetByIdAsync(request.ProcedureTypeId.Value, cancellationToken);
                    if (procedureType == null || procedureType.ClinicId != clinicResult.Value)
                    {
                        return Result<AppointmentDto>.Failure("Type de procédure introuvable.");
                    }

                    if (!procedureType.IsActive)
                    {
                        return Result<AppointmentDto>.Failure("Le type de procédure sélectionné n'est pas actif.");
                    }

                    appointment.SetProcedureType(
                        procedureType.Id,
                        procedureType.DefaultDurationMinutes,
                        procedureType.Color.Value);
                }
                else
                {
                    // Explicitly requested: clear the act and its snapshots.
                    appointment.SetProcedureType(null, null, null);
                }
            }

            // Update status if provided and different from current. An unparseable status is rejected rather
            // than ignored — it used to return 200 having changed nothing, so a typo read as success.
            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                if (!Enum.TryParse<AppointmentStatus>(request.Status, true, out var newStatus))
                {
                    return Result<AppointmentDto>.Failure($"Statut de rendez-vous invalide : « {request.Status} ».");
                }

                if (appointment.Status != newStatus)
                {
                    // AC-P1.2 / AC-P1.3: ask the domain's declared transition set instead of the fall-through
                    // `switch` that used to live here. Every arm of that switch was a silent no-op guard, so
                    // an illegal transition — « Terminé » on a Scheduled appointment, « En cours » on a
                    // cancelled one — returned **HTTP 200 having changed nothing**. The user saw a success
                    // toast and a status that had not moved.
                    if (!Appointment.CanTransition(appointment.Status, newStatus))
                    {
                        return Result<AppointmentDto>.Failure(
                            $"Transition impossible : un rendez-vous « {Appointment.FrenchLabel(appointment.Status)} » "
                            + $"ne peut pas passer à « {Appointment.FrenchLabel(newStatus)} ».");
                    }

                    // The transition is legal — route it to the mutator that owns the extra state each one
                    // carries (a cancellation reason, clearing one on reactivation). The domain re-checks the
                    // same table, so these calls cannot disagree with the guard above.
                    switch (newStatus)
                    {
                        case AppointmentStatus.Scheduled:
                            // Two legal sources, per the table: Cancelled (reactivation) and NoShow (rebooking).
                            // Reschedule() refuses a cancelled appointment by design, so un-cancelling routes
                            // through Reactivate() — which also clears the reason and the cancelled-at stamp.
                            // From NoShow, Reschedule() is what drops the absence (AC-P1.9).
                            if (appointment.Status == AppointmentStatus.Cancelled)
                            {
                                appointment.Reactivate(appointment.AppointmentDateTime);
                            }
                            else
                            {
                                appointment.Reschedule(appointment.AppointmentDateTime);
                            }
                            break;
                        case AppointmentStatus.Confirmed:
                            appointment.Confirm();
                            break;
                        case AppointmentStatus.InProgress:
                            appointment.Start();
                            break;
                        case AppointmentStatus.Completed:
                            appointment.Complete();
                            break;
                        case AppointmentStatus.Cancelled:
                            _logger.LogInformation(
                                "Cancelling appointment {AppointmentId} (from {FromStatus}). Current GoogleCalendarEventId: {GoogleEventId}",
                                appointment.Id, appointment.Status, appointment.GoogleCalendarEventId ?? "(none)");
                            appointment.Cancel(request.CancellationReason);
                            break;
                        case AppointmentStatus.NoShow:
                            appointment.MarkAsNoShow();
                            break;
                    }
                }
            }

            // Move or clear the treatment-plan act this appointment schedules (AC-17). Only when the caller
            // actually sent the field — see the tri-state note on the command. Rescheduling an appointment
            // onto a different act now updates the link instead of leaving a stale one pointing at the act
            // the patient is no longer coming in for.
            if (request.TreatmentPlanItemIdSpecified
                && request.TreatmentPlanItemId != appointment.TreatmentPlanItemId)
            {
                if (request.TreatmentPlanItemId.HasValue)
                {
                    // Same validation the create path uses: the act must exist on a plan of this clinic AND
                    // of this appointment's patient, so a link can never cross a tenant or a patient.
                    var linkResult = await AppointmentPlanLink.ValidateAsync(
                        _treatmentPlanRepository, request.TreatmentPlanId, request.TreatmentPlanItemId.Value,
                        clinicResult.Value, appointment.PatientId, cancellationToken);
                    if (linkResult.IsFailure)
                    {
                        return Result<AppointmentDto>.Failure(linkResult.Error!);
                    }
                }

                appointment.SetTreatmentPlanItem(request.TreatmentPlanItemId);
            }

            // Hard double-booking guard: after applying the requested changes, reject an overlapping,
            // still-active appointment for the same practitioner (excluding this one). Only when the
            // schedule actually changed (date/duration/doctor) and the appointment is still active — so a
            // notes-only edit never trips on a pre-existing clash, and cancelling never blocks.
            var scheduleChanged = appointment.AppointmentDateTime != oldDateTime
                                  || appointment.Duration != oldDuration
                                  || appointment.DoctorId != oldDoctorId;
            if (scheduleChanged
                && appointment.DoctorId.HasValue
                && appointment.Status != AppointmentStatus.Cancelled
                && appointment.Status != AppointmentStatus.Completed
                && appointment.Status != AppointmentStatus.NoShow)
            {
                // AC-P1.20/1.39: the same shared guard the create path uses, with the widened scan window (A-3).
                var collision = await AppointmentScheduling.FindCollisionAsync(
                    _appointmentRepository, clinicResult.Value, appointment.DoctorId,
                    appointment.AppointmentDateTime, appointment.Duration,
                    excludeAppointmentId: appointment.Id, cancellationToken);
                if (collision != null)
                {
                    return Result<AppointmentDto>.Failure(AppointmentScheduling.SlotTakenMessage(collision));
                }

                // AC-P1.20/1.28: moving an appointment is subject to the same working-hours rule as booking it.
                if (!request.AllowOutsideWorkingHours)
                {
                    var hoursCheck = await AppointmentScheduling.CheckWorkingHoursAsync(
                        _doctorRepository, _clinicRepository, clinicResult.Value, appointment.DoctorId,
                        appointment.AppointmentDateTime, appointment.Duration, cancellationToken);
                    if (hoursCheck.IsFailure)
                    {
                        return Result<AppointmentDto>.Failure(hoursCheck.Error!);
                    }
                }
                else
                {
                    appointment.MarkBookedOutsideWorkingHours();
                }
            }

            // Validate the save against the version the USER was editing, not the one this
            // handler just loaded — that one always matches and would detect nothing.
            _unitOfWork.SetExpectedVersion(appointment, request.Version);
            await _appointmentRepository.UpdateAsync(appointment, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Real-time "appointments changed" is broadcast centrally by RealtimeBroadcastBehavior after
            // this command returns success (covers cancellation — an update to status=Cancelled).

            // In-app staff notifications (best-effort, never fails this command). Only for appointments
            // with a patient (spec US-3). Actor is excluded from their own action's notification.
            if (appointment.PatientId.HasValue)
            {
                var actorUserId = _clinicContext.GetUserId();
                var patientName = appointment.Patient?.GetFullName() ?? "Patient";
                var becameCancelled = oldStatus != AppointmentStatus.Cancelled
                                      && appointment.Status == AppointmentStatus.Cancelled;
                // A cancelled→scheduled reactivation calls Reactivate(sameDateTime); guarding on an actual
                // date change means that no-op reactivation never emits a bogus "rescheduled" (plan R-3).
                var dateChanged = appointment.AppointmentDateTime != oldDateTime;
                // Reactivating a cancelled appointment: the cancel already deleted its reminder, so a
                // same-date reactivation would otherwise be left with no ~24h reminder (a date-changed
                // reactivation is covered by the reschedule branch, which recreates it).
                var becameReactivated = oldStatus == AppointmentStatus.Cancelled
                                        && appointment.Status == AppointmentStatus.Scheduled;

                if (becameCancelled)
                {
                    await _notificationGenerator.AppointmentCancelledAsync(
                        appointment.ClinicId, appointment.Id, actorUserId, patientName,
                        appointment.AppointmentDateTime, cancellationToken);
                }
                else if (dateChanged)
                {
                    await _notificationGenerator.AppointmentRescheduledAsync(
                        appointment.ClinicId, appointment.Id, actorUserId, patientName,
                        oldDateTime, appointment.AppointmentDateTime, cancellationToken);
                }
                else if (becameReactivated)
                {
                    await _notificationGenerator.ScheduleAppointmentReminderAsync(
                        appointment.ClinicId, appointment.Id, patientName,
                        appointment.AppointmentDateTime, cancellationToken);
                }

                // Post-visit review (independent of the reminder branches above): remove it on cancel or
                // no-show (a visit that never happened has no record to document), otherwise keep it in
                // sync with the current end time + doctor while the appointment is still active. This
                // covers reschedule, duration change, doctor change and reactivation.
                if (becameCancelled || appointment.Status == AppointmentStatus.NoShow)
                {
                    await _notificationGenerator.CancelPostVisitReviewAsync(
                        appointment.ClinicId, appointment.Id, cancellationToken);
                }
                else if (appointment.Status == AppointmentStatus.Scheduled
                         || appointment.Status == AppointmentStatus.Confirmed
                         || appointment.Status == AppointmentStatus.InProgress)
                {
                    await _notificationGenerator.EnsurePostVisitReviewAsync(
                        appointment.ClinicId, appointment.Id, appointment.DoctorId, patientName,
                        appointment.AppointmentDateTime + appointment.Duration, cancellationToken);
                }
                else if (appointment.Status == AppointmentStatus.Completed
                         && oldStatus != AppointmentStatus.Completed)
                {
                    // Visit just completed (P1-A): surface the "documenter / facturer / prochain RDV" prompt
                    // now instead of waiting for the originally-scheduled end time (e.g. when the dentist
                    // finishes early). Ensure = upsert, so this repoints the existing review row — no duplicate.
                    await _notificationGenerator.EnsurePostVisitReviewAsync(
                        appointment.ClinicId, appointment.Id, appointment.DoctorId, patientName,
                        DateTime.UtcNow, cancellationToken);
                }

                // Outbound SMS/WhatsApp reminders mirror the branches above: void unsent reminders on
                // cancel/no-show, void + re-enqueue on a reschedule (date change), and re-enqueue on
                // reactivation (the cancel had already voided them). Best-effort, never fails the update.
                var patientId = appointment.PatientId.Value;
                if (becameCancelled || appointment.Status == AppointmentStatus.NoShow)
                {
                    await _reminderScheduler.VoidForAppointmentAsync(appointment.Id, cancellationToken);
                }
                else if (dateChanged)
                {
                    await _reminderScheduler.RescheduleForAppointmentAsync(
                        appointment.ClinicId, appointment.Id, patientId, patientName,
                        appointment.AppointmentDateTime, cancellationToken);
                }
                else if (becameReactivated)
                {
                    await _reminderScheduler.ScheduleForAppointmentAsync(
                        appointment.ClinicId, appointment.Id, patientId, patientName,
                        appointment.AppointmentDateTime, cancellationToken);
                }
            }

            var dto = new AppointmentDto
            {
                Id = appointment.Id,
                ClinicId = appointment.ClinicId,
                PatientId = appointment.PatientId,
                PatientName = appointment.Patient?.GetFullName() ?? "Occupé",
                DoctorId = appointment.DoctorId,
                AppointmentDateTime = appointment.AppointmentDateTime.Kind == DateTimeKind.Utc
                    ? appointment.AppointmentDateTime
                    : DateTime.SpecifyKind(appointment.AppointmentDateTime, DateTimeKind.Utc),
                Duration = appointment.Duration,
                DoctorName = appointment.DoctorName,
                Notes = appointment.Notes,
                Status = appointment.Status.ToString(),
                AllowedNextStatuses = Appointment.NextStatusesFrom(appointment.Status).Select(s => s.ToString()).ToList(),
                CreatedAt = appointment.CreatedAt.Kind == DateTimeKind.Utc
                    ? appointment.CreatedAt
                    : DateTime.SpecifyKind(appointment.CreatedAt, DateTimeKind.Utc),
                Version = appointment.Version,
                ProcedureTypeId = appointment.ProcedureTypeId,
                ProcedureTypeName = appointment.ProcedureType?.Name,
                // Use current procedure type color if available, otherwise use stored color
                ProcedureColorHex = appointment.ProcedureType?.Color.Value ?? appointment.ProcedureColorHex,
                TreatmentPlanItemId = appointment.TreatmentPlanItemId,
                // Reflects committed state; the async Google sync below may set the id afterwards, and
                // the frontend refetches (bumps refreshKey) to clear the "non synchronisé" badge.
                IsSyncedToGoogle = appointment.GoogleCalendarEventId != null
            };

            // Push to Google Calendar post-commit (fire-and-forget, connectivity-gated). Best-effort — a
            // Google failure never affects the update; cancelled/completed appointments are removed from
            // Google by the sync service.
            _googleSyncDispatcher.Dispatch(appointment.Id);

            return Result<AppointmentDto>.Success(dto);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            // AC-13.2: the detail moves to the log; the caller gets French guidance, never exception text.
            _logger.LogError(ex, "Unhandled failure updating appointment {AppointmentId}", request.Id);
            return Result<AppointmentDto>.Failure("Erreur lors de la modification du rendez-vous. Veuillez réessayer.");
        }
    }

    private static bool Overlaps(DateTime aStart, TimeSpan aDuration, DateTime bStart, TimeSpan bDuration) =>
        aStart < bStart + bDuration && bStart < aStart + aDuration;
}

