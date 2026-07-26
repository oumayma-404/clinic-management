using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Enums;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

namespace ClinicManagement.Application.Features.Appointments.Commands;

public class UpdateAppointmentCommand : IRequest<Result<AppointmentDto>>
{
    public Guid Id { get; set; }
    public DateTime? AppointmentDateTime { get; set; }
    public int? DurationMinutes { get; set; }
    public string? DoctorName { get; set; }
    public string? Notes { get; set; }
    public string? Status { get; set; }
    public Guid? ProcedureTypeId { get; set; }
    /// <summary>Reassign the appointment's practitioner (an FK to Doctor). Ignored when null.</summary>
    public Guid? DoctorId { get; set; }

    /// <summary>The plan the linked act belongs to — required whenever <see cref="TreatmentPlanItemId"/> is set.</summary>
    public Guid? TreatmentPlanId { get; set; }

    private Guid? _treatmentPlanItemId;

    /// <summary>
    /// Move — or clear — the treatment-plan act this appointment schedules.
    /// <para>
    /// Deliberately tri-state, unlike the other nullable fields here: an explicit <c>null</c> clears the
    /// link, while <b>omitting</b> the property leaves it untouched. Every existing caller (the edit dialog,
    /// the calendar's status flips, the drag-to-reschedule) sends neither field, so treating "absent" as
    /// "clear" would silently orphan the plan link on any unrelated edit — and because the link has no FK,
    /// nothing at the database level would catch it.
    /// </para>
    /// </summary>
    public Guid? TreatmentPlanItemId
    {
        get => _treatmentPlanItemId;
        set
        {
            _treatmentPlanItemId = value;
            TreatmentPlanItemIdSpecified = true;
        }
    }

    /// <summary>True once the caller has actually sent <see cref="TreatmentPlanItemId"/> (System.Text.Json
    /// only assigns properties present in the payload). Not part of the wire contract.</summary>
    [JsonIgnore]
    public bool TreatmentPlanItemIdSpecified { get; private set; }
}

