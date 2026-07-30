using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

using ClinicManagement.Domain.Common;
namespace ClinicManagement.Application.Features.Billing.Queries;

/// <summary>The clinic-wide « Créances » (accounts-receivable) list — patients with a positive balance,
/// sorted by amount owed (descending). Clinic-scoped.</summary>
public class GetReceivablesQuery : IRequest<Result<ReceivablesPageDto>>
{
    /// <summary>1-based page and page size. Both null = every patient with a balance.</summary>
    public int? Page { get; set; }
    public int? PageSize { get; set; }

    /// <summary>
    /// Free-text filter on the patient's name. Matched <b>in memory</b> here, unlike every other list — see the
    /// handler for why this read has no queryable source to push it into.
    /// </summary>
    public string? SearchTerm { get; set; }
}

public class GetReceivablesQueryHandler : IRequestHandler<GetReceivablesQuery, Result<ReceivablesPageDto>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetReceivablesQueryHandler> _logger;

    public GetReceivablesQueryHandler(
        IInvoiceRepository invoiceRepository,
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetReceivablesQueryHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<ReceivablesPageDto>> Handle(GetReceivablesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<ReceivablesPageDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var now = DateTime.UtcNow;
            // The clinic's calendar day for the « depuis N jours » arithmetic below (AC-P6.4). `now` stays UTC —
            // the repository call takes an instant, not a date.
            var clinicToday = ClinicClock.ClinicToday(now);
            var invoiceByPatient = await _invoiceRepository.GetOutstandingByPatientAsync(clinicId, cancellationToken);

            // A plan bridged into a real invoice is already counted on the invoice track above; counting its
            // échéancier too would bill the same acts twice here while « Solde patient » counts them once.
            // Same shared rule, one light projection (no lines/payments loaded).
            var billedPlanIds = PlanBillingRules.BilledPlanIds(
                await _invoiceRepository.GetTreatmentPlanLinksAsync(clinicId, cancellationToken));
            var planByPatient = await _planRepository.GetInstallmentOutstandingByPatientAsync(
                clinicId, now, billedPlanIds, cancellationToken);

            // Merge the two tracks per patient.
            var totals = new Dictionary<Guid, decimal>();
            var oldestOverdue = new Dictionary<Guid, DateTime?>();

            foreach (var row in invoiceByPatient)
            {
                totals[row.PatientId] = totals.GetValueOrDefault(row.PatientId) + row.Outstanding;
            }
            foreach (var row in planByPatient)
            {
                totals[row.PatientId] = totals.GetValueOrDefault(row.PatientId) + row.Outstanding;
                if (row.OldestOverdueDueDate is not null)
                {
                    oldestOverdue[row.PatientId] = row.OldestOverdueDueDate;
                }
            }

            // Round and drop the settled rows FIRST, then resolve names in one round trip (AC-P6.21). This loop
            // used to call `GetByIdAsync` per patient — one query, plus its flags and both history collections,
            // for every patient with a balance, on the screen the clinic opens to chase money.
            var owing = totals
                .Select(kv => (PatientId: kv.Key, Outstanding: InvoiceCalculator.RoundMoney(kv.Value)))
                .Where(t => t.Outstanding > 0m)
                .ToList();

            var patients = await _patientRepository.GetByIdsAsync(
                clinicId, owing.Select(t => t.PatientId).ToList(), cancellationToken);

            var receivables = new List<ReceivableDto>();
            foreach (var (patientId, outstanding) in owing)
            {
                // Absent = outside the clinic or deleted; the repository already applied the clinic filter, so
                // this keeps the same defensive skip without a per-row ClinicId comparison.
                if (!patients.TryGetValue(patientId, out var patient))
                {
                    continue;
                }

                var overdue = oldestOverdue.GetValueOrDefault(patientId);
                int? daysOverdue = overdue is not null ? Math.Max(0, (clinicToday - overdue.Value.Date).Days) : null;

                receivables.Add(new ReceivableDto
                {
                    PatientId = patientId,
                    PatientName = patient.GetFullName(),
                    TotalOutstanding = outstanding,
                    OldestOverdueDate = overdue,
                    DaysOverdue = daysOverdue,
                });
            }

            var sorted = receivables
                .OrderByDescending(r => r.TotalOutstanding)
                .ThenBy(r => r.PatientName)
                .ToList();

            // Filtered and paged in memory, and this is the one place that is the right answer. « Créances » is
            // the union of two independent debt ledgers — invoice outstanding and plan échéancier outstanding —
            // netted per patient and then ranked by the total. A patient's rank is not known until both sides are
            // summed, so there is no query to put a LIMIT on: paging either input would page the wrong thing.
            // (The two ledger reads themselves are already bounded by « owes something », not by the clinic's
            // whole history.)
            var filtered = string.IsNullOrWhiteSpace(request.SearchTerm)
                ? sorted
                : sorted.Where(r => SearchTerm.Matches(request.SearchTerm, r.PatientName)).ToList();

            // The total is computed over `filtered` — every matching debtor — BEFORE the page is cut, because
            // « Total dû » is a statement about the clinic's receivables and not about the 25 rows on screen.
            // It does honour the search: filtering to one patient and being shown the clinic's whole debt under
            // their name would be its own kind of lie.
            var page = PagedResult<ReceivableDto>.FromSource(
                filtered, PageRequest.From(request.Page, request.PageSize));

            return Result<ReceivablesPageDto>.Success(new ReceivablesPageDto
            {
                Items = page.Items.ToList(),
                TotalOutstanding = filtered.Sum(r => r.TotalOutstanding),
                Page = page.Page,
                PageSize = page.PageSize,
                TotalCount = page.TotalCount,
                TotalPages = page.TotalPages,
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error building the receivables list");
            return Result<ReceivablesPageDto>.Failure("Erreur lors du calcul des créances.");
        }
    }
}
