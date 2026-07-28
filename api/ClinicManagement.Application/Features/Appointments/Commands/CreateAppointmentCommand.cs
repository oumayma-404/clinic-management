using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments.Commands;

public class CreateAppointmentCommand : IRequest<Result<AppointmentDto>>
{
    public Guid? PatientId { get; set; }
    public Guid? DoctorId { get; set; }
    public DateTime AppointmentDateTime { get; set; }
    public int DurationMinutes { get; set; }

    /// <summary>
    /// Confirmed override for a booking outside the practitioner's working hours (AC-P1.31). Available to any
    /// role that can book; the acceptance is recorded on the appointment via
    /// <c>MarkBookedOutsideWorkingHours()</c>, never silently allowed.
    /// </summary>
    public bool AllowOutsideWorkingHours { get; set; }
    public string? DoctorName { get; set; }
    public string? Notes { get; set; }
    public Guid? ProcedureTypeId { get; set; }
    /// <summary>Optional treatment plan the linked step belongs to (required when <see cref="TreatmentPlanItemId"/> is set).</summary>
    public Guid? TreatmentPlanId { get; set; }
    /// <summary>Optional treatment-plan step this appointment schedules.</summary>
    public Guid? TreatmentPlanItemId { get; set; }
}

public class CreateAppointmentCommandHandler : IRequestHandler<CreateAppointmentCommand, Result<AppointmentDto>>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly ITreatmentPlanRepository _treatmentPlanRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationGenerator _notificationGenerator;
    private readonly IReminderScheduler _reminderScheduler;
    private readonly IAppointmentGoogleSyncDispatcher _googleSyncDispatcher;
    private readonly ILogger<CreateAppointmentCommandHandler> _logger;

    public CreateAppointmentCommandHandler(
        IAppointmentRepository appointmentRepository,
        IPatientRepository patientRepository,
        IDoctorRepository doctorRepository,
        IClinicRepository clinicRepository,
        IProcedureTypeRepository procedureTypeRepository,
        ITreatmentPlanRepository treatmentPlanRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        INotificationGenerator notificationGenerator,
        IReminderScheduler reminderScheduler,
        IAppointmentGoogleSyncDispatcher googleSyncDispatcher,
        ILogger<CreateAppointmentCommandHandler> logger)
    {
        _appointmentRepository = appointmentRepository;
        _patientRepository = patientRepository;
        _doctorRepository = doctorRepository;
        _clinicRepository = clinicRepository;
        _procedureTypeRepository = procedureTypeRepository;
        _treatmentPlanRepository = treatmentPlanRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
        _notificationGenerator = notificationGenerator;
        _reminderScheduler = reminderScheduler;
        _googleSyncDispatcher = googleSyncDispatcher;
        _logger = logger;
    }

    public async Task<Result<AppointmentDto>> Handle(CreateAppointmentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Get user ID from token
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<AppointmentDto>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            // Get user from database to get clinic ID
            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<AppointmentDto>.Failure("Utilisateur introuvable.");
            }

            var clinicId = user.ClinicId;

            // If patient is provided, verify it exists
            Patient? patient = null;
            if (request.PatientId.HasValue)
            {
                patient = await _patientRepository.GetByIdAsync(request.PatientId.Value, cancellationToken);
                if (patient == null || patient.ClinicId != clinicId)
                {
                    return Result<AppointmentDto>.Failure("Patient introuvable.");
                }
            }

            // Validate the practitioner (if one was chosen) belongs to this clinic — the DoctorId FK plus
            // an explicit tenant guard (no cross-clinic doctor assignment).
            if (request.DoctorId.HasValue)
            {
                var doctor = await _doctorRepository.GetByIdAsync(request.DoctorId.Value, cancellationToken);
                if (doctor == null || doctor.ClinicId != clinicId)
                {
                    return Result<AppointmentDto>.Failure("Praticien introuvable.");
                }
            }

            // Get procedure type if specified
            Guid? procedureTypeId = request.ProcedureTypeId;
            int? procedureDurationMinutes = null;
            string? procedureColorHex = null;
            string? procedureTypeName = null;

            if (procedureTypeId.HasValue)
            {
                var procedureType = await _procedureTypeRepository.GetByIdAsync(procedureTypeId.Value, cancellationToken);
                if (procedureType == null || procedureType.ClinicId != clinicId)
                {
                    return Result<AppointmentDto>.Failure("Type de procédure introuvable.");
                }
                if (!procedureType.IsActive)
                {
                    return Result<AppointmentDto>.Failure("Le type de procédure sélectionné n'est pas actif.");
                }
                procedureDurationMinutes = procedureType.DefaultDurationMinutes;
                procedureColorHex = procedureType.Color.Value;
                procedureTypeName = procedureType.Name;
                // Use procedure duration if not specified
                if (request.DurationMinutes == 0)
                {
                    request.DurationMinutes = procedureType.DefaultDurationMinutes;
                }
            }

            // Validate the treatment-plan step link, if one was chosen (must belong to this clinic + patient).
            if (request.TreatmentPlanItemId.HasValue)
            {
                var linkResult = await AppointmentPlanLink.ValidateAsync(
                    _treatmentPlanRepository, request.TreatmentPlanId, request.TreatmentPlanItemId.Value,
                    clinicId, request.PatientId, cancellationToken);
                if (linkResult.IsFailure)
                {
                    return Result<AppointmentDto>.Failure(linkResult.Error!);
                }
            }

            var duration = TimeSpan.FromMinutes(request.DurationMinutes);

            // Double-booking guard. Now one shared helper (AC-P1.39) rather than the third of three drifted
            // copies, and its scan window no longer misses an appointment that started >24 h earlier (A-3).
            // The database's exclusion constraint is the real guarantee (AC-P1.15) — this exists to produce a
            // readable French refusal naming the clash instead of a raw 23P01.
            var collision = await AppointmentScheduling.FindCollisionAsync(
                _appointmentRepository, clinicId, request.DoctorId,
                request.AppointmentDateTime, duration, excludeAppointmentId: null, cancellationToken);
            if (collision != null)
            {
                return Result<AppointmentDto>.Failure(AppointmentScheduling.SlotTakenMessage(collision));
            }

            // Working hours (AC-P1.28). Unrestricted when nothing is configured, so a clinic that never opened
            // the settings screen is unaffected (R-12). A refusal can be overridden by ANY role that can book
            // (AC-P1.31) — the alternative is the guard being worked around by falsifying the time — and the
            // override is recorded on the appointment rather than silently allowed.
            if (!request.AllowOutsideWorkingHours)
            {
                var hoursCheck = await AppointmentScheduling.CheckWorkingHoursAsync(
                    _doctorRepository, _clinicRepository, clinicId, request.DoctorId,
                    request.AppointmentDateTime, duration, cancellationToken);
                if (hoursCheck.IsFailure)
                {
                    return Result<AppointmentDto>.Failure(hoursCheck.Error!);
                }
            }

            var appointment = new Appointment(
                Guid.NewGuid(),
                clinicId,
                request.PatientId,
                request.DoctorId,
                request.AppointmentDateTime,
                duration,
                request.DoctorName,
                request.Notes,
                null, // recurringAppointmentId
                procedureTypeId,
                procedureDurationMinutes,
                procedureColorHex,
                request.TreatmentPlanItemId);

            if (request.AllowOutsideWorkingHours)
            {
                appointment.MarkBookedOutsideWorkingHours();
            }

            await _appointmentRepository.AddAsync(appointment, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Real-time "appointments changed" is broadcast centrally by RealtimeBroadcastBehavior after
            // this command returns success (i.e. after the commit above) — no per-handler broadcast here.

            // In-app staff notifications (best-effort, never fails this command). Only for real patient
            // appointments — patient-less "busy slot" appointments generate nothing (spec US-2). The creator
            // is actor-excluded from the "created" notification; the ~24h reminder is visible to all staff.
            if (patient != null)
            {
                var patientName = patient.GetFullName();
                await _notificationGenerator.AppointmentCreatedAsync(
                    clinicId, appointment.Id, userId, patientName, appointment.AppointmentDateTime, cancellationToken);
                await _notificationGenerator.ScheduleAppointmentReminderAsync(
                    clinicId, appointment.Id, patientName, appointment.AppointmentDateTime, cancellationToken);
                // Post-visit review: becomes visible at the appointment end (start + duration), targeted at
                // the linked doctor if any (else all staff). Deferred visibility replaces a background job.
                await _notificationGenerator.EnsurePostVisitReviewAsync(
                    clinicId, appointment.Id, appointment.DoctorId, patientName,
                    appointment.AppointmentDateTime + appointment.Duration, cancellationToken);

                // Outbound SMS/WhatsApp reminder(s): enqueued to the Notification outbox per configured
                // channel, sent later by the connectivity-gated dispatcher. Best-effort, never fails create.
                await _reminderScheduler.ScheduleForAppointmentAsync(
                    clinicId, appointment.Id, patient.Id, patientName, appointment.AppointmentDateTime, cancellationToken);
            }

            var dto = new AppointmentDto
            {
                Id = appointment.Id,
                ClinicId = appointment.ClinicId,
                PatientId = appointment.PatientId,
                PatientName = patient?.GetFullName() ?? "Occupé",
                DoctorId = appointment.DoctorId,
                DoctorName = appointment.DoctorName,
                AppointmentDateTime = appointment.AppointmentDateTime,
                Duration = appointment.Duration,
                Notes = appointment.Notes,
                Status = appointment.Status.ToString(),
                AllowedNextStatuses = Appointment.NextStatusesFrom(appointment.Status).Select(s => s.ToString()).ToList(),
                ProcedureTypeId = appointment.ProcedureTypeId,
                ProcedureTypeName = procedureTypeName,
                ProcedureColorHex = appointment.ProcedureColorHex,
                TreatmentPlanItemId = appointment.TreatmentPlanItemId,
                CreatedAt = appointment.CreatedAt,
                Version = appointment.Version,
                IsSyncedToGoogle = appointment.GoogleCalendarEventId != null
            };

            // Push to Google Calendar post-commit (fire-and-forget, connectivity-gated). Best-effort — a
            // Google failure never affects the created appointment; patient-less slots are skipped by the service.
            _googleSyncDispatcher.Dispatch(appointment.Id);

            return Result<AppointmentDto>.Success(dto);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            // AC-13.2: the detail goes to the log; the caller only ever sees French guidance.
            _logger.LogError(ex, "Unhandled failure creating appointment");
            return Result<AppointmentDto>.Failure("Erreur lors de la création du rendez-vous. Veuillez réessayer.");
        }
    }

    private static bool Overlaps(DateTime aStart, TimeSpan aDuration, DateTime bStart, TimeSpan bDuration) =>
        aStart < bStart + bDuration && bStart < aStart + aDuration;

    private static DateTime NormalizeUtc(DateTime dateTime) => dateTime.Kind switch
    {
        DateTimeKind.Utc => dateTime,
        DateTimeKind.Local => dateTime.ToUniversalTime(),
        _ => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
    };
}