public class UpdateAppointmentCommandHandler : IRequestHandler<UpdateAppointmentCommand, Result<AppointmentDto>>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly IDoctorRepository _doctorRepository;
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
                return Result<AppointmentDto>.Failure("Appointment not found");
            }

            // Explicit tenant check (defense-in-depth alongside the global query filter): an appointment
            // from another clinic reads as "not found".
            if (appointment.ClinicId != clinicResult.Value)
            {
                return Result<AppointmentDto>.Failure("Appointment not found");
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

            // Update duration if provided
            if (request.DurationMinutes.HasValue && request.DurationMinutes.Value > 0)
            {
                var newDuration = TimeSpan.FromMinutes(request.DurationMinutes.Value);
                if (appointment.Duration != newDuration)
                {
                    appointment.UpdateDuration(newDuration);
                }
            }

            // Update doctor name if provided
            if (request.DoctorName != null)
            {
                appointment.UpdateDoctorName(request.DoctorName);
            }

            // Reassign the practitioner if provided (validated against the clinic — the DoctorId FK).
            if (request.DoctorId.HasValue && request.DoctorId != appointment.DoctorId)
            {
                var doctor = await _doctorRepository.GetByIdAsync(request.DoctorId.Value, cancellationToken);
                if (doctor == null || doctor.ClinicId != clinicResult.Value)
                {
                    return Result<AppointmentDto>.Failure("Doctor not found");
                }
                appointment.SetDoctorId(request.DoctorId);
            }

            // Update notes if provided
            if (request.Notes != null)
            {
                appointment.UpdateNotes(request.Notes);
            }

            // Update procedure type - check if it's being changed
            // If request.ProcedureTypeId is different from current, update it
            if (request.ProcedureTypeId != appointment.ProcedureTypeId)
            {
                if (request.ProcedureTypeId.HasValue)
                {
                    var procedureType = await _procedureTypeRepository.GetByIdAsync(request.ProcedureTypeId.Value, cancellationToken);
                    if (procedureType == null || procedureType.ClinicId != clinicResult.Value)
                    {
                        return Result<AppointmentDto>.Failure("Procedure type not found");
                    }
                    
                    if (!procedureType.IsActive)
                    {
                        return Result<AppointmentDto>.Failure("Selected procedure type is not active");
                    }
                    
                    appointment.SetProcedureType(
                        procedureType.Id,
                        procedureType.DefaultDurationMinutes,
                        procedureType.Color.Value);
                }
                else
                {
                    // Clear procedure type
                    appointment.SetProcedureType(null, null, null);
                }
            }

            // Update status if provided and different from current
            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                if (Enum.TryParse<AppointmentStatus>(request.Status, true, out var newStatus))
                {
                    // Only update if status is different
                    if (appointment.Status != newStatus)
                    {
                        switch (newStatus)
                        {
                            case AppointmentStatus.Scheduled:
                                // If currently cancelled, reactivate it (Reschedule forbids a cancelled appt).
                                if (appointment.Status == AppointmentStatus.Cancelled)
                                {
                                    appointment.Reactivate(appointment.AppointmentDateTime);
                                }
                                // If status is already scheduled, no change needed
                                break;
                            case AppointmentStatus.Confirmed:
                                if (appointment.Status != AppointmentStatus.Confirmed)
                                {
                                    appointment.Confirm();
                                }
                                break;
                            case AppointmentStatus.Completed:
                                if (appointment.Status == AppointmentStatus.InProgress)
                                {
                                    appointment.Complete();
                                }
                                // Note: Can't directly set to Completed from other states
                                break;
                            case AppointmentStatus.Cancelled:
                                if (appointment.Status != AppointmentStatus.Cancelled && 
                                    appointment.Status != AppointmentStatus.Completed)
                                {
                                    _logger.LogInformation("Cancelling appointment {AppointmentId}. Current GoogleCalendarEventId: {GoogleEventId}", 
                                        appointment.Id, appointment.GoogleCalendarEventId ?? "(none)");
                                    appointment.Cancel();
                                }
                                break;
                            case AppointmentStatus.InProgress:
                                if (appointment.Status == AppointmentStatus.Confirmed || 
                                    appointment.Status == AppointmentStatus.Scheduled)
                                {
                                    appointment.Start();
                                }
                                break;
                            case AppointmentStatus.NoShow:
                                if (appointment.Status != AppointmentStatus.Completed && 
                                    appointment.Status != AppointmentStatus.Cancelled)
                                {
                                    appointment.MarkAsNoShow();
                                }
                                break;
                        }
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
                var windowStart = appointment.AppointmentDateTime.AddDays(-1);
                var windowEnd = appointment.AppointmentDateTime + appointment.Duration;
                var others = await _appointmentRepository.GetByClinicIdAsync(
                    clinicResult.Value, windowStart, windowEnd, appointment.DoctorId, cancellationToken);
                var collides = others.Any(e =>
                    e.Id != appointment.Id &&
                    e.Status != AppointmentStatus.Cancelled &&
                    e.Status != AppointmentStatus.NoShow &&
                    Overlaps(e.AppointmentDateTime, e.Duration, appointment.AppointmentDateTime, appointment.Duration));
                if (collides)
                {
                    return Result<AppointmentDto>.Failure("Ce créneau est déjà réservé pour ce praticien.");
                }
            }

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
                CreatedAt = appointment.CreatedAt.Kind == DateTimeKind.Utc
                    ? appointment.CreatedAt
                    : DateTime.SpecifyKind(appointment.CreatedAt, DateTimeKind.Utc),
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
        catch (Exception ex)
        {
            return Result<AppointmentDto>.Failure($"Error updating appointment: {ex.Message}");
        }
    }

    private static bool Overlaps(DateTime aStart, TimeSpan aDuration, DateTime bStart, TimeSpan bDuration) =>
        aStart < bStart + bDuration && bStart < aStart + aDuration;
}

