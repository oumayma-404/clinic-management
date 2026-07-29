using MediatR;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Recall.Queries;

/// <summary>
/// The « à rappeler » worklist — every patient worth calling, and why.
///
/// <para>Four reasons are aggregated per patient: an <b>overdue échéance</b>, a <b>stalled devis</b> (accepted, acts
/// left, nothing booked), an <b>unanswered devis</b>, and the original <b>overdue visit</b>. The last one used to be
/// the whole feature, which for a perio/implant practice is the least informative of the four — a patient seen last
/// week who stopped halfway through an accepted plan is both lost revenue and an unfinished surgical case, and a
/// time-since-last-visit rule can never surface them.</para>
///
/// <para>Excludes, for every reason alike, patients who are archived, snoozed, or have a future booking. A patient who
/// is coming in on Tuesday is not chased — whatever the reason, staff handle it in the chair.</para>
///
/// <para>Four reads total, all clinic-scoped projections: the eligible population, plan facts, installment
/// outstanding (which already computes the oldest overdue due date for « Créances »), and the bridge links needed to
/// de-duplicate a devis already billed to an invoice.</para>
/// </summary>
public class GetPatientsToRecallQuery : IRequest<Result<IEnumerable<RecallDto>>>
{
}

public class GetPatientsToRecallQueryHandler : IRequestHandler<GetPatientsToRecallQuery, Result<IEnumerable<RecallDto>>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetPatientsToRecallQueryHandler(
        IPatientRepository patientRepository,
        IClinicRepository clinicRepository,
        ITreatmentPlanRepository planRepository,
        IInvoiceRepository invoiceRepository,
        ICurrentClinicResolver clinicResolver)
    {
        _patientRepository = patientRepository;
        _clinicRepository = clinicRepository;
        _planRepository = planRepository;
        _invoiceRepository = invoiceRepository;
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
            var intervalMonths = clinic?.RecallIntervalMonths ?? RecallDueRule.DefaultIntervalMonths;

            var now = DateTime.UtcNow;

            // The same de-duplication every money read applies: a devis already represented by a non-cancelled
            // invoice is counted through that invoice, so its échéancier must not also be chased here.
            var billedPlanIds = PlanBillingRules.BilledPlanIds(
                await _invoiceRepository.GetTreatmentPlanLinksAsync(clinicId, cancellationToken));

            var planFacts = (await _planRepository.GetRecallPlanFactsAsync(clinicId, cancellationToken))
                .Where(p => !billedPlanIds.Contains(p.PlanId))
                .ToList();
            var plansByPatient = planFacts
                .GroupBy(p => p.PatientId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var installments = await _planRepository.GetInstallmentOutstandingByPatientAsync(
                clinicId, now, billedPlanIds, cancellationToken);
            var installmentByPatient = installments.ToDictionary(r => r.PatientId, r => r);

            // Patients a non-visit reason already qualifies. They must come back from the population read even though
            // their last visit may be recent — a stalled devis has nothing to do with when they were last seen.
            // Passed as an explicit id set rather than by dropping the date bound, which would re-open the § 9.6
            // full-scan (AC-P4.41). Both source sets are naturally small: plans in flight, and patients with debt.
            var alwaysInclude = planFacts
                .Where(p => RecallWorklistRules.IsStalled(p, now) || RecallWorklistRules.IsUnanswered(p, now))
                .Select(p => p.PatientId)
                .Concat(installments.Where(r => r.OldestOverdueDueDate != null).Select(r => r.PatientId))
                .Distinct()
                .ToList();

            // The archived / snoozed / future-booking exclusions still apply to those ids — being owed money does not
            // override an archive, and a patient coming in on Tuesday is handled in the chair.
            var anchorOnOrBefore = RecallDueRule.AnchorUpperBound(now, intervalMonths);
            var candidates = await _patientRepository.GetRecallCandidatesAsync(
                clinicId, anchorOnOrBefore, now, alwaysInclude, cancellationToken);

            var recalls = new List<RecallDto>();
            foreach (var candidate in candidates)
            {
                plansByPatient.TryGetValue(candidate.PatientId, out var plans);
                installmentByPatient.TryGetValue(candidate.PatientId, out var money);

                var reasons = RecallWorklistRules.ReasonsFor(
                    candidate.RecallAnchorUtc,
                    intervalMonths,
                    plans ?? new List<RecallPlanFact>(),
                    money.OldestOverdueDueDate,
                    money.Outstanding,
                    now);

                if (reasons.Count == 0)
                    continue;

                var headline = reasons[0];
                recalls.Add(new RecallDto
                {
                    PatientId = candidate.PatientId,
                    PatientName = $"{candidate.FirstName} {candidate.LastName}",
                    PhoneNumber = candidate.PhoneNumber,
                    LastVisitDate = candidate.LastCompletedVisitUtc,
                    DueDate = headline.DueSince,
                    DaysOverdue = DaysOverdue(headline.DueSince, now),
                    PrimaryReason = headline.Kind.ToString(),
                    Reasons = reasons
                        .Select(r => new RecallReasonDto
                        {
                            Kind = r.Kind.ToString(),
                            DueSince = r.DueSince,
                            DaysOverdue = DaysOverdue(r.DueSince, now),
                            Detail = r.Detail
                        })
                        .ToList(),
                    Note = candidate.RecallReason,
                    LastContactedAt = candidate.LastRecallContactedAt
                });
            }

            // Most urgent kind first, then longest-waiting — so money and stalled surgical cases lead the list
            // rather than a routine six-month check-up.
            var sorted = recalls
                .OrderBy(r => ReasonRank(r.PrimaryReason))
                .ThenByDescending(r => r.DaysOverdue)
                .ThenBy(r => r.PatientName)
                .ToList();

            return Result<IEnumerable<RecallDto>>.Success(sorted);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<IEnumerable<RecallDto>>.Failure($"Erreur lors du calcul des patients à rappeler : {ex.Message}");
        }
    }

    /// <summary>
    /// How many days an échéance has been late, counted in <b>calendar days of the clinic's own day</b>
    /// (AC-P6.4). `nowUtc.Date` was the UTC day, so between 00:00 and 01:00 Tunis every « en retard depuis N
    /// jours » on this list was a day short — and this read takes no date arguments, so no caller could correct
    /// it.
    /// </summary>
    private static int DaysOverdue(DateTime dueSince, DateTime nowUtc) =>
        Math.Max(0, (ClinicClock.ClinicToday(nowUtc) - dueSince.Date).Days);

    private static int ReasonRank(string kind) =>
        Enum.TryParse<RecallReasonKind>(kind, out var parsed) ? (int)parsed : int.MaxValue;
}
