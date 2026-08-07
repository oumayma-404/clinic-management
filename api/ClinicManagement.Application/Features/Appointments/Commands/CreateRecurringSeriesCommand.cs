using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments.Commands;

/// <summary>
/// Creates a recurring appointment series and expands it into individual <see cref="Appointment"/> rows
/// linked via <c>RecurringAppointmentId</c> (AC-2.1). Occurrences in the past are skipped; the series is
/// capped at <see cref="MaxOccurrences"/>; occurrences that collide with an existing appointment for the same
/// practitioner are surfaced (returned as conflicts), not silently created (AC-2.3).
/// </summary>
public class CreateRecurringSeriesCommand : IRequest<Result<RecurringSeriesResultDto>>
{
    /// <summary>Confirmed override so out-of-hours occurrences are created instead of skipped (AC-P1.31).</summary>
    public bool AllowOutsideWorkingHours { get; set; }

    /// <summary>
    /// Confirmed override so a colliding occurrence is created instead of skipped — the same shape as
    /// <see cref="AllowOutsideWorkingHours"/>, and for the same reason: a double-booking is advisory (a second
    /// chair, an assistant preparing one patient while the dentist starts another), not a prohibition.
    ///
    /// <para>
    /// ⚠️ **Wired by L2b; the client had been sending it into a void.** `CreateRecurringSeriesPayload.allowOverlap`
    /// has existed on the frontend with no counterpart here, so the flag was silently dropped. That went unnoticed
    /// because the collision check it overrides was itself dead code (see the `existing` load below) — a flag with
    /// no effect over a check that never fired. The spec's instruction was "either wire it or delete it"; wiring is
    /// the right half, because a series is now the writer whose occurrences collide *most* often and the recurring
    /// path would otherwise be the only one of the three with no override at all.
    /// </para>
    /// </summary>
    public bool AllowOverlap { get; set; }

    public Guid PatientId { get; set; }
    public DateTime StartDateTime { get; set; }
    public int DurationMinutes { get; set; }
    public string Frequency { get; set; } = string.Empty; // Daily / Weekly / Monthly
    public int Interval { get; set; } = 1;
    public DateTime? EndDate { get; set; }
    public int? OccurrenceCount { get; set; }
    public Guid? DoctorId { get; set; }
    public string? DoctorName { get; set; }
    public Guid? ProcedureTypeId { get; set; }
    public string? Notes { get; set; }
}

public class CreateRecurringSeriesCommandHandler : IRequestHandler<CreateRecurringSeriesCommand, Result<RecurringSeriesResultDto>>
{
    private const int MaxOccurrences = 60;
    private const int DefaultOccurrences = 12;

    private readonly IRecurringAppointmentRepository _recurringRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationGenerator _notificationGenerator;
    private readonly IReminderScheduler _reminderScheduler;
    private readonly IAppointmentGoogleSyncDispatcher _googleSyncDispatcher;

    private readonly ILogger<CreateRecurringSeriesCommandHandler> _logger;

    public CreateRecurringSeriesCommandHandler(
        IRecurringAppointmentRepository recurringRepository,
        IAppointmentRepository appointmentRepository,
        IPatientRepository patientRepository,
        IDoctorRepository doctorRepository,
        IClinicRepository clinicRepository,
        IProcedureTypeRepository procedureTypeRepository,
        ICurrentClinicResolver clinicResolver,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        INotificationGenerator notificationGenerator,
        IReminderScheduler reminderScheduler,
        IAppointmentGoogleSyncDispatcher googleSyncDispatcher,
        ILogger<CreateRecurringSeriesCommandHandler> logger)
    {
        _recurringRepository = recurringRepository;
        _appointmentRepository = appointmentRepository;
        _patientRepository = patientRepository;
        _doctorRepository = doctorRepository;
        _clinicRepository = clinicRepository;
        _procedureTypeRepository = procedureTypeRepository;
        _clinicResolver = clinicResolver;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
        _notificationGenerator = notificationGenerator;
        _reminderScheduler = reminderScheduler;
        _googleSyncDispatcher = googleSyncDispatcher;
        _logger = logger;
    }

    public async Task<Result<RecurringSeriesResultDto>> Handle(CreateRecurringSeriesCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.PatientId == Guid.Empty)
            {
                return Result<RecurringSeriesResultDto>.Failure("Le patient est requis pour une série récurrente.");
            }
            if (!Enum.TryParse<RecurrenceFrequency>(request.Frequency, ignoreCase: true, out var frequency))
            {
                return Result<RecurringSeriesResultDto>.Failure("Fréquence de récurrence invalide (Daily/Weekly/Monthly).");
            }

