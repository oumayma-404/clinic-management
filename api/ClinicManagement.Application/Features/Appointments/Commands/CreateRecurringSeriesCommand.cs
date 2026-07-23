using MediatR;
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
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public CreateRecurringSeriesCommandHandler(
        IRecurringAppointmentRepository recurringRepository,
        IAppointmentRepository appointmentRepository,
        IPatientRepository patientRepository,
        IDoctorRepository doctorRepository,
        IProcedureTypeRepository procedureTypeRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _recurringRepository = recurringRepository;
        _appointmentRepository = appointmentRepository;
        _patientRepository = patientRepository;
        _doctorRepository = doctorRepository;
        _procedureTypeRepository = procedureTypeRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
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

            int? procedureDurationMinutes = null;
            string? procedureColorHex = null;
            var durationMinutes = request.DurationMinutes;
            if (request.ProcedureTypeId.HasValue)
            {
                var procedureType = await _procedureTypeRepository.GetByIdAsync(request.ProcedureTypeId.Value, cancellationToken);
                if (procedureType == null || procedureType.ClinicId != clinicId)
                    return Result<RecurringSeriesResultDto>.Failure("Type d'acte introuvable.");
                if (!procedureType.IsActive)
                    return Result<RecurringSeriesResultDto>.Failure("Le type d'acte sélectionné est inactif.");
                procedureDurationMinutes = procedureType.DefaultDurationMinutes;
                procedureColorHex = procedureType.Color.Value;
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

            // Load existing appointments for the practitioner in the window for conflict detection (AC-2.3).
            List<Appointment> existing = new();
            if (request.DoctorId.HasValue && occurrences.Count > 0)
            {
                var windowEnd = occurrences[^1] + duration;
                existing = (await _appointmentRepository.GetByClinicIdAsync(
                    clinicId, occurrences[0], windowEnd, request.DoctorId, cancellationToken)).ToList();
            }

            var now = DateTime.UtcNow;
            var skippedPast = 0;
            var conflicts = new List<DateTime>();
            var created = 0;

            foreach (var occ in occurrences)
            {
                if (occ <= now)
                {
                    skippedPast++;
                    continue;
                }

                var collides = request.DoctorId.HasValue && existing.Any(e =>
                    e.Status != AppointmentStatus.Cancelled &&
                    Overlaps(e.AppointmentDateTime, e.Duration, occ, duration));
                if (collides)
                {
                    conflicts.Add(occ);
                    continue;
                }

                var appointment = new Appointment(
                    Guid.NewGuid(), clinicId, patient.Id, request.DoctorId, occ, duration,
                    request.DoctorName, request.Notes, series.Id,
                    request.ProcedureTypeId, procedureDurationMinutes, procedureColorHex, null);
                await _appointmentRepository.AddAsync(appointment, cancellationToken);
                created++;
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<RecurringSeriesResultDto>.Success(new RecurringSeriesResultDto
            {
                RecurringAppointmentId = series.Id,
                CreatedCount = created,
                SkippedPastCount = skippedPast,
                Conflicts = conflicts
            });
        }
        catch (Exception ex)
        {
            return Result<RecurringSeriesResultDto>.Failure($"Erreur lors de la création de la série récurrente : {ex.Message}");
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
