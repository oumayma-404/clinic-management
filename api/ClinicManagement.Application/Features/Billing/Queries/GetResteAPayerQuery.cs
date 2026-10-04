using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Billing.Queries;

/// <summary>Which of the two « Reste à payer » lists to page.</summary>
public enum ResteAPayerList
{
    /// <summary>« À relancer » — money due now.</summary>
    Due,

    /// <summary>« En cours » — money not due yet.</summary>
    Running,
}

/// <summary>« Plus ancien » (due: oldest debt first · running: next séance or échéance first) or « Montant ».</summary>
public enum ResteAPayerSort
{
    Age,
    Amount,
}

/// <summary>
/// « Reste à payer » (À clôturer): every patient who owes, split into due now / not due yet — through the same
/// <see cref="PatientDebtSelection"/> + <see cref="PatientDebtLines.Project"/> as « Solde patient », so both always add up.
/// </summary>
public class GetResteAPayerQuery : IRequest<Result<ResteAPayerPageDto>>
{
    public ResteAPayerList List { get; set; } = ResteAPayerList.Due;
    public ResteAPayerSort Sort { get; set; } = ResteAPayerSort.Age;

    /// <summary>Patient name, matched in memory — the list is a per-patient fold, like « Créances ».</summary>
    public string? SearchTerm { get; set; }

    public int? Page { get; set; }
    public int? PageSize { get; set; }
}

