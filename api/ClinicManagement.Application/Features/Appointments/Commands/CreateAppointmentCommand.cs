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
    /// role that can book — a secretary handling an emergency Sunday call must have a path, or the guard simply
    /// gets worked around by falsifying the time.
    /// <para>⚠️ Nothing is persisted about the override: the flag that used to record it had no reader anywhere
    /// and was deleted (AC-25). This field is the permission, not a record of one.</para>
    /// </summary>
    public bool AllowOutsideWorkingHours { get; set; }

    /// <summary>
    /// Confirmed override for a booking that overlaps another for the same practitioner. The sibling of
    /// <see cref="AllowOutsideWorkingHours"/>, and deliberately the same shape: a double-booking is sometimes real
    /// work (a second chair, an emergency squeezed in), so the collision is advisory and the acceptance is
    /// <b>recorded</b> on the appointment via <c>MarkBookedWithOverlap()</c> rather than silently allowed.
    /// </summary>
    public bool AllowOverlap { get; set; }
    public string? DoctorName { get; set; }
    public string? Notes { get; set; }
    /// <summary>Single-act shorthand. Ignored when <see cref="Procedures"/> is non-empty.</summary>
    public Guid? ProcedureTypeId { get; set; }

    /// <summary>
    /// The acts of this séance. A visit is routinely several (« détartrage + deux obturations »), and each entry
    /// may carry its own devis step — which is how « ces deux actes ensemble, ces deux-là séparément » is
    /// expressed: one grouped booking with two entries, then two bookings with one each.
    /// <para>
    /// Additive: omit it and <see cref="ProcedureTypeId"/> still books a one-act visit exactly as before, which is
    /// what the AI dispatcher and the recurring-series expansion continue to send.
    /// </para>
    /// </summary>
    public List<AppointmentProcedureRequest>? Procedures { get; set; }

    /// <summary>Optional treatment plan the linked step belongs to (required when <see cref="TreatmentPlanItemId"/> is set).</summary>
    public Guid? TreatmentPlanId { get; set; }
    /// <summary>
    /// Optional treatment-plan step this appointment schedules. Single-act shorthand — with a grouped séance each
    /// act carries its own link on <see cref="Procedures"/>.
    /// </summary>
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

            // Resolve the séance's acts — one shared path for the multi-act list and the single-act shorthand, so
            // every act (not just the first) is tenant-checked and required to be active.
            var requestedProcedures = AppointmentProcedureSelection.Reconcile(
                request.Procedures, request.ProcedureTypeId, request.TreatmentPlanItemId);

            // Validate every devis step this séance carries out — one plan load for the whole set, and the acts
            // must come from the same plan (the appointment records a single TreatmentPlanId). Done *before* the
            // acts are resolved because it is also what supplies a link-only act its désignation.
            var linkResult = await AppointmentPlanLink.ValidateManyAsync(
                _treatmentPlanRepository, request.TreatmentPlanId,
                AppointmentProcedureSelection.PlanLinks(requestedProcedures),
                clinicId, request.PatientId, cancellationToken);
            if (linkResult.IsFailure)
            {
                return Result<AppointmentDto>.Failure(linkResult.Error!);
            }

            var proceduresResult = await AppointmentProcedureSelection.ResolveAsync(
                _procedureTypeRepository, clinicId, requestedProcedures, linkResult.Value!, cancellationToken);
            if (proceduresResult.IsFailure)
            {
                return Result<AppointmentDto>.Failure(proceduresResult.Error!);
            }
            var procedureInputs = proceduresResult.Value!;

            // Duration defaults to the SUM of the booked acts, not the first one's: a séance of three acts that
            // lasts as long as one of them would be double-booked against reality on every calendar it appears on.
            if (request.DurationMinutes == 0 && procedureInputs.Count > 0)
            {
                var summed = procedureInputs.Sum(p => p.DurationMinutes ?? 0);
                if (summed > 0)
                {
                    request.DurationMinutes = summed;
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
            // Advisory, not a prohibition (see AppointmentScheduling.SlotTakenCode): the refusal carries a code so
            // the dialog can offer « Continuer quand même », and the retry records the acceptance below.
            if (collision != null && !request.AllowOverlap)
            {
                return Result<AppointmentDto>.Failure(
                    AppointmentScheduling.SlotTakenMessage(collision), AppointmentScheduling.SlotTakenCode);
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
                    // FailureFrom, not Failure(Error): the working-hours refusal carries OutsideWorkingHoursCode,
                // and that code is the whole point — it is what lets the dialog offer « Continuer quand
                // même » instead of presenting a dead end.
                return Result<AppointmentDto>.FailureFrom(hoursCheck);
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
                null, // procedureTypeId — derived from the acts below, never set twice
                null,
                null,
                null);

            // The acts are the authority; SetProcedures re-derives the lead-act snapshot (id / duration / colour)
            // and the first devis link from them. Assigning those in the constructor as well is how the two would
            // drift, so the constructor is handed nulls on purpose.
            appointment.SetProcedures(procedureInputs);

            // Nothing is recorded for an out-of-hours booking: `AllowOutsideWorkingHours` above is what permits it,
            // and the flag that used to be stamped here was read by nothing at all (AC-25).

            // Only when a collision was actually found: the flag exempts the row from the database's exclusion
            // constraint, so setting it on every booking that merely PASSED the flag would quietly disable the
            // double-booking protection for the whole clinic.
            if (collision != null && request.AllowOverlap)
            {
                appointment.MarkBookedWithOverlap();
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
                ProcedureTypeName = appointment.LeadProcedureName(),
                ProcedureColorHex = appointment.ProcedureColorHex,
                Procedures = appointment.ToProcedureDtos(),
                TreatmentPlanItemId = appointment.TreatmentPlanItemId,
                CreatedAt = appointment.CreatedAt,
                Version = appointment.Version,
                IsSyncedToGoogle = appointment.GoogleCalendarEventId != null
            };

            // Push to Google Calendar post-commit (fire-and-forget, connectivity-gated). Best-effort — a
            // Google failure never affects the created appointment; patient-less slots are skipped by the service.
            _googleSyncDispatcher.Dispatch(appointment.Id, clinicId);

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
