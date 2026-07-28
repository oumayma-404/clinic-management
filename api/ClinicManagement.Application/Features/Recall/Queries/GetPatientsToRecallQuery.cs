using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Recall.Queries;

/// <summary>
/// The "patients à relancer" list — clinic patients whose next recall is due or overdue. Due date is derived
/// from each patient's last completed visit (or creation date if never seen) + the clinic recall interval.
/// Excludes patients with a future Scheduled/Confirmed appointment (they don't need a recall) or an active
/// snooze. Most overdue first. Clinic-scoped.
/// </summary>
public class GetPatientsToRecallQuery : IRequest<Result<IEnumerable<RecallDto>>>
{
}

public class GetPatientsToRecallQueryHandler : IRequestHandler<GetPatientsToRecallQuery, Result<IEnumerable<RecallDto>>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetPatientsToRecallQueryHandler(
        IPatientRepository patientRepository,
        IAppointmentRepository appointmentRepository,
        IClinicRepository clinicRepository,
        ICurrentClinicResolver clinicResolver)
    {
        _patientRepository = patientRepository;
        _appointmentRepository = appointmentRepository;
        _clinicRepository = clinicRepository;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<IEnumerable<RecallDto>>> Handle(GetPatientsToRecallQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
                return Result<IEnumerable<RecallDto>>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            var clinicId = clinicResult.Value;

            var clinic = await _clinicRepository.GetByIdAsync(clinicId, cancellationToken);
            var intervalMonths = clinic?.RecallIntervalMonths ?? 6;

            var now = DateTime.UtcNow;
            // Archived patients are excluded: relancing someone the clinic has archived is exactly what
            // archiving is meant to stop.
            var patients = await _patientRepository.GetByClinicIdAsync(
                clinicId, cancellationToken: cancellationToken);
            var appointments = await _appointmentRepository.GetByClinicIdAsync(clinicId, cancellationToken: cancellationToken);

            var apptsByPatient = appointments
                .Where(a => a.PatientId.HasValue)
                .GroupBy(a => a.PatientId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            var recalls = new List<RecallDto>();
            foreach (var patient in patients)
            {
                var appts = apptsByPatient.TryGetValue(patient.Id, out var list)
                    ? list
                    : new List<Domain.Entities.Appointment>();

                // A patient with a future booked appointment does not need a recall.
                var hasFutureBooking = appts.Any(a => a.AppointmentDateTime > now &&
                    (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Confirmed));
                if (hasFutureBooking)
                    continue;

                // An active snooze temporarily removes the patient from the list.
                if (patient.RecallSnoozedUntil.HasValue && patient.RecallSnoozedUntil.Value > now)
                    continue;

                // AC-P1.11 — the stated effect of the new Completed → Cancelled transition. `lastVisit` is
                // derived from Completed appointments only, so cancelling one removes it from this calculation
                // and the patient's recall due-date falls back to their previous completed visit (or, with
                // none, to their creation date). They may therefore REAPPEAR on the relance list.
                //
                // That is correct rather than unfortunate: a visit that was cancelled did not happen, so it is
                // not a contact that should postpone a recall. The alternative — keeping a cancelled visit as
                // the last one — would silently suppress a relance for a patient nobody actually saw, which is
                // precisely the class of invisible state this part exists to remove.
                var completedDates = appts
                    .Where(a => a.Status == AppointmentStatus.Completed)
                    .Select(a => a.AppointmentDateTime)
                    .ToList();
                DateTime? lastVisit = completedDates.Count > 0 ? completedDates.Max() : null;

                var dueDate = (lastVisit ?? patient.CreatedAt).AddMonths(intervalMonths);
                if (dueDate > now)
                    continue;

                recalls.Add(new RecallDto
                {
                    PatientId = patient.Id,
                    PatientName = patient.GetFullName(),
                    PhoneNumber = patient.PhoneNumber?.Value,
                    LastVisitDate = lastVisit,
                    DueDate = dueDate,
                    DaysOverdue = Math.Max(0, (now.Date - dueDate.Date).Days),
                    Reason = patient.RecallReason,
                    LastContactedAt = patient.LastRecallContactedAt
                });
            }

            var sorted = recalls.OrderByDescending(r => r.DaysOverdue).ThenBy(r => r.PatientName).ToList();
            return Result<IEnumerable<RecallDto>>.Success(sorted);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<IEnumerable<RecallDto>>.Failure($"Erreur lors du calcul des patients à relancer : {ex.Message}");
        }
    }
}