public class GetResteAPayerQueryHandler : IRequestHandler<GetResteAPayerQuery, Result<ResteAPayerPageDto>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetResteAPayerQueryHandler> _logger;

    public GetResteAPayerQueryHandler(
        IInvoiceRepository invoiceRepository,
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        IAppointmentRepository appointmentRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetResteAPayerQueryHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _appointmentRepository = appointmentRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<ResteAPayerPageDto>> Handle(GetResteAPayerQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<ResteAPayerPageDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var now = DateTime.UtcNow;
            var clinicToday = ClinicClock.ClinicToday(now);

            // Who owes anything — the same per-document test as the projector — then the full documents of those patients only.
            var billedPlanIds = PlanBillingRules.BilledPlanIds(
                await _invoiceRepository.GetTreatmentPlanLinksAsync(clinicId, cancellationToken));
            var candidates = (await _invoiceRepository.GetOutstandingByPatientAsync(clinicId, cancellationToken))
                .Select(r => r.PatientId)
                .Concat(await _planRepository.GetPatientIdsWithPlanOutstandingAsync(
                    clinicId, billedPlanIds, cancellationToken))
                .Distinct()
                .ToList();

            var invoicesByPatient = (await _invoiceRepository.GetByPatientIdsAsync(clinicId, candidates, cancellationToken))
                .ToLookup(i => i.PatientId);
            var plansByPatient = (await _planRepository.GetByPatientIdsAsync(clinicId, candidates, cancellationToken))
                .ToLookup(p => p.PatientId);
            var patients = await _patientRepository.GetByIdsAsync(clinicId, candidates, cancellationToken);
            var nextVisits = await _appointmentRepository.GetNextBookingByPatientAsync(
                clinicId, candidates, now, cancellationToken);

            var rows = new List<ResteAPayerRowDto>();
            foreach (var patientId in candidates)
            {
                if (!patients.TryGetValue(patientId, out var patient))
                {
                    continue;
                }

                var (liveInvoices, debtPlans) = PatientDebtSelection.Select(
                    invoicesByPatient[patientId], plansByPatient[patientId]);
                var row = BuildRow(
                    patientId,
                    patient.GetFullName(),
                    patient.PhoneNumber?.Value,
                    patient.PhoneNumber?.E164,
                    PatientDebtLines.Project(liveInvoices, debtPlans, clinicToday),
                    nextVisits.TryGetValue(patientId, out var next) ? next : null,
                    clinicToday);
                if (row is not null)
                {
                    rows.Add(row);
                }
            }

            var filtered = string.IsNullOrWhiteSpace(request.SearchTerm)
                ? rows
                : rows.Where(r => SearchTerm.Matches(request.SearchTerm, r.PatientName)).ToList();
            var due = filtered.Where(r => r.DueAmount > 0m).ToList();
            var running = filtered.Where(r => r.RunningAmount > 0m).ToList();

            var sorted = Sort(request.List == ResteAPayerList.Due ? due : running, request.List, request.Sort);
            var page = PagedResult<ResteAPayerRowDto>.FromSource(sorted, PageRequest.From(request.Page, request.PageSize));

            return Result<ResteAPayerPageDto>.Success(new ResteAPayerPageDto
            {
                Items = page.Items.ToList(),
                DueTotal = InvoiceCalculator.RoundMoney(due.Sum(r => r.DueAmount)),
                DueCount = due.Count,
                RunningTotal = InvoiceCalculator.RoundMoney(running.Sum(r => r.RunningAmount)),
                RunningCount = running.Count,
                Page = page.Page,
                PageSize = page.PageSize,
                TotalCount = page.TotalCount,
                TotalPages = page.TotalPages,
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error building the reste-à-payer list");
            return Result<ResteAPayerPageDto>.Failure("Erreur lors du calcul des restes à payer.");
        }
    }

    /// <summary>One patient's row, or null when nothing is owed once the documents are projected.</summary>
    private static ResteAPayerRowDto? BuildRow(
        Guid patientId,
        string name,
        string? phone,
        string? phoneE164,
        IReadOnlyList<PatientDebtLineDto> lines,
        DateTime? nextVisit,
        DateTime clinicToday)
    {
        var dueAmount = InvoiceCalculator.RoundMoney(lines.Sum(l => l.DueNow));
        var runningAmount = InvoiceCalculator.RoundMoney(lines.Sum(l => l.Outstanding - l.DueNow));
        if (dueAmount <= 0m && runningAmount <= 0m)
        {
            return null;
        }

        // Oldest debt first, undated last — the order the projector already gives the lines.
        var dueLines = lines.Where(l => l.DueNow > 0m).ToList();
        var leadingDue = dueLines
            .OrderBy(l => l.DueSince.HasValue ? 0 : 1)
            .ThenBy(l => l.DueSince ?? DateTime.MaxValue)
            .ThenBy(l => l.DocumentId)
            .FirstOrDefault();

        var runningLines = lines.Where(l => l.Outstanding - l.DueNow > 0m).ToList();
        var leadingRunning = runningLines
            .OrderByDescending(l => l.Outstanding - l.DueNow)
            .ThenBy(l => l.DocumentId)
            .FirstOrDefault();
        var runningIsPlan = leadingRunning?.Kind == PatientDebtLines.TreatmentPlanKind;

        return new ResteAPayerRowDto
        {
            PatientId = patientId,
            PatientName = name,
            PhoneNumber = phone,
            PhoneE164 = phoneE164,
            DueAmount = dueAmount,
            RunningAmount = runningAmount,
            DueReason = leadingDue?.DueReason,
            DueDocument = leadingDue is null ? null : DocumentLabel(leadingDue),
            DueDocumentCount = dueLines.Count,
            DueSince = leadingDue?.DueSince,
            // Both sides on the clinic's calendar — GetReceivablesQuery's « depuis N jours » rule.
            DueDays = leadingDue?.DueSince is { } since
                ? Math.Max(0, (clinicToday - ClinicClock.ToClinicLocal(since).Date).Days)
                : null,
            RunningReason = leadingRunning?.RunningReason,
            RunningDocument = leadingRunning is null ? null : DocumentLabel(leadingRunning),
            RunningDocumentCount = runningLines.Count,
            RunningActsDone = runningIsPlan ? leadingRunning!.Acts.Count(a => a.Done) : null,
            RunningActsTotal = runningIsPlan ? leadingRunning!.Acts.Count : null,
            NextVisit = nextVisit,
            NextInstallmentDue = runningLines
                .Select(l => l.NextInstallmentDue)
                .Where(d => d.HasValue)
                .Min(),
        };
    }

    /// <summary>« Note n° 2026-0042 · Obturation, Couronne ».</summary>
    private static string DocumentLabel(PatientDebtLineDto line)
    {
        var kind = line.Kind == PatientDebtLines.InvoiceKind ? "Note" : "Devis";
        var head = line.Number is null ? kind : $"{kind} n° {line.Number}";
        return string.IsNullOrWhiteSpace(line.Covers) ? head : $"{head} · {line.Covers}";
    }

    /// <summary>Every branch ends on <c>PatientId</c>, so a page boundary never shows a patient twice.</summary>
    private static List<ResteAPayerRowDto> Sort(List<ResteAPayerRowDto> rows, ResteAPayerList list, ResteAPayerSort sort) =>
        (list, sort) switch
        {
            (_, ResteAPayerSort.Amount) =>
                rows.OrderByDescending(r => list == ResteAPayerList.Due ? r.DueAmount : r.RunningAmount)
                    .ThenBy(r => r.PatientName)
                    .ThenBy(r => r.PatientId)
                    .ToList(),
            (ResteAPayerList.Due, _) =>
                rows.OrderBy(r => r.DueSince.HasValue ? 0 : 1)
                    .ThenBy(r => r.DueSince ?? DateTime.MaxValue)
                    .ThenBy(r => r.PatientName)
                    .ThenBy(r => r.PatientId)
                    .ToList(),
            _ =>
                rows.OrderBy(r => NextDate(r).HasValue ? 0 : 1)
                    .ThenBy(r => NextDate(r) ?? DateTime.MaxValue)
                    .ThenBy(r => r.PatientName)
                    .ThenBy(r => r.PatientId)
                    .ToList(),
        };

    private static DateTime? NextDate(ResteAPayerRowDto row) =>
        row.NextVisit is { } visit && row.NextInstallmentDue is { } due
            ? (visit < due ? visit : due)
            : row.NextVisit ?? row.NextInstallmentDue;
}
