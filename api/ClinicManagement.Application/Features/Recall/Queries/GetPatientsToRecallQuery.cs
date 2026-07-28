using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
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
    private readonly IClinicRepository _clinicRepository;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetPatientsToRecallQueryHandler(
        IPatientRepository patientRepository,
        IClinicRepository clinicRepository,
        ICurrentClinicResolver clinicResolver)
    {
        _patientRepository = patientRepository;
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

            // AC-P4.41 — bounded, and pushed to SQL. This used to load EVERY patient and EVERY appointment in
            // the clinic and do all of it in memory. The repository now applies clinic scope, the archived
            // exclusion (AC-P4.43), the active snooze, the future-booking exclusion and the last-completed-visit
            // lookup in the database, and returns only patients whose recall anchor is old enough to be worth
            // testing.
            //
            // The anchor bound is a deliberate SUPERSET, and the exact rule is applied below (AC-P4.42): the due
            // date is `anchor.AddMonths(interval)`, and AddMonths clamps to the end of a shorter month in a way
            // that does not survive being inverted into `anchor <= now.AddMonths(-interval)`. 31 January + 1
            // month is 28 February, so on 28 February that patient is due — while 28 February − 1 month is
            // 28 January, which would drop them. Three days is the largest clamp (31 → 28), so widening the
            // bound by three days cannot lose a row, and cannot gain one either: the test below is unchanged.
            var anchorOnOrBefore = now.AddMonths(-intervalMonths).AddDays(3);

            var candidates = await _patientRepository.GetRecallCandidatesAsync(
                clinicId, anchorOnOrBefore, now, cancellationToken);

            var recalls = new List<RecallDto>();
            foreach (var candidate in candidates)
            {
                // AC-P1.11 — the stated effect of the Completed → Cancelled transition. `LastCompletedVisitUtc`
                // is derived from Completed appointments only, so cancelling one removes it from this
                // calculation and the patient's recall due-date falls back to their previous completed visit
                // (or, with none, to their creation date). They may therefore REAPPEAR on the relance list.
                //
                // That is correct rather than unfortunate: a visit that was cancelled did not happen, so it is
                // not a contact that should postpone a recall. The alternative — keeping a cancelled visit as
                // the last one — would silently suppress a relance for a patient nobody actually saw, which is
                // precisely the class of invisible state this part exists to remove.
                var dueDate = candidate.RecallAnchorUtc.AddMonths(intervalMonths);
                if (dueDate > now)
                    continue; // inside the widened bound but not actually due — the three-day margin at work

                recalls.Add(new RecallDto
                {
                    PatientId = candidate.PatientId,
                    PatientName = $"{candidate.FirstName} {candidate.LastName}",
                    PhoneNumber = candidate.PhoneNumber,
                    LastVisitDate = candidate.LastCompletedVisitUtc,
                    DueDate = dueDate,
                    DaysOverdue = Math.Max(0, (now.Date - dueDate.Date).Days),
                    Reason = candidate.RecallReason,
                    LastContactedAt = candidate.LastRecallContactedAt
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