            var interval = request.Interval < 1 ? 1 : request.Interval;

            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
                return Result<RecurringSeriesResultDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            var clinicId = clinicResult.Value;

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicId)
                return Result<RecurringSeriesResultDto>.Failure("Patient introuvable.");

            if (request.DoctorId.HasValue)
            {
                var doctor = await _doctorRepository.GetByIdAsync(request.DoctorId.Value, cancellationToken);
                if (doctor == null || doctor.ClinicId != clinicId)
                    return Result<RecurringSeriesResultDto>.Failure("Praticien introuvable.");
            }

            // The series' act, resolved once and applied to every occurrence through `SetProcedures` below.
            //
            // A series carries **one** act, deliberately: it repeats the same appointment, and « ces trois actes
            // ensemble » is a decision about one visit, not a pattern. What matters here is that each occurrence
            // gets a real `AppointmentProcedures` row rather than only the derived scalars — the agenda badge, the
            // fiche de soins proposal and the edit dialog all read the list now, and an occurrence with scalars
            // but no row would read as a visit with no act.
            var seriesProcedures = new List<AppointmentProcedureInput>();
            var durationMinutes = request.DurationMinutes;
            if (request.ProcedureTypeId.HasValue)
            {
                var procedureType = await _procedureTypeRepository.GetByIdAsync(request.ProcedureTypeId.Value, cancellationToken);
                if (procedureType == null || procedureType.ClinicId != clinicId)
                    return Result<RecurringSeriesResultDto>.Failure("Type d'acte introuvable.");
                if (!procedureType.IsActive)
                    return Result<RecurringSeriesResultDto>.Failure("Le type d'acte sélectionné est inactif.");
                seriesProcedures.Add(new AppointmentProcedureInput(
                    procedureType.Id,
                    procedureType.Name,
                    procedureType.DefaultDurationMinutes,
                    procedureType.Color.Value,
                    null));
                if (durationMinutes <= 0)
                    durationMinutes = procedureType.DefaultDurationMinutes;
            }

            if (durationMinutes <= 0)
                return Result<RecurringSeriesResultDto>.Failure("La durée doit être supérieure à 0.");

            var duration = TimeSpan.FromMinutes(durationMinutes);
            var startUtc = NormalizeUtc(request.StartDateTime);
            var endUtc = request.EndDate.HasValue ? NormalizeUtc(request.EndDate.Value) : (DateTime?)null;
            // Require an end condition; default to a fixed count when neither an end date nor a count is given.
            var count = request.OccurrenceCount;
            if (endUtc == null && count == null)
                count = DefaultOccurrences;
            if (count.HasValue && count.Value < 1)
                return Result<RecurringSeriesResultDto>.Failure("Le nombre d'occurrences doit être au moins 1.");

            var series = new RecurringAppointment(
                Guid.NewGuid(), clinicId, patient.Id, startUtc, duration, frequency.ToString(),
                interval, endUtc, count, request.DoctorId, request.DoctorName, request.ProcedureTypeId, request.Notes);
            await _recurringRepository.AddAsync(series, cancellationToken);

            // Compute the occurrence dates (bounded by the cap and the end condition).
            var occurrences = new List<DateTime>();
            var cursor = startUtc;
            var generated = 0;
            while (occurrences.Count < MaxOccurrences)
            {
                if (endUtc.HasValue && cursor > endUtc.Value) break;
                if (count.HasValue && generated >= count.Value) break;
                occurrences.Add(cursor);
                generated++;
                cursor = frequency switch
                {
                    RecurrenceFrequency.Daily => cursor.AddDays(interval),
                    RecurrenceFrequency.Weekly => cursor.AddDays(7 * interval),
                    RecurrenceFrequency.Monthly => cursor.AddMonths(interval),
                    _ => cursor.AddDays(7 * interval)
                };
            }

            /*
             * Load the existing appointments this series could clash with (AC-2.3).
             *
             * ⚠️ **No longer gated on `request.DoctorId.HasValue`, and no longer filtered by it (L2b, blocker).**
             * The create form has no practitioner field, so `DoctorId` was *always* null — which meant this list
             * was never loaded, both collision branches below were dead, and the outcome panel's « conflits »
             * section was unreachable code. A twelve-week series booked straight over twelve existing patients and
             * reported a clean run. The database could not catch it either: the exclusion constraint is predicated
             * on `DoctorId IS NOT NULL`.
             *
             * Fetched clinic-wide because `AppointmentScheduling.CompetesFor` — the one authority on what competes
             * with what — must also see the clinic's *unassigned* rows (a « créneau occupé » block) even when a
             * practitioner is named. It is bounded by the series' own window, so this is one query over the weeks
             * the series actually occupies.
             */
            List<Appointment> existing = new();
            if (occurrences.Count > 0)
            {
                var windowStart = occurrences[0] - AppointmentScheduling.MaxCredibleAppointmentLength;
                var windowEnd = occurrences[^1] + duration;
                existing = (await _appointmentRepository.GetByClinicIdAsync(
                    clinicId, windowStart, windowEnd, doctorId: null, cancellationToken: cancellationToken)).ToList();
            }

            var now = DateTime.UtcNow;
            var skippedPast = 0;
            var conflicts = new List<DateTime>();
            var outsideHours = new List<DateTime>();
            var createdAppointments = new List<Appointment>();

            foreach (var occ in occurrences)
            {
                if (occ <= now)
                {
                    skippedPast++;
                    continue;
                }

                // AC-P1.38/1.39: one shared predicate. This copy excluded only `Cancelled`, so a series
                // refused to book over a NoShow slot the single-appointment path considered free.
                // ⚠️ The `request.DoctorId.HasValue &&` that used to open this expression is gone (L2b): it was
                // always false, so the whole clause was dead. `CompetesFor` is what decides now — see its remarks
                // for why an unassigned row competes with everything.
                var collides = existing.Any(e =>
                    AppointmentScheduling.OccupiesSlot(e.Status) &&
                    AppointmentScheduling.CompetesFor(request.DoctorId, e.DoctorId) &&
                    AppointmentScheduling.Overlaps(e.AppointmentDateTime, e.Duration, occ, duration));

                // ...and against the occurrences THIS call has already accepted. `existing` is loaded once
                // before the loop, so a series whose interval is shorter than its own duration used to
                // cheerfully double-book itself — and now that the database enforces the constraint
                // (AC-P1.19), that would abort the whole series instead of skipping one occurrence.
                // Every row here carries this series' own `DoctorId`, so it competes with the candidate by
                // definition; `CompetesFor` is still asked rather than assumed, so the two branches cannot drift.
                if (!collides)
                {
                    collides = createdAppointments.Any(a =>
                        AppointmentScheduling.CompetesFor(request.DoctorId, a.DoctorId) &&
                        AppointmentScheduling.Overlaps(a.AppointmentDateTime, a.Duration, occ, duration));
                }

                // Working hours (AC-P1.28) apply to every occurrence, and a refusal skips that occurrence
                // rather than the series — same skip-and-report contract as a conflict.
                if (!collides && !request.AllowOutsideWorkingHours)
                {
                    var hoursCheck = await AppointmentScheduling.CheckWorkingHoursAsync(
                        _doctorRepository, _clinicRepository, clinicId, request.DoctorId,
                        occ, duration, cancellationToken);
                    if (hoursCheck.IsFailure)
                    {
                        outsideHours.Add(occ);
                        continue;
                    }
                }

                // A collision skips the occurrence and is reported — unless the caller has confirmed the override,
                // in which case it is created *and* the exemption is recorded on the row (below), exactly as the
                // single-appointment path does. Still reported either way: the outcome panel must be able to say
                // « 3 créneaux étaient déjà pris » about a series the user chose to create anyway.
                if (collides)
                {
                    conflicts.Add(occ);
                    if (!request.AllowOverlap)
                    {
                        continue;
                    }
                }

                var appointment = new Appointment(
                    Guid.NewGuid(), clinicId, patient.Id, request.DoctorId, occ, duration,
                    request.DoctorName, request.Notes, series.Id,
                    // The act is applied below; the constructor's snapshot arguments are left null so the list
                    // stays the single authority and the two cannot drift.
                    null, null, null, null);
                appointment.SetProcedures(seriesProcedures);
                if (request.AllowOutsideWorkingHours)
                {
                    appointment.MarkBookedOutsideWorkingHours();
                }
                if (collides)
                {
                    appointment.MarkBookedWithOverlap();
                }

                await _appointmentRepository.AddAsync(appointment, cancellationToken);
                createdAppointments.Add(appointment);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Post-commit side effects — parity with the single-appointment path (CreateAppointmentCommand):
            // each occurrence gets an in-app "created" notification, a ~24h SMS/WhatsApp reminder, a
            // post-visit review, and a Google Calendar push. Best-effort — the generators/scheduler swallow
            // their own failures, and a Google push is fire-and-forget; none rolls back the committed series.
            var actorUserId = _clinicContext.GetUserId();
            var patientName = patient.GetFullName();
            foreach (var appointment in createdAppointments)
            {
                await _notificationGenerator.AppointmentCreatedAsync(
                    clinicId, appointment.Id, actorUserId, patientName, appointment.AppointmentDateTime, cancellationToken);
                await _notificationGenerator.ScheduleAppointmentReminderAsync(
                    clinicId, appointment.Id, patientName, appointment.AppointmentDateTime, cancellationToken);
                await _notificationGenerator.EnsurePostVisitReviewAsync(
                    clinicId, appointment.Id, appointment.DoctorId, patientName,
                    appointment.AppointmentDateTime + appointment.Duration, cancellationToken);
                await _reminderScheduler.ScheduleForAppointmentAsync(
                    clinicId, appointment.Id, patient.Id, patientName, appointment.AppointmentDateTime, cancellationToken);
                _googleSyncDispatcher.Dispatch(appointment.Id, clinicId);
            }

            return Result<RecurringSeriesResultDto>.Success(new RecurringSeriesResultDto
            {
                RecurringAppointmentId = series.Id,
                CreatedCount = createdAppointments.Count,
                SkippedPastCount = skippedPast,
                Conflicts = conflicts,
                OutsideWorkingHours = outsideHours
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            // A-7: the raw exception reached the clinic. Detail goes to the log.
            _logger.LogError(ex, "Unhandled failure creating a recurring series");
            return Result<RecurringSeriesResultDto>.Failure("Erreur lors de la création de la série récurrente. Veuillez réessayer.");
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
